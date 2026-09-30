using DressSharp.Architecture;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DressSharp.Parsing;

sealed class ParseContextResolver(string discoveryRoot, IMSBuildEvaluator? evaluator = null)
{
    readonly string _discoveryRoot = Path.GetFullPath(discoveryRoot);
    readonly IMSBuildEvaluator _evaluator = evaluator ?? new MSBuildEvaluationCache(new DotNetMSBuildEvaluator());

    public async ValueTask<ResolvedParseContexts> Resolve(
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
        {
            projectContexts.Sort((left, right) => PathComparer.Compare(left.Project, right.Project) is var byProject && byProject != 0
                ? byProject
                : string.CompareOrdinal(left.Framework, right.Framework));
        }

        var results = new Dictionary<string, ParseContextResolution>(PathComparer);
        foreach (var path in selected)
        {
            if (contexts[path].Count == 0)
                results[path] = await ResolveFileAppAsync(path, configuration ?? "Debug", cancellationToken);
            else
                results[path] = ResolveOwned(path, contexts[path]);
        }
        return new(results, SkippedProjectWarnings(selected, contexts));
    }

    /// <summary>
    /// One message per project that could not be read, naming how many of the selected files it keeps
    /// from being formatted. A project that owns none of them has nothing to say.
    /// </summary>
    static List<string> SkippedProjectWarnings(
        IReadOnlyList<string> selected,
        IReadOnlyDictionary<string, List<ProjectContext>> contexts)
    {
        var failures = new Dictionary<string, (string Diagnostic, HashSet<string> Paths)>(PathComparer);
        foreach (var path in selected)
        {
            foreach (var context in contexts[path])
            {
                if (context.Diagnostic is null)
                    continue;
                if (!failures.TryGetValue(context.Project, out var failure))
                    failures[context.Project] = failure = (context.Diagnostic, new HashSet<string>(PathComparer));
                failure.Paths.Add(path);
            }
        }

        return failures.OrderBy(pair => pair.Key, PathComparer)
            .Select(pair => $"skipping {pair.Value.Paths.Count} {(pair.Value.Paths.Count == 1 ? "file" : "files")} in {pair.Key}: {pair.Value.Diagnostic}")
            .ToList();
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

    /// <remarks>
    /// The symbols a file compiles with are added by an SDK target that only the build of one
    /// framework has, so a project is first asked what it declares, and then, for each framework it
    /// names, what that framework's build compiles with. A project that names no framework is not
    /// built by the SDK, and compiles with what it declares.
    /// </remarks>
    async ValueTask AddProjectContextsAsync(
        string project,
        string configuration,
        IReadOnlyDictionary<string, List<ProjectContext>> contexts,
        CancellationToken cancellationToken)
    {
        var declared = await _evaluator.EvaluateAsync(project, configuration, null, MSBuildEvaluationKind.Declaration, cancellationToken);
        if (!declared.Succeeded)
        {
            FailProject(project, declared.Diagnostic, contexts);
            return;
        }

        var frameworks = Split(declared.Properties.GetValueOrDefault("TargetFrameworks"));
        if (frameworks.Count == 0)
            frameworks = Split(declared.Properties.GetValueOrDefault("TargetFramework"));
        if (frameworks.Count == 0)
        {
            AddContexts(project, "", declared, contexts);
            return;
        }

        foreach (var framework in frameworks)
        {
            var compiled = await _evaluator.EvaluateAsync(project, configuration, framework, MSBuildEvaluationKind.Compilation, cancellationToken);
            if (!compiled.Succeeded)
            {
                FailProject(project, compiled.Diagnostic, contexts);
                return;
            }
            AddContexts(project, framework, compiled, contexts);
        }
    }

    static void AddContexts(
        string project,
        string framework,
        MSBuildEvaluation evaluation,
        IReadOnlyDictionary<string, List<ProjectContext>> contexts)
    {
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

    /// <summary>
    /// Records that a project could not be read. Which files it owns is unknown, so every selected file
    /// beneath its directory is taken to be one of them.
    /// </summary>
    static void FailProject(string project, string diagnostic, IReadOnlyDictionary<string, List<ProjectContext>> contexts)
    {
        var failed = ProjectContext.Failed(project, $"project evaluation failed. {diagnostic}");
        foreach (var path in contexts.Keys.Where(path => IsUnder(path, Path.GetDirectoryName(project)!)))
            AddContext(contexts[path], failed);
    }

    static void AddContext(List<ProjectContext> contexts, ProjectContext context)
    {
        lock (contexts)
            contexts.Add(context);
    }

    async ValueTask<ParseContextResolution> ResolveFileAppAsync(string path, string configuration, CancellationToken cancellationToken)
    {
        var evaluation = await _evaluator.EvaluateAsync(path, configuration, null, MSBuildEvaluationKind.Compilation, cancellationToken);
        if (!evaluation.Succeeded)
            return new([Fallback], [$"{path}: implicit file-app evaluation failed; using latest-stable fallback. {evaluation.Diagnostic}"]);
        try
        {
            return new([CreateOptions(evaluation.Properties)], []);
        }
        catch (UnsupportedLanguageVersionException exception)
        {
            return new([], [$"{path}: {exception.Message}"]);
        }
    }

    /// <remarks>
    /// A file compiled by several projects, or by several frameworks of one, is parsed each way it
    /// is compiled, so that every region a directive enables somewhere is formatted somewhere.
    /// </remarks>
    static ParseContextResolution ResolveOwned(string path, IReadOnlyList<ProjectContext> contexts)
    {
        if (contexts.Any(context => context.Diagnostic is not null))
            return new([], [], Skipped: true);
        var options = contexts.Select(context => context.Options!).Distinct(ParseOptionsComparer.Instance).ToArray();
        return new(options, options.Length > 1 ? [$"{path}: parsed {options.Length} ways by the projects that compile it."] : []);
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
