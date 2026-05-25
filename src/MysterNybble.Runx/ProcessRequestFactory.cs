namespace MysterNybble.Runx;

internal static class ProcessRequestFactory
{
    public static bool IsSupportedShell(string shell)
    {
        return shell is "cmd" or "pwsh" or "sh";
    }

    public static ProcessRequestBuildResult Build(
        LoadedRunxConfig config,
        string taskName,
        RunxTask task,
        IReadOnlyList<string> passthroughArguments)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrWhiteSpace(taskName);
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(passthroughArguments);

        string workingDirectory = ResolveWorkingDirectory(config.Directory, task.Cwd);
        Dictionary<string, string> environment = BuildEnvironment(task);

        if (!string.IsNullOrWhiteSpace(task.Command))
        {
            List<string> arguments = [];
            if (task.Args is not null)
            {
                foreach (string? argument in task.Args)
                {
                    if (argument is not null)
                    {
                        arguments.Add(argument);
                    }
                }
            }

            arguments.AddRange(passthroughArguments);
            return new ProcessRequestBuildResult(
                new ProcessRequest(
                    taskName,
                    config.Path,
                    "process",
                    task.Command,
                    arguments,
                    workingDirectory,
                    environment),
                Errors: []);
        }

        if (passthroughArguments.Count > 0)
        {
            return ProcessRequestBuildResult.Failed(
                $"Task '{taskName}' is a shell task. Argument passthrough is only supported for command tasks.");
        }

        if (string.IsNullOrWhiteSpace(task.Shell) || string.IsNullOrWhiteSpace(task.Script))
        {
            return ProcessRequestBuildResult.Failed($"Task '{taskName}' is not executable.");
        }

        ShellInvocation shellInvocation = GetShellInvocation(task.Shell, task.Script);
        return new ProcessRequestBuildResult(
            new ProcessRequest(
                taskName,
                config.Path,
                $"shell:{task.Shell}",
                shellInvocation.FileName,
                shellInvocation.Arguments,
                workingDirectory,
                environment),
            Errors: []);
    }

    private static string ResolveWorkingDirectory(string configDirectory, string? taskCwd)
    {
        return string.IsNullOrWhiteSpace(taskCwd)
            ? configDirectory
            : Path.GetFullPath(taskCwd, configDirectory);
    }

    private static Dictionary<string, string> BuildEnvironment(RunxTask task)
    {
        Dictionary<string, string> environment = new(StringComparer.Ordinal);
        if (task.Env is null)
        {
            return environment;
        }

        foreach (KeyValuePair<string, string?> env in task.Env)
        {
            if (env.Value is not null)
            {
                environment[env.Key] = env.Value;
            }
        }

        return environment;
    }

    private static ShellInvocation GetShellInvocation(string shell, string script)
    {
        return shell switch
        {
            "cmd" => new ShellInvocation("cmd.exe", ["/d", "/s", "/c", script]),
            "pwsh" => new ShellInvocation("pwsh", ["-NoLogo", "-NoProfile", "-NonInteractive", "-Command", script]),
            "sh" => new ShellInvocation("sh", ["-c", script]),
            _ => throw new InvalidOperationException($"Unsupported shell '{shell}'.")
        };
    }

    private sealed record ShellInvocation(string FileName, IReadOnlyList<string> Arguments);
}
