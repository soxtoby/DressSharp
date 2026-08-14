using System.Diagnostics;
using System.Text.Json;

namespace DressSharp.Parsing;

interface IMSBuildEvaluator
{
    ValueTask<MSBuildEvaluation> EvaluateAsync(
        string target,
        string configuration,
        string? targetFramework,
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
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = Path.GetDirectoryName(target)!,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
        startInfo.ArgumentList.Add("build");
        startInfo.ArgumentList.Add(target);
        startInfo.ArgumentList.Add("--nologo");
        startInfo.ArgumentList.Add("--no-restore");
        startInfo.ArgumentList.Add("--target:AddImplicitDefineConstants");
        startInfo.ArgumentList.Add($"--property:Configuration={configuration}");
        if (targetFramework is not null)
            startInfo.ArgumentList.Add($"--property:TargetFramework={targetFramework}");
        startInfo.ArgumentList.Add($"--getProperty:{string.Join(',', PropertyNames)}");
        startInfo.ArgumentList.Add("--getItem:Compile");

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start dotnet.");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var output = await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0)
            return new(false, new Dictionary<string, string>(), [], FirstDiagnostic(error, output));

        try
        {
            using var document = JsonDocument.Parse(output);
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