using DressSharp.Configuration;
using Xunit;

namespace DressSharp.UnitTests;

public sealed class EditorConfigTests : IDisposable
{
    readonly string _directory = Path.Combine(Path.GetTempPath(), "DressSharp.Tests", Guid.NewGuid().ToString("N"));

    public EditorConfigTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public async Task Resolver_applies_traversal_sections_precedence_and_unset()
    {
        await File.WriteAllTextAsync(Path.Combine(_directory, ".editorconfig"), "root = true\n[*.cs]\nmax_line_length = 100\nindent_style = space\n", TestContext.Current.CancellationToken);
        var child = Directory.CreateDirectory(Path.Combine(_directory, "src")).FullName;
        await File.WriteAllTextAsync(Path.Combine(child, ".editorconfig"), "[*.cs]\nmax_line_length = 180\nindent_style = unset\n", TestContext.Current.CancellationToken);
        var source = Path.Combine(child, "Example.cs");

        var result = await new EditorConfigResolver().ResolveAsync(source, TestContext.Current.CancellationToken);

        Assert.Equal("180", result.Preferences["max_line_length"]);
        Assert.False(result.Preferences.ContainsKey("indent_style"));
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

        var result = await new EditorConfigResolver().ResolveAsync(
            Path.Combine(root, "Example.cs"), TestContext.Current.CancellationToken);

        Assert.False(result.Preferences.ContainsKey("max_line_length"));
    }

    [Fact]
    public async Task Resolver_ignores_shadowed_invalid_and_unknown_values()
    {
        await File.WriteAllTextAsync(Path.Combine(_directory, ".editorconfig"), "[*.cs]\nindent_style = invalid\nfuture_key = !anything!\nindent_style = tab\n", TestContext.Current.CancellationToken);

        var result = await new EditorConfigResolver().ResolveAsync(Path.Combine(_directory, "Example.cs"), TestContext.Current.CancellationToken);

        Assert.Equal("tab", result.Preferences["indent_style"]);
        Assert.Equal("tab", result.Preferences["indent_size"]);
    }

    [Fact]
    public async Task Resolver_uses_editorconfig_brace_and_range_globs()
    {
        await File.WriteAllTextAsync(
            Path.Combine(_directory, ".editorconfig"),
            "[{src,tests}/File{1..3}.cs]\nmax_line_length = 180\n",
            TestContext.Current.CancellationToken);

        var matching = await new EditorConfigResolver().ResolveAsync(
            Path.Combine(_directory, "src", "File2.cs"), TestContext.Current.CancellationToken);
        var excluded = await new EditorConfigResolver().ResolveAsync(
            Path.Combine(_directory, "src", "File4.cs"), TestContext.Current.CancellationToken);

        Assert.Equal("180", matching.Preferences["max_line_length"]);
        Assert.False(excluded.Preferences.ContainsKey("max_line_length"));
    }

    [Fact]
    public async Task Resolver_rejects_invalid_effective_value()
    {
        await File.WriteAllTextAsync(Path.Combine(_directory, ".editorconfig"), "[*.cs]\nindent_style = invalid\n", TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<ConfigurationException>(async () =>
            await new EditorConfigResolver().ResolveAsync(Path.Combine(_directory, "Example.cs"), TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("indent_size", "0")]
    [InlineData("max_line_length", "0")]
    [InlineData("dress_using_kind_order", "ordinary,ordinary,alias")]
    [InlineData("csharp_new_line_before_open_brace", "methods,unknown")]
    [InlineData("csharp_space_between_parentheses", "expressions,expressions")]
    [InlineData("csharp_preferred_modifier_order", "public,private")]
    public async Task Resolver_rejects_invalid_catalog_values(string key, string value)
    {
        await File.WriteAllTextAsync(
            Path.Combine(_directory, ".editorconfig"),
            $"[*.cs]\n{key} = {value}\n",
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<ConfigurationException>(async () =>
            await new EditorConfigResolver().ResolveAsync(
                Path.Combine(_directory, "Example.cs"), TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("max_line_length", "off")]
    [InlineData("dress_blank_lines_between_members", "0")]
    [InlineData("dress_using_kind_order", "alias,ordinary,static")]
    [InlineData("csharp_new_line_before_open_brace", "methods, properties")]
    [InlineData("csharp_space_between_parentheses", "expressions, type_casts")]
    public async Task Resolver_accepts_catalog_alternatives(string key, string value)
    {
        await File.WriteAllTextAsync(
            Path.Combine(_directory, ".editorconfig"),
            $"[*.cs]\n{key} = {value}\n",
            TestContext.Current.CancellationToken);

        var result = await new EditorConfigResolver().ResolveAsync(
            Path.Combine(_directory, "Example.cs"), TestContext.Current.CancellationToken);

        Assert.Equal(value, result.Preferences[key]);
    }

    [Fact]
    public async Task Resolver_rejects_malformed_configuration_in_its_chain()
    {
        await File.WriteAllTextAsync(
            Path.Combine(_directory, ".editorconfig"),
            "[*.cs]\nbad key = value\n",
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<ConfigurationException>(async () =>
            await new EditorConfigResolver().ResolveAsync(
                Path.Combine(_directory, "Example.cs"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Preflight_returns_nothing_when_any_file_is_invalid()
    {
        await File.WriteAllTextAsync(Path.Combine(_directory, ".editorconfig"), "[bad.cs]\nindent_style = invalid\n", TestContext.Current.CancellationToken);
        var paths = new[] { Path.Combine(_directory, "good.cs"), Path.Combine(_directory, "bad.cs") };

        await Assert.ThrowsAsync<ConfigurationException>(async () =>
            await ConfigurationPreflight.ResolveAllAsync(paths, new EditorConfigResolver(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Init_creates_default_file_without_root()
    {
        var result = await EditorConfigInitializer.InitializeAsync(null, false, _directory, TestContext.Current.CancellationToken);
        var text = await File.ReadAllTextAsync(result.Path, TestContext.Current.CancellationToken);

        Assert.True(result.Changed);
        Assert.Equal(EditorConfigInitializer.BuildManagedBlock(), text);
        Assert.DoesNotContain("root = true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("dress_conditional_braces = balanced\n", text);
        Assert.DoesNotContain("dress_control_flow_braces", text);
        foreach (var pair in PreferenceCatalog.Familiar)
        {
            Assert.Contains($"{pair.Key} = {pair.Value}\n", text);
        }
    }

    [Fact]
    public async Task Familiar_block_matches_the_canonical_snapshot()
    {
        var expected = await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "Familiar.editorconfig"),
            TestContext.Current.CancellationToken);

        Assert.Equal(expected, EditorConfigInitializer.BuildManagedBlock());
    }

    [Fact]
    public async Task Init_replaces_one_block_at_eof_and_avoids_identical_rewrite()
    {
        var path = Path.Combine(_directory, ".editorconfig");
        await File.WriteAllTextAsync(path, "root = true\n\n# DressSharp Begin\n[*.cs]\nindent_size = 2\n# DressSharp End\n\n", TestContext.Current.CancellationToken);
        var first = await EditorConfigInitializer.InitializeAsync(path, false, _directory, TestContext.Current.CancellationToken);
        var timestamp = File.GetLastWriteTimeUtc(path);
        var second = await EditorConfigInitializer.InitializeAsync(path, false, _directory, TestContext.Current.CancellationToken);

        Assert.True(first.Changed);
        Assert.False(second.Changed);
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(path));
        Assert.Equal(1, (await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken)).Split(EditorConfigInitializer.BeginMarker).Length - 1);
    }

    [Fact]
    public async Task Init_conflicts_fail_unless_forced_and_force_preserves_text()
    {
        var path = Path.Combine(_directory, ".editorconfig");
        const string userText = "# mine\n[generated/*.cs]\nindent_size = 2\n";
        await File.WriteAllTextAsync(path, userText, TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<ConfigurationException>(() => EditorConfigInitializer.InitializeAsync(path, false, _directory, TestContext.Current.CancellationToken));
        Assert.Equal(userText, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));

        var result = await EditorConfigInitializer.InitializeAsync(path, true, _directory, TestContext.Current.CancellationToken);
        Assert.Single(result.Warnings);
        Assert.StartsWith(userText.TrimEnd(), await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Init_reports_original_conflict_lines_after_managed_block()
    {
        var path = Path.Combine(_directory, ".editorconfig");
        var text = $"root = true\n{EditorConfigInitializer.BeginMarker}\n[*.cs]\nindent_size = 4\n{EditorConfigInitializer.EndMarker}\n[generated.cs]\nindent_size = 2\n";
        await File.WriteAllTextAsync(path, text, TestContext.Current.CancellationToken);

        var exception = await Assert.ThrowsAsync<ConfigurationException>(() =>
            EditorConfigInitializer.InitializeAsync(path, false, _directory, TestContext.Current.CancellationToken));

        Assert.Contains($"{path}(7): indent_size", exception.Message);
        Assert.Equal(text, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Force_never_bypasses_bad_markers()
    {
        var path = Path.Combine(_directory, ".editorconfig");
        await File.WriteAllTextAsync(path, $"{EditorConfigInitializer.BeginMarker}\n{EditorConfigInitializer.BeginMarker}\n{EditorConfigInitializer.EndMarker}\n", TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<ConfigurationException>(() => EditorConfigInitializer.InitializeAsync(path, true, _directory, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("# DressSharp Begin\n")]
    [InlineData("# DressSharp End\n")]
    [InlineData("# DressSharp End\n# DressSharp Begin\n")]
    [InlineData("# DressSharp Begin\n# DressSharp End\n# DressSharp End\n")]
    public async Task Init_rejects_every_malformed_marker_shape(string text)
    {
        var path = Path.Combine(_directory, ".editorconfig");
        await File.WriteAllTextAsync(path, text, TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<ConfigurationException>(() =>
            EditorConfigInitializer.InitializeAsync(path, true, _directory, TestContext.Current.CancellationToken));

        Assert.Equal(text, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("[*.cs\nindent_size = 4\n")]
    [InlineData("[*.cs]\nmissing value\n")]
    [InlineData("[*.cs]\nbad key = value\n")]
    [InlineData("root = perhaps\n")]
    public async Task Init_rejects_malformed_configuration_even_when_forced(string text)
    {
        var path = Path.Combine(_directory, ".editorconfig");
        await File.WriteAllTextAsync(path, text, TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<ConfigurationException>(() =>
            EditorConfigInitializer.InitializeAsync(path, true, _directory, TestContext.Current.CancellationToken));

        Assert.Equal(text, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Nonexistent_trailing_separator_target_is_a_directory()
    {
        var target = Path.Combine(_directory, "nested") + Path.DirectorySeparatorChar;
        var result = await EditorConfigInitializer.InitializeAsync(target, false, _directory, TestContext.Current.CancellationToken);
        Assert.Equal(Path.Combine(_directory, "nested", ".editorconfig"), result.Path);
        Assert.True(File.Exists(result.Path));
    }

    [Fact]
    public async Task Existing_directory_target_gets_editorconfig()
    {
        var target = Directory.CreateDirectory(Path.Combine(_directory, "existing")).FullName;
        var result = await EditorConfigInitializer.InitializeAsync(target, false, _directory, TestContext.Current.CancellationToken);

        Assert.Equal(Path.Combine(target, ".editorconfig"), result.Path);
    }

    [Fact]
    public async Task Nonexistent_file_target_creates_parents()
    {
        var target = Path.Combine(_directory, "nested", "custom.editorconfig");
        var result = await EditorConfigInitializer.InitializeAsync(target, false, _directory, TestContext.Current.CancellationToken);

        Assert.Equal(target, result.Path);
        Assert.True(File.Exists(target));
    }

    public void Dispose() => Directory.Delete(_directory, true);
}
