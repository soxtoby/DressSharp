using DressSharp.Architecture;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DressSharp.Rules;

/// <summary>
/// Applies whole-file syntax rules with one tree visit for every using-list rule.
/// </summary>
static class FileScopedStructuralRules
{
    internal static TransformationResult Transform(
        SyntaxNode root,
        RuleCatalog catalog,
        FormattingConfiguration configuration)
    {
        try
        {
            var settings = RuleSettings.From(configuration);
            var context = new RuleContext(root, settings);
            var current = root;
            var skipped = 0;
            var usingRules = new List<(IFormattingRule Rule, string Preference)>();

            foreach (var rule in catalog.Rules)
            {
                if (!configuration.Preferences.TryGetValue(rule.Metadata.PreferenceKey, out var preference)
                    || preference.Equals("unset", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!rule.Metadata.AcceptedValues.Contains(preference, StringComparer.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Invalid value '{preference}' for '{rule.Metadata.PreferenceKey}'.");

                if (rule is NamespaceStyleRule namespaceStyle)
                {
                    var input = current;
                    current = namespaceStyle.Transform(current, preference, context);
                    skipped += context.TakeSkippedOccurrences();
                    if (!ReferenceEquals(input, current))
                    {
                        var knownWellFormed = !context.HasMalformedRegions;
                        context = new RuleContext(current, settings, knownWellFormed);
                    }
                }
                else
                {
                    usingRules.Add((rule, preference));
                }
            }

            if (usingRules.Count > 0)
                current = new UsingTree(usingRules, context).Rewrite((CompilationUnitSyntax)current);
            skipped += context.TakeSkippedOccurrences();
            return new(current, SkippedOccurrences: skipped);
        }
        catch (Exception exception)
        {
            return new(root, exception);
        }
    }

    static SyntaxList<UsingDirectiveSyntax> RewriteUsings(
        SyntaxList<UsingDirectiveSyntax> source,
        IReadOnlyList<(IFormattingRule Rule, string Preference)> rules,
        RuleContext context)
    {
        var current = source;
        foreach (var (rule, preference) in rules)
        {
            current = rule switch
                {
                    UsingOrderRule ordering => ordering.Rewrite(current, preference),
                    UsingDirectiveRule directive => directive.Rewrite(current, preference, context),
                    _ => throw new InvalidOperationException($"Unsupported file-scoped rule '{rule.GetType().Name}'.")
                };
        }

        return current;
    }

    sealed class UsingTree(
        IReadOnlyList<(IFormattingRule Rule, string Preference)> rules,
        RuleContext context)
    {
        internal CompilationUnitSyntax Rewrite(CompilationUnitSyntax unit)
        {
            var usings = RewriteUsings(unit.Usings, rules, context);
            var members = RewriteMembers(unit.Members);
            if (usings == unit.Usings && members == unit.Members)
                return unit;
            return unit.WithUsings(usings).WithMembers(members);
        }

        SyntaxList<MemberDeclarationSyntax> RewriteMembers(SyntaxList<MemberDeclarationSyntax> source)
        {
            List<MemberDeclarationSyntax>? changed = null;
            for (var index = 0; index < source.Count; index++)
            {
                var member = source[index];
                var rewritten = member switch
                    {
                        NamespaceDeclarationSyntax declaration => Rewrite(declaration),
                        FileScopedNamespaceDeclarationSyntax declaration => Rewrite(declaration),
                        _ => member
                    };
                if (ReferenceEquals(member, rewritten))
                    continue;
                changed ??= [.. source];
                changed[index] = rewritten;
            }

            return changed is null ? source : SyntaxFactory.List(changed);
        }

        NamespaceDeclarationSyntax Rewrite(NamespaceDeclarationSyntax declaration)
        {
            var usings = RewriteUsings(declaration.Usings, rules, context);
            var members = RewriteMembers(declaration.Members);
            if (usings == declaration.Usings && members == declaration.Members)
                return declaration;
            return declaration.WithUsings(usings).WithMembers(members);
        }

        FileScopedNamespaceDeclarationSyntax Rewrite(FileScopedNamespaceDeclarationSyntax declaration)
        {
            var usings = RewriteUsings(declaration.Usings, rules, context);
            var members = RewriteMembers(declaration.Members);
            if (usings == declaration.Usings && members == declaration.Members)
                return declaration;
            return declaration.WithUsings(usings).WithMembers(members);
        }
    }

}
