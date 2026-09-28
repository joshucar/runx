namespace MysterNybble.Runx;

internal static class CommandResolver
{
    private const string DefaultPathExtensions = ".COM;.EXE;.BAT;.CMD";
    private const UnixFileMode ExecuteModes = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;

    // Bare names are looked up only on the task's effective PATH. A name that is not found there fails
    // instead of falling back to Process.Start, which would search runx's own PATH and current directory.
    // Relative PATH entries are skipped so files in the repo cannot shadow tools.
    public static string? Resolve(
        string command,
        string workingDirectory,
        string? pathVariable,
        string? pathExtVariable,
        bool isWindows)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        string[] extensions = isWindows ? GetPathExtensions(pathExtVariable) : [];

        if (Path.IsPathFullyQualified(command) || ContainsDirectorySeparator(command, isWindows))
        {
            string candidate = Path.GetFullPath(command, workingDirectory);
            return FindExecutable(candidate, extensions) ?? candidate;
        }

        if (string.IsNullOrWhiteSpace(pathVariable))
        {
            return null;
        }

        foreach (string directory in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string trimmedDirectory = directory.Trim().Trim('"');
            if (trimmedDirectory.Length == 0 || !Path.IsPathFullyQualified(trimmedDirectory))
            {
                continue;
            }

            string? resolved = FindExecutable(Path.Combine(trimmedDirectory, command), extensions);
            if (resolved is not null)
            {
                return resolved;
            }
        }

        return null;
    }

    private static string? FindExecutable(string candidate, string[] extensions)
    {
        if (extensions.Length == 0)
        {
            return IsExecutableFile(candidate) ? candidate : null;
        }

        if (Path.HasExtension(candidate) && File.Exists(candidate))
        {
            return candidate;
        }

        foreach (string extension in extensions)
        {
            string candidateWithExtension = candidate + extension;
            if (File.Exists(candidateWithExtension))
            {
                return candidateWithExtension;
            }
        }

        return null;
    }

    private static bool IsExecutableFile(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        // Tests run Unix-style lookups on Windows too, where there are no execute bits to check.
        return OperatingSystem.IsWindows() || (File.GetUnixFileMode(path) & ExecuteModes) != 0;
    }

    private static bool ContainsDirectorySeparator(string command, bool isWindows)
    {
        return command.Contains('/', StringComparison.Ordinal)
            || (isWindows && command.Contains('\\', StringComparison.Ordinal));
    }

    private static string[] GetPathExtensions(string? pathExtVariable)
    {
        string value = string.IsNullOrWhiteSpace(pathExtVariable) ? DefaultPathExtensions : pathExtVariable;
        return value
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static extension => extension.StartsWith('.'))
            .ToArray();
    }
}
