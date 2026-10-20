using System.Collections.Immutable;
using DressSharp.Architecture;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DressSharp.Rules;

/// <summary>
/// The syntax that owns an opening brace, as far as its position is concerned. Braces a more
/// specific rule decides, such as an initializer or an embedded statement, belong to no family.
/// </summary>
enum OpenBraceFamily
{
    Declaration,
    Statement,
    Lambda
}

sealed class OpenBracePositionRule(RuleKey ruleKey, OpenBraceFamily family) : IFormattingRule
{
    public RuleMetadata Metadata { get; } = new()
        {
            RuleKey = ruleKey,
            Caption = family switch
                {
                    OpenBraceFamily.Declaration => "Declarations",
                    OpenBraceFamily.Statement => "Statements",
                    _ => "Lambdas"
                },
            ExpandedCaption = RuleMetadata.Humanize(ruleKey.ToName()),
            GroupName = "Braces and bodies",
            SubgroupName = "Open brace position",
            Description = family switch
                {
                    OpenBraceFamily.Declaration =>
                        "Places the opening brace of a namespace, type, method, constructor, operator, property, indexer, event, accessor, or local function on the same line as its header, or on the next line.",
                    OpenBraceFamily.Statement =>
                        "Places the opening brace of a try, catch, finally, switch, checked, unsafe, or bare block on the same line as its header, or on the next line. A block that is an embedded statement follows dress_embedded_statement_placement when that is set.",
                    _ => "Places the opening brace of a lambda or anonymous method body on the same line as the arrow or delegate keyword, or on the next line."
                }
                + " With the other open brace position rules, read in place of csharp_new_line_before_open_brace, which can then stay unset for tools that act on it, such as Visual Studio and Rider.",
            Values = RuleValues.Choice("same_line", "next_line"),
            DefaultValue = "next_line",
            StandardCounterpart = RuleKey.CSharpNewLineBeforeOpenBrace,
            // A brace this rule moves to its own line takes its column from brace indentation, so the examples show the Default one.
            ExamplePreferences = ImmutableDictionary<RuleKey, string>.Empty.Add(RuleKey.CSharpIndentBraces, "false"),
            Example = family switch
                {
                    OpenBraceFamily.Declaration => "namespace Example {\n    class Point {\n        int X {\n            get {\n                return 0;\n            }\n        }\n\n        void Run() {\n            Work();\n        }\n    }\n}",
                    OpenBraceFamily.Statement => "try {\n    Work();\n} catch (Exception) {\n    Recover();\n}\nswitch (value) {\n    case 1: {\n        break;\n    }\n}",
                    _ => "Action action = () => {\n    Work();\n};\nAction method = delegate {\n    Work();\n};"
                },
            OwnedSyntax = family switch
                {
                    OpenBraceFamily.Declaration => "the opening brace of a namespace, type, member, accessor, or local function",
                    OpenBraceFamily.Statement => "the opening brace of a statement block that is not an embedded statement",
                    _ => "the opening brace of a lambda or anonymous method body"
                },
            Invariant = "Only boundary whitespace changes; a brace decided by a more specific rule, such as embedded statement placement or initializer layout, is unchanged"
        };

    internal OpenBraceFamily Family => family;

    internal static IEnumerable<OpenBracePositionRule> All() =>
        [
            new(RuleKey.DressDeclarationOpenBracePosition, OpenBraceFamily.Declaration),
            new(RuleKey.DressStatementOpenBracePosition, OpenBraceFamily.Statement),
            new(RuleKey.DressLambdaOpenBracePosition, OpenBraceFamily.Lambda)
        ];

    /// <summary>The family of an opening brace, or null for a brace no position rule places.</summary>
    internal static OpenBraceFamily? FamilyOf(SyntaxToken openBrace) => openBrace.Parent switch
        {
            NamespaceDeclarationSyntax or BaseTypeDeclarationSyntax or AccessorListSyntax => OpenBraceFamily.Declaration,
            SwitchStatementSyntax => OpenBraceFamily.Statement,
            BlockSyntax { Parent: AccessorDeclarationSyntax or BaseMethodDeclarationSyntax or LocalFunctionStatementSyntax } =>
                OpenBraceFamily.Declaration,
            BlockSyntax { Parent: AnonymousFunctionExpressionSyntax } => OpenBraceFamily.Lambda,
            BlockSyntax { Parent: StatementSyntax or CatchClauseSyntax or FinallyClauseSyntax or ElseClauseSyntax or SwitchSectionSyntax or GlobalStatementSyntax } =>
                OpenBraceFamily.Statement,
            _ => null
        };
}

sealed class SingleLineBlocksRule : IFormattingRule
{
    public RuleMetadata Metadata { get; } = new()
        {
            RuleKey = RuleKey.DressSingleLineBlocks,
            Caption = "Blocks",
            ExpandedCaption = "Single-line blocks",
            GroupName = "Braces and bodies",
            SubgroupName = "Preserve single line",
            Description =
                "Keeps a block, accessor list, type, namespace, or switch that is written on one line as it is, expands it, or expands all but the trivial ones: an empty block and an accessor list of auto accessors. Read in place of csharp_preserve_single_line_blocks, which can then stay unset for tools that act on it, such as Visual Studio and Rider.",
            Values = RuleValues.Choice("preserve", "expand", "expand_unless_trivial"),
            DefaultValue = "expand_unless_trivial",
            StandardCounterpart = RuleKey.CSharpPreserveSingleLineBlocks,
            Example = "class Example { bool Ready { get; } void Run() { } void Work() { Ready = true; } }",
            OwnedSyntax = "existing single-line blocks and accessor lists",
            Invariant = "Preserve leaves them as written; expand_unless_trivial keeps empty blocks and auto-accessor lists on one line"
        };
}
