namespace MysterNybble.Runx;

internal sealed record GitRepository(string Root, string CommonDirectory)
{
    public string ConfigPath => Path.Combine(CommonDirectory, "config");

    // Ask Git once, from the caller's directory, so environment overrides and linked worktrees
    // have the same meaning in dispatch, installation, validation, and discovery.
    public static async Task<GitRepository?> FindAsync(string directory, CancellationToken cancellationToken)
    {
        if (CommandResolver.ResolveFromEnvironment("git", directory) is null)
        {
            return null;
        }

        CommandOutput result = await GitHooks.GitAsync(directory, ["rev-parse", "--show-toplevel", "--git-common-dir"], cancellationToken);
        string[] lines = result.Output.TrimEnd('\r', '\n').Split('\n');
        if (result.ExitCode != 0 || lines.Length != 2 || lines.Any(static line => string.IsNullOrWhiteSpace(line) || line.StartsWith("--", StringComparison.Ordinal)))
        {
            return null;
        }

        return new GitRepository(Path.GetFullPath(lines[0].TrimEnd('\r'), directory), Path.GetFullPath(lines[1].TrimEnd('\r'), directory));
    }

    public static async Task<GitRepository> RequireAsync(string directory, CancellationToken cancellationToken) =>
        await FindAsync(directory, cancellationToken)
            ?? throw new InvalidOperationException("Git hooks require Git on PATH and a non-bare Git working tree.");
}
