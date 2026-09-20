using DressSharp.Architecture;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DressSharp.Parsing;

sealed class ParseContextResolver(string discoveryRoot, IMSBuildEvaluator? evaluator = null)
{
    readonly string _discoveryRoot = Path.GetFullPath(discoveryRoot);
    readonly IMSBuildEvaluator _evaluator = evaluator ?? new MSBuildEvaluationCache(new DotNetMSBuildEvaluator());

    public async ValueTask<IReadOnlyDictionary<string, ParseContextResolution>> Resolve(
        IReadOnlyList<string> paths,
        string? configuration,
        CancellationToken cancellationToken)
    {
        var selected = paths.Select(Path.GetFullPath).Distinct(PathComparer).ToArray();
        var contexts = selected.ToDictionary(path => path, _ => new List<ProjectContext>(), PathComparer);

        // A project nearly always owns the files beneath its own directory, so the projects on the
        // way up from each selected file are asked first. Enumerating every project under the root
        // and consulting the cache for each costs more than formatting a handful of files, and is
        // only needed when a file is linked into a project that lives somewhere else.
        var ancestors = AncestorProjects(selected);
        await EvaluateAsync(ancestors, configuration ?? "Debug", contexts, cancellationToken);
        if (contexts.Values.Any(projectContexts => projectContexts.Count == 0))
        {
            var remaining = Directory.EnumerateFiles(_discoveryRoot, "*.csproj", SearchOption.AllDirectories)
                .Where(project => !ancestors.Contains(project));
            await EvaluateAsync(remaining, configuration ?? "Debug", contexts, cancellationToken);
        }
        foreach (var projectContexts in contexts.Values)
            projectContexts.Sort((left, right) => PathComparer.Compare(left.Project, right.Project));

        var results = new Dictionary<string, ParseContextResolution>(PathComparer);
        foreach (var path in selected)
        {
            if (contexts[path].Count == 0)
                results[path] = await ResolveFileAppAsync(path, configuration ?? "Debug", cancellationToken);
            else
                results[path] = ResolveOwned(path, contexts[path]);
        }
        return results;
    }

    HashSet<string> AncestorProjects(IEnumerable<string> paths)
    {
        var projects = new HashSet<string>(PathComparer);
        var visited = new HashSet<string>(PathComparer);
        foreach (var path in paths)
        {
            for (var directory = Path.GetDirectoryName(path);
                 directory is not null && IsUnder(directory, _discoveryRoot) && visited.Add(directory);
                 directory = Path.GetDirectoryName(directory))
            {
                if (Directory.Exists(directory))
                    projects.UnionWith(Directory.EnumerateFiles(directory, "*.csproj", SearchOption.TopDirectoryOnly));
            }
        }
        return projects;
    }

    Task EvaluateAsync(
        IEnumerable<string> projects,
        string configuration,
        IReadOnlyDictionary<string, List<ProjectContext>> contexts,
        CancellationToken cancellationToken) =>
        Task.WhenAll(projects.Order(PathComparer).Select(project => AddProjectContextsAsync(project, configuration, contexts, cancellationToken).AsTask()));

    async ValueTask AddProjectContextsAsync(
        string project,
        string configuration,
        IReadOnlyDictionary<string, List<ProjectContext>> contexts,
        CancellationToken cancellationToken)
    {
        var outer = await _evaluator.EvaluateAsync(project, configuration, null, cancellationToken);
        if (!outer.Succeeded)
        {
            foreach (var path in contexts.Keys.Where(path => IsUnder(path, Path.GetDirectoryName(project)!)))
                AddContext(contexts[path], ProjectContext.Failed(project, outer.Diagnostic));
            return;
        }

        var frameworks = Split(outer.Properties.GetValueOrDefault("TargetFrameworks"));
        if (frameworks.Count == 0)
            frameworks = [outer.Properties.GetValueOrDefault("TargetFramework") ?? ""];
        foreach (var framework in frameworks.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            var evaluation = frameworks.Count == 1 && outer.Properties.GetValueOrDefault("TargetFramework") == framework
                ? outer
                : await _evaluator.EvaluateAsync(project, configuration, framework, cancellationToken);
            if (!evaluation.Succeeded)
            {
                foreach (var path in contexts.Keys.Where(path => IsUnder(path, Path.GetDirectoryName(project)!)))
                    AddContext(contexts[path], ProjectContext.Failed(project, evaluation.Diagnostic));
                continue;
            }
            try
            {
                var options = CreateOptions(evaluation.Properties);
                foreach (var item in evaluation.CompileItems.Where(contexts.ContainsKey))
                    AddContext(contexts[item], new(project, framework, options, null));
            }
            catch (UnsupportedLanguageVersionException exception)
            {
                foreach (var item in evaluation.CompileItems.Where(contexts.ContainsKey))
                    AddContext(contexts[item], ProjectContext.Failed(project, exception.Message));
            }
        }
    }

    static void AddContext(List<ProjectContext> contexts, ProjectContext context)
    {
        lock (contexts)
            contexts.Add(context);
    }

    async ValueTask<ParseContextResolution> ResolveFileAppAsync(string path, string configuration, CancellationToken cancellationToken)
    {
        var evaluation = await _evaluator.EvaluateAsync(path, configuration, null, cancellationToken);
        if (!evaluation.Succeeded)
            return new(Fallback, [$"{path}: implicit file-app evaluation failed; using latest-stable fallback. {evaluation.Diagnostic}"]);
        try
        {
            return new(CreateOptions(evaluation.Properties), []);
        }
        catch (UnsupportedLanguageVersionException exception)
        {
            return new(null, [$"{path}: {exception.Message}"]);
        }
    }

    static ParseContextResolution ResolveOwned(string path, IReadOnlyList<ProjectContext> contexts)
    {
        var failure = contexts.FirstOrDefault(context => context.Diagnostic is not null);
        if (failure is not null)
            return new(null, [$"{path}: project evaluation failed for {failure.Project}. {failure.Diagnostic}"]);
        var selectedContexts = contexts.GroupBy(context => context.Project, PathComparer)
            .Select(group => group.Aggregate((left, right) => IsLower(right.Framework, left.Framework) ? right : left))
            .ToArray();
        if (selectedContexts.Select(context => context.Options!).Distinct(ParseOptionsComparer.Instance).Skip(1).Any())
            return new(null, [$"{path}: owning projects provide differing parse contexts."]);
        return new(selectedContexts[0].Options, []);
    }

    static CSharpParseOptions CreateOptions(IReadOnlyDictionary<string, string> properties)
    {
        var requested = properties.GetValueOrDefault("LangVersion");
        if (string.IsNullOrWhiteSpace(requested))
            requested = "default";
        if (!LanguageVersionFacts.TryParse(requested, out var languageVersion))
            throw new UnsupportedLanguageVersionException($"Language version '{requested}' is newer than bundled Roslyn or unsupported.");
        languageVersion = languageVersion.MapSpecifiedToEffectiveVersion();
        var documentationMode = bool.TryParse(properties.GetValueOrDefault("GenerateDocumentationFile"), out var generatesDocumentation)
            && generatesDocumentation
                ? DocumentationMode.Diagnose
                : DocumentationMode.Parse;
        return new(languageVersion, documentationMode: documentationMode, preprocessorSymbols: Split(properties.GetValueOrDefault("DefineConstants")));
    }

    static bool IsLower(string candidate, string current)
    {
        var candidateVersion = FrameworkVersion(candidate);
        var currentVersion = FrameworkVersion(current);
        return candidateVersion.Family == currentVersion.Family && candidateVersion.Version < currentVersion.Version;
    }

    static (string Family, Version Version) FrameworkVersion(string framework)
    {
        var separator = framework.IndexOf('-');
        var portable = separator < 0 ? framework : framework[..separator];
        var firstDigit = portable.IndexOfAny("0123456789".ToCharArray());
        if (firstDigit < 0)
            return (portable, new Version());
        var family = portable[..firstDigit];
        var versionText = portable[firstDigit..];
        if (!versionText.Contains('.'))
            versionText = string.Join('.', versionText.ToCharArray());
        return (family, Version.TryParse(versionText, out var version) ? version : new Version());
    }

    static List<string> Split(string? value) =>
        (value ?? "").Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    static bool IsUnder(string path, string directory) =>
        Path.GetRelativePath(directory, path) is var relative
        && relative != ".."
        && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    static readonly StringComparer PathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    static CSharpParseOptions Fallback { get; } = new(LanguageVersion.Latest.MapSpecifiedToEffectiveVersion(), documentationMode: DocumentationMode.Parse);

    sealed record ProjectContext(string Project, string Framework, CSharpParseOptions? Options, string? Diagnostic)
    {
        internal static ProjectContext Failed(string project, string diagnostic) => new(project, "", null, diagnostic);
    }

    sealed class ParseOptionsComparer : IEqualityComparer<CSharpParseOptions>
    {
        internal static ParseOptionsComparer Instance { get; } = new();

        public bool Equals(CSharpParseOptions? left, CSharpParseOptions? right) =>
            left?.LanguageVersion == right?.LanguageVersion
            && left?.Kind == right?.Kind
            && left?.DocumentationMode == right?.DocumentationMode
            && (left?.PreprocessorSymbolNames ?? []).SequenceEqual(right?.PreprocessorSymbolNames ?? [], StringComparer.Ordinal);

        public int GetHashCode(CSharpParseOptions options)
        {
            var hash = new HashCode();
            hash.Add(options.LanguageVersion);
            hash.Add(options.Kind);
            hash.Add(options.DocumentationMode);
            foreach (var symbol in options.PreprocessorSymbolNames)
                hash.Add(symbol, StringComparer.Ordinal);
            return hash.ToHashCode();
        }
    }
}

sealed class UnsupportedLanguageVersionException(string message) : Exception(message);
