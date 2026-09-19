using DressSharp.Parsing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using EasyAssertions;

namespace DressSharp.UnitTests;

public sealed class ParseContextResolverTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), $"DressSharp-{Guid.NewGuid():N}");

    [Fact]
    public async Task Lowest_target_context_wins_and_configuration_is_forwarded()
    {
        var file = File("Program.cs");
        var project = File("App.csproj");
        var evaluator = new FakeEvaluator
            {
                [project, null] = Success(("TargetFrameworks", "net9.0;net8.0")),
                [project, "net9.0"] = Success(file, ("TargetFramework", "net9.0"), ("LangVersion", "13"), ("DefineConstants", "NET9;DEBUG")),
                [project, "net8.0"] = Success(file, ("TargetFramework", "net8.0"), ("LangVersion", "12"), ("DefineConstants", "NET8;DEBUG")),
            };

        var result = await Resolve(evaluator, file, "Release");

        result.Options!.LanguageVersion.ShouldBe(LanguageVersion.CSharp12);
        result.Options.PreprocessorSymbolNames.ShouldContain("NET8");
        evaluator.Configurations.AllItemsSatisfy(value => value.ShouldBe("Release"));
    }

    [Fact]
    public async Task Differing_shared_file_contexts_are_skipped()
    {
        var file = File("Shared.cs");
        var first = File("First.csproj");
        var second = File("Second.csproj");
        var evaluator = new FakeEvaluator
            {
                [first, null] = Success(file, ("TargetFramework", "net10.0"), ("LangVersion", "latest"), ("DefineConstants", "FIRST")),
                [second, null] = Success(file, ("TargetFramework", "net10.0"), ("LangVersion", "latest"), ("DefineConstants", "SECOND")),
            };

        var result = await Resolve(evaluator, file);

        result.CanFormat.ShouldBe(false);
        result.Diagnostics.Single().ShouldContain("differing parse contexts");
    }

    [Fact]
    public async Task Project_evaluations_run_concurrently()
    {
        var file = File("Program.cs");
        File("First.csproj");
        File("Second.csproj");
        var evaluator = new ConcurrentEvaluator(file, 2);

        await new ParseContextResolver(root, evaluator).Resolve([file], null, TestContext.Current.CancellationToken);

        evaluator.MaximumConcurrency.ShouldBe(2);
    }

    [Fact]
    public async Task Failed_owned_project_is_skipped()
    {
        var file = File("Program.cs");
        var project = File("App.csproj");
        var evaluator = new FakeEvaluator { [project, null] = Failure("broken import") };

        var result = await Resolve(evaluator, file);

        result.CanFormat.ShouldBe(false);
        result.Diagnostics.Single().ShouldContain("broken import");
    }

    [Fact]
    public async Task Failed_file_app_uses_reported_latest_stable_fallback()
    {
        var file = File("Program.cs");
        var evaluator = new FakeEvaluator { [file, null] = Failure("SDK unavailable") };

        var result = await Resolve(evaluator, file);

        result.CanFormat.ShouldBe(true);
        result.Options!.LanguageVersion.ShouldBe(LanguageVersion.Latest.MapSpecifiedToEffectiveVersion());
        result.Options.DocumentationMode.ShouldBe(DocumentationMode.Parse);
        result.Options.PreprocessorSymbolNames.ShouldBeEmpty();
        result.Diagnostics.Single().ShouldContain("latest-stable fallback");
    }

    [Fact]
    public async Task Unsupported_owned_language_version_skips_file()
    {
        var file = File("Program.cs");
        var project = File("App.csproj");
        var evaluator = new FakeEvaluator
            {
                [project, null] = Success(file, ("TargetFramework", "net10.0"), ("LangVersion", "999")),
            };

        var result = await Resolve(evaluator, file);

        result.CanFormat.ShouldBe(false);
        result.Diagnostics.Single().ShouldContain("Language version");
    }

    [Fact]
    public async Task Dotnet_evaluation_includes_generated_framework_symbols()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory.GetFiles("DressSharp.slnx").Length == 0)
            directory = directory.Parent!;
        var projectRoot = Path.Combine(directory.FullName, "src", "DressSharp");
        var file = Path.Combine(projectRoot, "Program.cs");

        var results = await new ParseContextResolver(projectRoot).Resolve([file], null, TestContext.Current.CancellationToken);

        results[file].CanFormat.ShouldBe(true, string.Join(Environment.NewLine, results[file].Diagnostics));
        var options = results[file].Options.ShouldBeA<CSharpParseOptions>().And;
        options.PreprocessorSymbolNames.ShouldContain("NET10_0");
        options.PreprocessorSymbolNames.ShouldContain("DEBUG");
        options.Kind.ShouldBe(SourceCodeKind.Regular);
    }

    async Task<DressSharp.Architecture.ParseContextResolution> Resolve(FakeEvaluator evaluator, string file, string? configuration = null)
    {
        var results = await new ParseContextResolver(root, evaluator).Resolve([file], configuration, TestContext.Current.CancellationToken);
        return results[file];
    }

    string File(string name)
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, name);
        System.IO.File.WriteAllText(path, "");
        return path;
    }

    static MSBuildEvaluation Success(params (string Key, string Value)[] properties) => Success(null, properties);

    static MSBuildEvaluation Success(string? compileItem, params (string Key, string Value)[] properties) =>
        new(true, properties.ToDictionary(item => item.Key, item => item.Value), compileItem is null ? [] : [compileItem], "");

    static MSBuildEvaluation Failure(string diagnostic) => new(false, new Dictionary<string, string>(), [], diagnostic);

    public void Dispose()
    {
        if (Directory.Exists(root))
            Directory.Delete(root, true);
        GC.SuppressFinalize(this);
    }

    sealed class FakeEvaluator : IMSBuildEvaluator
    {
        readonly Dictionary<(string Target, string? Framework), MSBuildEvaluation> evaluations = new();

        internal List<string> Configurations { get; } = [];

        internal MSBuildEvaluation this[string target, string? framework]
        {
            set => evaluations[(target, framework)] = value;
        }

        public ValueTask<MSBuildEvaluation> EvaluateAsync(string target, string configuration, string? targetFramework, CancellationToken cancellationToken)
        {
            Configurations.Add(configuration);
            return ValueTask.FromResult(evaluations[(target, targetFramework)]);
        }
    }

    sealed class ConcurrentEvaluator(string compileItem, int expectedConcurrency) : IMSBuildEvaluator
    {
        readonly TaskCompletionSource _allStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int _active;
        int _maximumConcurrency;

        internal int MaximumConcurrency => Volatile.Read(ref _maximumConcurrency);

        public async ValueTask<MSBuildEvaluation> EvaluateAsync(
            string target,
            string configuration,
            string? targetFramework,
            CancellationToken cancellationToken
        ) {
            var concurrency = Interlocked.Increment(ref _active);
            var observed = Volatile.Read(ref _maximumConcurrency);
            while (observed < concurrency)
            {
                var exchanged = Interlocked.CompareExchange(ref _maximumConcurrency, concurrency, observed);
                if (exchanged == observed)
                    break;
                observed = exchanged;
            }
            if (concurrency == expectedConcurrency)
                _allStarted.TrySetResult();
            await Task.WhenAny(_allStarted.Task, Task.Delay(250, cancellationToken));
            Interlocked.Decrement(ref _active);
            return Success(compileItem, ("TargetFramework", "net10.0"), ("LangVersion", "latest"));
        }
    }
}
