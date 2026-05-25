using System.Reflection;

namespace MysterNybble.Runx;

internal static class RunxCli
{
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
            Directory.GetCurrentDirectory(),
            cancellationToken);
    }

    internal static async Task<int> RunAsync(
        IReadOnlyList<string> args,
        TextWriter output,
        TextWriter error,
        IProcessRunner processRunner,
        string currentDirectory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(processRunner);
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

        if (parseResult.Init)
        {
            return await RunxInitializer.InitializeAsync(parseResult, currentDirectory, output, error, cancellationToken);
        }

        if (parseResult.Force)
        {
            error.WriteLine("--force can only be used with --init.");
            return 1;
        }

        if (parseResult.DryRun && parseResult.TaskName is null)
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

        if (searchResult.ConfigPath is null)
        {
            if (parseResult.TaskName is null && !parseResult.List)
            {
                WriteHelp(output);
                output.WriteLine();
                output.WriteLine(searchResult.Message);
                output.WriteLine("Run 'runx --init' to create a starter runx.json.");
                return 0;
            }

            error.WriteLine(searchResult.Message);
            error.WriteLine("Run 'runx --init' to create a starter runx.json.");
            return 1;
        }

        ConfigLoadResult loadResult = await RunxConfigLoader.LoadAsync(searchResult.ConfigPath, cancellationToken);
        if (loadResult.Errors.Count > 0 || loadResult.Config is null)
        {
            foreach (string loadError in loadResult.Errors)
            {
                error.WriteLine(loadError);
            }

            return 1;
        }

        if (parseResult.TaskName is null)
        {
            WriteTaskList(loadResult.Config, output);
            return 0;
        }

        if (!loadResult.Config.Tasks.TryGetValue(parseResult.TaskName, out RunxTask? task))
        {
            error.WriteLine($"Task '{parseResult.TaskName}' was not found in {loadResult.Config.Path}.");
            return 1;
        }

        ProcessRequestBuildResult requestResult = ProcessRequestFactory.Build(
            loadResult.Config,
            parseResult.TaskName,
            task,
            parseResult.TaskArguments);

        if (requestResult.Errors.Count > 0 || requestResult.Request is null)
        {
            foreach (string requestError in requestResult.Errors)
            {
                error.WriteLine(requestError);
            }

            return 1;
        }

        if (parseResult.DryRun)
        {
            WriteDryRun(requestResult.Request, output);
            return 0;
        }

        return await processRunner.RunAsync(requestResult.Request, output, error, cancellationToken);
    }

    private static void WriteHelp(TextWriter output)
    {
        output.WriteLine("runx - repo command runner for .NET projects");
        output.WriteLine();
        output.WriteLine("Usage:");
        output.WriteLine("  runx [options]");
        output.WriteLine("  runx [options] <task> [-- <args>...]");
        output.WriteLine();
        output.WriteLine("Options:");
        output.WriteLine("  --help                  Show help.");
        output.WriteLine("  --version               Show version.");
        output.WriteLine("  --list                  List tasks from runx.json.");
        output.WriteLine("  --init                  Create a starter runx.json.");
        output.WriteLine("  --force                 Overwrite an existing runx.json with --init.");
        output.WriteLine("  --file <path>           Use an explicit runx.json path.");
        output.WriteLine("  --allow-parent-search   Allow config discovery beyond the safe search boundary.");
        output.WriteLine("  --dry-run               Print the resolved task without executing it.");
    }

    private static void WriteTaskList(LoadedRunxConfig config, TextWriter output)
    {
        if (config.Tasks.Count == 0)
        {
            output.WriteLine($"No tasks defined in {config.Path}.");
            return;
        }

        int longestNameLength = config.Tasks.Keys.Max(static taskName => taskName.Length);
        foreach (KeyValuePair<string, RunxTask> task in config.Tasks.OrderBy(static task => task.Key, StringComparer.Ordinal))
        {
            string description = string.IsNullOrWhiteSpace(task.Value.Description)
                ? string.Empty
                : $"  {task.Value.Description}";

            output.WriteLine($"{task.Key.PadRight(longestNameLength)}{description}");
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
