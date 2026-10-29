namespace MysterNybble.Runx;

// Where personal task files live (RUNX_HOME, or ~/.runx) and how to ask Git about a repo.
internal sealed record PersonalTasksSettings(string RunxHome, IGitClient Git)
{
    public const string HomeVariable = "RUNX_HOME";

    public static PersonalTasksSettings? FromEnvironment()
    {
        string? home = Environment.GetEnvironmentVariable(HomeVariable);
        if (string.IsNullOrWhiteSpace(home))
        {
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrEmpty(profile))
            {
                return null;
            }

            home = Path.Combine(profile, ".runx");
        }

        return new PersonalTasksSettings(Path.GetFullPath(home), new GitCli());
    }

    public string GlobalFilePath => Path.Combine(RunxHome, "global.json");

    public string ReposDirectory => Path.Combine(RunxHome, "repos");
}

internal sealed record RepoIdentity(string RepoRoot, string Key, string KeySource, string? RemoteName = null);

// One personal tasks file that can apply to a repo. Scope is how the user names it on the command
// line ("global", "origin", "origin/acme", "repo"); Tag is how its tasks are marked in lists.
internal sealed record PersonalLayer(string Scope, string Path, bool IsRepoLevel, string? KeyPrefix = null)
{
    public const string RepoScope = "repo";
    public const string GlobalScope = "global";

    public bool Exists => File.Exists(Path);

    public string Tag => IsRepoLevel ? "personal" : Scope;
}

internal sealed record PersonalTasksFile(string Path, string RepoRoot, string Tag, bool IsRepoLevel);

internal sealed record PersonalTasksLookup(string RepoRoot, RepoIdentity? Identity, IReadOnlyList<PersonalLayer> Layers)
{
    // Broadest first, so more specific files are merged last and win.
    public IReadOnlyList<PersonalTasksFile> ExistingFiles()
    {
        return Layers
            .Where(static layer => layer.Exists)
            .Select(layer => new PersonalTasksFile(layer.Path, RepoRoot, layer.Tag, layer.IsRepoLevel))
            .ToArray();
    }
}

internal static class PersonalTasks
{
    public static async Task<PersonalTasksLookup?> FindAsync(
        PersonalTasksSettings? settings,
        string currentDirectory,
        bool evenWithoutPersonalFiles,
        CancellationToken cancellationToken)
    {
        if (settings is null)
        {
            return null;
        }

        string? repoRoot = RunxConfigDiscovery.FindGitRoot(Path.GetFullPath(currentDirectory));
        if (repoRoot is null)
        {
            return null;
        }

        // Git is only asked about the repo once some repo-keyed file exists; global.json alone needs no key.
        if (!evenWithoutPersonalFiles && !Directory.Exists(settings.ReposDirectory))
        {
            PersonalLayer global = new(PersonalLayer.GlobalScope, settings.GlobalFilePath, IsRepoLevel: false);
            return global.Exists ? new PersonalTasksLookup(repoRoot, Identity: null, [global]) : null;
        }

        RepoIdentity identity = await IdentifyAsync(repoRoot, settings.Git, OperatingSystem.IsWindows(), cancellationToken);
        return new PersonalTasksLookup(repoRoot, identity, GetLayers(settings, identity));
    }

    // global.json, then one file per shorter form of the repo key, then the repo's own file. Broader
    // scopes are named after the remote rather than its host: "origin" covers every repo on origin's
    // host, and "origin/acme" every repo under acme there.
    public static IReadOnlyList<PersonalLayer> GetLayers(PersonalTasksSettings settings, RepoIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(identity);

        string[] segments = identity.Key.Split('/');
        List<PersonalLayer> layers = [new(PersonalLayer.GlobalScope, settings.GlobalFilePath, IsRepoLevel: false)];
        for (int length = 1; length < segments.Length; length++)
        {
            string[] prefix = segments[..length];
            string scope = identity.RemoteName is null
                ? string.Join('/', prefix)
                : string.Join('/', [identity.RemoteName, .. prefix[1..]]);

            string keyPrefix = string.Join('/', prefix);
            layers.Add(new PersonalLayer(scope, RepoKey.ToFilePath(settings.RunxHome, keyPrefix), IsRepoLevel: false, keyPrefix));
        }

        layers.Add(new PersonalLayer(PersonalLayer.RepoScope, RepoKey.ToFilePath(settings.RunxHome, identity.Key), IsRepoLevel: true, identity.Key));
        return layers;
    }

    // Every worktree of a repo shares its remotes and its main checkout, so all of them get the same key.
    public static async Task<RepoIdentity> IdentifyAsync(
        string repoRoot,
        IGitClient git,
        bool isWindows,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoRoot);
        ArgumentNullException.ThrowIfNull(git);

        string remoteName = "origin";
        string? remoteUrl = await git.RunAsync(repoRoot, ["config", "--get", "remote.origin.url"], cancellationToken);
        if (remoteUrl is null)
        {
            string? remotes = await git.RunAsync(repoRoot, ["remote"], cancellationToken);
            string[] remoteNames = remotes?.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
            if (remoteNames.Length == 1)
            {
                remoteName = remoteNames[0];
                remoteUrl = await git.RunAsync(repoRoot, ["config", "--get", $"remote.{remoteName}.url"], cancellationToken);
            }
        }

        if (remoteUrl is not null && RepoKey.FromRemoteUrl(remoteUrl, isWindows) is string remoteKey)
        {
            return new RepoIdentity(repoRoot, remoteKey, $"remote '{remoteName}'", remoteName);
        }

        // Canonicalize through Git so a symlinked main checkout and its worktrees share a key.
        string? commonDirectory = await git.RunAsync(repoRoot, ["rev-parse", "--path-format=absolute", "--git-common-dir"], cancellationToken);
        if (commonDirectory is null)
        {
            return new RepoIdentity(repoRoot, RepoKey.FromLocalPath(repoRoot, isWindows), "repo folder, Git did not answer");
        }

        string mainCheckout = GetMainCheckout(Path.GetFullPath(commonDirectory, repoRoot));
        return new RepoIdentity(repoRoot, RepoKey.FromLocalPath(mainCheckout, isWindows), "main checkout folder, no remote");
    }

    private static string GetMainCheckout(string commonDirectory)
    {
        string trimmed = commonDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return Path.GetFileName(trimmed).Equals(".git", StringComparison.OrdinalIgnoreCase)
            ? Path.GetDirectoryName(trimmed) ?? trimmed
            : trimmed;
    }
}
