using System.Text.Json;
using System.Text.Json.Serialization;

namespace MysterNybble.Runx;

internal sealed class RunxConfig
{
    [JsonPropertyName("$schema")]
    public string? Schema { get; set; }

    public int? Version { get; set; }

    public Dictionary<string, RunxTask>? Tasks { get; set; }

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

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

internal sealed record LoadedRunxConfig(
    string Path,
    string Directory,
    IReadOnlyDictionary<string, RunxTask> Tasks);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
[JsonSerializable(typeof(RunxConfig))]
internal sealed partial class RunxJsonSerializerContext : JsonSerializerContext;
