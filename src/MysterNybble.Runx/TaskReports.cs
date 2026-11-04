using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace MysterNybble.Runx;

// Descriptions of tasks for people and tools: runx --list --json, --info, and --check.
internal static class TaskReports
{
    // With no config there are no tasks, which is still a valid, empty list.
    public static void WriteJsonList(LoadedRunxConfig? config, TextWriter output, HookConfiguration? hooks = null, IReadOnlyList<string>? warnings = null)
    {
        ArgumentNullException.ThrowIfNull(output);

        WriteJson(output, writer =>
        {
            writer.WriteStartObject();
            writer.WriteStartArray("tasks");
            foreach (string taskName in config?.Tasks.Keys.Order(StringComparer.Ordinal) ?? Enumerable.Empty<string>())
            {
                WriteTask(writer, config!, taskName, plan: null, hooks);
            }

            writer.WriteEndArray();
            writer.WriteStartObject("hooks");
            foreach ((string hook, string target) in (hooks?.Bindings ?? new Dictionary<string, string>()).OrderBy(static pair => pair.Key, StringComparer.Ordinal))
            {
                writer.WriteString(hook, target);
            }

            writer.WriteEndObject();
            writer.WriteString("hookConfig", hooks?.ConfigPath);
            WriteStrings(writer, "hookErrors", hooks?.Errors ?? []);
            WriteStrings(writer, "warnings", warnings ?? []);
            writer.WriteEndObject();
        });
    }

    public static int WriteInfo(LoadedRunxConfig config, string taskName, bool json, TextWriter output, TextWriter error, HookConfiguration? hookConfig = null, IReadOnlyList<string>? warnings = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrWhiteSpace(taskName);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        if (!config.Tasks.TryGetValue(taskName, out RunxTask? task))
        {
            error.WriteLine(config.Unavailable?.GetValueOrDefault(taskName)
                ?? $"Task '{taskName}' was not found in {config.Location}.");
            return 1;
        }

        string[] plan = TaskPlanner.GetExecutionOrder(config.Tasks, taskName)
            .Where(name => ProcessRequestFactory.IsRunnable(config.Tasks[name]))
            .ToArray();

        if (json)
        {
            WriteJson(output, writer => WriteTask(writer, config, taskName, plan, hookConfig, warnings));
            return 0;
        }

        TaskSource source = config.Sources[taskName];
        List<(string Label, string Value)> rows =
        [
            ("Task", taskName),
            ("Description", string.IsNullOrWhiteSpace(task.Description) ? "(none)" : task.Description),
            ("Source", $"{GetSourceLabel(source)}  {source.ConfigPath}")
        ];

        if (task.Usage is not null)
        {
            string[] lines = task.Usage.ReplaceLineEndings("\n").Split('\n');
            rows.Add(("Usage", lines[0]));
            rows.AddRange(lines.Skip(1).Select(static line => (string.Empty, line)));
        }

        string[] hooks = hookConfig?.EventsFor(taskName, source) ?? [];
        if (hooks.Length > 0)
        {
            rows.Add(("Git hooks", string.Join(", ", hooks)));
            rows.Add(("Hook config", hookConfig!.ConfigPath));
        }

        string[] runs = DescribeRun(task);
        rows.Add(("Runs", runs[0]));
        rows.AddRange(runs.Skip(1).Select(static line => (string.Empty, line)));

        if (GetWorkingDirectory(task, source) is string workingDirectory)
        {
            rows.Add(("Working dir", workingDirectory));
        }

        if (task.Env is { Count: > 0 })
        {
            rows.Add(("Env", string.Join(", ", task.Env.Keys.Order(StringComparer.Ordinal))));
        }

        if (task.DependsOn is { Count: > 0 })
        {
            rows.Add(("Depends on", string.Join(", ", task.DependsOn)));
        }

        if (plan.Length > 1)
        {
            rows.Add(("Plan", string.Join(" -> ", plan)));
        }

        if (task.Hide == true)
        {
            rows.Add(("Hidden", "yes; it still runs by name and as a dependency"));
        }

        foreach ((string label, string value) in rows)
        {
            output.WriteLine(label.Length == 0 ? $"{new string(' ', 14)}{value}" : $"{(label + ":").PadRight(14)}{value}");
        }

        return 0;
    }

    public static int WriteCheck(ConfigLoadResult result, bool nothingFound, bool json, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(output);

        IReadOnlyList<string> warnings = result.Warnings ?? [];
        bool valid = result.Errors.Count == 0 && (nothingFound || result.Config is not null);
        string summary = nothingFound && valid
            ? "No tasks here: no runx.json, no task folder, and no personal tasks."
            : valid ? $"OK: {DescribeCounts(result.Config!)}." : "runx --check found problems.";

        if (json)
        {
            WriteJson(output, writer =>
            {
                writer.WriteStartObject();
                writer.WriteBoolean("valid", valid);
                writer.WriteNumber("taskCount", result.Config?.Tasks.Count ?? 0);
                WriteStrings(writer, "errors", result.Errors);
                WriteStrings(writer, "warnings", warnings);
                writer.WriteString("summary", summary);
                writer.WriteEndObject();
            });
        }
        else
        {
            foreach (string loadError in result.Errors)
            {
                output.WriteLine(loadError);
            }

            foreach (string warning in warnings)
            {
                output.WriteLine($"warning: {warning}");
            }

            output.WriteLine(summary);
        }

        return valid ? 0 : 1;
    }

    public static string DescribeCounts(LoadedRunxConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        int fromConfig = config.Sources.Values.Count(static source => !source.IsPersonal && !source.IsScript);
        int scripts = config.Sources.Values.Count(static source => source.IsScript);
        int personal = config.Sources.Values.Count(static source => source.IsPersonal);
        int hidden = config.Tasks.Values.Count(static task => task.Hide == true);

        List<string> parts = [];
        if (fromConfig > 0)
        {
            parts.Add($"{fromConfig} from runx.json");
        }

        if (scripts > 0)
        {
            parts.Add($"{scripts} {(scripts == 1 ? "script" : "scripts")}");
        }

        if (personal > 0)
        {
            parts.Add($"{personal} personal");
        }

        string counts = $"{config.Tasks.Count} {(config.Tasks.Count == 1 ? "task" : "tasks")}";
        if (parts.Count > 0)
        {
            counts += $" ({string.Join(", ", parts)})";
        }

        return hidden > 0 ? $"{counts}, {hidden} hidden" : counts;
    }

    private static void WriteTask(Utf8JsonWriter writer, LoadedRunxConfig config, string taskName, IReadOnlyList<string>? plan, HookConfiguration? hooks = null, IReadOnlyList<string>? warnings = null)
    {
        RunxTask task = config.Tasks[taskName];
        TaskSource source = config.Sources[taskName];

        writer.WriteStartObject();
        writer.WriteString("name", taskName);
        writer.WriteString("description", task.Description);
        writer.WriteString("usage", task.Usage);
        WriteStrings(writer, "hooks", hooks?.EventsFor(taskName, source) ?? []);
        writer.WriteString("hookConfig", hooks?.ConfigPath);
        writer.WriteString("source", source.IsPersonal ? "personal" : source.IsScript ? "script" : "config");
        writer.WriteString("scope", source.PersonalTag);
        writer.WriteString("file", source.ConfigPath);

        if (task.Command is not null)
        {
            writer.WriteString("kind", "command");
            writer.WriteString("command", task.Command);
            WriteStrings(writer, "args", task.Args?.OfType<string>().ToArray() ?? []);
        }
        else if (task.Script is not null)
        {
            writer.WriteString("kind", "shell");
            writer.WriteString("shell", task.Shell);
            writer.WriteString("script", task.Script);
        }
        else
        {
            writer.WriteString("kind", "dependencies");
        }

        writer.WriteString("cwd", GetWorkingDirectory(task, source));
        WriteStrings(writer, "env", task.Env?.Keys.Order(StringComparer.Ordinal).ToArray() ?? []);
        WriteStrings(writer, "dependsOn", task.DependsOn?.OfType<string>().ToArray() ?? []);
        writer.WriteBoolean("hidden", task.Hide == true);

        if (plan is not null)
        {
            WriteStrings(writer, "plan", plan);
            WriteStrings(writer, "hookErrors", hooks?.Errors ?? []);
            WriteStrings(writer, "warnings", warnings ?? []);
        }

        writer.WriteEndObject();
    }

    private static string GetSourceLabel(TaskSource source)
    {
        if (source.IsScript)
        {
            return "script";
        }

        if (!source.IsPersonal)
        {
            return "runx.json";
        }

        return source.IsBroad ? $"personal ({source.PersonalTag} scope)" : "personal";
    }

    private static string[] DescribeRun(RunxTask task)
    {
        if (task.Command is not null)
        {
            return [TaskPreview.FormatCommandLine(task.Command, task.Args)];
        }

        if (task.Script is not null)
        {
            string[] lines = task.Script.ReplaceLineEndings("\n").Split('\n');
            return [$"{task.Shell}: {lines[0]}", .. lines.Skip(1).Select(line => $"{new string(' ', (task.Shell?.Length ?? 0) + 2)}{line}")];
        }

        return ["only its dependencies"];
    }

    private static string? GetWorkingDirectory(RunxTask task, TaskSource source)
    {
        if (!ProcessRequestFactory.IsRunnable(task))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(string.IsNullOrWhiteSpace(task.Cwd) ? "." : task.Cwd, source.BaseDirectory);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static void WriteStrings(Utf8JsonWriter writer, string propertyName, IEnumerable<string> values)
    {
        writer.WriteStartArray(propertyName);
        foreach (string value in values)
        {
            writer.WriteStringValue(value);
        }

        writer.WriteEndArray();
    }

    private static void WriteJson(TextWriter output, Action<Utf8JsonWriter> write)
    {
        using MemoryStream stream = new();
        // Relaxed escaping keeps quotes and apostrophes readable; this JSON is never embedded in HTML.
        JsonWriterOptions options = new() { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        using (Utf8JsonWriter writer = new(stream, options))
        {
            write(writer);
        }

        output.WriteLine(Encoding.UTF8.GetString(stream.ToArray()));
    }
}
