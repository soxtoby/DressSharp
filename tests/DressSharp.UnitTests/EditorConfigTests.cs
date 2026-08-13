using DressSharp.Configuration;
using Xunit;

namespace DressSharp.UnitTests;

public sealed class EditorConfigTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "DressSharp.Tests", Guid.NewGuid().ToString("N"));

    public EditorConfigTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public async Task Resolver_applies_traversal_sections_precedence_and_unset()
    {
        await File.WriteAllTextAsync(Path.Combine(_directory, ".editorconfig"), "root = true\n[*.cs]\ndress_max_line_length = 100\nindent_style = space\n", TestContext.Current.CancellationToken);
        var child = Directory.CreateDirectory(Path.Combine(_directory, "src")).FullName;
        await File.WriteAllTextAsync(Path.Combine(child, ".editorconfig"), "[*.cs]\ndress_max_line_length = 180\nindent_style = unset\n", TestContext.Current.CancellationToken);
        var source = Path.Combine(child, "Example.cs");

        var result = await new EditorConfigResolver().ResolveAsync(source, TestContext.Current.CancellationToken);

        Assert.Equal("180", result.Preferences["dress_max_line_length"]);
        Assert.False(result.Preferences.ContainsKey("indent_style"));
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
            "[{src,tests}/File{1..3}.cs]\ndress_max_line_length = 180\n",
            TestContext.Current.CancellationToken);

        var matching = await new EditorConfigResolver().ResolveAsync(
            Path.Combine(_directory, "src", "File2.cs"), TestContext.Current.CancellationToken);
        var excluded = await new EditorConfigResolver().ResolveAsync(
            Path.Combine(_directory, "src", "File4.cs"), TestContext.Current.CancellationToken);

        Assert.Equal("180", matching.Preferences["dress_max_line_length"]);
        Assert.False(excluded.Preferences.ContainsKey("dress_max_line_length"));
    }

    [Fact]
    public async Task Resolver_rejects_invalid_effective_value()
    {
        await File.WriteAllTextAsync(Path.Combine(_directory, ".editorconfig"), "[*.cs]\nindent_style = invalid\n", TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<ConfigurationException>(async () =>
            await new EditorConfigResolver().ResolveAsync(Path.Combine(_directory, "Example.cs"), TestContext.Current.CancellationToken));
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
        Assert.StartsWith("# DressSharp Begin\n[*.cs]\n", text);
        Assert.EndsWith("# DressSharp End\n", text);
        Assert.DoesNotContain("root = true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("dress_conditional_braces = balanced\n", text);
        Assert.DoesNotContain("dress_control_flow_braces", text);
        foreach (var pair in PreferenceCatalog.Familiar)
        {
            Assert.Contains($"{pair.Key} = {pair.Value}\n", text);
        }
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
    public async Task Force_never_bypasses_bad_markers()
    {
        var path = Path.Combine(_directory, ".editorconfig");
        await File.WriteAllTextAsync(path, $"{EditorConfigInitializer.BeginMarker}\n{EditorConfigInitializer.BeginMarker}\n{EditorConfigInitializer.EndMarker}\n", TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<ConfigurationException>(() => EditorConfigInitializer.InitializeAsync(path, true, _directory, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Nonexistent_trailing_separator_target_is_a_directory()
    {
        var target = Path.Combine(_directory, "nested") + Path.DirectorySeparatorChar;
        var result = await EditorConfigInitializer.InitializeAsync(target, false, _directory, TestContext.Current.CancellationToken);
        Assert.Equal(Path.Combine(_directory, "nested", ".editorconfig"), result.Path);
        Assert.True(File.Exists(result.Path));
    }

    public void Dispose() => Directory.Delete(_directory, true);
}
