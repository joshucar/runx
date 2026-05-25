using System.Text.Json;

namespace MysterNybble.Runx;

internal static class RunxInitializer
{
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

        if (File.Exists(targetPath) && !parseResult.Force)
        {
            error.WriteLine($"{targetPath} already exists. Use --force to overwrite it.");
            return 1;
        }

        RunxConfig config = new()
        {
            Schema = "https://schemas.mysternybble.com/runx/v1/runx.schema.json",
            Version = 1,
            Tasks = new Dictionary<string, RunxTask>(StringComparer.Ordinal)
            {
                ["dotnet-version"] = new RunxTask
                {
                    Description = "Print the .NET SDK version.",
                    Command = "dotnet",
                    Args = ["--version"]
                }
            }
        };

        string json = JsonSerializer.Serialize(config, RunxJsonSerializerContext.Default.RunxConfig);
        await File.WriteAllTextAsync(targetPath, json + Environment.NewLine, cancellationToken);
        output.WriteLine($"Created {targetPath}.");
        return 0;
    }
}
