using System.ComponentModel;

namespace MysterNybble.Runx;

internal interface IGitClient
{
    // Returns trimmed standard output, or null when Git is missing, fails, or times out.
    Task<string?> RunAsync(string repoRoot, IReadOnlyList<string> arguments, CancellationToken cancellationToken);
}

internal sealed class GitCli : IGitClient
{
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(10);

    public async Task<string?> RunAsync(string repoRoot, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoRoot);
        ArgumentNullException.ThrowIfNull(arguments);

        string? executable = CommandResolver.ResolveFromEnvironment("git", repoRoot);
        if (executable is null)
        {
            return null;
        }

        try
        {
            CommandOutput result = await CapturedProcess.RunAsync(repoRoot, executable, arguments, s_timeout, cancellationToken);
            string output = result.Output.Trim();
            return result.ExitCode == 0 && output.Length > 0 ? output : null;
        }
        catch (Exception ex) when (ex is Win32Exception or TimeoutException)
        {
            return null;
        }
    }
}
