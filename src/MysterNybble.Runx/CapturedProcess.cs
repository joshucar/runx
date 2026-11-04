using System.Diagnostics;

namespace MysterNybble.Runx;

// Infrastructure commands have closed stdin and captured output. Callers decide whether a
// missing executable, timeout, or nonzero exit is optional information or an actionable error.
internal static class CapturedProcess
{
    public static async Task<CommandOutput> RunAsync(string directory, string executable, IReadOnlyList<string> arguments,
        TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (!Path.IsPathFullyQualified(executable))
        {
            throw new ArgumentException("Resolve the executable to an absolute path before starting it.", nameof(executable));
        }

        using Process process = new()
        {
            StartInfo = new ProcessStartInfo(executable)
            {
                WorkingDirectory = directory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true
            }
        };
        foreach (string argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        cancellationToken.ThrowIfCancellationRequested();
        process.Start();
        process.StandardInput.Close();
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        Task<string> output = process.StandardOutput.ReadToEndAsync(deadline.Token);
        Task<string> error = process.StandardError.ReadToEndAsync(deadline.Token);
        try
        {
            await process.WaitForExitAsync(deadline.Token);
            return new CommandOutput(process.ExitCode, await output, await error);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            await process.WaitForExitAsync(CancellationToken.None);
            try
            {
                await Task.WhenAll(output, error);
            }
            catch (OperationCanceledException)
            {
                // The streams use the same deadline; observe their cancellation after reaping.
            }
            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException($"'{executable}' timed out.");
        }
    }
}

internal sealed record CommandOutput(int ExitCode, string Output, string Error)
{
    public void EnsureSuccess()
    {
        if (ExitCode != 0)
        {
            throw new InvalidOperationException($"Command failed: {Error.Trim()}");
        }
    }
}
