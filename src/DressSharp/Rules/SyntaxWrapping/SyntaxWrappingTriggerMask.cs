using Microsoft.CodeAnalysis.CSharp;

namespace DressSharp.Rules;

readonly record struct SyntaxWrappingTriggerMask(
    bool OpenParen,
    bool OpenBracket,
    bool OpenBrace,
    bool Colon,
    bool Where,
    bool From,
    bool Member,
    bool Question,
    bool Binary)
{
    internal static SyntaxWrappingTriggerMask For(SyntaxWrappingSettings.Setting?[] byKind)
    {
        bool Enabled(SyntaxWrappingKind kind) => byKind[(int)kind] is not null;
        var arguments = Enabled(SyntaxWrappingKind.Arguments);
        var parameters = Enabled(SyntaxWrappingKind.Parameters);
        var member = Enabled(SyntaxWrappingKind.MemberAccessChains);
        return new(
            arguments || parameters,
            arguments
            || parameters
            || Enabled(SyntaxWrappingKind.CollectionExpressions)
            || Enabled(SyntaxWrappingKind.Attributes),
            Enabled(SyntaxWrappingKind.ObjectInitializers)
            || Enabled(SyntaxWrappingKind.CollectionInitializers)
            || Enabled(SyntaxWrappingKind.ArrayInitializers)
            || Enabled(SyntaxWrappingKind.WithInitializers),
            Enabled(SyntaxWrappingKind.BaseTypeLists),
            Enabled(SyntaxWrappingKind.ConstraintClauses),
            Enabled(SyntaxWrappingKind.QueryClauses),
            member,
            member || Enabled(SyntaxWrappingKind.ConditionalExpressions),
            Enabled(SyntaxWrappingKind.BinaryExpressions));
    }

    internal bool Matches(SyntaxKind kind) => kind switch
        {
            SyntaxKind.OpenParenToken => OpenParen,
            SyntaxKind.OpenBracketToken => OpenBracket,
            SyntaxKind.OpenBraceToken => OpenBrace,
            SyntaxKind.ColonToken => Colon,
            SyntaxKind.WhereKeyword => Where,
            SyntaxKind.FromKeyword => From,
            SyntaxKind.DotToken or SyntaxKind.MinusGreaterThanToken => Member,
            SyntaxKind.QuestionToken => Question,
            _ => Binary && IsBinaryOperator(kind)
        };

    internal static bool IsBinaryOperator(SyntaxKind kind) => kind is
        SyntaxKind.PlusToken
        or SyntaxKind.MinusToken
        or SyntaxKind.AsteriskToken
        or SyntaxKind.SlashToken
        or SyntaxKind.PercentToken
        or SyntaxKind.AmpersandToken
        or SyntaxKind.BarToken
        or SyntaxKind.CaretToken
        or SyntaxKind.LessThanLessThanToken
        or SyntaxKind.GreaterThanGreaterThanToken
        or SyntaxKind.GreaterThanGreaterThanGreaterThanToken
        or SyntaxKind.EqualsEqualsToken
        or SyntaxKind.ExclamationEqualsToken
        or SyntaxKind.LessThanToken
        or SyntaxKind.LessThanEqualsToken
        or SyntaxKind.GreaterThanToken
        or SyntaxKind.GreaterThanEqualsToken
        or SyntaxKind.AmpersandAmpersandToken
        or SyntaxKind.BarBarToken
        or SyntaxKind.QuestionQuestionToken
        or SyntaxKind.IsKeyword
        or SyntaxKind.AsKeyword;
}
