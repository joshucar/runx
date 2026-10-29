using System.Text.Json;

namespace MysterNybble.Runx;

internal static class RunxInitializer
{
    private const string SchemaUrl = "https://raw.githubusercontent.com/joshucar/runx/master/schemas/v1/runx.schema.json";

    public static async Task<int> InitializeAsync(
        CliParseResult parseResult,
        string currentDirectory,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parseResult);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentDirectory);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        string targetPath = string.IsNullOrWhiteSpace(parseResult.FilePath)
            ? Path.Combine(Path.GetFullPath(currentDirectory), RunxConfigDiscovery.ConfigFileName)
            : Path.GetFullPath(parseResult.FilePath, currentDirectory);

        string? parentDirectory = Path.GetDirectoryName(targetPath);
        if (parentDirectory is null || !Directory.Exists(parentDirectory))
        {
            error.WriteLine($"Cannot create {targetPath} because its parent directory does not exist.");
            return 1;
        }

        RunxTask starterTask = new()
        {
            Description = "Print the .NET SDK version.",
            Command = "dotnet",
            Args = ["--version"]
        };

        int exitCode = await WriteStarterAsync(targetPath, "dotnet-version", starterTask, parseResult.Force, error, cancellationToken);
        if (exitCode == 0)
        {
            output.WriteLine($"Created {targetPath}.");
        }

        return exitCode;
    }

    public static async Task<int> InitializePersonalAsync(
        CliParseResult parseResult,
        string currentDirectory,
        PersonalTasksSettings? personalTasks,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parseResult);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentDirectory);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        if (parseResult.FilePath is not null)
        {
            error.WriteLine("--personal cannot be combined with --file.");
            return 1;
        }

        if (personalTasks is null)
        {
            error.WriteLine($"Personal tasks are not available because the user profile folder is unknown. Set {PersonalTasksSettings.HomeVariable}.");
            return 1;
        }

        PersonalTasksLookup? lookup = await PersonalTasks.FindAsync(
            personalTasks,
            currentDirectory,
            evenWithoutPersonalFiles: true,
            cancellationToken);

        if (lookup is null)
        {
            error.WriteLine($"Personal tasks belong to a Git repo, and {Path.GetFullPath(currentDirectory)} is not inside one.");
            return 1;
        }

        string scope = parseResult.Scope ?? PersonalLayer.RepoScope;
        PersonalLayer? layer = lookup.Layers.FirstOrDefault(candidate => candidate.Scope == scope);
        if (layer is null)
        {
            string scopes = string.Join(", ", lookup.Layers.Select(static layer => layer.Scope));
            error.WriteLine($"Unknown scope '{scope}'. Scopes for this repo, broadest first: {scopes}.");
            return 1;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(layer.Path)!);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error.WriteLine($"Failed to create the folder for {layer.Path}: {ex.Message}");
            return 1;
        }

        RunxTask starterTask = new()
        {
            Description = "Show the branch and changed files (an example personal task).",
            Command = "git",
            Args = ["status", "--short", "--branch"]
        };

        int exitCode = await WriteStarterAsync(layer.Path, "my:status", starterTask, parseResult.Force, error, cancellationToken);
        if (exitCode == 0)
        {
            string appliesTo = layer.IsRepoLevel
                ? $"{layer.KeyPrefix} in every worktree"
                : layer.KeyPrefix is null ? "every repo" : $"every repo under {layer.KeyPrefix}";

            output.WriteLine($"Created {layer.Path}.");
            output.WriteLine($"Tasks in it are yours alone and apply to {appliesTo}.");
        }

        return exitCode;
    }

    private static async Task<int> WriteStarterAsync(
        string targetPath,
        string taskName,
        RunxTask task,
        bool force,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        if (Directory.Exists(targetPath))
        {
            error.WriteLine($"Cannot create {targetPath} because it is a directory.");
            return 1;
        }

        if (File.Exists(targetPath) && !force)
        {
            error.WriteLine($"{targetPath} already exists. Use --force to overwrite it.");
            return 1;
        }

        RunxConfig config = new()
        {
            Schema = SchemaUrl,
            Version = 1,
            Tasks = new Dictionary<string, RunxTask>(StringComparer.Ordinal) { [taskName] = task }
        };

        string json = JsonSerializer.Serialize(config, RunxJsonSerializerContext.Default.RunxConfig);
        try
        {
            await File.WriteAllTextAsync(targetPath, json + Environment.NewLine, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error.WriteLine($"Failed to write {targetPath}: {ex.Message}");
            return 1;
        }

        return 0;
    }
}
