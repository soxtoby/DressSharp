using DressSharp.Architecture;
using DressSharp.Configuration;
using EasyAssertions;
using Xunit;

namespace DressSharp.UnitTests;

public class PlannedLayoutFixpointTests
{
    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void Binary_argument_continuations_follow_the_planned_operand_indent(string ending)
    {
        const string source = "class C\n{\n    void M()\n    {\n        Call(\n                value ??\n                [\n                    \"a\",\n                    \"b\"\n                ]);\n    }\n}";
        const string expected = "class C\n{\n    void M()\n    {\n        Call(\n            value\n            ?? [\n                    \"a\",\n                    \"b\"\n                ]);\n    }\n}";

        var first = Format(source.ReplaceLineEndings(ending));

        first.ShouldBe(expected.ReplaceLineEndings(ending));
        Format(first).ShouldBe(first);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void Binary_switch_arm_continuations_follow_the_planned_operand_indent(string ending)
    {
        const string source = "class C\n{\n    void M()\n    {\n        string Local(int value) =>\n            value switch\n            {\n                _ =>\n                    \"prefix\"\n                    + CreateValue()\n                    + \"suffix\"\n            };\n    }\n}";
        const string expected = "class C\n{\n    void M()\n    {\n        string Local(int value) =>\n            value switch\n                {\n                    _ =>\n                        \"prefix\"\n                        + CreateValue()\n                        + \"suffix\"\n                };\n    }\n}";

        var first = Format(source.ReplaceLineEndings(ending));

        first.ShouldBe(expected.ReplaceLineEndings(ending));
        Format(first).ShouldBe(first);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void Removed_braces_measure_the_body_on_its_planned_line(string ending)
    {
        const string source = "class C\n{\n    void M()\n    {\n        if ((this.LogEventOperationCompleted == null)) {\n            this.LogEventOperationCompleted = new System.Threading.SendOrPostCallback(this.OnLogEventOperationCompleted);\n        }\n    }\n}";
        const string expected = "class C\n{\n    void M()\n    {\n        if ((this.LogEventOperationCompleted == null))\n            this.LogEventOperationCompleted = new System.Threading.SendOrPostCallback(this.OnLogEventOperationCompleted);\n    }\n}";

        var first = Format(source.ReplaceLineEndings(ending));

        first.ShouldBe(expected.ReplaceLineEndings(ending));
        Format(first).ShouldBe(first);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void Lambda_body_wrapping_uses_the_indentation_after_brace_planning(string ending)
    {
        const string source = "class C\n{\n    void M()\n    {\n        Call(() =>\n        {\n            if (value)\n                Call(\"long text long text long text long text long text long text long text long text long text long text long text long text long text\");\n            else\n                Call(\"much longer text much longer text much longer text much longer text much longer text much longer text much longer text much longer text\");\n        });\n    }\n}";

        var first = Format(source.ReplaceLineEndings(ending));

        Format(first).ShouldBe(first);
        first.ShouldContain("else" + ending + "                {" + ending + "                    Call(" + ending);
    }

    static string Format(string source)
    {
        var preferences = PreferenceCatalog.Defaults
            .Select(preference => (preference.Key.ToName(), preference.Key == RuleKey.MaxLineLength ? "160" : preference.Default))
            .ToArray();
        return EmitterTestHarness.Format(source, preferences);
    }
}
