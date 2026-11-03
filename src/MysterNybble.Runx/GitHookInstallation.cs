namespace MysterNybble.Runx;

internal static class GitHookInstallation
{
    private static readonly string[] s_ownedProperties = ["event", "command", "runxowner", "runxlauncher"];
    // Git's shared repository config applies to every linked worktree. Registrations contain no
    // checkout path: the dispatcher discovers runx.json in the worktree where Git fires the event.
    public static async Task UpdateAsync(string root, IReadOnlyDictionary<string, string> hooks, string? launcher, TextWriter output, CancellationToken cancellationToken, bool prune = false) =>
        await UpdateAsync(await GitRepository.RequireAsync(root, cancellationToken), hooks, launcher, output, cancellationToken, prune);

    public static async Task UpdateAsync(GitRepository repository, IReadOnlyDictionary<string, string> hooks, string? launcher, TextWriter output, CancellationToken cancellationToken, bool prune = false)
    {
        string root = repository.Root;
        if (launcher is not null)
        {
            await GitHooks.RequireSupportedGitAsync(root, cancellationToken);
        }
        string configPath = repository.ConfigPath;
        string lockPath = configPath + ".lock";
        bool ownsLock = false;
        try
        {
            if (new FileInfo(configPath).LinkTarget is not null)
            {
                throw new InvalidOperationException("Git hook installation does not replace symlinked Git config files. Configure the hooks manually instead.");
            }

            // Hold Git's normal config lock for the whole transaction. git config edits the staged
            // file using its own .lock.lock, and the final rename publishes all changes together.
            using (FileStream locked = new(lockPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                ownsLock = true;
                using FileStream source = File.OpenRead(configPath);
                await source.CopyToAsync(locked, cancellationToken);
            }

            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(lockPath, File.GetUnixFileMode(configPath));
            }

            Dictionary<string, string[]> local = await ReadEntriesAsync(root, lockPath, cancellationToken);
            Dictionary<string, string[]> effective = await ReadEntriesAsync(root, null, cancellationToken);
            string[] managed = OwnedSections(local);
            IEnumerable<string> desired = launcher is null ? [] : hooks.Keys.Select(static name => GitHooks.Prefix + name);
            string[] removing = managed.Where(section => launcher is null || (prune && !hooks.ContainsKey(section[GitHooks.Prefix.Length..]))).ToArray();
            foreach (string section in desired.Concat(removing).Distinct(StringComparer.Ordinal))
            {
                // An enabled preference can survive uninstall (or be set before install).
                // It is not itself a registration, and we preserve it when reinstalling.
                if (HasRegistration(local, section) && !IsOwned(local, section))
                {
                    throw new InvalidOperationException($"Git registration '{section}' already exists and is not owned by runx. No configuration was changed.");
                }

                if (!SectionMatches(local, effective, section))
                {
                    throw new InvalidOperationException($"Git registration '{section}' is also configured outside the shared local Git config. Resolve that conflict before installing or removing runx hooks.");
                }
            }

            foreach (string section in removing)
            {
                foreach (string property in s_ownedProperties)
                {
                    await EditAsync(root, lockPath, ["--unset-all", section + "." + property], cancellationToken, missingAllowed: true);
                }
            }

            if (launcher is not null)
            {
                foreach (string name in hooks.Keys.Order(StringComparer.Ordinal))
                {
                    string section = GitHooks.Prefix + name;
                    await EditAsync(root, lockPath, ["--replace-all", section + ".command", GitHooks.HookCommand(launcher, name)], cancellationToken);
                    await EditAsync(root, lockPath, ["--replace-all", section + ".event", name], cancellationToken);
                    await EditAsync(root, lockPath, ["--replace-all", section + ".runxlauncher", launcher], cancellationToken);
                    await EditAsync(root, lockPath, ["--replace-all", section + ".runxowner", GitHooks.Owner], cancellationToken);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(lockPath, configPath, overwrite: true);
            ownsLock = false;
            output.WriteLine(launcher is null
                ? $"Removed {removing.Length} runx hook registrations."
                : $"Installed {hooks.Count} runx hook registrations using {launcher}. Removed {removing.Length} unbound registrations.");
            output.WriteLine("Hook registrations apply to all worktrees of this clone; bindings come from each worktree's root runx.json.");
        }
        finally
        {
            if (ownsLock)
            {
                File.Delete(lockPath);
            }
        }
    }

    public static async Task<IReadOnlyList<string>> CheckAsync(string root, IReadOnlyDictionary<string, string> hooks, CancellationToken cancellationToken) =>
        await CheckAsync(await GitRepository.RequireAsync(root, cancellationToken), hooks, cancellationToken);

    public static async Task<IReadOnlyList<string>> CheckAsync(GitRepository repository, IReadOnlyDictionary<string, string> hooks, CancellationToken cancellationToken)
    {
        string root = repository.Root;
        List<string> warnings = [];
        bool hasHooks = hooks.Count > 0;
        try
        {
            string configPath = repository.ConfigPath;
            Dictionary<string, string[]> effective = await ReadEntriesAsync(root, null, cancellationToken);
            // One config read is enough when no bindings or registrations exist. Still inspect
            // Git when this branch has no bindings, because another worktree may have installed hooks.
            if (hooks.Count == 0 && !effective.Keys.Any(static key => key.StartsWith(GitHooks.Prefix, StringComparison.Ordinal) && !key.EndsWith(".enabled", StringComparison.Ordinal)))
            {
                return warnings;
            }

            Dictionary<string, string[]> local = await ReadEntriesAsync(root, configPath, cancellationToken);
            string[] owned = OwnedSections(local);
            hasHooks |= owned.Length > 0;
            if (hooks.Count == 0 && owned.Length == 0)
            {
                return warnings;
            }

            await GitHooks.RequireSupportedGitAsync(root, cancellationToken);
            bool manifest = GitHooks.HasLocalManifest(root);
            Dictionary<string, string[]> enabled = hooks.Count > 0
                ? await ReadEntriesAsync(root, null, cancellationToken, booleans: true)
                : new(StringComparer.Ordinal);
            foreach ((string name, string target) in hooks.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
            {
                string section = GitHooks.Prefix + name;
                if (!IsOwned(local, section))
                {
                    warnings.Add(HasRegistration(effective, section)
                        ? $"Git hook '{name}' has a conflicting registration; runx will not overwrite it."
                        : $"Git hook '{name}' -> '{target}' is not installed. Run 'runx --install-hooks' to enable it locally.");
                    continue;
                }

                string? command = Single(local, section + ".command");
                string? launcher = Single(local, section + ".runxlauncher");
                if (Single(local, section + ".event") != name || command is null
                    || launcher is null || command != GitHooks.HookCommand(launcher, name)
                    || manifest != command.Contains(" tool run runx -- --hook ", StringComparison.Ordinal))
                {
                    warnings.Add($"Git hook '{name}' registration is out of date. Run 'runx --install-hooks' again.");
                }

                if (launcher is not null && (GitHooks.LauncherExecutable(launcher) is not string executable
                    || CommandResolver.ResolveFromEnvironment(executable, root) is not string resolved
                    || !File.Exists(resolved)))
                {
                    warnings.Add($"Git hook '{name}' launcher is missing. Restore or reinstall the tool, then run 'runx --install-hooks'.");
                }

                if (!SectionMatches(local, effective, section))
                {
                    warnings.Add($"Git hook '{name}' has registration overrides outside the shared local Git config.");
                }

                if (IsDisabled(enabled, section + ".enabled") || IsDisabled(enabled, "hook." + name + ".enabled"))
                {
                    warnings.Add($"Git hook '{name}' is disabled in Git configuration.");
                }
            }

            foreach (string section in owned.Where(section => !hooks.ContainsKey(section[GitHooks.Prefix.Length..])))
            {
                warnings.Add($"Git registration '{section}' has no binding in this worktree; it will do nothing. It is retained for other worktrees. Use 'runx --install-hooks --prune' to remove unbound registrations from the clone, or 'runx --uninstall-hooks' to remove all runx registrations.");
            }

        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.Text.Json.JsonException)
        {
            if (hasHooks)
            {
                warnings.Add($"Git hook installation: {ex.Message}");
            }
        }

        return warnings;
    }

    private static bool IsDisabled(Dictionary<string, string[]> enabled, string key) =>
        enabled.GetValueOrDefault(key)?.LastOrDefault() == "false";

    private static async Task EditAsync(string root, string file, string[] arguments, CancellationToken cancellationToken, bool missingAllowed = false)
    {
        CommandOutput result = await GitHooks.GitAsync(root, ["config", "--file", file, .. arguments], cancellationToken);
        if (!(missingAllowed && result.ExitCode == 5))
        {
            result.EnsureSuccess();
        }
    }

    private static async Task<Dictionary<string, string[]>> ReadEntriesAsync(string root, string? file, CancellationToken cancellationToken, bool booleans = false)
    {
        string[] location = file is null ? [] : ["--file", file, "--no-includes"];
        string[] conversion = booleans ? ["--type=bool"] : [];
        string pattern = booleans ? @"^hook\..*\.enabled$" : @"^hook\.";
        CommandOutput result = await GitHooks.GitAsync(root, ["config", .. location, .. conversion, "--null", "--get-regexp", pattern], cancellationToken);
        if (result.ExitCode != 1)
        {
            result.EnsureSuccess();
        }

        return result.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Select(static entry => entry.Split('\n', 2))
            .GroupBy(static pair => pair[0], StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Select(static pair => pair.Length == 2 ? pair[1] : string.Empty).ToArray(), StringComparer.Ordinal);
    }

    private static string[] OwnedSections(Dictionary<string, string[]> entries) => entries.Keys
        .Where(static key => key.StartsWith(GitHooks.Prefix, StringComparison.Ordinal) && key.EndsWith(".runxowner", StringComparison.Ordinal))
        .Select(static key => key[..^".runxowner".Length])
        .Where(section => IsOwned(entries, section)).ToArray();

    private static bool IsOwned(Dictionary<string, string[]> entries, string section) => Single(entries, section + ".runxowner") == GitHooks.Owner;

    private static string? Single(Dictionary<string, string[]> entries, string key) =>
        entries.TryGetValue(key, out string[]? values) && values.Length == 1 ? values[0] : null;

    private static bool HasRegistration(Dictionary<string, string[]> entries, string section) =>
        entries.Keys.Any(key => key.StartsWith(section + ".", StringComparison.Ordinal) && key != section + ".enabled");

    private static bool SectionMatches(Dictionary<string, string[]> local, Dictionary<string, string[]> effective, string section) =>
        s_ownedProperties.Select(property => section + "." + property)
            .All(key => (local.GetValueOrDefault(key) ?? []).SequenceEqual(effective.GetValueOrDefault(key) ?? []));
}
