using System.ComponentModel;
using System.Diagnostics;

namespace MysterNybble.Runx;

internal interface IProcessRunner
{
    Task<int> RunAsync(
        ProcessRequest request,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken);
}

internal sealed class DefaultProcessRunner : IProcessRunner
{
    public async Task<int> RunAsync(
        ProcessRequest request,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        ProcessStartInfo startInfo = new(request.FileName)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = request.WorkingDirectory
        };

        foreach (string argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (KeyValuePair<string, string> env in request.Environment)
        {
            startInfo.Environment[env.Key] = env.Value;
        }

        using Process process = new()
        {
            StartInfo = startInfo
        };

        try
        {
            if (!process.Start())
            {
                error.WriteLine($"Failed to start '{request.FileName}'.");
                return 1;
            }
        }
        catch (Win32Exception ex)
        {
            error.WriteLine($"Failed to start '{request.FileName}': {ex.Message}");
            return 1;
        }
        catch (InvalidOperationException ex)
        {
            error.WriteLine($"Failed to start '{request.FileName}': {ex.Message}");
            return 1;
        }

        Task outputTask = CopyToAsync(process.StandardOutput, output, cancellationToken);
        Task errorTask = CopyToAsync(process.StandardError, error, cancellationToken);

        await process.WaitForExitAsync(cancellationToken);
        await Task.WhenAll(outputTask, errorTask);

        return process.ExitCode;
    }

    private static async Task CopyToAsync(TextReader reader, TextWriter writer, CancellationToken cancellationToken)
    {
        char[] buffer = new char[4096];
        int read;
        while ((read = await reader.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await writer.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }
}
