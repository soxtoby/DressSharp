using DressSharp.Parsing;
using Xunit;
using EasyAssertions;
using Kind = DressSharp.Parsing.MSBuildEvaluationKind;

namespace DressSharp.UnitTests;

public sealed class InProcessMSBuildEvaluatorTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), $"DressSharp-{Guid.NewGuid():N}");
    readonly FallbackEvaluator _fallback = new();
    readonly InProcessMSBuildEvaluator _evaluator;

    public InProcessMSBuildEvaluatorTests()
    {
        _evaluator = new(_fallback);
    }

    [Fact]
    public async Task Single_framework_project_is_evaluated_once_for_its_declaration_and_its_compilation()
    {
        var file = File("Program.cs");
        var project = Project("App.csproj", "<TargetFramework>net10.0</TargetFramework>");

        var declared = await _evaluator.EvaluateAsync(project, "Debug", null, Kind.Declaration, TestContext.Current.CancellationToken);
        var compiled = await _evaluator.EvaluateAsync(project, "Debug", "net10.0", Kind.Compilation, TestContext.Current.CancellationToken);

        declared.Succeeded.ShouldBe(true, declared.Diagnostic);
        declared.Properties["TargetFramework"].ShouldBe("net10.0");
        declared.CompileItems.ShouldContain(file);
        compiled.Succeeded.ShouldBe(true, compiled.Diagnostic);
        Symbols(compiled).ShouldContain("NET10_0");
        Symbols(compiled).ShouldContain("DEBUG");
        compiled.CompileItems.ShouldContain(file);
        _evaluator.Evaluations.ShouldBe(1);
        _fallback.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Multi_framework_project_is_evaluated_once_more_per_framework()
    {
        File("Program.cs");
        var project = Project("App.csproj", "<TargetFrameworks>net10.0;net9.0</TargetFrameworks>");

        var declared = await _evaluator.EvaluateAsync(project, "Debug", null, Kind.Declaration, TestContext.Current.CancellationToken);
        var newest = await _evaluator.EvaluateAsync(project, "Debug", "net10.0", Kind.Compilation, TestContext.Current.CancellationToken);
        var oldest = await _evaluator.EvaluateAsync(project, "Debug", "net9.0", Kind.Compilation, TestContext.Current.CancellationToken);

        declared.Properties["TargetFrameworks"].ShouldBe("net10.0;net9.0");
        Symbols(newest).ShouldContain("NET10_0");
        Symbols(oldest).ShouldContain("NET9_0");
        Symbols(oldest).ShouldNotContain("NET10_0");
        _evaluator.Evaluations.ShouldBe(3);
    }

    [Fact]
    public async Task Configuration_reaches_the_compilation()
    {
        File("Program.cs");
        var project = Project("App.csproj", "<TargetFramework>net10.0</TargetFramework>");

        await _evaluator.EvaluateAsync(project, "Release", null, Kind.Declaration, TestContext.Current.CancellationToken);
        var compiled = await _evaluator.EvaluateAsync(project, "Release", "net10.0", Kind.Compilation, TestContext.Current.CancellationToken);

        Symbols(compiled).ShouldContain("RELEASE");
        Symbols(compiled).ShouldNotContain("DEBUG");
    }

    [Fact]
    public async Task Project_that_cannot_be_read_fails_with_the_first_error()
    {
        var project = Project("App.csproj", "<TargetFramework>net10.0</TargetFramework>", "<Import Project=\"Missing.props\" />");

        var declared = await _evaluator.EvaluateAsync(project, "Debug", null, Kind.Declaration, TestContext.Current.CancellationToken);

        declared.Succeeded.ShouldBe(false);
        declared.Diagnostic.ShouldContain("MSB4019");
        declared.Diagnostic.ShouldContain("Missing.props");
    }

    [Fact]
    public async Task Projects_evaluate_concurrently_on_one_evaluator()
    {
        File("Program.cs");
        var projects = Enumerable.Range(0, 8)
            .Select(index => Project($"App{index}/App{index}.csproj", "<TargetFramework>net10.0</TargetFramework>"))
            .ToArray();

        var declared = await Task.WhenAll(projects.Select(project =>
            _evaluator.EvaluateAsync(project, "Debug", null, Kind.Declaration, TestContext.Current.CancellationToken).AsTask()));
        var compiled = await Task.WhenAll(projects.Select(project =>
            _evaluator.EvaluateAsync(project, "Debug", "net10.0", Kind.Compilation, TestContext.Current.CancellationToken).AsTask()));

        declared.AllItemsSatisfy(evaluation => evaluation.Succeeded.ShouldBe(true, evaluation.Diagnostic));
        compiled.AllItemsSatisfy(evaluation => Symbols(evaluation).ShouldContain("NET10_0"));
        _evaluator.Evaluations.ShouldBe(8);
    }

    [Fact]
    public async Task File_based_app_goes_to_the_fallback()
    {
        var file = File("Program.cs");

        var evaluation = await _evaluator.EvaluateAsync(file, "Debug", null, Kind.Compilation, TestContext.Current.CancellationToken);

        evaluation.Diagnostic.ShouldBe("fallback");
        _fallback.Requests.ShouldMatch([file]);
        _evaluator.Evaluations.ShouldBe(0);
    }

    static IEnumerable<string> Symbols(MSBuildEvaluation evaluation) =>
        evaluation.Properties["DefineConstants"].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    string Project(string name, string properties, string extra = "")
    {
        var path = File(name);
        System.IO.File.WriteAllText(path, $"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup>{properties}</PropertyGroup>{extra}</Project>");
        return path;
    }

    string File(string name)
    {
        var path = Path.GetFullPath(Path.Combine(_root, name));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllText(path, "");
        return path;
    }

    public void Dispose()
    {
        _evaluator.Dispose();
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
        GC.SuppressFinalize(this);
    }

    sealed class FallbackEvaluator : IMSBuildEvaluator
    {
        internal List<string> Requests { get; } = [];

        public ValueTask<MSBuildEvaluation> EvaluateAsync(
            string target,
            string configuration,
            string? targetFramework,
            Kind kind,
            CancellationToken cancellationToken)
        {
            lock (Requests)
                Requests.Add(target);
            return ValueTask.FromResult(new MSBuildEvaluation(false, new Dictionary<string, string>(), [], "fallback"));
        }
    }
}
