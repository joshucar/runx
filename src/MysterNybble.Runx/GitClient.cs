using System.ComponentModel;
using System.Diagnostics;

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

        ProcessStartInfo startInfo = new("git")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        startInfo.ArgumentList.Add("-C");
        startInfo.ArgumentList.Add(repoRoot);
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(s_timeout);

        Process? process;
        try
        {
            process = Process.Start(startInfo);
        }
        catch (Win32Exception)
        {
            return null;
        }

        if (process is null)
        {
            return null;
        }

        using (process)
        {
            try
            {
                Task<string> output = process.StandardOutput.ReadToEndAsync(timeout.Token);
                Task<string> error = process.StandardError.ReadToEndAsync(timeout.Token);
                await process.WaitForExitAsync(timeout.Token);
                await Task.WhenAll(output, error);

                string result = (await output).Trim();
                return process.ExitCode == 0 && result.Length > 0 ? result : null;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                process.Kill(entireProcessTree: true);
                return null;
            }
        }
    }
}
