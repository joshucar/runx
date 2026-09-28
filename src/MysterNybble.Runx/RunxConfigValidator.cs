using System.Text.Json;

namespace MysterNybble.Runx;

internal static class RunxConfigValidator
{
    private static readonly HashSet<string> s_taskPropertyNames = new(StringComparer.Ordinal)
    {
        "description", "command", "args", "script", "shell", "cwd", "env", "dependsOn", "hide"
    };

    public static List<string> Validate(RunxConfig? config, JsonElement root, string configPath)
    {
        List<string> errors = [];
        if (config is null)
        {
            errors.Add($"{configPath} must contain a JSON object.");
            return errors;
        }

        AddUnknownPropertyErrors("$", config.ExtensionData, errors);

        if (config.Version is null)
        {
            errors.Add("Config property 'version' is required.");
        }
        else if (config.Version != 1)
        {
            errors.Add("Config property 'version' must be 1.");
        }

        ValidateTaskFolders(config.TaskFolders, root, errors);

        if (config.Tasks is null)
        {
            errors.Add("Config property 'tasks' is required.");
            return errors;
        }

        foreach (KeyValuePair<string, RunxTask> task in config.Tasks)
        {
            ValidateTask(task.Key, task.Value, errors);
        }

        AddExplicitNullErrors(root, errors);
        return errors;
    }

    // Checked across every loaded file, because personal tasks may depend on the repo's tasks.
    // Repo tasks may only depend on repo tasks, since teammates do not have your personal file.
    public static List<string> ValidateDependencies(
        IReadOnlyDictionary<string, RunxTask> tasks,
        IReadOnlyDictionary<string, TaskSource> sources,
        bool checkCycles)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        ArgumentNullException.ThrowIfNull(sources);

        List<string> errors = [];
        foreach (KeyValuePair<string, RunxTask> task in tasks)
        {
            foreach (string? dependency in task.Value?.DependsOn ?? [])
            {
                if (dependency is null)
                {
                    continue;
                }

                if (!tasks.ContainsKey(dependency))
                {
                    errors.Add($"Task '{task.Key}' depends on unknown task '{dependency}'.");
                }
                else if (!sources[task.Key].IsPersonal && sources[dependency].IsPersonal)
                {
                    errors.Add($"Task '{task.Key}' in {sources[task.Key].ConfigPath} cannot depend on personal task '{dependency}'.");
                }
            }
        }

        if (checkCycles && errors.Count == 0)
        {
            AddDependencyCycleErrors(tasks, errors);
        }

        return errors;
    }

    private static void ValidateTask(string taskName, RunxTask? task, List<string> errors)
    {
        if (!IsValidTaskName(taskName))
        {
            errors.Add($"Task name '{taskName}' is invalid. Use letters, digits, '_', '-', '.', or ':', and do not start with '-'.");
        }

        if (task is null)
        {
            errors.Add($"Task '{taskName}' must be a JSON object.");
            return;
        }

        AddUnknownPropertyErrors($"tasks.{taskName}", task.ExtensionData, errors);

        // Presence, not content, decides the task kind, so "command": "" is an error rather than a
        // dependsOn-only task.
        bool hasCommand = task.Command is not null;
        bool hasScript = task.Script is not null;

        if (hasCommand && string.IsNullOrWhiteSpace(task.Command))
        {
            errors.Add($"Task '{taskName}' command must not be empty.");
        }

        if (hasScript && string.IsNullOrWhiteSpace(task.Script))
        {
            errors.Add($"Task '{taskName}' script must not be empty.");
        }

        if (hasCommand && hasScript)
        {
            errors.Add($"Task '{taskName}' must define exactly one of 'command' or 'script'.");
        }
        else if (!hasCommand && !hasScript)
        {
            if (task.DependsOn is null)
            {
                errors.Add($"Task '{taskName}' must define 'command', 'script', or 'dependsOn'.");
            }

            AddDependsOnOnlyErrors(taskName, task, errors);
        }

        ValidateDependsOn(taskName, task.DependsOn, errors);

        if (hasCommand && task.Shell is not null)
        {
            errors.Add($"Task '{taskName}' cannot define 'shell' when using 'command'.");
        }

        if (hasScript && string.IsNullOrWhiteSpace(task.Shell))
        {
            errors.Add($"Task '{taskName}' must define 'shell' when using 'script'.");
        }

        if (hasScript && task.Args is not null)
        {
            errors.Add($"Task '{taskName}' cannot define 'args' when using 'script'.");
        }

        if (!string.IsNullOrWhiteSpace(task.Shell) && !ProcessRequestFactory.IsSupportedShell(task.Shell))
        {
            errors.Add($"Task '{taskName}' has unsupported shell '{task.Shell}'. Supported shells are cmd, pwsh, and sh.");
        }

        if (task.Args is not null)
        {
            for (int i = 0; i < task.Args.Count; i++)
            {
                if (task.Args[i] is null)
                {
                    errors.Add($"Task '{taskName}' args[{i}] must be a string.");
                }

                AddNulCharacterError(taskName, $"args[{i}]", task.Args[i], errors);
            }
        }

        if (task.Env is not null)
        {
            foreach (KeyValuePair<string, string?> env in task.Env)
            {
                if (string.IsNullOrWhiteSpace(env.Key))
                {
                    errors.Add($"Task '{taskName}' has an empty environment variable name.");
                }
                else if (env.Key.Contains('=', StringComparison.Ordinal))
                {
                    errors.Add($"Task '{taskName}' environment variable name '{env.Key}' cannot contain '='.");
                }

                if (env.Value is null)
                {
                    errors.Add($"Task '{taskName}' environment variable '{env.Key}' must be a string.");
                }

                AddNulCharacterError(taskName, "environment variable name", env.Key, errors);
                AddNulCharacterError(taskName, $"environment variable '{env.Key}'", env.Value, errors);
            }
        }

        AddNulCharacterError(taskName, "command", task.Command, errors);
        AddNulCharacterError(taskName, "script", task.Script, errors);
        AddNulCharacterError(taskName, "cwd", task.Cwd, errors);

        if (task.Cwd is not null && string.IsNullOrWhiteSpace(task.Cwd))
        {
            errors.Add($"Task '{taskName}' cwd must not be empty.");
        }
    }

    private static void ValidateTaskFolders(List<string?>? taskFolders, JsonElement root, List<string> errors)
    {
        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("taskFolders", out JsonElement value)
            && value.ValueKind == JsonValueKind.Null)
        {
            errors.Add("Config property 'taskFolders' must not be null. Omit it to use the default folders.");
        }

        for (int i = 0; i < (taskFolders?.Count ?? 0); i++)
        {
            string? folder = taskFolders![i];
            if (string.IsNullOrWhiteSpace(folder))
            {
                errors.Add($"Config property 'taskFolders[{i}]' must be a non-empty string.");
            }
            else if (folder.Contains('\0', StringComparison.Ordinal))
            {
                errors.Add($"Config property 'taskFolders[{i}]' must not contain NUL characters.");
            }
        }
    }

    // Deserialization cannot tell "command": null from an omitted command, and an omitted command is
    // how a dependsOn-only task is written, so explicit nulls are rejected from the raw JSON.
    private static void AddExplicitNullErrors(JsonElement root, List<string> errors)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("tasks", out JsonElement tasks)
            || tasks.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (JsonProperty task in tasks.EnumerateObject())
        {
            if (task.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            foreach (JsonProperty property in task.Value.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.Null && s_taskPropertyNames.Contains(property.Name))
                {
                    errors.Add($"Task '{task.Name}' {property.Name} must not be null. Omit it instead.");
                }
            }
        }
    }

    // Operating systems end strings at NUL, so a NUL would silently truncate the command line.
    private static void AddNulCharacterError(string taskName, string property, string? value, List<string> errors)
    {
        if (value is not null && value.Contains('\0', StringComparison.Ordinal))
        {
            errors.Add($"Task '{taskName}' {property} must not contain NUL characters.");
        }
    }

    private static void AddDependsOnOnlyErrors(string taskName, RunxTask task, List<string> errors)
    {
        (string Name, bool IsSet)[] properties =
        [
            ("args", task.Args is not null),
            ("shell", task.Shell is not null),
            ("cwd", task.Cwd is not null),
            ("env", task.Env is not null)
        ];

        foreach ((string name, bool isSet) in properties)
        {
            if (isSet)
            {
                errors.Add($"Task '{taskName}' cannot define '{name}' without a 'command' or 'script'.");
            }
        }
    }

    private static void ValidateDependsOn(string taskName, List<string?>? dependsOn, List<string> errors)
    {
        if (dependsOn is null)
        {
            return;
        }

        if (dependsOn.Count == 0)
        {
            errors.Add($"Task '{taskName}' dependsOn must not be empty.");
            return;
        }

        HashSet<string> seen = new(StringComparer.Ordinal);
        for (int i = 0; i < dependsOn.Count; i++)
        {
            string? dependency = dependsOn[i];
            if (dependency is null)
            {
                errors.Add($"Task '{taskName}' dependsOn[{i}] must be a string.");
            }
            else if (!seen.Add(dependency))
            {
                errors.Add($"Task '{taskName}' lists '{dependency}' in dependsOn more than once.");
            }
        }
    }

    private static void AddDependencyCycleErrors(IReadOnlyDictionary<string, RunxTask> tasks, List<string> errors)
    {
        HashSet<string> finished = new(StringComparer.Ordinal);
        List<string> path = [];

        foreach (string taskName in tasks.Keys.Order(StringComparer.Ordinal))
        {
            Visit(taskName);
        }

        void Visit(string taskName)
        {
            if (finished.Contains(taskName))
            {
                return;
            }

            int cycleStart = path.IndexOf(taskName);
            if (cycleStart >= 0)
            {
                IEnumerable<string> cycle = path.Skip(cycleStart).Append(taskName);
                errors.Add($"Task dependencies form a cycle: {string.Join(" -> ", cycle)}.");
                return;
            }

            path.Add(taskName);
            foreach (string? dependency in tasks[taskName].DependsOn ?? [])
            {
                Visit(dependency!);
            }

            path.RemoveAt(path.Count - 1);
            finished.Add(taskName);
        }
    }

    private static void AddUnknownPropertyErrors(
        string path,
        Dictionary<string, System.Text.Json.JsonElement>? extensionData,
        List<string> errors)
    {
        if (extensionData is null)
        {
            return;
        }

        foreach (string propertyName in extensionData.Keys.Order(StringComparer.Ordinal))
        {
            errors.Add($"Unknown config property '{path}.{propertyName}'.");
        }
    }

    public static bool IsValidTaskName(string taskName)
    {
        if (string.IsNullOrWhiteSpace(taskName) || taskName[0] == '-')
        {
            return false;
        }

        foreach (char character in taskName)
        {
            if (char.IsAsciiLetterOrDigit(character)
                || character is '_' or '-' or '.' or ':')
            {
                continue;
            }

            return false;
        }

        return true;
    }
}
