using DressSharp.Architecture;
using DressSharp.Configuration;
using Xunit;
using EasyAssertions;
using static DressSharp.UnitTests.EmitterTestHarness;

namespace DressSharp.UnitTests;

public class IndentationEmitterTests
{
    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void Disabling_block_indentation_still_indents_declarations_like_dotnet_format(string lineEnding)
    {
        var source = """
            namespace N {
            class C {
            void M() {
            if (true) {
            Console.WriteLine("a");
            }
            }
            int P {
            get {
            return 1;
            }
            }
            }
            }
            """.ReplaceLineEndings(lineEnding);
        var expected = """
            namespace N {
                class C {
                    void M() {
                    if (true) {
                    Console.WriteLine("a");
                    }
                    }
                    int P {
                        get {
                        return 1;
                        }
                    }
                }
            }
            """.ReplaceLineEndings(lineEnding);
        var preferences = new[] { ("csharp_indent_block_contents", "false"), ("csharp_indent_braces", "false") };
        var result = Format(source, preferences);
        result.ShouldBe(expected);
        Format(result, preferences).ShouldBe(result);
    }

    [Theory]
    [InlineData("unset", "if (true) {\n    if (true) {\nConsole.WriteLine(\"a\");\nConsole.WriteLine(\"b\");\n    }\n}")]
    [InlineData("true", "if (true) {\n    if (true) {\n        Console.WriteLine(\"a\");\n        Console.WriteLine(\"b\");\n    }\n}")]
    [InlineData("false", "if (true) {\nif (true) {\nConsole.WriteLine(\"a\");\nConsole.WriteLine(\"b\");\n}\n}")]
    public void Nested_same_line_blocks_follow_the_selected_content_indentation(string value, string expected)
    {
        const string source = "if (true) {\n    if (true) {\nConsole.WriteLine(\"a\");\nConsole.WriteLine(\"b\");\n    }\n}";
        var preferences = new[] { ("csharp_indent_block_contents", value), ("csharp_indent_braces", "false") };
        var result = Format(source, preferences);
        result.ShouldBe(expected);
        Format(result, preferences).ShouldBe(result);
    }

    [Theory]
    [InlineData("unset", false)]
    [InlineData(null, false)]
    [InlineData("unset", true)]
    [InlineData(null, true)]
    public void Brace_alignment_preserves_existing_member_indentation_without_a_content_preference(string? value, bool indentBraces)
    {
        const string source = "class C\n{\n    void M()\n    {\n        Work();\n    }\n}";
        var preferences = new List<(string, string)> { ("csharp_indent_braces", indentBraces ? "true" : "false") };
        if (value is not null)
            preferences.Add(("csharp_indent_block_contents", value));
        var result = Format(source, preferences.ToArray());
        result.ShouldBe(indentBraces
            ? "class C\n    {\n    void M()\n        {\n        Work();\n        }\n    }"
            : source);
        Format(result, preferences.ToArray()).ShouldBe(result);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Else_if_body_has_the_same_indent_as_the_if_body(bool useDefaults)
    {
        const string source = """
            static void RestoreFinalNewline(List<EditorConfigLine> lines, bool hadFinalNewline)
            {
                if (!hadFinalNewline)
                    lines[^1].Ending = string.Empty;
                else if (lines[^1].Ending.Length == 0)
                    lines[^1].Ending = PreferredNewline(lines, lines.Count - 1);
            }
            """;
        var preferences = useDefaults
            ? PreferenceCatalog.Defaults.Select(item => (item.Key.ToName(), item.Default)).ToArray()
            : new[] { ("csharp_indent_block_contents", "true"), ("dress_embedded_statement_placement", "next_line") };
        var result = Format(source, preferences);
        result.ShouldBe(source);
        Format(result, preferences).ShouldBe(result);
    }

    [Fact]
    public void Else_if_chains_retain_surrounding_unbraced_nesting()
    {
        const string source = """
            void M()
            {
                while (ready)
                    if (a)
                        A();
                    else if (b)
                        if (c)
                            B();
                        else
                            C();
                    else if (d)
                        D();
                    else
                    {
                        E();
                        F();
                    }
            }
            """;
        var preferences = new[] { ("csharp_indent_block_contents", "true"), ("dress_embedded_statement_placement", "next_line") };
        var result = Format(source, preferences);
        result.ShouldBe(source);
        Format(result, preferences).ShouldBe(result);
    }

    [Theory]
    [InlineData("() =>")]
    [InlineData("async () =>")]
    [InlineData("delegate")]
    [InlineData("(int x,\n        int y) =>")]
    public void Lambda_block_follows_a_continuation_line(string callback)
    {
        var source = $$"""
            M(
                {{callback}}
                {
                    Work();
                });
            """;
        Format(source, ("csharp_indent_braces", "false")).ShouldBe(source);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Nested_lambda_block_follows_its_containing_argument_indent(bool useDefaults)
    {
        const string source = """
            foreach (var config in resolved.EditorConfigFiles)
            {
                var configPath = Path.Combine(config.Directory, config.FileName);
                _ = _validated.GetOrAdd(
                    configPath,
                    key => new Lazy<bool>(() =>
                    {
                        EditorConfigSyntaxValidator.DecodeAndValidate(key, File.ReadAllBytes(key));
                        return true;
                    })).Value;
            }
            """;
        var preferences = useDefaults
            ? PreferenceCatalog.Defaults.Select(item => (item.Key.ToName(), item.Default)).ToArray()
            : new[] { ("csharp_indent_block_contents", "true"), ("csharp_indent_braces", "false") };
        const string input = """
            foreach (var config in resolved.EditorConfigFiles)
            {
                var configPath = Path.Combine(config.Directory, config.FileName);
                _ = _validated.GetOrAdd(
                    configPath,
                    key => new Lazy<bool>(() =>
                        {
                            EditorConfigSyntaxValidator.DecodeAndValidate(key, File.ReadAllBytes(key));
                            return true;
                        })).Value;
            }
            """;
        var result = Format(input, preferences);
        result.ShouldBe(source);
        Format(result, preferences).ShouldBe(result);
    }

    [Fact]
    public void Block_indentation_preserves_continuation_indentation()
    {
        Format("""
                class C
                {
                    int M(bool value) =>
                        value
                            ? 1
                            : 2;
                }
                """,
                ("csharp_indent_block_contents", "true"))
            .ShouldBe("""
                class C
                {
                    int M(bool value) =>
                        value
                            ? 1
                            : 2;
                }
                """);
    }

    [Fact]
    public void Switch_expression_arms_follow_the_reindented_brace()
    {
        Format("""
                class C
                {
                int M(int value)
                {
                var result = value switch
                    {
                                0
                                    or 1 => 1,
                                _ => 2
                    };
                return result;
                }
                }
                """,
                ("csharp_indent_block_contents", "true"),
                ("csharp_indent_braces", "false"))
            .ShouldBe("""
                class C
                {
                    int M(int value)
                    {
                        var result = value switch
                        {
                            0
                                or 1 => 1,
                            _ => 2
                        };
                        return result;
                    }
                }
                """);
    }

    [Fact]
    public void Switch_expression_inside_a_wrapped_argument_uses_that_argument_indent()
    {
        Format(
                """
                class C : B(kind switch
                {
                0 => 1,
                _ => 2
                }) { }
                """,
                ("dress_arguments_layout", "always_multi"),
                ("csharp_new_line_before_open_brace", "all"),
                ("csharp_indent_block_contents", "true"),
                ("csharp_indent_braces", "false"))
            .ShouldBe("""
                class C : B(
                    kind switch
                    {
                        0 => 1,
                        _ => 2
                    }
                )
                { }
                """);
    }

    [Fact]
    public void Switch_label_setting_preserves_unrelated_source_indentation()
    {
        Format("""
                class C
                  {
                      void M(int x)
                        {
                          switch (x)
                            {
                 case 0:
                                  break;
                            }
                        }
                  }
                """,
                ("csharp_indent_switch_labels", "false"))
            .ShouldBe("""
                class C
                  {
                      void M(int x)
                        {
                          switch (x)
                            {
                            case 0:
                                  break;
                            }
                        }
                  }
                """);
    }

    [Fact]
    public void Indents_switch_labels()
    {
        var result = Format("""
            class C
            {
            void M(int x)
            {
            switch (x)
            {
                            case 0:
                            break;
            }
            }
            }
            """,
            ("csharp_indent_block_contents", "true"),
            ("csharp_indent_switch_labels", "true"));

        result.ShouldBe("""
            class C
            {
                void M(int x)
                {
                    switch (x)
                    {
                        case 0:
                        break;
                    }
                }
            }
            """);
        Format(result,
            ("csharp_indent_block_contents", "true"),
            ("csharp_indent_switch_labels", "true")).ShouldBe(result);
    }

    [Fact]
    public void Does_not_indent_switch_labels()
    {
        var result = Format("""
            class C
            {
            void M(int x)
            {
            switch (x)
            {
            case 0:
                            break;
            }
            }
            }
            """,
            ("csharp_indent_block_contents", "true"),
            ("csharp_indent_switch_labels", "false"),
            ("csharp_indent_case_contents", "true"));

        result.ShouldBe("""
            class C
            {
                void M(int x)
                {
                    switch (x)
                    {
                    case 0:
                        break;
                    }
                }
            }
            """);
        Format(result,
            ("csharp_indent_block_contents", "true"),
            ("csharp_indent_switch_labels", "false"),
            ("csharp_indent_case_contents", "true")).ShouldBe(result);
    }

    [Fact]
    public void Flushes_labels_left()
    {
        var result = Format("""
            class C
            {
            void M()
            {
                  retry:
            return;
            }
            }
            """,
            ("csharp_indent_block_contents", "true"),
            ("csharp_indent_labels", "flush_left"));

        result.ShouldBe("""
            class C
            {
                void M()
                {
            retry:
                    return;
                }
            }
            """);
        Format(result,
            ("csharp_indent_block_contents", "true"),
            ("csharp_indent_labels", "flush_left")).ShouldBe(result);
    }

    [Fact]
    public void Indents_labels_one_less_than_current()
    {
        var result = Format("""
            class C
            {
            void M()
            {
                  retry:
            return;
            }
            }
            """,
            ("csharp_indent_block_contents", "true"),
            ("csharp_indent_labels", "one_less_than_current"));

        result.ShouldBe("""
            class C
            {
                void M()
                {
                retry:
                    return;
                }
            }
            """);
        Format(result,
            ("csharp_indent_block_contents", "true"),
            ("csharp_indent_labels", "one_less_than_current")).ShouldBe(result);
    }

    [Fact]
    public void Leaves_labels_unchanged()
    {
        var result = Format("""
            class C
            {
            void M()
            {
                  retry:
            return;
            }
            }
            """,
            ("csharp_indent_block_contents", "true"),
            ("csharp_indent_labels", "no_change"));

        result.ShouldBe("""
            class C
            {
                void M()
                {
                  retry:
                    return;
                }
            }
            """);
        Format(result,
            ("csharp_indent_block_contents", "true"),
            ("csharp_indent_labels", "no_change")).ShouldBe(result);
    }

    [Fact]
    public void Indents_case_blocks()
    {
        var result = Format("""
            class C
            {
            void M(int x)
            {
            switch (x)
            {
            case 0:
            {
            return;
            }
            }
            }
            }
            """,
            ("csharp_indent_block_contents", "true"),
            ("csharp_indent_braces", "true"),
            ("csharp_indent_switch_labels", "false"),
            ("csharp_indent_case_contents", "false"),
            ("csharp_indent_case_contents_when_block", "true"));

        result.ShouldBe("""
            class C
                {
                    void M(int x)
                        {
                            switch (x)
                                {
                                case 0:
                                    {
                                        return;
                                    }
                                }
                        }
                }
            """);
    }

    [Fact]
    public void Does_not_indent_case_blocks()
    {
        var result = Format("""
            class C
            {
            void M(int x)
            {
            switch (x)
            {
            case 0:
            {
            return;
            }
            }
            }
            }
            """,
            ("csharp_indent_block_contents", "true"),
            ("csharp_indent_case_contents_when_block", "false"));

        result.ShouldBe("""
            class C
            {
                void M(int x)
                {
                    switch (x)
                    {
                        case 0:
                        {
                            return;
                        }
                    }
                }
            }
            """);
    }

    [Fact]
    public void Preserves_comments_and_directives_attached_to_indentation_targets()
    {
        var result = Format("""
            class C
            {
            void M(int x)
            {
            switch (x)
            {
                      // label
                      case 0:
                      // block
                      {
            return;
            }
            #if true
                      case 1:
                      break;
            #endif
            }
                  // goto label
                  retry:
            return;
            }
            }
            """,
            ("csharp_indent_block_contents", "true"),
            ("csharp_indent_switch_labels", "false"),
            ("csharp_indent_case_contents", "false"),
            ("csharp_indent_labels", "flush_left"),
            ("csharp_indent_case_contents_when_block", "false"));
        result.ShouldBe("""
            class C
            {
                void M(int x)
                {
                    switch (x)
                    {
                      // label
                      case 0:
                      // block
                      {
                        return;
                    }
            #if true
                      case 1:
                    break;
            #endif
            }
                  // goto label
                  retry:
                    return;
                }
            }
            """);
        Format(result,
            ("csharp_indent_block_contents", "true"),
            ("csharp_indent_switch_labels", "false"),
            ("csharp_indent_case_contents", "false"),
            ("csharp_indent_labels", "flush_left"),
            ("csharp_indent_case_contents_when_block", "false")).ShouldBe(result);
    }

    [Fact]
    public void Leading_block_comment_does_not_become_same_line_brace_indentation()
    {
        var result = Format("""
            class C
            {
                /* keep */ void M() {
            return;
            }
            }
            """,
            ("csharp_indent_block_contents", "true"));

        result.ShouldBe("""
            class C
            {
                /* keep */ void M() {
                    return;
            }
            }
            """);
        Format(result, ("csharp_indent_block_contents", "true")).ShouldBe(result);
    }

    [Fact]
    public void Skips_malformed_switch_sections_and_labeled_statements()
    {
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

        Format("""
            class C
            {
            void M(int x)
            {
            switch (x)
            {
                      case :
                      {
            return +;
            }
            }
                  retry:
            return +;
            }
            }
            """,
            oppositePreferences).ShouldBe(Format("""
            class C
            {
            void M(int x)
            {
            switch (x)
            {
                      case :
                      {
            return +;
            }
            }
                  retry:
            return +;
            }
            }
            """,
            firstPreferences));
    }

    [Theory]
    [InlineData("csharp_indent_switch_labels")]
    [InlineData("csharp_indent_case_contents")]
    [InlineData("csharp_indent_labels")]
    [InlineData("csharp_indent_case_contents_when_block")]
    public void Unset_indentation_preferences_add_no_rule_opinion(string key)
    {
        (string, string)[] baseline = [("csharp_indent_block_contents", "true")];

        Format("""
            class C
            {
            void M(int x)
            {
            switch (x)
            {
                  case 0:
                  {
            return;
            }
            }
               retry:
            return;
            }
            }
            """,
            ("csharp_indent_block_contents", "true"),
            (key, "unset")).ShouldBe(Format("""
            class C
            {
            void M(int x)
            {
            switch (x)
            {
                  case 0:
                  {
            return;
            }
            }
               retry:
            return;
            }
            }
            """,
            baseline));
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
        Format("""
            class C
              {
                  void M(int x)
                    {
                      switch (x)
                        {
             case 0:
                              break;
                        }
                    }
              }
            """,
            (key, "unset")).ShouldBe("""
            class C
              {
                  void M(int x)
                    {
                      switch (x)
                        {
             case 0:
                              break;
                        }
                    }
              }
            """);
    }
}
