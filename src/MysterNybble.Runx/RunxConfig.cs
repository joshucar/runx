using System.Text.Json;
using System.Text.Json.Serialization;

namespace MysterNybble.Runx;

internal sealed class RunxConfig
{
    [JsonPropertyName("$schema")]
    public string? Schema { get; set; }

    public int? Version { get; set; }

    public Dictionary<string, RunxTask>? Tasks { get; set; }

    public List<string?>? TaskFolders { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

internal sealed class RunxTask
{
    public string? Description { get; set; }

    public string? Command { get; set; }

    public List<string?>? Args { get; set; }

    public string? Script { get; set; }

    public string? Shell { get; set; }

    public string? Cwd { get; set; }

    public Dictionary<string, string?>? Env { get; set; }

    public List<string?>? DependsOn { get; set; }

    public bool? Hide { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

// Where a task was defined: its file (a config file or a script) and the directory its cwd resolves
// from. Personal tasks carry the tag shown next to them, IsBroad marks personal files that apply to
// more than this one repo, and IsScript marks tasks discovered from a task folder.
internal sealed record TaskSource(
    string ConfigPath,
    string BaseDirectory,
    string? PersonalTag = null,
    bool IsBroad = false,
    bool IsScript = false)
{
    public bool IsPersonal => PersonalTag is not null;

    public string? Tag => PersonalTag ?? (IsScript ? "script" : null);
}

// The tasks from the repo's runx.json and the user's personal tasks files, merged. Unavailable holds
// broad personal tasks hidden in this repo, with the reason, so running one by name can explain why.
// PersonalFiles lists the personal files that were loaded, broadest first, and TaskFolders the
// folders searched for script tasks, in order.
internal sealed record LoadedRunxConfig(
    IReadOnlyDictionary<string, RunxTask> Tasks,
    IReadOnlyDictionary<string, TaskSource> Sources,
    string Location,
    IReadOnlyDictionary<string, string>? Unavailable = null,
    IReadOnlyList<string>? PersonalFiles = null,
    TaskFolderSettings? TaskFolders = null)
{
    public LoadedRunxConfig(string path, string directory, IReadOnlyDictionary<string, RunxTask> tasks)
        : this(tasks, tasks.Keys.ToDictionary(static name => name, _ => new TaskSource(path, directory), StringComparer.Ordinal), path)
    {
    }


    public string? Tag(string taskName) => Sources[taskName].Tag;

    // Hidden tasks still run by name and as dependencies; they are only left out of lists.
    public IReadOnlyDictionary<string, RunxTask> VisibleTasks =>
        Tasks.Where(static task => task.Value.Hide != true).ToDictionary(StringComparer.Ordinal);
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    AllowDuplicateProperties = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true)]
[JsonSerializable(typeof(RunxConfig))]
internal sealed partial class RunxJsonSerializerContext : JsonSerializerContext;
