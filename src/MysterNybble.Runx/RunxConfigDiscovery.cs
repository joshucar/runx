namespace MysterNybble.Runx;

internal static class RunxConfigDiscovery
{
    public const string ConfigFileName = "runx.json";

    public static ConfigSearchResult FindConfig(
        string? explicitFilePath,
        bool allowParentSearch,
        string currentDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentDirectory);

        if (!string.IsNullOrWhiteSpace(explicitFilePath))
        {
            string explicitFullPath = Path.GetFullPath(explicitFilePath, currentDirectory);
            return File.Exists(explicitFullPath)
                ? ConfigSearchResult.Found(explicitFullPath)
                : ConfigSearchResult.NotFound($"Config file not found: {explicitFullPath}");
        }

        string startDirectory = Path.GetFullPath(currentDirectory);
        string? gitRoot = FindGitRoot(startDirectory);

        if (gitRoot is not null)
        {
            string? configPath = SearchAncestors(startDirectory, allowParentSearch ? null : gitRoot);
            if (configPath is not null)
            {
                return ConfigSearchResult.Found(configPath);
            }

            string boundaryMessage = allowParentSearch
                ? $"No {ConfigFileName} found from {startDirectory} to the filesystem root."
                : $"No {ConfigFileName} found from {startDirectory} to Git repo root {gitRoot}.";

            return ConfigSearchResult.NotFound(boundaryMessage);
        }

        if (!allowParentSearch)
        {
            string currentConfigPath = Path.Combine(startDirectory, ConfigFileName);
            return File.Exists(currentConfigPath)
                ? ConfigSearchResult.Found(currentConfigPath)
                : ConfigSearchResult.NotFound($"No {ConfigFileName} found in {startDirectory}.");
        }

        string? parentConfigPath = SearchAncestors(startDirectory, boundaryDirectory: null);
        return parentConfigPath is not null
            ? ConfigSearchResult.Found(parentConfigPath)
            : ConfigSearchResult.NotFound($"No {ConfigFileName} found from {startDirectory} to the filesystem root.");
    }

    private static string? SearchAncestors(string startDirectory, string? boundaryDirectory)
    {
        DirectoryInfo? current = new(startDirectory);
        string? normalizedBoundary = boundaryDirectory is null
            ? null
            : Path.GetFullPath(boundaryDirectory).TrimEndingDirectorySeparator();

        while (current is not null)
        {
            string currentPath = current.FullName.TrimEndingDirectorySeparator();
            string candidate = Path.Combine(current.FullName, ConfigFileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            if (normalizedBoundary is not null && PathsEqual(currentPath, normalizedBoundary))
            {
                break;
            }

            current = current.Parent;
        }

        return null;
    }

    private static string? FindGitRoot(string startDirectory)
    {
        DirectoryInfo? current = new(startDirectory);
        while (current is not null)
        {
            string gitPath = Path.Combine(current.FullName, ".git");
            if (Directory.Exists(gitPath) || File.Exists(gitPath))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return null;
    }

    private static string TrimEndingDirectorySeparator(this string path)
    {
        return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static bool PathsEqual(string left, string right)
    {
        return StringComparer.OrdinalIgnoreCase.Equals(left, right);
    }
}

internal sealed record ConfigSearchResult(string? ConfigPath, string Message)
{
    public static ConfigSearchResult Found(string configPath)
    {
        return new ConfigSearchResult(configPath, string.Empty);
    }

    public static ConfigSearchResult NotFound(string message)
    {
        return new ConfigSearchResult(ConfigPath: null, message);
    }
}
