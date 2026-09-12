using System.Collections.Immutable;
using DressSharp.Architecture;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DressSharp.Rules;

sealed class AttributeTargetSpacingRule() : TokenSpacingRule(
    RuleKey.DressSpaceAfterAttributeTargetColon,
    "After attribute target colon",
    "Attributes",
    ["true", "false"],
    "true",
    "spacing after attribute target colons, such as assembly, module, and return",
    "[assembly: System.CLSCompliant(true)]")
{
    internal override ImmutableArray<SyntaxKind> TriggerKinds { get; } = [SyntaxKind.ColonToken];

    internal override bool? DesiredSpace(SyntaxToken left, SyntaxToken right, string preference) =>
        left.Parent is AttributeTargetSpecifierSyntax target && left == target.ColonToken
            ? preference == "true"
            : null;
}
