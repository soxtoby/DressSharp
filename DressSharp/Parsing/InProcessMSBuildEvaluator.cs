using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Exceptions;
using Microsoft.Build.Execution;
using Microsoft.Build.Framework;
using Microsoft.Build.Locator;

namespace DressSharp.Parsing;

/// <summary>Evaluates projects with the SDK's own MSBuild loaded into this process.</summary>
/// <remarks>
/// A <c>dotnet msbuild</c> process costs over half a second per project and framework, most of it
/// start-up and SDK resolution repeated every time. Loaded once, the same MSBuild evaluates a
/// project in tens of milliseconds and shares its parsed imports between projects: a cold run over
/// 55 projects took 19 s one process at a time and about 1 s this way. A file-based app still needs
/// <c>dotnet build</c>, which wraps the file in a generated project, so those go to
/// <paramref name="fallback"/>, as does everything when no SDK can be loaded.
/// </remarks>
sealed class InProcessMSBuildEvaluator(IMSBuildEvaluator fallback) : IMSBuildEvaluator, IDisposable
{
    static readonly Lock RegistrationLock = new();
    static bool? _registered;

    Engine? _engine;

    /// <summary>How many times MSBuild has evaluated a project for this evaluator.</summary>
    internal int Evaluations => _engine?.Evaluations ?? 0;

    public ValueTask<MSBuildEvaluation> EvaluateAsync(
        string target,
        string configuration,
        string? targetFramework,
        MSBuildEvaluationKind kind,
        CancellationToken cancellationToken)
    {
        if (IsFileApp(target) || !TryRegister())
            return fallback.EvaluateAsync(target, configuration, targetFramework, kind, cancellationToken);
        return new(EvaluateInProcessAsync(target, configuration, targetFramework, kind, cancellationToken));
    }

    // The MSBuild assemblies are found at run time, so no method that names one of their types may be
    // compiled before the SDK is registered. Everything that does is kept out of line, behind
    // registration.
    [MethodImpl(MethodImplOptions.NoInlining)]
    Task<MSBuildEvaluation> EvaluateInProcessAsync(
        string target,
        string configuration,
        string? targetFramework,
        MSBuildEvaluationKind kind,
        CancellationToken cancellationToken)
        => LazyInitializer.EnsureInitialized(ref _engine).EvaluateAsync(target, configuration, targetFramework, kind, cancellationToken);

    public void Dispose() => DisposeEngine();

    [MethodImpl(MethodImplOptions.NoInlining)]
    void DisposeEngine() => _engine?.Dispose();

    static bool IsFileApp(string target) =>
        Path.GetExtension(target).Equals(".cs", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Points the process at the SDK that <c>dotnet</c> would run from the current directory, once.
    /// </summary>
    static bool TryRegister()
    {
        lock (RegistrationLock)
        {
            if (_registered is null)
            {
                try
                {
                    _registered = MSBuildLocator.IsRegistered || MSBuildLocator.RegisterDefaults() is not null;
                }
                catch (Exception exception) when (exception is InvalidOperationException or IOException or DllNotFoundException)
                {
                    // Whatever kept the SDK from loading, the process-based evaluator still answers correctly.
                    _registered = false;
                }
            }
            return _registered.Value;
        }
    }

    sealed class Engine : IDisposable
    {
        const string CompilationTarget = "AddImplicitDefineConstants";

        static readonly string[] PropertyNames =
            [
                "TargetFrameworks",
                "TargetFramework",
                "LangVersion",
                "DefineConstants",
                "OutputType",
                "GenerateDocumentationFile",
            ];

        readonly ProjectCollection _collection = new();
        readonly BuildManager _buildManager = new("DressSharp");
        readonly ConcurrentDictionary<(string Target, string Configuration), ProjectInstance> _declarations = new();
        readonly ConcurrentDictionary<string, string> _firstErrors = new(StringComparer.OrdinalIgnoreCase);
        readonly Lock _buildLock = new();
        bool _building;
        string? _savedDirectory;
        int _evaluations;

        internal int Evaluations => Volatile.Read(ref _evaluations);

        /// <remarks>
        /// The resolver starts every project's evaluation from one thread and awaits them together, so
        /// evaluating where it was started would run the projects one after another; each is sent to
        /// the pool instead. Building then waits without holding a pool thread, since MSBuild's build
        /// manager runs its own work on that pool and a pool full of waiting evaluations stalls it.
        /// </remarks>
        internal async Task<MSBuildEvaluation> EvaluateAsync(
            string target,
            string configuration,
            string? targetFramework,
            MSBuildEvaluationKind kind,
            CancellationToken cancellationToken)
        {
            try
            {
                if (kind == MSBuildEvaluationKind.Declaration)
                    return Read(await Task.Run(() => Declare(target, configuration), cancellationToken));

                var instance = await Task.Run(() => BuildOf(target, configuration, targetFramework), cancellationToken);
                var result = await BuildAsync(instance);
                if (result.OverallResult != BuildResultCode.Success || result.ProjectStateAfterBuild is null)
                {
                    _firstErrors.TryGetValue(target, out var error);
                    return Failed(error ?? result.Exception?.Message ?? $"MSBuild could not run the {CompilationTarget} target.");
                }
                return Read(result.ProjectStateAfterBuild);
            }
            catch (InvalidProjectFileException exception)
            {
                // Laid out as MSBuild itself reports it, so that a user can search for the code.
                return Failed($"{exception.ProjectFile}({exception.LineNumber},{exception.ColumnNumber}): error {exception.ErrorCode}: {exception.BaseMessage}");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                return Failed(exception.Message);
            }
        }

        ProjectInstance Declare(string target, string configuration)
        {
            var instance = Evaluate(target, configuration, null);
            _declarations[(target, configuration)] = instance;
            return instance;
        }

        /// <remarks>
        /// A project that names one framework is already that framework's build when it is declared, so
        /// the declaration is built as it stands. Only a project that names several is evaluated again,
        /// once per framework, as the SDK's outer build would.
        /// </remarks>
        ProjectInstance BuildOf(string target, string configuration, string? targetFramework) =>
            _declarations.TryGetValue((target, configuration), out var instance) && IsBuildOf(instance, targetFramework)
                ? instance
                : Evaluate(target, configuration, targetFramework);

        static bool IsBuildOf(ProjectInstance instance, string? targetFramework) =>
            instance.GetPropertyValue("TargetFrameworks").Length == 0
            && string.Equals(instance.GetPropertyValue("TargetFramework"), targetFramework, StringComparison.OrdinalIgnoreCase);

        ProjectInstance Evaluate(string target, string configuration, string? targetFramework)
        {
            var globalProperties = new Dictionary<string, string> { ["Configuration"] = configuration };
            if (targetFramework is not null)
                globalProperties["TargetFramework"] = targetFramework;
            Interlocked.Increment(ref _evaluations);
            return new ProjectInstance(target, globalProperties, null, _collection);
        }

        Task<BuildResult> BuildAsync(ProjectInstance instance)
        {
            _buildLock.Enter();
            try
            {
                if (!_building)
                {
                    // The node that builds in this process makes each project's directory the current
                    // one while it runs there, so the directory is put back when the session ends.
                    _savedDirectory = Environment.CurrentDirectory;
                    // One node, in this process: the target only derives properties, and a second node
                    // would be another process to start.
                    _buildManager.BeginBuild(new BuildParameters(_collection)
                        {
                            MaxNodeCount = 1,
                            EnableNodeReuse = false,
                            Loggers = [new FirstErrorLogger(_firstErrors)],
                        });
                    _building = true;
                }
            }
            finally
            {
                _buildLock.Exit();
            }

            var request = new BuildRequestData(instance, [CompilationTarget], null, BuildRequestDataFlags.ProvideProjectStateAfterBuild);
            var completion = new TaskCompletionSource<BuildResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            _buildManager.PendBuildRequest(request).ExecuteAsync(
                submission =>
                    {
                        if (submission.BuildResult is { } result)
                            completion.TrySetResult(result);
                        else
                            completion.TrySetException(new InvalidOperationException($"MSBuild finished the {CompilationTarget} target without a result."));
                    },
                null);
            return completion.Task;
        }

        static MSBuildEvaluation Read(ProjectInstance instance)
        {
            var properties = PropertyNames.ToDictionary(name => name, instance.GetPropertyValue, StringComparer.OrdinalIgnoreCase);
            var compileItems = instance.GetItems("Compile")
                .Select(item => Path.GetFullPath(item.GetMetadataValue("FullPath")))
                .ToArray();
            return new(true, properties, compileItems, "");
        }

        static MSBuildEvaluation Failed(string diagnostic) => new(false, new Dictionary<string, string>(), [], diagnostic);

        public void Dispose()
        {
            _buildLock.Enter();
            try
            {
                if (_building)
                {
                    _buildManager.EndBuild();
                    if (_savedDirectory is not null && Directory.Exists(_savedDirectory))
                        Environment.CurrentDirectory = _savedDirectory;
                }
                _building = false;
            }
            finally
            {
                _buildLock.Exit();
            }
            _buildManager.Dispose();
            _collection.Dispose();
        }

        /// <summary>Keeps the first error each project raises, as the diagnostic for its failure.</summary>
        sealed class FirstErrorLogger(ConcurrentDictionary<string, string> errors) : ILogger
        {
            public LoggerVerbosity Verbosity { get; set; } = LoggerVerbosity.Quiet;

            public string? Parameters { get; set; }

            public void Initialize(IEventSource eventSource) =>
                eventSource.ErrorRaised += (_, error) =>
                    {
                        if (error.ProjectFile is not null)
                            errors.TryAdd(error.ProjectFile, $"error {error.Code}: {error.Message}");
                    };

            public void Shutdown()
            {
            }
        }
    }
}
