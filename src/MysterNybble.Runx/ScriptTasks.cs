using System.Globalization;
using System.Text;

namespace MysterNybble.Runx;

// The folders searched for script tasks, as full paths, and the config file that chose them (null
// when the defaults apply).
internal sealed record TaskFolderSettings(IReadOnlyList<string> Folders, string? SetBy);

// A script discovered in a task folder, turned into an ordinary command task.
internal sealed record ScriptTask(string Name, string FilePath, string Folder, RunxTask Task);

// Discovers script tasks: every script in a task folder is a task, subfolders become ':'-separated
// name prefixes, a file named _default is its folder's task, and '# runx key=value' header comments
// carry the description, dependencies, and hide flag.
internal static class ScriptTasks
{
    // Follows the .NET convention of keeping engineering scripts under eng/.
    public const string DefaultFolder = "eng/tasks";

    private const int HeaderLineLimit = 50;
    private const string DefaultFileName = "_default";

    private const string HeaderMarker = "runx";

    // Comment markers for PowerShell and shell scripts, C#, and batch files.
    private static readonly string[] s_commentMarkers = ["#", "//", "::", "REM"];

    public static IReadOnlyList<ScriptTask> Discover(string root, IReadOnlyList<string> folders, bool isWindows)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(folders);

        // Earlier folders win, and within a folder the best script for this OS wins.
        Dictionary<string, ScriptTask> tasks = new(StringComparer.Ordinal);
        foreach (string folder in folders.Where(Directory.Exists))
        {
            Dictionary<string, (int Rank, ScriptTask Task)> folderTasks = new(StringComparer.Ordinal);
            foreach (string file in EnumerateScriptFiles(folder))
            {
                string? name = GetTaskName(folder, file);
                if (name is null || tasks.ContainsKey(name))
                {
                    continue;
                }

                ScriptRunner? runner = GetRunner(root, file, isWindows);
                if (runner is null)
                {
                    continue;
                }

                if (folderTasks.TryGetValue(name, out (int Rank, ScriptTask Task) existing) && existing.Rank <= runner.Rank)
                {
                    continue;
                }

                folderTasks[name] = (runner.Rank, new ScriptTask(name, file, folder, CreateTask(file, runner)));
            }

            foreach (KeyValuePair<string, (int Rank, ScriptTask Task)> task in folderTasks.OrderBy(static task => task.Key, StringComparer.Ordinal))
            {
                tasks.Add(task.Key, task.Value.Task);
            }
        }

        return tasks.Values.ToArray();
    }

    public static string? GetTaskName(string folder, string file)
    {
        string relative = Path.GetRelativePath(folder, file);
        string[] segments = relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return null;
        }

        segments[^1] = Path.GetFileNameWithoutExtension(segments[^1]);
        string[] nameSegments = segments[^1] == DefaultFileName
            ? segments.Length == 1 ? ["default"] : segments[..^1]
            : segments;

        string name = string.Join(':', nameSegments);
        return RunxConfigValidator.IsValidTaskName(name) ? name : null;
    }

    public static (string Command, IReadOnlyList<string> Arguments)? ParseShebang(string firstLine, bool isWindows)
    {
        ArgumentNullException.ThrowIfNull(firstLine);
        if (!firstLine.StartsWith("#!", StringComparison.Ordinal))
        {
            return null;
        }

        string[] parts = firstLine[2..].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return null;
        }

        int commandIndex = 0;
        if (Path.GetFileName(parts[0]) == "env")
        {
            // #!/usr/bin/env [-S] interpreter [args]
            commandIndex = parts.Length > 1 && parts[1] == "-S" ? 2 : 1;
            if (commandIndex >= parts.Length)
            {
                return null;
            }
        }

        string command = parts[commandIndex];

        // Unix interpreter paths such as /bin/bash do not exist on Windows, so look the name up on PATH.
        if (isWindows && command.StartsWith('/'))
        {
            command = Path.GetFileName(command);
        }

        return (command, parts[(commandIndex + 1)..]);
    }

    private static IEnumerable<string> EnumerateScriptFiles(string folder)
    {
        EnumerationOptions options = new()
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System
        };

        foreach (string file in Directory.EnumerateFiles(folder, "*", options))
        {
            // Names starting with '.' or '_' (other than _default) are helpers, not tasks.
            string relative = Path.GetRelativePath(folder, file);
            bool isHelper = relative
                .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar])
                .Any(static segment => segment.StartsWith('.')
                    || (segment.StartsWith('_') && Path.GetFileNameWithoutExtension(segment) != DefaultFileName));

            if (!isHelper)
            {
                yield return file;
            }
        }
    }

    private sealed record ScriptRunner(string Command, IReadOnlyList<string> Arguments, int Rank);

    // Lower ranks are preferred when several scripts share a name, such as build.ps1 and build.sh.
    private static ScriptRunner? GetRunner(string root, string file, bool isWindows)
    {
        string path = ToCommandPath(root, file);
        string extension = Path.GetExtension(file).ToUpperInvariant();

        switch (extension)
        {
            case ".PS1":
                return new ScriptRunner("pwsh", ["-NoLogo", "-NoProfile", "-File", path], isWindows ? 0 : 2);
            case ".CS":
                return new ScriptRunner("dotnet", ["run", "--file", path, "--"], 3);
            case ".CMD":
                return isWindows ? new ScriptRunner(path, [], 1) : null;
            case ".BAT":
                return isWindows ? new ScriptRunner(path, [], 2) : null;
        }

        if (!isWindows && IsExecutable(file))
        {
            return new ScriptRunner(path, [], extension == ".SH" ? 0 : 1);
        }

        if (ParseShebang(ReadFirstLine(file), isWindows) is (string command, IReadOnlyList<string> arguments))
        {
            return new ScriptRunner(command, [.. arguments, path], extension == ".SH" && !isWindows ? 0 : 4);
        }

        return extension == ".SH" ? new ScriptRunner("sh", [path], isWindows ? 5 : 0) : null;
    }

    private static RunxTask CreateTask(string file, ScriptRunner runner)
    {
        Dictionary<string, object> header = ReadHeader(file);
        return new RunxTask
        {
            Description = header.GetValueOrDefault("description") as string,
            Command = runner.Command,
            Args = [.. runner.Arguments],
            DependsOn = header.GetValueOrDefault("depends") switch
            {
                string dependency => [dependency],
                List<string> dependencies => [.. dependencies],
                _ => null
            },
            Hide = header.GetValueOrDefault("hide") as bool?
        };
    }

    // Paths inside the repo stay relative, with forward slashes, so the status bar and --dry-run
    // read like the command you would type. Tasks run from the repo root, so they still resolve.
    private static string ToCommandPath(string root, string file)
    {
        string relative = Path.GetRelativePath(root, file);
        return Path.IsPathRooted(relative) || relative.StartsWith("..", StringComparison.Ordinal)
            ? file
            : relative.Replace('\\', '/');
    }

    private static bool IsExecutable(string file)
    {
        const UnixFileMode executeModes = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
        return !OperatingSystem.IsWindows() && (File.GetUnixFileMode(file) & executeModes) != 0;
    }

    private static string ReadFirstLine(string file)
    {
        try
        {
            using StreamReader reader = new(file);
            return reader.ReadLine() ?? string.Empty;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    // Reads '# runx key=value' lines near the top of a script, with the comment marker for its language
    // ('#', '//', '::', or 'REM'). Values are quoted strings, true or false, or ["a", "b"] lists.
    private static Dictionary<string, object> ReadHeader(string file)
    {
        Dictionary<string, object> header = new(StringComparer.Ordinal);
        IEnumerable<string> lines;
        try
        {
            lines = File.ReadLines(file).Take(HeaderLineLimit).ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return header;
        }

        foreach (string rawLine in lines)
        {
            if (GetHeaderAssignment(rawLine) is not string assignment)
            {
                continue;
            }

            int equals = assignment.IndexOf('=', StringComparison.Ordinal);
            string key = equals > 0 ? assignment[..equals].Trim() : string.Empty;
            if (!IsHeaderKey(key))
            {
                continue;
            }

            if (TryParseValue(assignment[(equals + 1)..].Trim(), out object? value))
            {
                header[key] = value;
            }
        }

        return header;
    }

    // "# runx description=..." yields "description=...". The marker may be spaced from the comment
    // (as formatters like to do) but must be followed by whitespace, so "# runxy" is not a header.
    public static string? GetHeaderAssignment(string line)
    {
        ArgumentNullException.ThrowIfNull(line);

        string trimmed = line.TrimStart();
        foreach (string commentMarker in s_commentMarkers)
        {
            if (!trimmed.StartsWith(commentMarker, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string rest = trimmed[commentMarker.Length..].TrimStart();
            if (rest.Length > HeaderMarker.Length
                && rest.StartsWith(HeaderMarker, StringComparison.OrdinalIgnoreCase)
                && char.IsWhiteSpace(rest[HeaderMarker.Length]))
            {
                return rest[HeaderMarker.Length..].Trim();
            }
        }

        return null;
    }

    private static bool IsHeaderKey(string key)
    {
        return key.Length > 0
            && char.IsAsciiLetter(key[0])
            && key.All(static character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-');
    }

    public static bool TryParseValue(string text, out object value)
    {
        ArgumentNullException.ThrowIfNull(text);
        value = string.Empty;

        if (text is "true" or "false")
        {
            value = text == "true";
            return true;
        }

        int position = 0;
        if (text.StartsWith('['))
        {
            List<string> items = [];
            position = 1;
            while (true)
            {
                SkipWhitespace(text, ref position);
                if (position < text.Length && text[position] == ']')
                {
                    value = items;
                    return true;
                }

                if (!TryParseString(text, ref position, out string? item))
                {
                    return false;
                }

                items.Add(item);
                SkipWhitespace(text, ref position);
                if (position < text.Length && text[position] == ',')
                {
                    position++;
                }
            }
        }

        if (TryParseString(text, ref position, out string? single))
        {
            value = single;
            return true;
        }

        return false;
    }

    private static bool TryParseString(string text, ref int position, out string value)
    {
        value = string.Empty;
        if (position >= text.Length || text[position] is not ('"' or '\''))
        {
            return false;
        }

        char quote = text[position++];
        StringBuilder result = new();
        while (position < text.Length)
        {
            char character = text[position++];
            if (character == quote)
            {
                value = result.ToString();
                return true;
            }

            // Only basic ("...") strings have escapes; literal ('...') strings are taken as written.
            if (character == '\\' && quote == '"' && position < text.Length)
            {
                char escaped = text[position++];
                switch (escaped)
                {
                    case 'n':
                        result.Append('\n');
                        break;
                    case 't':
                        result.Append('\t');
                        break;
                    case 'u' when position + 4 <= text.Length
                        && int.TryParse(text.AsSpan(position, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int codePoint):
                        result.Append((char)codePoint);
                        position += 4;
                        break;
                    default:
                        result.Append(escaped);
                        break;
                }

                continue;
            }

            result.Append(character);
        }

        return false;
    }

    private static void SkipWhitespace(string text, ref int position)
    {
        while (position < text.Length && char.IsWhiteSpace(text[position]))
        {
            position++;
        }
    }
}
