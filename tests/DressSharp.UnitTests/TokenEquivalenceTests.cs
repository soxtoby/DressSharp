using DressSharp.Execution;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace DressSharp.UnitTests;

public class TokenEquivalenceTests
{
    [Theory]
    [InlineData("class C { int A; }", "class   C\n{\n    int A;\n}\n")]
    [InlineData("class C { void M() { } }", "class C{void M(){}}")]
    [InlineData("class C { string M() => $\"a{1 + 2}b\"; }", "class C\n{\n    string M() => $\"a{ 1 + 2 }b\";\n}\n")]
    public void Accepts_whitespace_only_differences(string source, string formatted) =>
        Assert.Null(TokenEquivalence.FirstDifference(Parse(source), formatted, Options));

    [Theory]
    [InlineData("class C { int A; }", "class C { int B; }")]
    [InlineData("class C { int A; }", "class C { int A; int B; }")]
    [InlineData("class C { int A; int B; }", "class C { int A; }")]
    public void Reports_a_changed_token_stream(string source, string formatted) =>
        Assert.NotNull(TokenEquivalence.FirstDifference(Parse(source), formatted, Options));

    [Fact]
    public void Reports_what_a_token_became()
    {
        var difference = TokenEquivalence.FirstDifference(Parse("class C { int A; }"), "class C { int B; }", Options);
        Assert.Contains("'A'", difference);
        Assert.Contains("'B'", difference);
    }

    [Fact]
    public void Detects_whitespace_written_into_string_content()
    {
        // The emitter's first corpus run did this: it treated the brace opening an interpolation hole
        // as a block brace and broke the line inside the literal, rewriting the string's content.
        // Whitespace inside the hole is harmless; whitespace inside the text is not.
        const string source = "class C { string M() => $\"a b{1}\"; }";
        Assert.NotNull(TokenEquivalence.FirstDifference(Parse(source), "class C { string M() => $\"a  b{1}\"; }", Options));
    }

    static Microsoft.CodeAnalysis.SyntaxNode Parse(string source) =>
        CSharpSyntaxTree.ParseText(source, Options).GetRoot();

    static readonly CSharpParseOptions Options = new(LanguageVersion.Latest);
}
