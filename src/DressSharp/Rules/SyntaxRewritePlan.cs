using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DressSharp.Rules;

/// <summary>
/// The members of a file that a syntax rule wants to change, each already rewritten on its own.
/// </summary>
/// <remarks>
/// Rewriting a member inside the file's tree makes Roslyn rebuild every node between it and the
/// root, and every later rule then walks that new tree. Rewriting the member detached costs only the
/// member, and the emitter writes the result straight out, so the file's tree is never rebuilt at
/// all. Members no rule wants are never copied, and are written from the original source.
/// </remarks>
sealed class SyntaxRewritePlan
{
    /// <summary>
    /// Replacements in document order, none overlapping, each standing in for the span it replaces.
    /// </summary>
    internal IReadOnlyList<Replacement> Replacements { get; }

    static SyntaxRewritePlan Empty { get; } = new([]);

    SyntaxRewritePlan(IReadOnlyList<Replacement> replacements) => Replacements = replacements;

    internal readonly record struct Replacement(TextSpan Original, SyntaxNode Rewritten, string Text);

    internal static SyntaxRewritePlan For(
        SyntaxNode root,
        MemberRuleSet rules,
        RuleContext context,
        Func<SyntaxNode, SyntaxNode>? finishMember = null)
    {
        if (rules.IsEmpty && finishMember is null)
            return Empty;

        List<Replacement>? replacements = null;
        foreach (var member in Members(root))
        {
            var claims = rules.ClaimsFor(member);
            if (claims == 0 && finishMember is null)
                continue;

            var current = member;
            for (var index = 0; index < rules.Count; index++)
            {
                if ((claims & (1UL << index)) != 0)
                    current = rules.Transform(index, current, context);
            }

            if (finishMember is not null)
                current = finishMember(current);

            if (!ReferenceEquals(current, member))
                (replacements ??= []).Add(new(member.FullSpan, current, current.ToFullString()));
        }

        return replacements is null ? Empty : new(replacements);
    }

    /// <summary>
    /// The same members again, each offered to a rule that runs after the ones this plan holds.
    /// </summary>
    /// <remarks>
    /// A member already rewritten is handed on in its rewritten form, together with the index of
    /// the stream segment its tokens were laid out as, so a rule that has to ask the plan where a
    /// line falls can name the tokens it is looking at.
    /// </remarks>
    internal SyntaxRewritePlan Then(SyntaxNode root, Func<SyntaxNode, int, SyntaxNode> transform)
    {
        List<Replacement>? replacements = null;
        var next = 0;
        foreach (var member in Members(root))
        {
            var replaced = next < Replacements.Count && Replacements[next].Original == member.FullSpan;
            var current = replaced ? Replacements[next].Rewritten : member;
            var transformed = transform(current, replaced ? next : -1);
            if (replaced)
                next++;
            if (ReferenceEquals(transformed, member))
                continue;

            (replacements ??= []).Add(ReferenceEquals(transformed, current) && replaced
                ? Replacements[next - 1]
                : new(member.FullSpan, transformed, transformed.ToFullString()));
        }

        return replacements is null ? Empty : new(replacements);
    }

    /// <summary>
    /// The members a rewrite is scoped to: declarations that are not themselves containers. A local
    /// function inside a method is covered by rewriting the method, so the walk stops at the member.
    /// </summary>
    static IEnumerable<SyntaxNode> Members(SyntaxNode node)
    {
        foreach (var child in node.ChildNodes())
        {
            if (child is MemberDeclarationSyntax and not BaseTypeDeclarationSyntax and not BaseNamespaceDeclarationSyntax)
            {
                yield return child;
                continue;
            }

            foreach (var nested in Members(child))
                yield return nested;
        }
    }
}

/// <summary>
/// The member-scoped syntax rules a configuration turns on, prepared so that asking which of them
/// want a given member costs one scan of it and no allocation.
/// </summary>
sealed class MemberRuleSet
{
    readonly (ISyntaxFormattingRule Rule, string Preference, ulong Wanted)[] _rules;
    readonly Dictionary<int, ulong> _kindBits;
    readonly ulong _allWanted;

    MemberRuleSet((ISyntaxFormattingRule, string, ulong)[] rules, Dictionary<int, ulong> kindBits, ulong allWanted)
    {
        _rules = rules;
        _kindBits = kindBits;
        _allWanted = allWanted;
    }

    internal bool IsEmpty => _rules.Length == 0;
    internal int Count => _rules.Length;

    internal SyntaxNode Transform(int index, SyntaxNode node, RuleContext context) =>
        _rules[index].Rule.Transform(node, _rules[index].Preference, context);

    /// <summary>
    /// The bit set of rules that have something to look at in this member.
    /// </summary>
    internal ulong ClaimsFor(SyntaxNode member)
    {
        var present = _kindBits.GetValueOrDefault(member.RawKind);
        var claims = 0UL;
        var unresolved = false;
        for (var index = 0; index < _rules.Length; index++)
        {
            var wanted = _rules[index].Wanted;
            if (wanted == 0 || (wanted & present) != 0)
                claims |= 1UL << index;
            else
                unresolved = true;
        }

        if (!unresolved)
            return claims;

        // Lambdas and conditionals live inside a member rather than being one, so what the member
        // contains has to be scanned. One scan answers for every rule still undecided, and it stops
        // as soon as everything anyone asked about has turned up.
        foreach (var node in member.DescendantNodes())
        {
            present |= _kindBits.GetValueOrDefault(node.RawKind);
            if (present == _allWanted)
                break;
        }

        for (var index = 0; index < _rules.Length; index++)
        {
            if ((_rules[index].Wanted & present) != 0)
                claims |= 1UL << index;
        }

        return claims;
    }

    internal static MemberRuleSet From(RuleCatalog catalog, Architecture.FormattingConfiguration configuration)
    {
        var enabled = new List<(ISyntaxFormattingRule, string)>();
        foreach (var rule in catalog.MemberRules)
        {
            if (configuration.Preferences.TryGetValue(rule.Metadata.RuleKey, out var preference)
                && !preference.Equals("unset", StringComparison.OrdinalIgnoreCase))
            {
                enabled.Add((rule, preference));
            }
        }

        var kindBits = new Dictionary<int, ulong>();
        var next = 0;
        foreach (var (rule, _) in enabled)
        {
            foreach (var kind in rule.TargetKinds)
            {
                if (!kindBits.ContainsKey((int)kind))
                    kindBits[(int)kind] = 1UL << next++;
            }
        }

        var allWanted = next == 64 ? ulong.MaxValue : (1UL << next) - 1;
        var prepared = new (ISyntaxFormattingRule, string, ulong)[enabled.Count];
        for (var index = 0; index < enabled.Count; index++)
        {
            var wanted = 0UL;
            foreach (var kind in enabled[index].Item1.TargetKinds)
                wanted |= kindBits[(int)kind];
            prepared[index] = (enabled[index].Item1, enabled[index].Item2, wanted);
        }

        return new(prepared, kindBits, allWanted);
    }
}
