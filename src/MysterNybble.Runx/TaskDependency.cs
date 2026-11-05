using System.Text.Json;
using System.Text.Json.Serialization;

namespace MysterNybble.Runx;

// Arguments belong to this dependency edge, not to the task or its other callers.
[JsonConverter(typeof(TaskDependencyJsonConverter))]
internal sealed record TaskDependency(string Task, IReadOnlyList<string> Args)
{
    public TaskDependency(string task) : this(task, [])
    {
    }

    public override string ToString() => Args.Count == 0
        ? Task
        : TaskPreview.FormatCommandLine(Task, ["--", .. Args]);
}

internal sealed class TaskDependencyJsonConverter : JsonConverter<TaskDependency>
{
    public override TaskDependency Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return new TaskDependency(reader.GetString()!);
        }

        using JsonDocument document = JsonDocument.ParseValue(ref reader);
        JsonElement value = document.RootElement;
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("A dependency must be a task name or an object with 'task' and optional 'args'.");
        }

        string? task = null;
        List<string> args = [];
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (JsonProperty property in value.EnumerateObject())
        {
            if (!seen.Add(property.Name))
            {
                throw new JsonException($"Duplicate dependency property '{property.Name}'.");
            }

            switch (property.Name)
            {
                case "task" when property.Value.ValueKind == JsonValueKind.String:
                    task = property.Value.GetString();
                    break;
                case "args" when property.Value.ValueKind == JsonValueKind.Array:
                    foreach (JsonElement argument in property.Value.EnumerateArray())
                    {
                        if (argument.ValueKind != JsonValueKind.String)
                        {
                            throw new JsonException("Dependency args must contain only strings.");
                        }

                        args.Add(argument.GetString()!);
                    }

                    break;
                case "task":
                    throw new JsonException("Dependency 'task' must be a string.");
                case "args":
                    throw new JsonException("Dependency 'args' must be an array of strings.");
                default:
                    throw new JsonException($"Unknown dependency property '{property.Name}'.");
            }
        }

        return task is null
            ? throw new JsonException("Dependency object requires 'task'.")
            : new TaskDependency(task, args);
    }

    public override void Write(Utf8JsonWriter writer, TaskDependency value, JsonSerializerOptions options)
    {
        if (value.Args.Count == 0)
        {
            writer.WriteStringValue(value.Task);
            return;
        }

        writer.WriteStartObject();
        writer.WriteString("task", value.Task);
        writer.WriteStartArray("args");
        foreach (string argument in value.Args)
        {
            writer.WriteStringValue(argument);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }
}
