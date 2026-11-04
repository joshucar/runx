using System.Text.Json;

namespace MysterNybble.Runx;

internal sealed record HookDeclarations(bool IsDeclared, IReadOnlyDictionary<string, string> Bindings, IReadOnlyList<string> Errors)
{
    public static HookDeclarations Parse(JsonElement value, string? selectedEvent = null)
    {
        Dictionary<string, string> bindings = new(StringComparer.Ordinal);
        List<string> errors = [];
        if (value.ValueKind == JsonValueKind.Undefined)
        {
            return new(false, bindings, errors);
        }

        if (value.ValueKind != JsonValueKind.Object)
        {
            errors.Add("Config property 'hooks' must be an object and must not be null. Omit it or use an empty object.");
            return new(true, bindings, errors);
        }

        foreach (JsonProperty property in value.EnumerateObject())
        {
            if (selectedEvent is not null && property.Name != selectedEvent)
            {
                continue;
            }

            if (!GitHooks.Events.ContainsKey(property.Name))
            {
                errors.Add($"Unknown or unsupported Git hook '{property.Name}'. Supported hooks: {string.Join(", ", GitHooks.Events.Keys)}.");
                continue;
            }

            if (property.Value.ValueKind != JsonValueKind.String || property.Value.GetString() is not string target || !RunxConfigValidator.IsValidTaskName(target))
            {
                errors.Add($"Git hook '{property.Name}' must name a task.");
            }
            else if (!bindings.TryAdd(property.Name, target))
            {
                errors.Add($"Git hook '{property.Name}' is declared more than once.");
            }
        }

        return new(true, bindings, errors);
    }
}

// Hook definitions and task identities always come from Git's worktree root, independent of
// the task view selected by --file, a nested runx.json, or personal task folder overrides.
internal sealed record HookConfiguration(
    GitRepository Repository,
    IReadOnlyDictionary<string, string> Bindings,
    LoadedRunxConfig? Tasks,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings)
{
    public string ConfigPath => Path.Combine(Repository.Root, RunxConfigDiscovery.ConfigFileName);

    public static async Task<HookConfiguration> LoadAsync(GitRepository repository, CancellationToken cancellationToken, string? selectedEvent = null)
    {
        string path = Path.Combine(repository.Root, RunxConfigDiscovery.ConfigFileName);
        Dictionary<string, string> empty = new(StringComparer.Ordinal);
        string json;
        HookDeclarations declarations;
        try
        {
            json = await File.ReadAllTextAsync(path, cancellationToken);
            using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions { AllowDuplicateProperties = false });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return new(repository, empty, null, [$"{path} must contain a JSON object."], []);
            }

            declarations = HookDeclarations.Parse(document.RootElement.TryGetProperty("hooks", out JsonElement hooks) ? hooks : default, selectedEvent);
        }
        catch (FileNotFoundException)
        {
            return new(repository, empty, null, [], []);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new(repository, empty, null, [$"Could not read Git hook config {path}: {ex.Message}"], []);
        }

        // An unbound event does not depend on any task definitions or script folders. JSON must
        // still parse successfully: a broken document cannot establish that a binding is absent.
        if (declarations.Bindings.Count == 0 || declarations.Errors.Count > 0)
        {
            return new(repository, declarations.Bindings, null, declarations.Errors, []);
        }

        ConfigLoadResult result = await RunxConfigLoader.LoadAsync(path, [], repository.Root, cancellationToken, repoJson: json);
        List<string> errors = [.. result.Errors];
        if (result.Config is not null)
        {
            foreach ((string hook, string target) in declarations.Bindings)
            {
                if (!result.Config.Tasks.TryGetValue(target, out RunxTask? task))
                {
                    errors.Add($"Git hook '{hook}' targets unknown task '{target}'.");
                }
                else if (GitHooks.Events[hook].MaxArguments > 0 && task.Command is null)
                {
                    errors.Add($"Git hook '{hook}' passes arguments; target '{target}' must be a command or script-file task. Use a script entry point to receive Git's arguments.");
                }
            }
        }

        return new(repository, declarations.Bindings, result.Config, errors, result.Warnings ?? []);
    }

    public string[] EventsFor(string taskName, TaskSource source)
    {
        if (source.IsPersonal || Tasks is null || !Tasks.Sources.TryGetValue(taskName, out TaskSource? actual)
            || !SamePath(source.ConfigPath, actual.ConfigPath) || !SamePath(source.BaseDirectory, actual.BaseDirectory))
        {
            return [];
        }

        return Bindings.Where(pair => pair.Value == taskName).Select(static pair => pair.Key).Order(StringComparer.Ordinal).ToArray();
    }

    internal static bool SamePath(string left, string right)
    {
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), comparison))
        {
            return true;
        }

        // Git canonicalizes the worktree root; task discovery can retain a symlinked ancestor
        // (including macOS /var -> /private/var). Compare both identities in the same form.
        try
        {
            return string.Equals(ResolveLinks(left), ResolveLinks(right), comparison);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string ResolveLinks(string path)
    {
        string full = Path.GetFullPath(path);
        string resolved = Path.GetPathRoot(full)!;
        foreach (string part in full[resolved.Length..].Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
        {
            string next = Path.Combine(resolved, part);
            FileSystemInfo entry = Directory.Exists(next) ? new DirectoryInfo(next) : new FileInfo(next);
            resolved = entry.LinkTarget is null ? next : entry.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? next;
        }

        return resolved;
    }
}
