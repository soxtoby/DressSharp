using DressSharp.Parsing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using EasyAssertions;
using Kind = DressSharp.Parsing.MSBuildEvaluationKind;

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
                [project, null, Kind.Declaration] = Success(("TargetFrameworks", "net9.0;net8.0"), ("TargetFramework", "")),
                [project, "net9.0", Kind.Compilation] = Success(file, ("TargetFramework", "net9.0"), ("LangVersion", "13"), ("DefineConstants", "NET9;DEBUG")),
                [project, "net8.0", Kind.Compilation] = Success(file, ("TargetFramework", "net8.0"), ("LangVersion", "12"), ("DefineConstants", "NET8;DEBUG")),
            };

        var result = await Resolve(evaluator, file, "Release");

        result.Options!.LanguageVersion.ShouldBe(LanguageVersion.CSharp12);
        result.Options.PreprocessorSymbolNames.ShouldContain("NET8");
        evaluator.Configurations.AllItemsSatisfy(value => value.ShouldBe("Release"));
    }

    [Fact]
    public async Task Single_target_project_compiles_the_framework_it_declares()
    {
        var file = File("Program.cs");
        var project = File("App.csproj");
        var evaluator = new FakeEvaluator
            {
                [project, null, Kind.Declaration] = Success(file, ("TargetFramework", "net10.0"), ("DefineConstants", "DEBUG")),
                [project, "net10.0", Kind.Compilation] = Success(
                    file,
                    ("TargetFramework", "net10.0"),
                    ("LangVersion", "latest"),
                    ("DefineConstants", "DEBUG;NET10_0")),
            };

        var result = await Resolve(evaluator, file);

        result.CanFormat.ShouldBe(true);
        result.Options!.PreprocessorSymbolNames.ShouldContain("NET10_0");
        evaluator.Requests.ShouldMatch([Request(project, null, Kind.Declaration), Request(project, "net10.0", Kind.Compilation)]);
    }

    [Fact]
    public async Task Project_without_a_framework_compiles_with_what_it_declares()
    {
        var file = File("Program.cs");
        var project = File("Legacy.csproj");
        var evaluator = new FakeEvaluator
            {
                [project, null, Kind.Declaration] = Success(file, ("LangVersion", "7.3"), ("DefineConstants", "DEBUG;TRACE")),
            };

        var result = await Resolve(evaluator, file);

        result.CanFormat.ShouldBe(true);
        result.Options!.LanguageVersion.ShouldBe(LanguageVersion.CSharp7_3);
        result.Options.PreprocessorSymbolNames.ShouldContain("TRACE");
        evaluator.Requests.ShouldMatch([Request(project, null, Kind.Declaration)]);
    }

    [Fact]
    public async Task Differing_shared_file_contexts_are_skipped()
    {
        var file = File("Shared.cs");
        var first = File("First.csproj");
        var second = File("Second.csproj");
        var evaluator = new FakeEvaluator
            {
                [first, null, Kind.Declaration] = Success(("TargetFramework", "net10.0")),
                [first, "net10.0", Kind.Compilation] = Success(file, ("TargetFramework", "net10.0"), ("LangVersion", "latest"), ("DefineConstants", "FIRST")),
                [second, null, Kind.Declaration] = Success(("TargetFramework", "net10.0")),
                [second, "net10.0", Kind.Compilation] = Success(file, ("TargetFramework", "net10.0"), ("LangVersion", "latest"), ("DefineConstants", "SECOND")),
            };

        var result = await Resolve(evaluator, file);

        result.CanFormat.ShouldBe(false);
        result.Skipped.ShouldBe(false);
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
    public async Task Only_ancestor_projects_are_evaluated_when_they_own_every_file()
    {
        var file = File("src/App/Program.cs");
        var project = File("src/App/App.csproj");
        var outer = File("Directory.csproj");
        File("other/Other.csproj");
        var evaluator = new FakeEvaluator
            {
                [project, null, Kind.Declaration] = Success(("TargetFramework", "net10.0")),
                [project, "net10.0", Kind.Compilation] = Success(file, ("TargetFramework", "net10.0"), ("LangVersion", "latest")),
                [outer, null, Kind.Declaration] = Success(("TargetFramework", "net10.0")),
                [outer, "net10.0", Kind.Compilation] = Success(("TargetFramework", "net10.0"), ("LangVersion", "latest")),
            };

        var result = await Resolve(evaluator, file);

        result.CanFormat.ShouldBe(true);
        evaluator.Requests.Select(request => request.Target).Distinct().ShouldMatch([outer, project]);
    }

    [Fact]
    public async Task Linked_file_is_found_by_scanning_remaining_projects()
    {
        var file = File("shared/Shared.cs");
        var project = File("src/App/App.csproj");
        var evaluator = new FakeEvaluator
            {
                [project, null, Kind.Declaration] = Success(("TargetFramework", "net10.0")),
                [project, "net10.0", Kind.Compilation] = Success(file, ("TargetFramework", "net10.0"), ("LangVersion", "latest"), ("DefineConstants", "LINKED")),
            };

        var result = await Resolve(evaluator, file);

        result.CanFormat.ShouldBe(true);
        result.Options!.PreprocessorSymbolNames.ShouldContain("LINKED");
    }

    [Fact]
    public async Task Failed_project_is_reported_once_and_its_files_are_skipped()
    {
        var first = File("Program.cs");
        var second = File("Nested/Other.cs");
        var project = File("App.csproj");
        var evaluator = new FakeEvaluator { [project, null, Kind.Declaration] = Failure("broken import") };

        var resolved = await new ParseContextResolver(root, evaluator).Resolve([first, second], null, TestContext.Current.CancellationToken);

        foreach (var path in new[] { first, second })
        {
            resolved.Files[path].CanFormat.ShouldBe(false);
            resolved.Files[path].Skipped.ShouldBe(true);
            resolved.Files[path].Diagnostics.ShouldBeEmpty();
        }
        resolved.Warnings.Single().ShouldBe($"skipping 2 files in {project}: project evaluation failed. broken import");
    }

    [Fact]
    public async Task Failed_framework_skips_the_project_without_compiling_the_rest()
    {
        var file = File("Program.cs");
        var project = File("App.csproj");
        var evaluator = new FakeEvaluator
            {
                [project, null, Kind.Declaration] = Success(("TargetFrameworks", "net9.0;net8.0")),
                [project, "net9.0", Kind.Compilation] = Failure("missing workload"),
                [project, "net8.0", Kind.Compilation] = Success(file, ("TargetFramework", "net8.0"), ("LangVersion", "12")),
            };

        var resolved = await new ParseContextResolver(root, evaluator).Resolve([file], null, TestContext.Current.CancellationToken);

        resolved.Files[file].Skipped.ShouldBe(true);
        resolved.Warnings.Single().ShouldBe($"skipping 1 file in {project}: project evaluation failed. missing workload");
        evaluator.Requests.ShouldMatch([Request(project, null, Kind.Declaration), Request(project, "net9.0", Kind.Compilation)]);
    }

    [Fact]
    public async Task Failed_project_that_owns_no_selected_file_is_not_reported()
    {
        var file = File("src/App/Program.cs");
        var project = File("src/App/App.csproj");
        var other = File("other/Other.csproj");
        var evaluator = new FakeEvaluator
            {
                [project, null, Kind.Declaration] = Success(("TargetFramework", "net10.0")),
                [project, "net10.0", Kind.Compilation] = Success(("TargetFramework", "net10.0"), ("LangVersion", "latest")),
                [other, null, Kind.Declaration] = Failure("broken import"),
                [file, null, Kind.Compilation] = Success(("TargetFramework", "net10.0"), ("LangVersion", "latest")),
            };

        var resolved = await new ParseContextResolver(root, evaluator).Resolve([file], null, TestContext.Current.CancellationToken);

        resolved.Warnings.ShouldBeEmpty();
        resolved.Files[file].CanFormat.ShouldBe(true);
        evaluator.Requests.Select(request => request.Target).ShouldContain(other);
    }

    [Fact]
    public async Task Failed_file_app_uses_reported_latest_stable_fallback()
    {
        var file = File("Program.cs");
        var evaluator = new FakeEvaluator { [file, null, Kind.Compilation] = Failure("SDK unavailable") };

        var result = await Resolve(evaluator, file);

        result.CanFormat.ShouldBe(true);
        result.Options!.LanguageVersion.ShouldBe(LanguageVersion.Latest.MapSpecifiedToEffectiveVersion());
        result.Options.DocumentationMode.ShouldBe(DocumentationMode.Parse);
        result.Options.PreprocessorSymbolNames.ShouldBeEmpty();
        result.Diagnostics.Single().ShouldContain("latest-stable fallback");
    }

    [Fact]
    public async Task Unsupported_owned_language_version_skips_the_project()
    {
        var file = File("Program.cs");
        var project = File("App.csproj");
        var evaluator = new FakeEvaluator
            {
                [project, null, Kind.Declaration] = Success(("TargetFramework", "net10.0")),
                [project, "net10.0", Kind.Compilation] = Success(file, ("TargetFramework", "net10.0"), ("LangVersion", "999")),
            };

        var resolved = await new ParseContextResolver(root, evaluator).Resolve([file], null, TestContext.Current.CancellationToken);

        resolved.Files[file].CanFormat.ShouldBe(false);
        resolved.Files[file].Skipped.ShouldBe(true);
        resolved.Warnings.Single().ShouldStartWith($"skipping 1 file in {project}: Language version");
    }

    [Fact]
    public async Task Dotnet_evaluation_includes_generated_framework_symbols()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory.GetFiles("DressSharp.slnx").Length == 0)
            directory = directory.Parent!;
        var projectRoot = Path.Combine(directory.FullName, "DressSharp");
        var file = Path.Combine(projectRoot, "Program.cs");

        var results = await new ParseContextResolver(projectRoot).Resolve([file], null, TestContext.Current.CancellationToken);

        results.Files[file].CanFormat.ShouldBe(true, string.Join(Environment.NewLine, results.Files[file].Diagnostics));
        var options = results.Files[file].Options.ShouldBeA<CSharpParseOptions>().And;
        options.PreprocessorSymbolNames.ShouldContain("NET10_0");
        options.PreprocessorSymbolNames.ShouldContain("DEBUG");
        options.Kind.ShouldBe(SourceCodeKind.Regular);
    }

    [Fact]
    public async Task Dotnet_evaluation_reads_each_framework_of_a_multi_targeting_project()
    {
        var file = File("Program.cs");
        var project = File("App.csproj");
        System.IO.File.WriteAllText(
            project,
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFrameworks>net10.0;net9.0</TargetFrameworks></PropertyGroup></Project>");

        var results = await new ParseContextResolver(root, new DotNetMSBuildEvaluator()).Resolve([file], null, TestContext.Current.CancellationToken);

        results.Warnings.ShouldBeEmpty();
        results.Files[file].CanFormat.ShouldBe(true);
        var options = results.Files[file].Options.ShouldBeA<CSharpParseOptions>().And;
        options.PreprocessorSymbolNames.ShouldContain("NET9_0");
        options.PreprocessorSymbolNames.ShouldNotContain("NET10_0");
    }

    async Task<DressSharp.Architecture.ParseContextResolution> Resolve(FakeEvaluator evaluator, string file, string? configuration = null)
    {
        var results = await new ParseContextResolver(root, evaluator).Resolve([file], configuration, TestContext.Current.CancellationToken);
        return results.Files[file];
    }

    string File(string name)
    {
        Directory.CreateDirectory(root);
        var path = Path.GetFullPath(Path.Combine(root, name));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllText(path, "");
        return path;
    }

    static MSBuildEvaluation Success(params (string Key, string Value)[] properties) => Success(null, properties);

    static MSBuildEvaluation Success(string? compileItem, params (string Key, string Value)[] properties) =>
        new(true, properties.ToDictionary(item => item.Key, item => item.Value), compileItem is null ? [] : [compileItem], "");

    static MSBuildEvaluation Failure(string diagnostic) => new(false, new Dictionary<string, string>(), [], diagnostic);

    static (string Target, string? Framework, Kind Kind) Request(string target, string? framework, Kind kind) => (target, framework, kind);

    public void Dispose()
    {
        if (Directory.Exists(root))
            Directory.Delete(root, true);
        GC.SuppressFinalize(this);
    }

    sealed class FakeEvaluator : IMSBuildEvaluator
    {
        readonly Dictionary<(string Target, string? Framework, Kind Kind), MSBuildEvaluation> evaluations = new();

        internal List<string> Configurations { get; } = [];

        internal List<(string Target, string? Framework, Kind Kind)> Requests { get; } = [];

        internal MSBuildEvaluation this[string target, string? framework, Kind kind]
        {
            set => evaluations[(target, framework, kind)] = value;
        }

        public ValueTask<MSBuildEvaluation> EvaluateAsync(
            string target,
            string configuration,
            string? targetFramework,
            Kind kind,
            CancellationToken cancellationToken)
        {
            lock (Requests)
            {
                Configurations.Add(configuration);
                Requests.Add((target, targetFramework, kind));
            }
            return ValueTask.FromResult(evaluations[(target, targetFramework, kind)]);
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
            Kind kind,
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
