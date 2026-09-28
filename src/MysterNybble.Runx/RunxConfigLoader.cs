using System.Text.Json;

namespace MysterNybble.Runx;

internal static class RunxConfigLoader
{
    public static Task<ConfigLoadResult> LoadAsync(string configPath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configPath);
        return LoadAsync(configPath, personalFiles: [], scriptRoot: null, cancellationToken);
    }

    // Merges, from most to least specific: the repo-level personal file, the repo's runx.json, script
    // tasks from the repo's task folders, then broader personal files. The repo-level personal file
    // must not reuse a repo or script task's name, runx.json wins over a script of the same name, and
    // both win over broad personal files. Personal and script tasks resolve cwd from the repo root.
    // Script tasks are discovered only when scriptRoot is given.
    public static async Task<ConfigLoadResult> LoadAsync(
        string? repoConfigPath,
        IReadOnlyList<PersonalTasksFile> personalFiles,
        string? scriptRoot,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(personalFiles);

        Dictionary<string, RunxTask> tasks = new(StringComparer.Ordinal);
        Dictionary<string, TaskSource> sources = new(StringComparer.Ordinal);
        List<string> locations = [];
        List<string> errors = [];

        FileLoadResult? repo = repoConfigPath is null ? null : await LoadFileAsync(repoConfigPath, cancellationToken);
        if (repo is not null)
        {
            errors.AddRange(repo.Errors);
            locations.Add(repo.Path);

            foreach (KeyValuePair<string, RunxTask> task in repo.Tasks ?? [])
            {
                tasks.Add(task.Key, task.Value);
                sources.Add(task.Key, new TaskSource(repo.Path, repo.Directory));
            }
        }

        List<(PersonalTasksFile File, FileLoadResult Result)> personal = [];
        foreach (PersonalTasksFile personalFile in personalFiles)
        {
            FileLoadResult result = await LoadFileAsync(personalFile.Path, cancellationToken);
            personal.Add((personalFile, result));
            locations.Add(result.Path);

            if (result.Errors.Count > 0)
            {
                errors.Add($"In personal tasks file {result.Path}:");
                errors.AddRange(result.Errors.Select(static error => $"  {error}"));
            }
        }

        TaskFolderSettings? taskFolders = null;
        if (scriptRoot is not null)
        {
            taskFolders = ResolveTaskFolders(scriptRoot, repo, personal);
            foreach (ScriptTask script in ScriptTasks.Discover(scriptRoot, taskFolders.Folders, OperatingSystem.IsWindows()))
            {
                if (!tasks.ContainsKey(script.Name))
                {
                    tasks.Add(script.Name, script.Task);
                    sources.Add(script.Name, new TaskSource(script.FilePath, scriptRoot, IsScript: true));
                }
            }

            locations.AddRange(taskFolders.Folders.Where(Directory.Exists));
        }

        foreach ((PersonalTasksFile personalFile, FileLoadResult result) in personal)
        {
            foreach (KeyValuePair<string, RunxTask> task in result.Tasks ?? [])
            {
                if (sources.TryGetValue(task.Key, out TaskSource? existing) && !existing.IsPersonal)
                {
                    if (personalFile.IsRepoLevel)
                    {
                        errors.Add($"Task '{task.Key}' is defined in both {existing.ConfigPath} and {result.Path}.");
                    }

                    continue;
                }

                tasks[task.Key] = task.Value;
                sources[task.Key] = new TaskSource(result.Path, personalFile.RepoRoot, personalFile.Tag, IsBroad: !personalFile.IsRepoLevel);
            }
        }

        Dictionary<string, string> unavailable = RemoveBroadTasksWithMissingDependencies(tasks, sources);

        errors.AddRange(RunxConfigValidator.ValidateDependencies(tasks, sources, checkCycles: errors.Count == 0));
        if (errors.Count > 0)
        {
            return new ConfigLoadResult(Config: null, errors);
        }

        return new ConfigLoadResult(
            new LoadedRunxConfig(
                tasks,
                sources,
                string.Join(" and ", locations),
                unavailable,
                personalFiles.Select(static file => Path.GetFullPath(file.Path)).ToArray(),
                taskFolders),
            Errors: []);
    }

    // The most specific file that sets taskFolders decides: the repo-level personal file, then the
    // repo's runx.json, then broader personal files. Folders in runx.json are relative to it; folders
    // in personal files are relative to the repo root.
    private static TaskFolderSettings ResolveTaskFolders(
        string scriptRoot,
        FileLoadResult? repo,
        List<(PersonalTasksFile File, FileLoadResult Result)> personal)
    {
        List<(List<string?>? Folders, string BaseDirectory, string SetBy)> candidates = [];
        candidates.AddRange(personal
            .Where(static item => item.File.IsRepoLevel)
            .Select(item => (item.Result.TaskFolders, scriptRoot, item.Result.Path)));

        if (repo is not null)
        {
            candidates.Add((repo.TaskFolders, repo.Directory, repo.Path));
        }

        candidates.AddRange(personal
            .Where(static item => !item.File.IsRepoLevel)
            .Reverse<(PersonalTasksFile File, FileLoadResult Result)>()
            .Select(item => (item.Result.TaskFolders, scriptRoot, item.Result.Path)));

        foreach ((List<string?>? folders, string baseDirectory, string setBy) in candidates)
        {
            if (folders is not null)
            {
                string[] resolved = folders
                    .OfType<string>()
                    .Where(static folder => folder.Trim().Length > 0)
                    .Select(folder => Path.GetFullPath(folder, baseDirectory))
                    .ToArray();

                return new TaskFolderSettings(resolved, setBy);
            }
        }

        return new TaskFolderSettings([Path.GetFullPath(ScriptTasks.DefaultFolder, scriptRoot)], SetBy: null);
    }

    // A global or remote-wide task may depend on tasks that only some repos define. In repos without
    // them it is hidden rather than making the whole config invalid.
    private static Dictionary<string, string> RemoveBroadTasksWithMissingDependencies(
        Dictionary<string, RunxTask> tasks,
        Dictionary<string, TaskSource> sources)
    {
        Dictionary<string, string> unavailable = new(StringComparer.Ordinal);
        bool removedAny;
        do
        {
            removedAny = false;
            foreach (KeyValuePair<string, RunxTask> task in tasks.ToArray())
            {
                TaskSource source = sources[task.Key];
                string? missing = source.IsBroad
                    ? task.Value?.DependsOn?.FirstOrDefault(dependency => dependency is not null && !tasks.ContainsKey(dependency))
                    : null;

                if (missing is null)
                {
                    continue;
                }

                string reason = unavailable.ContainsKey(missing)
                    ? $"it depends on '{missing}', which is not available here either"
                    : $"it depends on '{missing}', which this repo does not define";

                unavailable[task.Key] = $"Task '{task.Key}' from {source.ConfigPath} is not available in this repo because {reason}.";
                tasks.Remove(task.Key);
                sources.Remove(task.Key);
                removedAny = true;
            }
        }
        while (removedAny);

        return unavailable;
    }

    private static async Task<FileLoadResult> LoadFileAsync(string configPath, CancellationToken cancellationToken)
    {
        string fullPath = Path.GetFullPath(configPath);
        string configDirectory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException($"Could not determine config directory for {fullPath}.");

        string json;
        try
        {
            json = await File.ReadAllTextAsync(fullPath, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return FileLoadResult.Failed(fullPath, configDirectory, $"Failed to read {fullPath}: {ex.Message}");
        }

        RunxConfig? config;
        JsonDocument document;
        try
        {
            config = JsonSerializer.Deserialize(json, RunxJsonSerializerContext.Default.RunxConfig);
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            // Some messages, such as the one for duplicate properties, omit where the problem is.
            string location = ex.LineNumber is null || ex.Message.Contains("LineNumber", StringComparison.Ordinal)
                ? string.Empty
                : $" (at {ex.Path}, line {ex.LineNumber + 1})";

            return FileLoadResult.Failed(fullPath, configDirectory, $"Failed to parse {fullPath}: {ex.Message}{location}");
        }

        List<string> errors;
        using (document)
        {
            errors = RunxConfigValidator.Validate(config, document.RootElement, fullPath);
        }

        return new FileLoadResult(fullPath, configDirectory, config?.Tasks, errors, config?.TaskFolders);
    }

    private sealed record FileLoadResult(
        string Path,
        string Directory,
        Dictionary<string, RunxTask>? Tasks,
        IReadOnlyList<string> Errors,
        List<string?>? TaskFolders = null)
    {
        public static FileLoadResult Failed(string path, string directory, string error)
        {
            return new FileLoadResult(path, directory, Tasks: null, [error]);
        }
    }
}

internal sealed record ConfigLoadResult(LoadedRunxConfig? Config, IReadOnlyList<string> Errors)
{
    public static ConfigLoadResult Failed(string error)
    {
        return new ConfigLoadResult(Config: null, [error]);
    }
}
