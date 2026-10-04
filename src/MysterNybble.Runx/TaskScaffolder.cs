using System.Text;

namespace MysterNybble.Runx;

// Creates a script task for 'runx --new': the file goes in the first task folder, at the path its
// name implies, with the header filled in and a TODO body that fails until it is replaced.
internal static class TaskScaffolder
{
    public static IReadOnlyList<string> ScriptTypes { get; } = ["ps1", "cs", "sh", "cmd"];

    public static async Task<int> CreateAsync(
        CliParseResult parseResult,
        LoadedRunxConfig config,
        string scriptRoot,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parseResult);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrWhiteSpace(scriptRoot);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        string taskName = parseResult.NewTaskName!;
        string type = (parseResult.ScriptType ?? "ps1").TrimStart('.').ToUpperInvariant() switch
        {
            "PS1" => "ps1",
            "CS" => "cs",
            "SH" => "sh",
            "CMD" => "cmd",
            _ => string.Empty
        };

        if (type.Length == 0)
        {
            error.WriteLine($"Unknown script type '{parseResult.ScriptType}'. Use one of: {string.Join(", ", ScriptTypes)}.");
            return 1;
        }

        string[] segments = taskName.Split(':');
        if (!RunxConfigValidator.IsValidTaskName(taskName)
            || segments.Any(static segment => segment.Length == 0 || segment[0] is '_' or '.'))
        {
            error.WriteLine(
                $"'{taskName}' cannot be a script task name. Use letters, digits, '_', '-', and '.', separate folders with ':', " +
                "and do not start a part with '_' or '.' (those are helpers).");
            return 1;
        }

        if (config.TaskFolders is not TaskFolderSettings taskFolders || taskFolders.Folders.Count == 0)
        {
            error.WriteLine(config.TaskFolders?.SetBy is string setBy
                ? $"Script tasks are turned off here: taskFolders is empty in {setBy}."
                : "Script tasks are not available here.");
            return 1;
        }

        string folder = taskFolders.Folders[0];
        string path = Path.Combine([folder, .. segments[..^1], $"{segments[^1]}.{type}"]);
        string displayPath = RunxGuide.ToDisplayPath(scriptRoot, path);

        if (config.Sources.TryGetValue(taskName, out TaskSource? existing) && !IsSameScript(existing, path))
        {
            if (!existing.IsScript && !(existing.IsPersonal && existing.IsBroad))
            {
                error.WriteLine($"{existing.ConfigPath} already defines '{taskName}', so a script with that name would not be used.");
                return 1;
            }

            if (existing.IsScript && !IsSibling(existing.ConfigPath, path))
            {
                error.WriteLine($"Task '{taskName}' already comes from {existing.ConfigPath}.");
                return 1;
            }
        }

        if (File.Exists(path) && !parseResult.Force)
        {
            error.WriteLine($"{displayPath} already exists. Use --force to overwrite it.");
            return 1;
        }

        foreach (string dependency in parseResult.Depends)
        {
            if (dependency == taskName || !config.Tasks.ContainsKey(dependency))
            {
                error.WriteLine(dependency == taskName
                    ? $"Task '{taskName}' cannot depend on itself."
                    : $"Task '{dependency}' does not exist, so '{taskName}' cannot depend on it. Run 'runx --list' to see the tasks.");
                return 1;
            }
        }

        string contents = CreateContents(type, taskName, displayPath, parseResult.Description, parseResult.Depends, parseResult.Hide);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, contents, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken);
            if (type == "sh" && !OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(path, File.GetUnixFileMode(path) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error.WriteLine($"Failed to write {path}: {ex.Message}");
            return 1;
        }

        output.WriteLine($"Created {displayPath} for task '{taskName}'.");
        if (existing is { IsScript: true } && IsSibling(existing.ConfigPath, path))
        {
            output.WriteLine($"It pairs with {RunxGuide.ToDisplayPath(scriptRoot, existing.ConfigPath)}: Windows prefers .ps1, then .cmd and .cs; Linux and macOS prefer .sh.");
        }

        output.WriteLine($"Replace the TODO with the task's work, then run 'runx {taskName}'.");
        return 0;
    }

    public static string CreateContents(
        string type,
        string taskName,
        string displayPath,
        string? description,
        IReadOnlyList<string> depends,
        bool hide)
    {
        ArgumentNullException.ThrowIfNull(depends);

        string comment = type switch
        {
            "cs" => "//",
            "cmd" => "::",
            _ => "#"
        };

        List<string> header = [];
        if (!string.IsNullOrWhiteSpace(description))
        {
            header.Add($"{comment} runx description={Quote(description)}");
        }

        if (depends.Count > 0)
        {
            header.Add($"{comment} runx depends=[{string.Join(", ", depends.Select(Quote))}]");
        }

        if (hide)
        {
            header.Add($"{comment} runx hide=true");
        }

        string todo = $"TODO: implement {taskName} in {displayPath}";
        string[] lines = type switch
        {
            "ps1" =>
            [
                .. header,
                "#Requires -Version 7",
                "# Runs from the repo root. Arguments after 'runx " + taskName + " --' bind to param().",
                "param()",
                string.Empty,
                "$ErrorActionPreference = 'Stop'",
                string.Empty,
                $"throw '{todo.Replace("'", "''", StringComparison.Ordinal)}'"
            ],
            "cs" =>
            [
                .. header,
                "// A .NET file-based app run with 'dotnet run --file' from the repo root.",
                "// Arguments after 'runx " + taskName + " --' arrive in args; return non-zero on failure.",
                string.Empty,
                $"Console.Error.WriteLine(\"{todo.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\");",
                "return 1;"
            ],
            "sh" =>
            [
                "#!/usr/bin/env sh",
                .. header,
                "# Runs from the repo root. Arguments after 'runx " + taskName + " --' arrive as \"$@\".",
                "set -eu",
                string.Empty,
                $"echo '{todo.Replace("'", "'\\''", StringComparison.Ordinal)}' >&2",
                "exit 1"
            ],
            _ =>
            [
                "@echo off",
                .. header,
                ":: Runs from the repo root. Arguments after 'runx " + taskName + " --' arrive as %*.",
                string.Empty,
                $"echo {todo} 1>&2",
                "exit /b 1"
            ]
        };

        string newLine = type == "cmd" ? "\r\n" : "\n";
        return string.Join(newLine, lines) + newLine;
    }

    // A header string value: quoted, with backslashes, quotes, and line breaks escaped.
    private static string Quote(string value)
    {
        StringBuilder quoted = new("\"");
        foreach (char character in value)
        {
            quoted.Append(character switch
            {
                '\\' => "\\\\",
                '"' => "\\\"",
                '\n' => "\\n",
                '\t' => "\\t",
                '\r' => string.Empty,
                _ => character.ToString()
            });
        }

        return quoted.Append('"').ToString();
    }

    private static bool IsSameScript(TaskSource existing, string path)
    {
        return existing.IsScript && Path.GetFullPath(existing.ConfigPath) == Path.GetFullPath(path);
    }

    // build.ps1 and build.sh in the same folder are one task on different operating systems.
    private static bool IsSibling(string existingPath, string path)
    {
        return Path.ChangeExtension(Path.GetFullPath(existingPath), null) == Path.ChangeExtension(Path.GetFullPath(path), null);
    }
}
