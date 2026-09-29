using System.Diagnostics;
using System.Text.Json;

namespace DressSharp.Parsing;

/// <summary>What an evaluation asks MSBuild for.</summary>
enum MSBuildEvaluationKind
{
    /// <summary>
    /// The properties and items as the project declares them, with no target run. Every project
    /// answers: the outer build of one that targets several frameworks, which has no framework of
    /// its own, and a project outside the SDK, which names none.
    /// </summary>
    Declaration,

    /// <summary>
    /// The properties and items as the compiler sees them, after the SDK's target has added the
    /// framework's implicit preprocessor symbols. Only the build of one framework has that target.
    /// </summary>
    Compilation,
}

interface IMSBuildEvaluator
{
    ValueTask<MSBuildEvaluation> EvaluateAsync(
        string target,
        string configuration,
        string? targetFramework,
        MSBuildEvaluationKind kind,
        CancellationToken cancellationToken);
}

sealed class DotNetMSBuildEvaluator : IMSBuildEvaluator
{
    static readonly string[] PropertyNames =
        [
            "TargetFrameworks",
            "TargetFramework",
            "LangVersion",
            "DefineConstants",
            "OutputType",
            "GenerateDocumentationFile",
        ];

    public async ValueTask<MSBuildEvaluation> EvaluateAsync(
        string target,
        string configuration,
        string? targetFramework,
        MSBuildEvaluationKind kind,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = Path.GetDirectoryName(target)!,
            };
        if (kind == MSBuildEvaluationKind.Compilation)
        {
            // Only `build` accepts a file-based app, which it wraps in a project of its own.
            startInfo.ArgumentList.Add("build");
            startInfo.ArgumentList.Add(target);
            startInfo.ArgumentList.Add("--nologo");
            startInfo.ArgumentList.Add("--no-restore");
            startInfo.ArgumentList.Add("--target:AddImplicitDefineConstants");
        }
        else
        {
            // With no target named, MSBuild evaluates the project and builds nothing.
            startInfo.ArgumentList.Add("msbuild");
            startInfo.ArgumentList.Add(target);
            startInfo.ArgumentList.Add("-nologo");
        }
        startInfo.ArgumentList.Add($"-property:Configuration={configuration}");
        if (targetFramework is not null)
            startInfo.ArgumentList.Add($"-property:TargetFramework={targetFramework}");
        startInfo.ArgumentList.Add($"-getProperty:{string.Join(',', PropertyNames)}");
        startInfo.ArgumentList.Add("-getItem:Compile");

        var result = await ProcessRunner.TryRunAsync(startInfo, cancellationToken)
            ?? throw new InvalidOperationException("Could not start dotnet.");
        if (result.ExitCode != 0)
            return new(false, new Dictionary<string, string>(), [], FirstDiagnostic(result.StandardError, result.StandardOutput));

        try
        {
            using var document = JsonDocument.Parse(result.StandardOutput);
            var properties = document.RootElement.GetProperty("Properties").EnumerateObject()
                .ToDictionary(property => property.Name, property => property.Value.GetString() ?? "", StringComparer.OrdinalIgnoreCase);
            var compileItems = document.RootElement.GetProperty("Items").GetProperty("Compile").EnumerateArray()
                .Select(item => item.GetProperty("FullPath").GetString())
                .Where(path => path is not null)
                .Select(path => Path.GetFullPath(path!))
                .ToArray();
            return new(true, properties, compileItems, "");
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException)
        {
            return new(false, new Dictionary<string, string>(), [], $"MSBuild returned an unreadable evaluation: {exception.Message}");
        }
    }

    static string FirstDiagnostic(string error, string output)
    {
        var diagnostic = string.IsNullOrWhiteSpace(error) ? output : error;
        return diagnostic.Trim().Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()
            ?? "MSBuild evaluation failed.";
    }
}

sealed record MSBuildEvaluation(
    bool Succeeded,
    IReadOnlyDictionary<string, string> Properties,
    IReadOnlyList<string> CompileItems,
    string Diagnostic);
