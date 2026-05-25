namespace MysterNybble.Runx;

internal static class RunxConfigValidator
{
    public static List<string> Validate(RunxConfig? config, string configPath)
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

        if (config.Tasks is null)
        {
            errors.Add("Config property 'tasks' is required.");
            return errors;
        }

        foreach (KeyValuePair<string, RunxTask> task in config.Tasks)
        {
            ValidateTask(task.Key, task.Value, errors);
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

        bool hasCommand = !string.IsNullOrWhiteSpace(task.Command);
        bool hasScript = !string.IsNullOrWhiteSpace(task.Script);

        if (hasCommand == hasScript)
        {
            errors.Add($"Task '{taskName}' must define exactly one of 'command' or 'script'.");
        }

        if (hasCommand && !string.IsNullOrWhiteSpace(task.Shell))
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

                if (env.Value is null)
                {
                    errors.Add($"Task '{taskName}' environment variable '{env.Key}' must be a string.");
                }
            }
        }

        if (task.Cwd is not null && string.IsNullOrWhiteSpace(task.Cwd))
        {
            errors.Add($"Task '{taskName}' cwd must not be empty.");
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

    private static bool IsValidTaskName(string taskName)
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
