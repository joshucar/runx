namespace MysterNybble.Runx;

internal static class TaskPreview
{
    private const int MaxScriptLines = 3;

    public static string FormatCommandLine(string command, IEnumerable<string?>? args)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);

        IEnumerable<string> parts = [command, .. args?.OfType<string>() ?? []];
        return string.Join(' ', parts.Select(QuoteIfNeeded));
    }

    public static IReadOnlyList<string> GetScriptLines(string script)
    {
        ArgumentNullException.ThrowIfNull(script);

        string[] lines = script
            .Split('\n')
            .Select(static line => line.TrimEnd('\r'))
            .Where(static line => !string.IsNullOrWhiteSpace(line))
            .ToArray();

        if (lines.Length <= MaxScriptLines)
        {
            return lines;
        }

        int hiddenLineCount = lines.Length - (MaxScriptLines - 1);
        return [.. lines.Take(MaxScriptLines - 1), $"... {hiddenLineCount} more lines"];
    }

    public static string? FormatDetails(RunxTask task)
    {
        ArgumentNullException.ThrowIfNull(task);

        List<string> details = [];
        if (task.DependsOn is { Count: > 0 })
        {
            details.Add($"depends on: {string.Join(", ", task.DependsOn)}");
        }

        if (!string.IsNullOrWhiteSpace(task.Cwd))
        {
            details.Add($"cwd: {task.Cwd}");
        }

        if (task.Env is { Count: > 0 })
        {
            details.Add($"env: {string.Join(", ", task.Env.Keys.Order(StringComparer.Ordinal))}");
        }

        return details.Count == 0 ? null : string.Join("   ", details);
    }

    private static string QuoteIfNeeded(string value)
    {
        return value.Length > 0 && !value.Any(static character => char.IsWhiteSpace(character) || character == '"')
            ? value
            : $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
    }
}
