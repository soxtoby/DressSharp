using System.Text;
using DressSharp.Architecture;
using DressSharp.Configuration;
using EasyAssertions;
using Xunit;

namespace DressSharp.UnitTests;

public sealed class InteractiveEditorConfigTests : IDisposable
{
    readonly string _directory = Path.Combine(Path.GetTempPath(), "DressSharp.Tests", Guid.NewGuid().ToString("N"));

    public InteractiveEditorConfigTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public async Task Load_exposes_local_inherited_effective_and_derived_values()
    {
        await Write(
            Path.Combine(_directory, ".editorconfig"),
            "root = true\n[*.cs]\nindent_style = tab\nindent_size = unset\nmax_line_length = 90\n[*.CS]\nmax_line_length = 40\n");
        var child = Directory.CreateDirectory(Path.Combine(_directory, "src")).FullName;
        var target = Path.Combine(child, ".editorconfig");
        await Write(
            target,
            "[*.cs]\nmax_line_length = 100\n[generated/*.cs]\nmax_line_length = 30\n[*.cs]\nmax_line_length = 120\n");

        var loaded = await InteractiveEditorConfig.LoadAsync(null, child, Token);

        loaded.TargetPath.ShouldBe(target);
        loaded.InteractiveRoot.ShouldBe(child);
        loaded.Revision.Length.ShouldBe(64);
        Preference(loaded, RuleKey.MaxLineLength).Local.ShouldBe(PreferenceAssignment.Explicit("120"));
        Preference(loaded, RuleKey.MaxLineLength).Inherited.ShouldBe(PreferenceAssignment.Explicit("90"));
        Preference(loaded, RuleKey.MaxLineLength).EffectiveValue.ShouldBe("120");
        Preference(loaded, RuleKey.MaxLineLength).EffectiveSourcePath.ShouldBe(target);
        Preference(loaded, RuleKey.IndentSize).Local.ShouldBe(PreferenceAssignment.Absent);
        Preference(loaded, RuleKey.IndentSize).Inherited.ShouldBe(PreferenceAssignment.Unset);
        Preference(loaded, RuleKey.IndentSize).InheritedSourcePath.ShouldBe(Path.Combine(_directory, ".editorconfig"));
        Preference(loaded, RuleKey.IndentSize).EffectiveValue.ShouldBe("tab");
        Preference(loaded, RuleKey.IndentStyle).EffectiveValue.ShouldBe("tab");
    }

    [Fact]
    public async Task Load_derives_indentation_values_without_changing_assignment_states()
    {
        var path = Path.Combine(_directory, ".editorconfig");
        await Write(path, "root = true\n[*.cs]\nindent_style = tab\n");

        var loaded = await InteractiveEditorConfig.LoadAsync(path, _directory, Token);

        Preference(loaded, RuleKey.IndentSize).Local.ShouldBe(PreferenceAssignment.Absent);
        Preference(loaded, RuleKey.IndentSize).EffectiveValue.ShouldBe("tab");
    }

    [Fact]
    public async Task Merge_reloads_external_edits_and_changes_only_requested_rules()
    {
        var path = Path.Combine(_directory, ".editorconfig");
        var original = "root = true\r\n[*.cs]\nINDENT_STYLE   =   space   \r\nindent_size = 2\n[other.cs]\r\nfuture = untouched\n[*.cs]\r\nindent_size = 3";
        await File.WriteAllBytesAsync(
            path,
            [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(original)],
            Token);
        var loaded = await InteractiveEditorConfig.LoadAsync(path, _directory, Token);
        var external = original.Replace("root = true", "# external\r\nroot = true", StringComparison.Ordinal);
        await File.WriteAllBytesAsync(
            path,
            [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(external)],
            Token);

        var merged = await InteractiveEditorConfig.MergeAsync(
            loaded,
            [
                new(RuleKey.MaxLineLength, PreferenceAssignment.Explicit("100")),
                new(RuleKey.IndentSize, PreferenceAssignment.Absent),
                new(RuleKey.Charset, PreferenceAssignment.Unset),
                new(RuleKey.IndentStyle, PreferenceAssignment.Explicit("TAB"))
            ],
            Token);

        var bytes = await File.ReadAllBytesAsync(path, Token);
        bytes.AsSpan(0, 3).SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF }).ShouldBe(true);
        var text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        text.ShouldContain("# external\r\nroot = true");
        text.ShouldContain("INDENT_STYLE   =   tab   \r\n");
        text.ShouldNotContain("indent_size =");
        text.ShouldContain("[other.cs]\r\nfuture = untouched\n");
        text.ShouldContain("[*.cs]\r\ncharset = unset\r\nmax_line_length = 100");
        text.EndsWith('\r').ShouldBe(false);
        text.EndsWith('\n').ShouldBe(false);
        Preference(merged, RuleKey.Charset).Local.ShouldBe(PreferenceAssignment.Unset);
        Preference(merged, RuleKey.MaxLineLength).EffectiveValue.ShouldBe("100");
    }

    [Fact]
    public async Task Merge_appends_missing_exact_section_without_adding_final_newline()
    {
        var path = Path.Combine(_directory, ".editorconfig");
        await Write(path, "root = true\n[generated.cs]\nindent_size = 2");
        var loaded = await InteractiveEditorConfig.LoadAsync(path, _directory, Token);

        await InteractiveEditorConfig.MergeAsync(
            loaded,
            [new(RuleKey.IndentStyle, PreferenceAssignment.Explicit("space"))],
            Token);

        (await File.ReadAllTextAsync(path, Token)).ShouldBe(
            "root = true\n[generated.cs]\nindent_size = 2\n\n[*.cs]\nindent_style = space");
    }

    [Fact]
    public async Task Merge_removal_preserves_missing_final_newline()
    {
        var path = Path.Combine(_directory, ".editorconfig");
        await Write(path, "root = true\n[*.cs]\nindent_style = space");
        var loaded = await InteractiveEditorConfig.LoadAsync(path, _directory, Token);

        await InteractiveEditorConfig.MergeAsync(
            loaded,
            [new(RuleKey.IndentStyle, PreferenceAssignment.Absent)],
            Token);

        (await File.ReadAllTextAsync(path, Token)).ShouldBe("root = true\n[*.cs]");
    }

    [Fact]
    public async Task Empty_merge_does_not_touch_or_reread_the_target()
    {
        var path = Path.Combine(_directory, ".editorconfig");
        await Write(path, "root = true\n[*.cs]\nindent_style = space\n");
        var loaded = await InteractiveEditorConfig.LoadAsync(path, _directory, Token);
        File.Delete(path);

        var result = await InteractiveEditorConfig.MergeAsync(loaded, [], Token);

        result.ShouldReferTo(loaded);
    }

    [Theory]
    [InlineData("[*.cs]\nindent_style: space\n")]
    [InlineData("[*.cs] # comment\nindent_style = space\n")]
    [InlineData("[*.cs]\rindent_style = space\r")]
    public async Task Load_rejects_nonconforming_syntax(string text)
    {
        var path = Path.Combine(_directory, ".editorconfig");
        await Write(path, text);

        InteractiveEditorConfig.LoadAsync(path, _directory, Token).AsTask().ShouldFailWith<ConfigurationException>();
    }

    [Fact]
    public async Task Load_rejects_utf16()
    {
        var path = Path.Combine(_directory, ".editorconfig");
        await File.WriteAllTextAsync(path, "[*.cs]\nindent_style = space\n", Encoding.Unicode, Token);

        InteractiveEditorConfig.LoadAsync(path, _directory, Token).AsTask().ShouldFailWith<ConfigurationException>();
    }

    [Fact]
    public async Task Load_rejects_malformed_inherited_file_even_when_target_masks_it()
    {
        await Write(Path.Combine(_directory, ".editorconfig"), "[other.cs] trailing\nfuture = value\n");
        var child = Directory.CreateDirectory(Path.Combine(_directory, "src")).FullName;
        var path = Path.Combine(child, ".editorconfig");
        await Write(path, "[*.cs]\nindent_style = space\n");

        InteractiveEditorConfig.LoadAsync(path, child, Token).AsTask().ShouldFailWith<ConfigurationException>();
    }

    [Fact]
    public async Task Merge_rejects_duplicate_or_invalid_edits_before_reading()
    {
        var path = Path.Combine(_directory, ".editorconfig");
        await Write(path, "root = true\n[*.cs]\nindent_style = space\n");
        var loaded = await InteractiveEditorConfig.LoadAsync(path, _directory, Token);
        File.Delete(path);

        InteractiveEditorConfig.MergeAsync(
            loaded,
            [
                new(RuleKey.IndentStyle, PreferenceAssignment.Explicit("space")),
                new(RuleKey.IndentStyle, PreferenceAssignment.Explicit("tab"))
            ],
            Token).AsTask().ShouldFailWith<ArgumentException>();
        InteractiveEditorConfig.MergeAsync(
            loaded,
            [new(RuleKey.IndentStyle, PreferenceAssignment.Explicit("invalid"))],
            Token).AsTask().ShouldFailWith<ArgumentException>();
    }

    [Fact]
    public void Load_requires_an_existing_target()
    {
        InteractiveEditorConfig.LoadAsync(null, _directory, Token).AsTask().ShouldFailWith<ConfigurationException>();
    }

    static InteractivePreference Preference(InteractiveEditorConfigData data, RuleKey key) =>
        data.Preferences.Single(preference => preference.RuleKey == key);

    static CancellationToken Token => TestContext.Current.CancellationToken;

    static Task Write(string path, string text) => File.WriteAllTextAsync(path, text, new UTF8Encoding(false), Token);

    public void Dispose() => Directory.Delete(_directory, true);
}
