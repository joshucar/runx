using System.Reflection;
using Spectre.Console;

namespace MysterNybble.Runx;

internal static class RunxCli
{
    private const int CancelledExitCode = 130;

    private const string InitGuidance =
        "Run 'runx --init' to create a starter runx.json, or 'runx --init --personal' for tasks only you see.";

    public static Task<int> RunAsync(
        IReadOnlyList<string> args,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        return RunAsync(
            args,
            output,
            error,
            new DefaultProcessRunner(),
            new SpectreTaskSelector(AnsiConsole.Console),
            Directory.GetCurrentDirectory(),
            PersonalTasksSettings.FromEnvironment(),
            cancellationToken);
    }

    // Without personal task settings, only the repo's runx.json is used.
    internal static Task<int> RunAsync(
        IReadOnlyList<string> args,
        TextWriter output,
        TextWriter error,
        IProcessRunner processRunner,
        ITaskSelector taskSelector,
        string currentDirectory,
        CancellationToken cancellationToken)
    {
        return RunAsync(args, output, error, processRunner, taskSelector, currentDirectory, personalTasks: null, cancellationToken);
    }

    internal static async Task<int> RunAsync(
        IReadOnlyList<string> args,
        TextWriter output,
        TextWriter error,
        IProcessRunner processRunner,
        ITaskSelector taskSelector,
        string currentDirectory,
        PersonalTasksSettings? personalTasks,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(processRunner);
        ArgumentNullException.ThrowIfNull(taskSelector);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentDirectory);

        CliParseResult parseResult = RunxCommandLine.Parse(args);
        if (parseResult.Errors.Count > 0)
        {
            foreach (string parseError in parseResult.Errors)
            {
                error.WriteLine(parseError);
            }

            error.WriteLine("Run 'runx --help' for usage.");
            return 1;
        }

        if (parseResult.ShowHelp)
        {
            WriteHelp(output);
            return 0;
        }

        if (parseResult.ShowVersion)
        {
            output.WriteLine(GetVersion());
            return 0;
        }

        if (parseResult.DryRun && parseResult.Init)
        {
            error.WriteLine("--dry-run can only be used with a task.");
            return 1;
        }

        if (parseResult.Scope is not null && !(parseResult.Init && parseResult.Personal))
        {
            error.WriteLine("--scope can only be used with --init --personal.");
            return 1;
        }

        if (parseResult.Init)
        {
            return parseResult.Personal
                ? await RunxInitializer.InitializePersonalAsync(parseResult, currentDirectory, personalTasks, output, error, cancellationToken)
                : await RunxInitializer.InitializeAsync(parseResult, currentDirectory, output, error, cancellationToken);
        }

        if (parseResult.Personal)
        {
            error.WriteLine("--personal can only be used with --init.");
            return 1;
        }

        if (parseResult.Force)
        {
            error.WriteLine("--force can only be used with --init.");
            return 1;
        }

        if (parseResult.DryRun && parseResult.TaskName is null && (parseResult.List || !taskSelector.IsInteractive))
        {
            error.WriteLine("--dry-run requires a task name.");
            return 1;
        }

        if (parseResult.TaskName is not null && parseResult.TaskName.StartsWith('-'))
        {
            error.WriteLine($"Task name '{parseResult.TaskName}' is invalid. Task names cannot start with '-'.");
            return 1;
        }

        ConfigSearchResult searchResult = RunxConfigDiscovery.FindConfig(
            parseResult.FilePath,
            parseResult.AllowParentSearch,
            currentDirectory);

        // --file means exactly that file, so personal and script tasks only join automatic discovery.
        PersonalTasksLookup? personal = parseResult.FilePath is null
            ? await PersonalTasks.FindAsync(personalTasks, currentDirectory, evenWithoutPersonalFiles: parseResult.Where, cancellationToken)
            : null;

        IReadOnlyList<PersonalTasksFile> personalFiles = personal?.ExistingFiles() ?? [];
        string fullCurrentDirectory = Path.GetFullPath(currentDirectory);
        string? scriptRoot = parseResult.FilePath is null
            ? RunxConfigDiscovery.FindGitRoot(fullCurrentDirectory) ?? fullCurrentDirectory
            : null;

        if (parseResult.Where)
        {
            ConfigLoadResult whereResult = await RunxConfigLoader.LoadAsync(searchResult.ConfigPath, personalFiles, scriptRoot, cancellationToken);
            WriteWhere(searchResult, personal, personalTasks, whereResult.Config, parseResult.FilePath is not null, currentDirectory, output);
            return 0;
        }

        // With no config files, only the default task folder can hold tasks.
        bool hasDefaultTaskFolder = scriptRoot is not null
            && Directory.Exists(Path.Combine(scriptRoot, ScriptTasks.DefaultFolder));

        if (searchResult.ConfigPath is null && personalFiles.Count == 0 && !hasDefaultTaskFolder)
        {
            if (parseResult.TaskName is null && !parseResult.List && parseResult.FilePath is null)
            {
                WriteHelp(output);
                output.WriteLine();
                output.WriteLine(searchResult.Message);
                output.WriteLine(InitGuidance);
                return 0;
            }

            error.WriteLine(searchResult.Message);
            error.WriteLine(InitGuidance);
            return 1;
        }

        ConfigLoadResult loadResult = await RunxConfigLoader.LoadAsync(searchResult.ConfigPath, personalFiles, scriptRoot, cancellationToken);
        if (loadResult.Errors.Count > 0 || loadResult.Config is null)
        {
            foreach (string loadError in loadResult.Errors)
            {
                error.WriteLine(loadError);
            }

            return 1;
        }

        string? taskName = parseResult.TaskName;
        if (taskName is null)
        {
            if (parseResult.List || !taskSelector.IsInteractive || loadResult.Config.VisibleTasks.Count == 0)
            {
                WriteTaskList(loadResult.Config, output);
                return 0;
            }

            taskName = await taskSelector.SelectTaskAsync(loadResult.Config, cancellationToken);
            if (taskName is null)
            {
                return CancelledExitCode;
            }
        }

        if (!loadResult.Config.Tasks.TryGetValue(taskName, out RunxTask? task))
        {
            error.WriteLine(loadResult.Config.Unavailable?.GetValueOrDefault(taskName)
                ?? $"Task '{taskName}' was not found in {loadResult.Config.Location}.");
            return 1;
        }

        if (!ProcessRequestFactory.IsRunnable(task) && parseResult.TaskArguments.Count > 0)
        {
            error.WriteLine($"Task '{taskName}' only runs its dependencies. Argument passthrough is not supported.");
            return 1;
        }

        List<ProcessRequest> requests = [];
        foreach (string stepName in TaskPlanner.GetExecutionOrder(loadResult.Config.Tasks, taskName))
        {
            RunxTask stepTask = loadResult.Config.Tasks[stepName];
            if (!ProcessRequestFactory.IsRunnable(stepTask))
            {
                continue;
            }

            ProcessRequestBuildResult requestResult = ProcessRequestFactory.Build(
                loadResult.Config,
                stepName,
                stepTask,
                stepName == taskName ? parseResult.TaskArguments : []);

            if (requestResult.Errors.Count > 0 || requestResult.Request is null)
            {
                foreach (string requestError in requestResult.Errors)
                {
                    error.WriteLine(requestError);
                }

                return 1;
            }

            requests.Add(requestResult.Request);
        }

        if (parseResult.DryRun)
        {
            WriteDryRun(requests, output);
            return 0;
        }

        return await RunStepsAsync(requests, processRunner, error, cancellationToken);
    }

    private static async Task<int> RunStepsAsync(
        List<ProcessRequest> requests,
        IProcessRunner processRunner,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        if (requests.Count == 1)
        {
            ProcessResult result = await processRunner.RunAsync(requests[0], error, cancellationToken);
            return result.ExitCode;
        }

        for (int i = 0; i < requests.Count; i++)
        {
            ProcessRequest request = requests[i];
            error.WriteLine($"> runx {request.TaskName} ({i + 1}/{requests.Count})");

            ProcessResult result = await processRunner.RunAsync(request, error, cancellationToken);
            if (result.Interrupted && i < requests.Count - 1)
            {
                // The child may exit cleanly on Ctrl+C, but the user still asked for the run to stop.
                error.WriteLine($"Interrupted during task '{request.TaskName}'. Remaining tasks were skipped.");
                return result.ExitCode == 0 ? CancelledExitCode : result.ExitCode;
            }

            if (result.ExitCode != 0)
            {
                error.WriteLine($"Task '{request.TaskName}' failed with exit code {result.ExitCode}.");
                return result.ExitCode;
            }
        }

        return 0;
    }

    private static void WriteHelp(TextWriter output)
    {
        output.WriteLine("runx - repo command runner for .NET projects");
        output.WriteLine();
        output.WriteLine("Usage:");
        output.WriteLine("  runx [options]                         Pick a task to run (lists tasks when not interactive).");
        output.WriteLine("  runx [options] <task> [-- <args>...]   Run a task.");
        output.WriteLine();
        output.WriteLine("Options:");
        output.WriteLine("  --help                  Show help.");
        output.WriteLine("  --version               Show version.");
        output.WriteLine("  --list                  List tasks without prompting.");
        output.WriteLine("  --init                  Create a starter runx.json.");
        output.WriteLine("  --init --personal       Create your personal tasks file for this repo, outside the repo.");
        output.WriteLine("  --scope <scope>         With --init --personal: global, a remote such as origin, or repo.");
        output.WriteLine("  --force                 Overwrite an existing file with --init.");
        output.WriteLine("  --where                 Show which task files apply here.");
        output.WriteLine("  --file <path>           Use an explicit runx.json path.");
        output.WriteLine("  --allow-parent-search   Allow config discovery beyond the safe search boundary.");
        output.WriteLine("  --dry-run               Print the resolved task without executing it.");
    }

    private static void WriteTaskList(LoadedRunxConfig config, TextWriter output)
    {
        IReadOnlyDictionary<string, RunxTask> tasks = config.VisibleTasks;
        if (tasks.Count == 0)
        {
            output.WriteLine($"No tasks defined in {config.Location}.");
            return;
        }

        int longestNameLength = tasks.Keys.Max(static taskName => taskName.Length);
        foreach (KeyValuePair<string, RunxTask> task in tasks.OrderBy(static task => task.Key, StringComparer.Ordinal))
        {
            string line = string.IsNullOrWhiteSpace(task.Value.Description)
                ? task.Key
                : $"{task.Key.PadRight(longestNameLength)}  {task.Value.Description}";

            if (config.Tag(task.Key) is string tag)
            {
                line += string.IsNullOrWhiteSpace(task.Value.Description)
                    ? $"{new string(' ', longestNameLength - task.Key.Length)}  ({tag})"
                    : $"  ({tag})";
            }

            output.WriteLine(line);
        }

        HashSet<string> usedPaths = config.Sources.Values.Select(static source => source.ConfigPath).ToHashSet(StringComparer.Ordinal);
        string[] scriptFolders = (config.TaskFolders?.Folders ?? [])
            .Where(folder => config.Sources.Values.Any(source => source.IsScript && IsInFolder(source.ConfigPath, folder)))
            .ToArray();
        string[] personalConfigPaths = (config.PersonalFiles ?? []).Where(usedPaths.Contains).ToArray();

        if (scriptFolders.Length > 0 || personalConfigPaths.Length > 0)
        {
            output.WriteLine();
            foreach (string scriptFolder in scriptFolders)
            {
                output.WriteLine($"Script tasks from {scriptFolder}");
            }

            foreach (string personalConfigPath in personalConfigPaths)
            {
                output.WriteLine($"Personal tasks from {personalConfigPath}");
            }
        }
    }

    private static bool IsInFolder(string path, string folder)
    {
        string relative = Path.GetRelativePath(folder, path);
        return !relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative);
    }

    private static void WriteWhere(
        ConfigSearchResult searchResult,
        PersonalTasksLookup? personal,
        PersonalTasksSettings? personalTasks,
        LoadedRunxConfig? config,
        bool explicitFile,
        string currentDirectory,
        TextWriter output)
    {
        string? repoRoot = RunxConfigDiscovery.FindGitRoot(Path.GetFullPath(currentDirectory));
        output.WriteLine($"Repo root:       {repoRoot ?? "(not inside a Git repo)"}");
        output.WriteLine($"Repo tasks:      {searchResult.ConfigPath ?? $"none. {searchResult.Message}"}");

        if (explicitFile)
        {
            output.WriteLine("Script tasks:    not used with --file");
            output.WriteLine("Personal tasks:  not used with --file");
            return;
        }

        if (config?.TaskFolders is not TaskFolderSettings taskFolders)
        {
            output.WriteLine("Script tasks:    unknown until the config errors are fixed; run 'runx --list' to see them");
        }
        else
        {
            output.WriteLine(taskFolders.SetBy is null
                ? "Script tasks:    default folder; set taskFolders to change it"
                : $"Script tasks:    folders from taskFolders in {taskFolders.SetBy}");

            foreach (string folder in taskFolders.Folders)
            {
                output.WriteLine($"  {folder}{(Directory.Exists(folder) ? string.Empty : " (not found)")}");
            }
        }

        if (personalTasks is null)
        {
            output.WriteLine($"Personal tasks:  unavailable because the user profile folder is unknown; set {PersonalTasksSettings.HomeVariable}");
            return;
        }

        output.WriteLine($"Runx home:       {personalTasks.RunxHome}");
        if (personal?.Identity is not RepoIdentity identity)
        {
            output.WriteLine("Personal tasks:  unavailable outside a Git repo");
            return;
        }

        output.WriteLine($"Repo key:        {identity.Key} (from {identity.KeySource})");
        output.WriteLine("Personal tasks:  broadest first; create one with 'runx --init --personal --scope <scope>'");

        int longestScopeLength = personal.Layers.Max(static layer => layer.Scope.Length);
        foreach (PersonalLayer layer in personal.Layers)
        {
            output.WriteLine($"  {layer.Scope.PadRight(longestScopeLength)}  {layer.Path}{(layer.Exists ? string.Empty : " (not created)")}");
        }
    }

    private static void WriteDryRun(List<ProcessRequest> requests, TextWriter output)
    {
        if (requests.Count > 1)
        {
            output.WriteLine($"Plan: {string.Join(" -> ", requests.Select(static request => request.TaskName))}");
        }

        foreach (ProcessRequest request in requests)
        {
            if (requests.Count > 1)
            {
                output.WriteLine();
            }

            WriteDryRun(request, output);
        }
    }

    private static void WriteDryRun(ProcessRequest request, TextWriter output)
    {
        output.WriteLine($"Task: {request.TaskName}");
        output.WriteLine($"Config: {request.ConfigPath}");
        output.WriteLine($"Mode: {request.Mode}");
        output.WriteLine($"Cwd: {request.WorkingDirectory}");
        output.WriteLine($"Command: {request.FileName}");

        if (request.Arguments.Count > 0)
        {
            output.WriteLine("Args:");
            foreach (string argument in request.Arguments)
            {
                output.WriteLine($"  {argument}");
            }
        }
        else
        {
            output.WriteLine("Args: (none)");
        }

        if (request.Environment.Count > 0)
        {
            output.WriteLine("Env:");
            foreach (string key in request.Environment.Keys.Order(StringComparer.Ordinal))
            {
                output.WriteLine($"  {key}");
            }
        }
        else
        {
            output.WriteLine("Env: (none)");
        }

        if (request.BatchFileContents is not null)
        {
            output.WriteLine("Batch file:");
            foreach (string line in request.BatchFileContents.ReplaceLineEndings("\n").TrimEnd('\n').Split('\n'))
            {
                output.WriteLine($"  {line}");
            }
        }
    }

    private static string GetVersion()
    {
        Assembly assembly = typeof(RunxCli).Assembly;
        AssemblyInformationalVersionAttribute? versionAttribute =
            assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();

        return versionAttribute?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "0.0.0";
    }
}
