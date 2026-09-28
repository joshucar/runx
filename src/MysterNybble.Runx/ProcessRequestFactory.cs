namespace MysterNybble.Runx;

internal static class ProcessRequestFactory
{
    public static bool IsSupportedShell(string shell)
    {
        return shell is "cmd" or "pwsh" or "sh";
    }

    public static bool IsRunnable(RunxTask task)
    {
        ArgumentNullException.ThrowIfNull(task);
        return task.Command is not null || task.Script is not null;
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

        TaskSource source = config.Sources[taskName];
        string? workingDirectory = ResolveWorkingDirectory(source.BaseDirectory, task.Cwd);
        if (workingDirectory is null)
        {
            return ProcessRequestBuildResult.Failed($"Task '{taskName}' cwd '{task.Cwd}' is not a valid path.");
        }

        Dictionary<string, string> environment = BuildEnvironment(task);

        if (task.Command is not null)
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
                    source.ConfigPath,
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

        if (task.Shell is null || task.Script is null)
        {
            return ProcessRequestBuildResult.Failed($"Task '{taskName}' is not executable.");
        }

        ShellInvocation shellInvocation = GetShellInvocation(task.Shell, task.Script);
        return new ProcessRequestBuildResult(
            new ProcessRequest(
                taskName,
                source.ConfigPath,
                $"shell:{task.Shell}",
                shellInvocation.FileName,
                shellInvocation.Arguments,
                workingDirectory,
                environment,
                shellInvocation.VerbatimArguments,
                shellInvocation.BatchFileContents),
            Errors: []);
    }

    private static string? ResolveWorkingDirectory(string configDirectory, string? taskCwd)
    {
        if (string.IsNullOrWhiteSpace(taskCwd))
        {
            return configDirectory;
        }

        try
        {
            return Path.GetFullPath(taskCwd, configDirectory);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
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
            "cmd" => GetCmdInvocation(script),
            "pwsh" => new ShellInvocation("pwsh", ["-NoLogo", "-NoProfile", "-Command", script]),
            "sh" => new ShellInvocation("sh", ["-c", script]),
            _ => throw new InvalidOperationException($"Unsupported shell '{shell}'.")
        };
    }

    private static ShellInvocation GetCmdInvocation(string script)
    {
        // cmd /c only runs the first line of a command line, so multi-line scripts run as a temporary
        // batch file that the process runner writes just before it starts cmd.exe.
        if (script.Contains('\n', StringComparison.Ordinal) || script.Contains('\r', StringComparison.Ordinal))
        {
            string[] lines = script.ReplaceLineEndings("\n").Split('\n');
            string contents = string.Join("\r\n", ["@echo off", .. lines]) + "\r\n";
            return new ShellInvocation(
                WindowsCommandLine.CmdPath,
                [.. WindowsCommandLine.CmdSwitches, "\"<temporary batch file>\""],
                BatchFileContents: contents);
        }

        // cmd.exe does not understand the backslash-escaped quotes used for normal arguments, so a
        // one-line script goes through verbatim inside the quotes that /s strips.
        return new ShellInvocation(
            WindowsCommandLine.CmdPath,
            [.. WindowsCommandLine.CmdSwitches, $"\"{script}\""],
            VerbatimArguments: true);
    }

    private sealed record ShellInvocation(
        string FileName,
        IReadOnlyList<string> Arguments,
        bool VerbatimArguments = false,
        string? BatchFileContents = null);
}
