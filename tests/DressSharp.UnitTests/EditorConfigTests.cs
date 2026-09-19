using DressSharp.Architecture;
using DressSharp.Configuration;
using Xunit;
using EasyAssertions;

namespace DressSharp.UnitTests;

public sealed class EditorConfigTests : IDisposable
{
    readonly string _directory = Path.Combine(Path.GetTempPath(), "DressSharp.Tests", Guid.NewGuid().ToString("N"));

    public EditorConfigTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public async Task Resolver_applies_traversal_sections_precedence_and_unset()
    {
        await File.WriteAllTextAsync(
            Path.Combine(_directory, ".editorconfig"),
            "root = true\n[*.cs]\nmax_line_length = 100\nindent_style = space\n",
            TestContext.Current.CancellationToken);
        var child = Directory.CreateDirectory(Path.Combine(_directory, "src")).FullName;
        await File.WriteAllTextAsync(
            Path.Combine(child, ".editorconfig"),
            "[*.cs]\nmax_line_length = 180\nindent_style = unset\n",
            TestContext.Current.CancellationToken);
        var source = Path.Combine(child, "Example.cs");

        var result = Resolve(source);

        result.Preferences[RuleKey.MaxLineLength].ShouldBe("180");
        result.Preferences.ContainsKey(RuleKey.IndentStyle).ShouldBe(false);
    }

    [Fact]
    public async Task Resolver_stops_at_root()
    {
        await File.WriteAllTextAsync(
            Path.Combine(_directory, ".editorconfig"),
            "[*.cs]\nmax_line_length = 90\n",
            TestContext.Current.CancellationToken);
        var root = Directory.CreateDirectory(Path.Combine(_directory, "root")).FullName;
        await File.WriteAllTextAsync(
            Path.Combine(root, ".editorconfig"),
            "root = true\n[*.cs]\nindent_style = space\n",
            TestContext.Current.CancellationToken);

        var result = Resolve(Path.Combine(root, "Example.cs"));

        result.Preferences.ContainsKey(RuleKey.MaxLineLength).ShouldBe(false);
    }

    [Fact]
    public async Task Resolver_ignores_shadowed_invalid_and_unknown_values()
    {
        await File.WriteAllTextAsync(
            Path.Combine(_directory, ".editorconfig"),
            "[*.cs]\nindent_style = invalid\nfuture_key = !anything!\nindent_style = tab\n",
            TestContext.Current.CancellationToken);

        var result = Resolve(Path.Combine(_directory, "Example.cs"));

        result.Preferences[RuleKey.IndentStyle].ShouldBe("tab");
        result.Preferences[RuleKey.IndentSize].ShouldBe("tab");
    }

    [Fact]
    public async Task Resolver_uses_editorconfig_brace_and_range_globs()
    {
        await File.WriteAllTextAsync(
            Path.Combine(_directory, ".editorconfig"),
            "[{src,tests}/File{1..3}.cs]\nmax_line_length = 180\n",
            TestContext.Current.CancellationToken);

        var matching = Resolve(Path.Combine(_directory, "src", "File2.cs"));
        var excluded = Resolve(Path.Combine(_directory, "src", "File4.cs"));

        matching.Preferences[RuleKey.MaxLineLength].ShouldBe("180");
        excluded.Preferences.ContainsKey(RuleKey.MaxLineLength).ShouldBe(false);
    }

    [Fact]
    public async Task Batch_reuses_only_identical_effective_configurations()
    {
        await File.WriteAllTextAsync(
            Path.Combine(_directory, ".editorconfig"),
            "root = true\n[*.cs]\nindent_style = space\n[Generated*.cs]\nindent_style = tab\n",
            TestContext.Current.CancellationToken);
        var resolver = new EditorConfigResolver();

        var result = resolver.ResolveAll(
            [
                Path.Combine(_directory, "First.cs"),
                Path.Combine(_directory, "Second.cs"),
                Path.Combine(_directory, "GeneratedFirst.cs")
            ],
            TestContext.Current.CancellationToken);

        result[Path.Combine(_directory, "Second.cs")].ShouldReferTo(result[Path.Combine(_directory, "First.cs")]);
        result[Path.Combine(_directory, "GeneratedFirst.cs")].ShouldNotReferTo(result[Path.Combine(_directory, "First.cs")]);
    }

    [Fact]
    public async Task Resolver_rejects_invalid_effective_value()
    {
        await File.WriteAllTextAsync(Path.Combine(_directory, ".editorconfig"), "[*.cs]\nindent_style = invalid\n", TestContext.Current.CancellationToken);
        Should.Throw<ConfigurationException>(() =>
            Resolve(Path.Combine(_directory, "Example.cs")));
    }

    [Theory]
    [InlineData("indent_size", "0")]
    [InlineData("max_line_length", "0")]
    [InlineData("dress_using_kind_order", "ordinary,ordinary,alias")]
    [InlineData("csharp_new_line_before_open_brace", "methods,unknown")]
    [InlineData("csharp_space_between_parentheses", "expressions,expressions")]
    [InlineData("csharp_preferred_modifier_order", "public,private")]
    [InlineData("csharp_space_around_declaration_statements", "true")]
    [InlineData("dress_embedded_statement_placement", "separate_line")]
    [InlineData("dress_embedded_statement_braces", "never")]
    [InlineData("dress_braces_for_multiline_statement_header", "sometimes")]
    public async Task Resolver_rejects_invalid_catalog_values(string key, string value)
    {
        await File.WriteAllTextAsync(
            Path.Combine(_directory, ".editorconfig"),
            $"[*.cs]\n{key} = {value}\n",
            TestContext.Current.CancellationToken);

        Should.Throw<ConfigurationException>(() =>
            Resolve(Path.Combine(_directory, "Example.cs")));
    }

    [Theory]
    [InlineData("max_line_length", "off")]
    [InlineData("dress_blank_lines_between_members", "0")]
    [InlineData("dress_using_kind_order", "alias,ordinary,static")]
    [InlineData("csharp_new_line_before_open_brace", "methods, properties")]
    [InlineData("csharp_space_between_parentheses", "expressions, type_casts")]
    [InlineData("csharp_space_around_declaration_statements", "ignore")]
    [InlineData("dress_embedded_statement_placement", "same_line")]
    [InlineData("dress_embedded_statement_braces", "compact")]
    [InlineData("dress_braces_for_multiline_statement_header", "false")]
    public async Task Resolver_accepts_catalog_alternatives(string key, string value)
    {
        await File.WriteAllTextAsync(
            Path.Combine(_directory, ".editorconfig"),
            $"[*.cs]\n{key} = {value}\n",
            TestContext.Current.CancellationToken);

        var result = Resolve(Path.Combine(_directory, "Example.cs"));

        result.Preferences[RuleKeys.Parse(key)].ShouldBe(value);
    }

    [Fact]
    public async Task Resolver_rejects_malformed_configuration_in_its_chain()
    {
        await File.WriteAllTextAsync(
            Path.Combine(_directory, ".editorconfig"),
            "[*.cs]\nbad key: value\n",
            TestContext.Current.CancellationToken);

        Should.Throw<ConfigurationException>(() =>
            Resolve(Path.Combine(_directory, "Example.cs")));
    }

    [Fact]
    public async Task Batch_resolution_fails_when_any_file_is_invalid()
    {
        await File.WriteAllTextAsync(Path.Combine(_directory, ".editorconfig"), "[bad.cs]\nindent_style = invalid\n", TestContext.Current.CancellationToken);
        var paths = new[] { Path.Combine(_directory, "good.cs"), Path.Combine(_directory, "bad.cs") };

        Should.Throw<ConfigurationException>(() =>
            new EditorConfigResolver().ResolveAll(paths, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Init_creates_default_file_without_root()
    {
        var result = await EditorConfigInitializer.InitializeAsync(null, _directory, TestContext.Current.CancellationToken);
        var text = await File.ReadAllTextAsync(result.Path, TestContext.Current.CancellationToken);

        result.Changed.ShouldBe(true);
        text.ShouldBe(EditorConfigInitializer.BuildDefaultSection());
        text.Contains("root = true", StringComparison.OrdinalIgnoreCase).ShouldBe(false);
        text.ShouldContain("dress_embedded_statement_placement = next_line\n");
        text.ShouldContain("dress_embedded_statement_braces = balanced\n");
        text.ShouldContain("dress_braces_for_multiline_statement_header = true\n");
        text.ShouldNotContain("dress_control_flow_braces");
        foreach (var (key, value) in PreferenceCatalog.Defaults)
            text.ShouldContain($"{key.ToName()} = {value}\n");
    }

    [Fact]
    public async Task Default_preferences_match_the_canonical_snapshot()
    {
        var expected = await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "Default.editorconfig"),
            TestContext.Current.CancellationToken);

        EditorConfigInitializer.BuildDefaultSection().ShouldBe(expected);
    }

    [Fact]
    public async Task Init_appends_missing_preferences_to_an_existing_csharp_section()
    {
        var path = Path.Combine(_directory, ".editorconfig");
        const string original = "root = true\n\n[*.cs]\nindent_size = 2\n\n[generated.cs]\ngenerated_code = true\n";
        await File.WriteAllTextAsync(path, original, TestContext.Current.CancellationToken);

        var first = await EditorConfigInitializer.InitializeAsync(path, _directory, TestContext.Current.CancellationToken);
        var text = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
        var second = await EditorConfigInitializer.InitializeAsync(path, _directory, TestContext.Current.CancellationToken);

        first.Changed.ShouldBe(true);
        second.Changed.ShouldBe(false);
        text.ShouldStartWith("root = true\n\n[*.cs]\nindent_size = 2\ncharset = utf-8\n");
        text.ShouldContain("dress_multiline_parameter_list_open_brace_position = next_line\n\n[generated.cs]");
        (text.Split("indent_size =", StringSplitOptions.None).Length - 1).ShouldBe(1);
    }

    [Fact]
    public async Task Init_does_not_repeat_preferences_assigned_in_other_sections()
    {
        var path = Path.Combine(_directory, ".editorconfig");
        const string original = "[generated/*.cs]\nindent_size = 2\n\n[*.cs]\ncharset = latin1\n";
        await File.WriteAllTextAsync(path, original, TestContext.Current.CancellationToken);

        await EditorConfigInitializer.InitializeAsync(path, _directory, TestContext.Current.CancellationToken);

        var text = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
        (text.Split("indent_size =", StringSplitOptions.None).Length - 1).ShouldBe(1);
        (text.Split("charset =", StringSplitOptions.None).Length - 1).ShouldBe(1);
        text.ShouldEndWith("dress_multiline_parameter_list_open_brace_position = next_line\n");
    }

    [Fact]
    public async Task Init_adds_a_csharp_section_at_the_bottom_when_missing()
    {
        var path = Path.Combine(_directory, ".editorconfig");
        const string original = "root = true\n\n[generated.cs]\nindent_size = 2\n";
        await File.WriteAllTextAsync(path, original, TestContext.Current.CancellationToken);

        await EditorConfigInitializer.InitializeAsync(path, _directory, TestContext.Current.CancellationToken);

        var text = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
        text.ShouldStartWith(original);
        text.ShouldContain("\n[*.cs]\ncharset = utf-8\n");
        (text.Split("indent_size =", StringSplitOptions.None).Length - 1).ShouldBe(1);
    }

    [Theory]
    [InlineData("[*.cs\nindent_size = 4\n")]
    [InlineData("[*.cs]\nmissing value\n")]
    [InlineData("[*.cs]\nbad key: value\n")]
    [InlineData("root = perhaps\n")]
    public async Task Init_rejects_malformed_configuration_even_when_forced(string text)
    {
        var path = Path.Combine(_directory, ".editorconfig");
        await File.WriteAllTextAsync(path, text, TestContext.Current.CancellationToken);

        EditorConfigInitializer.InitializeAsync(path, _directory, TestContext.Current.CancellationToken).ShouldFailWith<ConfigurationException>();

        (await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken)).ShouldBe(text);
    }

    [Fact]
    public async Task Nonexistent_trailing_separator_target_is_a_directory()
    {
        var target = Path.Combine(_directory, "nested") + Path.DirectorySeparatorChar;
        var result = await EditorConfigInitializer.InitializeAsync(target, _directory, TestContext.Current.CancellationToken);
        result.Path.ShouldBe(Path.Combine(_directory, "nested", ".editorconfig"));
        File.Exists(result.Path).ShouldBe(true);
    }

    [Fact]
    public async Task Existing_directory_target_gets_editorconfig()
    {
        var target = Directory.CreateDirectory(Path.Combine(_directory, "existing")).FullName;
        var result = await EditorConfigInitializer.InitializeAsync(target, _directory, TestContext.Current.CancellationToken);

        result.Path.ShouldBe(Path.Combine(target, ".editorconfig"));
    }

    [Fact]
    public async Task Nonexistent_file_target_creates_parents()
    {
        var target = Path.Combine(_directory, "nested", "custom.editorconfig");
        var result = await EditorConfigInitializer.InitializeAsync(target, _directory, TestContext.Current.CancellationToken);

        result.Path.ShouldBe(target);
        File.Exists(target).ShouldBe(true);
    }

    static FormattingConfiguration Resolve(string path) =>
        new EditorConfigResolver().ResolveAll([path], TestContext.Current.CancellationToken)[path];

    public void Dispose() => Directory.Delete(_directory, true);
}
