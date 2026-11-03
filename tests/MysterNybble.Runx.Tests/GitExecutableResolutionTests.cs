using System.Diagnostics;
using System.Text.Json;

namespace MysterNybble.Runx.Tests;

public sealed class GitExecutableResolutionTests
{
    [Theory]
    [InlineData(false, ".", true)]
    [InlineData(true, ".", true)]
    [InlineData(false, "tools", true)]
    [InlineData(true, "tools", true)]
    [InlineData(false, ".", false)]
    [InlineData(true, ".", false)]
    [InlineData(false, "tools", false)]
    [InlineData(true, "tools", false)]
    public async Task CheckNeverExecutesGitFromTheCheckout(bool personalLookup, string shadowDirectory, bool gitOnPath)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The shadow executable fixture uses a Unix shell script.");
        using TempDirectory temp = new();
        string? path = Environment.GetEnvironmentVariable("PATH");
        string dotnet = CommandResolver.Resolve("dotnet", temp.Path, path, null, isWindows: false)
            ?? throw new InvalidOperationException("This test requires dotnet on PATH.");
        string git = CommandResolver.Resolve("git", temp.Path, path, null, isWindows: false)
            ?? throw new InvalidOperationException("This test requires Git on PATH.");
        string runxHome = Path.Combine(temp.Path, "personal");
        if (personalLookup)
        {
            // Trigger the older GitCli path as well as the new hook status check.
            Directory.CreateDirectory(Path.Combine(runxHome, "repos"));
        }

        string gitConfig = Path.Combine(temp.Path, "empty-git-config");
        File.WriteAllText(gitConfig, string.Empty);
        Dictionary<string, string> environment = new()
        {
            ["RUNX_HOME"] = runxHome,
            ["GIT_CONFIG_GLOBAL"] = gitConfig,
            ["GIT_CONFIG_NOSYSTEM"] = "1"
        };
        CommandOutput initialized = await RunAsync(git, ["init", "--quiet"], temp.Path, environment);
        Assert.True(initialized.ExitCode == 0, initialized.Error);

        string shadowFolder = Path.Combine(temp.Path, shadowDirectory);
        Directory.CreateDirectory(shadowFolder);
        string shadow = Path.Combine(shadowFolder, "git");
        File.WriteAllText(shadow, "#!/bin/sh\nprintf shadowed > git-shadow-marker\nexit 42\n");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(shadow, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        string trustedPath = gitOnPath ? Path.GetDirectoryName(git)! : string.Empty;
        environment["PATH"] = shadowDirectory == "." ? trustedPath : "tools" + Path.PathSeparator + trustedPath;

        // A separate process is essential: Process.Start searches the parent's actual cwd,
        // so an in-process CLI call with only a currentDirectory argument misses this bug.
        CommandOutput check = await RunAsync(dotnet, [typeof(RunxCli).Assembly.Location, "--check", "--json"], temp.Path, environment);
        Assert.False(File.Exists(Path.Combine(temp.Path, "git-shadow-marker")), "Validation executed the checkout's fake Git executable.");
        Assert.True(check.ExitCode == 0, check.Error);
        using JsonDocument report = JsonDocument.Parse(check.Output);
        Assert.True(report.RootElement.GetProperty("valid").GetBoolean());
        Assert.Equal(0, report.RootElement.GetProperty("taskCount").GetInt32());
    }

    internal static async Task<CommandOutput> RunAsync(string executable, string[] arguments, string directory, Dictionary<string, string> environment)
    {
        using Process process = new()
        {
            StartInfo = new ProcessStartInfo(executable)
            {
                WorkingDirectory = directory,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        foreach (string argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        foreach ((string key, string value) in environment)
        {
            process.StartInfo.Environment[key] = value;
        }

        process.Start();
        process.StandardInput.Close();
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            return new CommandOutput(process.ExitCode, await output, await error);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }
        }
    }
}
