using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace MysterNybble.Runx;

internal interface IProcessRunner
{
    Task<ProcessResult> RunAsync(
        ProcessRequest request,
        TextWriter error,
        CancellationToken cancellationToken);
}

internal sealed record ProcessResult(int ExitCode, bool Interrupted = false);

internal sealed class DefaultProcessRunner : IProcessRunner
{
    public async Task<ProcessResult> RunAsync(
        ProcessRequest request,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(error);

        string? batchFilePath = null;
        try
        {
            ProcessStartInfo? startInfo = CreateStartInfo(request, error, out batchFilePath);
            return startInfo is null
                ? new ProcessResult(1)
                : await RunAsync(request, startInfo, error, cancellationToken);
        }
        finally
        {
            if (batchFilePath is not null)
            {
                TryDeleteFile(batchFilePath);
            }
        }
    }

    private static async Task<ProcessResult> RunAsync(
        ProcessRequest request,
        ProcessStartInfo startInfo,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        using Process process = new()
        {
            StartInfo = startInfo
        };

        // Ctrl+C reaches the child directly; runx stays alive so it can report the child's exit code,
        // and remembers the interrupt so no further tasks start.
        bool interrupted = false;
        void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
        {
            e.Cancel = true;
            interrupted = true;
        }

        Console.CancelKeyPress += OnCancelKeyPress;
        try
        {
            try
            {
                if (!process.Start())
                {
                    error.WriteLine($"Failed to start '{request.FileName}'.");
                    return new ProcessResult(1);
                }
            }
            catch (Win32Exception ex)
            {
                error.WriteLine($"Failed to start '{request.FileName}': {ex.Message}");
                return new ProcessResult(1);
            }
            catch (InvalidOperationException ex)
            {
                error.WriteLine($"Failed to start '{request.FileName}': {ex.Message}");
                return new ProcessResult(1);
            }

            try
            {
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                throw;
            }

            return new ProcessResult(process.ExitCode, interrupted);
        }
        finally
        {
            Console.CancelKeyPress -= OnCancelKeyPress;
        }
    }

    private static ProcessStartInfo? CreateStartInfo(ProcessRequest request, TextWriter error, out string? batchFilePath)
    {
        batchFilePath = null;

        // The child inherits runx's console so it keeps colors, progress output, and interactivity.
        ProcessStartInfo startInfo = new()
        {
            UseShellExecute = false,
            WorkingDirectory = request.WorkingDirectory
        };

        foreach (KeyValuePair<string, string> env in request.Environment)
        {
            startInfo.Environment[env.Key] = env.Value;
        }

        if (request.BatchFileContents is not null)
        {
            batchFilePath = Path.Combine(Path.GetTempPath(), $"runx-{Guid.NewGuid():N}.cmd");
            try
            {
                File.WriteAllText(batchFilePath, request.BatchFileContents, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                error.WriteLine($"Failed to write a temporary batch file for task '{request.TaskName}': {ex.Message}");
                return null;
            }

            startInfo.FileName = WindowsCommandLine.CmdPath;
            startInfo.Arguments = WindowsCommandLine.CreateBatchFileArguments(batchFilePath, []);
            return startInfo;
        }

        string? fileName = CommandResolver.Resolve(
            request.FileName,
            request.WorkingDirectory,
            GetEffectiveVariable(request, "PATH"),
            GetEffectiveVariable(request, "PATHEXT"),
            OperatingSystem.IsWindows());

        if (fileName is null)
        {
            error.WriteLine($"Failed to start '{request.FileName}': it was not found on PATH.");
            return null;
        }

        startInfo.FileName = fileName;

        if (request.VerbatimArguments)
        {
            startInfo.Arguments = string.Join(' ', request.Arguments);
        }
        else if (OperatingSystem.IsWindows() && WindowsCommandLine.IsBatchFile(startInfo.FileName))
        {
            if (!request.Arguments.All(WindowsCommandLine.CanPassToBatchFile))
            {
                error.WriteLine($"Failed to start '{request.FileName}': batch file arguments cannot contain line breaks.");
                return null;
            }

            startInfo.Arguments = WindowsCommandLine.CreateBatchFileArguments(startInfo.FileName, request.Arguments);
            startInfo.FileName = WindowsCommandLine.CmdPath;
        }
        else
        {
            foreach (string argument in request.Arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }
        }

        return startInfo;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover file in the temp directory is harmless.
        }
    }

    private static string? GetEffectiveVariable(ProcessRequest request, string name)
    {
        StringComparer comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        foreach (KeyValuePair<string, string> env in request.Environment)
        {
            if (comparer.Equals(env.Key, name))
            {
                return env.Value;
            }
        }

        return Environment.GetEnvironmentVariable(name);
    }
}
