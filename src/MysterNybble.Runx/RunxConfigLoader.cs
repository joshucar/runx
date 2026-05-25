using System.Text.Json;

namespace MysterNybble.Runx;

internal static class RunxConfigLoader
{
    public static async Task<ConfigLoadResult> LoadAsync(string configPath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configPath);

        string fullPath = Path.GetFullPath(configPath);
        string json;
        try
        {
            json = await File.ReadAllTextAsync(fullPath, cancellationToken);
        }
        catch (IOException ex)
        {
            return ConfigLoadResult.Failed($"Failed to read {fullPath}: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            return ConfigLoadResult.Failed($"Failed to read {fullPath}: {ex.Message}");
        }

        RunxConfig? config;
        try
        {
            config = JsonSerializer.Deserialize(json, RunxJsonSerializerContext.Default.RunxConfig);
        }
        catch (JsonException ex)
        {
            return ConfigLoadResult.Failed($"Failed to parse {fullPath}: {ex.Message}");
        }

        List<string> errors = RunxConfigValidator.Validate(config, fullPath);
        if (config is null || config.Tasks is null || errors.Count > 0)
        {
            return new ConfigLoadResult(Config: null, Errors: errors);
        }

        string configDirectory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException($"Could not determine config directory for {fullPath}.");

        return new ConfigLoadResult(
            new LoadedRunxConfig(fullPath, configDirectory, config.Tasks),
            Errors: []);
    }
}

internal sealed record ConfigLoadResult(LoadedRunxConfig? Config, IReadOnlyList<string> Errors)
{
    public static ConfigLoadResult Failed(string error)
    {
        return new ConfigLoadResult(Config: null, [error]);
    }
}
