using Xunit;

namespace DressSharp.UnitTests;

public class IndentationEmitterTests
{
    [Fact]
    public void Switch_label_setting_preserves_unrelated_source_indentation()
    {
        const string source = "class C\n  {\n      void M(int x)\n        {\n          switch (x)\n            {\n case 0:\n                  break;\n            }\n        }\n  }";
        const string expected = "class C\n  {\n      void M(int x)\n        {\n          switch (x)\n            {\n            case 0:\n                  break;\n            }\n        }\n  }";

        Assert.Equal(expected, Emit(source, ("csharp_indent_switch_labels", "false")));
    }

    [Theory]
    [InlineData("true", "            case 0:")]
    [InlineData("false", "        case 0:")]
    public void Indents_switch_labels_from_the_switch_brace(string preference, string expectedLabel)
    {
        const string source = "class C\n{\nvoid M(int x)\n{\nswitch (x)\n{\n                case 0:\n                break;\n}\n}\n}";

        var result = Emit(source,
            ("csharp_indent_block_contents", "true"),
            ("csharp_indent_switch_labels", preference));

        Assert.Contains($"\n{expectedLabel}\n", result);
        Assert.Equal(result, Emit(result,
            ("csharp_indent_block_contents", "true"),
            ("csharp_indent_switch_labels", preference)));
    }

    [Theory]
    [InlineData("true", "            break;")]
    [InlineData("false", "        break;")]
    public void Indents_case_contents_from_the_effective_label(string preference, string expectedStatement)
    {
        const string source = "class C\n{\nvoid M(int x)\n{\nswitch (x)\n{\ncase 0:\n                break;\n}\n}\n}";

        var result = Emit(source,
            ("csharp_indent_block_contents", "true"),
            ("csharp_indent_switch_labels", "false"),
            ("csharp_indent_case_contents", preference));

        Assert.Contains("\n        case 0:\n", result);
        Assert.Contains($"\n{expectedStatement}\n", result);
        Assert.Equal(result, Emit(result,
            ("csharp_indent_block_contents", "true"),
            ("csharp_indent_switch_labels", "false"),
            ("csharp_indent_case_contents", preference)));
    }

    [Theory]
    [InlineData("flush_left", "")]
    [InlineData("one_less_than_current", "    ")]
    [InlineData("no_change", "      ")]
    public void Applies_label_indentation_after_block_indentation(string preference, string expectedIndent)
    {
        const string source = "class C\n{\nvoid M()\n{\n      retry:\nreturn;\n}\n}";

        var result = Emit(source,
            ("csharp_indent_block_contents", "true"),
            ("csharp_indent_labels", preference));

        Assert.Contains($"\n{expectedIndent}retry:\n        return;", result);
        Assert.Equal(result, Emit(result,
            ("csharp_indent_block_contents", "true"),
            ("csharp_indent_labels", preference)));
    }

    [Theory]
    [InlineData("true", "                        ")]
    [InlineData("false", "                    ")]
    public void Case_block_indentation_overrides_case_contents_and_brace_indentation(
        string preference,
        string expectedBraceIndent)
    {
        const string source = "class C\n{\nvoid M(int x)\n{\nswitch (x)\n{\ncase 0:\n{\nreturn;\n}\n}\n}\n}";

        var result = Emit(source,
            ("csharp_indent_block_contents", "true"),
            ("csharp_indent_braces", "true"),
            ("csharp_indent_switch_labels", "false"),
            ("csharp_indent_case_contents", "false"),
            ("csharp_indent_case_contents_when_block", preference));

        Assert.Contains($"\n                    case 0:\n{expectedBraceIndent}{{\n{expectedBraceIndent}    return;\n{expectedBraceIndent}}}", result);
        Assert.Equal(result, Emit(result,
            ("csharp_indent_block_contents", "true"),
            ("csharp_indent_braces", "true"),
            ("csharp_indent_switch_labels", "false"),
            ("csharp_indent_case_contents", "false"),
            ("csharp_indent_case_contents_when_block", preference)));
    }

    [Theory]
    [InlineData("true", "                ")]
    [InlineData("false", "            ")]
    public void Case_block_indentation_does_not_require_a_switch_label_preference(
        string preference,
        string expectedBraceIndent)
    {
        const string source = "class C\n{\nvoid M(int x)\n{\nswitch (x)\n{\ncase 0:\n{\nreturn;\n}\n}\n}\n}";

        var result = Emit(source,
            ("csharp_indent_block_contents", "true"),
            ("csharp_indent_case_contents_when_block", preference));

        Assert.Contains($"\n            case 0:\n{expectedBraceIndent}{{\n{expectedBraceIndent}    return;\n{expectedBraceIndent}}}", result);
    }

    [Fact]
    public void Preserves_comments_and_directives_attached_to_indentation_targets()
    {
        const string source = "class C\n{\nvoid M(int x)\n{\nswitch (x)\n{\n          // label\n          case 0:\n          // block\n          {\nreturn;\n}\n#if true\n          case 1:\n          break;\n#endif\n}\n      // goto label\n      retry:\nreturn;\n}\n}";

        var result = Emit(source,
            ("csharp_indent_block_contents", "true"),
            ("csharp_indent_switch_labels", "false"),
            ("csharp_indent_case_contents", "false"),
            ("csharp_indent_labels", "flush_left"),
            ("csharp_indent_case_contents_when_block", "false"));

        Assert.Contains("// label\n          case 0:", result);
        Assert.Contains("// block\n          {", result);
        Assert.Contains("#if true\n          case 1:", result);
        Assert.Contains("// goto label\n      retry:", result);
        Assert.Equal(result, Emit(result,
            ("csharp_indent_block_contents", "true"),
            ("csharp_indent_switch_labels", "false"),
            ("csharp_indent_case_contents", "false"),
            ("csharp_indent_labels", "flush_left"),
            ("csharp_indent_case_contents_when_block", "false")));
    }

    [Fact]
    public void Leading_block_comment_does_not_become_same_line_brace_indentation()
    {
        const string source = "class C\n{\n    /* keep */ void M() {\nreturn;\n}\n}";
        const string expected = "class C\n{\n    /* keep */ void M() {\n        return;\n}\n}";

        var result = Emit(source, ("csharp_indent_block_contents", "true"));

        Assert.Equal(expected, result);
        Assert.Equal(result, Emit(result, ("csharp_indent_block_contents", "true")));
    }

    [Fact]
    public void Skips_malformed_switch_sections_and_labeled_statements()
    {
        const string source = "class C\n{\nvoid M(int x)\n{\nswitch (x)\n{\n          case :\n          {\nreturn +;\n}\n}\n      retry:\nreturn +;\n}\n}";
        (string, string)[] firstPreferences =
        [
            ("csharp_indent_block_contents", "true"),
            ("csharp_indent_switch_labels", "true"),
            ("csharp_indent_case_contents", "true"),
            ("csharp_indent_labels", "flush_left"),
            ("csharp_indent_case_contents_when_block", "true")
        ];
        (string, string)[] oppositePreferences =
        [
            ("csharp_indent_block_contents", "true"),
            ("csharp_indent_switch_labels", "false"),
            ("csharp_indent_case_contents", "false"),
            ("csharp_indent_labels", "one_less_than_current"),
            ("csharp_indent_case_contents_when_block", "false")
        ];

        Assert.Equal(Emit(source, firstPreferences), Emit(source, oppositePreferences));
    }

    [Theory]
    [InlineData("csharp_indent_switch_labels")]
    [InlineData("csharp_indent_case_contents")]
    [InlineData("csharp_indent_labels")]
    [InlineData("csharp_indent_case_contents_when_block")]
    public void Unset_indentation_preferences_add_no_rule_opinion(string key)
    {
        const string source = "class C\n{\nvoid M(int x)\n{\nswitch (x)\n{\n      case 0:\n      {\nreturn;\n}\n}\n   retry:\nreturn;\n}\n}";
        (string, string)[] baseline = [("csharp_indent_block_contents", "true")];

        Assert.Equal(Emit(source, baseline), Emit(source,
            ("csharp_indent_block_contents", "true"),
            (key, "unset")));
    }

    [Theory]
    [InlineData("csharp_indent_braces")]
    [InlineData("csharp_indent_block_contents")]
    [InlineData("csharp_indent_switch_labels")]
    [InlineData("csharp_indent_case_contents")]
    [InlineData("csharp_indent_labels")]
    [InlineData("csharp_indent_case_contents_when_block")]
    public void Unset_indentation_preference_is_inactive(string key)
    {
        const string source = "class C\n  {\n      void M(int x)\n        {\n          switch (x)\n            {\n case 0:\n                  break;\n            }\n        }\n  }";

        Assert.Equal(source, Emit(source, (key, "unset")));
    }

    static string Emit(string source, params (string Key, string Value)[] preferences) =>
        EmitterTestHarness.Format(source, preferences);
}
