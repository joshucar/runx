using System.Text.Json;

namespace MysterNybble.Runx.Tests;

public sealed class GitHookProcessTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OrdinaryCommandsDoNotInspectHooksOrWarnWhenGitIsUnavailable(bool gitOnPath)
    {
        Assert.SkipWhen(gitOnPath && OperatingSystem.IsWindows(), "The Git tracing fixture is a Unix shell script.");
        using TempDirectory temp = new();
        await GitHooksTests.InitAsync(temp.Path);
        string dotnet = CommandResolver.ResolveFromEnvironment("dotnet", temp.Path)!;
        string git = CommandResolver.ResolveFromEnvironment("git", temp.Path)!;
        string marker = Path.Combine(temp.Path, "git-was-called");
        string bin = Path.Combine(temp.Path, "bin");
        Directory.CreateDirectory(bin);
        if (gitOnPath)
        {
            WriteShell(Path.Combine(bin, "git"), $"printf called > {GitHooks.ShellQuote(marker)}\nexec {GitHooks.ShellQuote(git)} \"$@\"\n");
        }

        HookConfigurationTests.WriteConfig(temp.Path,
            "{\"version\":1,\"hooks\":{\"pre-commit\":\"missing\"},\"tasks\":{\"build\":{\"command\":"
            + JsonSerializer.Serialize(dotnet) + ",\"args\":[\"--version\"]}}}");
        // The task's absolute executable still works when PATH contains no Git. Personal task
        // lookup is disabled by RunCliAsync so this measures hook-related work only.
        Dictionary<string, string> environment = new() { ["PATH"] = bin };
        foreach (string[] arguments in new string[][]
        {
            ["build"], ["--file", "runx.json", "build"], ["--dry-run", "build"],
            ["--list"], ["--new", "generated", "--type", "cs"]
        })
        {
            CommandOutput result = await RunCliAsync(temp.Path, arguments, environment);
            Assert.True(result.ExitCode == 0, result.Error);
            Assert.DoesNotContain("warning:", result.Error, StringComparison.Ordinal);
            Assert.False(File.Exists(marker), $"{string.Join(' ', arguments)} inspected Git hooks.");
        }
    }

    [Fact]
    public async Task CheckAndDispatchHonorTheSameGitEnvironmentRoot()
    {
        using TempDirectory temp = new();
        string actual = Path.Combine(temp.Path, "actual");
        string current = Path.Combine(temp.Path, "current");
        Directory.CreateDirectory(actual);
        Directory.CreateDirectory(current);
        await GitHooksTests.InitAsync(actual);
        await GitHooksTests.InitAsync(current);
        HookConfigurationTests.WriteConfig(actual, """{"version":1,"hooks":{"pre-commit":"task-a"},"tasks":{"task-a":{"command":"dotnet"}}}""");
        HookConfigurationTests.WriteConfig(current, """{"version":1,"hooks":{"pre-commit":"missing-b"},"tasks":{"task-b":{"command":"dotnet"}}}""");
        Dictionary<string, string> environment = new()
        {
            ["GIT_DIR"] = Path.Combine(actual, ".git"),
            ["GIT_WORK_TREE"] = actual
        };
        CommandOutput preview = await RunCliAsync(current, ["--dry-run", "--hook", "pre-commit"], environment);
        Assert.Equal(0, preview.ExitCode);
        Assert.Contains("Task: task-a", preview.Output);
        CommandOutput check = await RunCliAsync(current, ["--check", "--json"], environment);
        Assert.Equal(0, check.ExitCode);
        Assert.Contains("'pre-commit' -> 'task-a'", check.Output);
        Assert.DoesNotContain("missing-b", check.Output);
        CommandOutput info = await RunCliAsync(current, ["--list", "--json"], environment);
        using JsonDocument report = JsonDocument.Parse(info.Output);
        Assert.Equal("task-a", report.RootElement.GetProperty("hooks").GetProperty("pre-commit").GetString());
        Assert.Equal(0, report.RootElement.GetProperty("tasks")[0].GetProperty("hooks").GetArrayLength());
    }

    [Fact]
    public async Task DotnetHostedInstallationNeverSubstitutesARunxFromPath()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The unrelated launcher fixture is a shell script.");
        using TempDirectory temp = new();
        await GitHooksTests.InitAsync(temp.Path);
        HookConfigurationTests.WriteConfig(temp.Path, """{"version":1,"hooks":{"pre-commit":"check"},"tasks":{"check":{"command":"dotnet"}}}""");
        string bin = Path.Combine(temp.Path, "unrelated");
        Directory.CreateDirectory(bin);
        string marker = Path.Combine(temp.Path, "probed-unrelated-tool");
        WriteShell(Path.Combine(bin, "runx"), $"printf probed > {GitHooks.ShellQuote(marker)}\nprintf '1\\n'\n");
        string configPath = Path.Combine(temp.Path, ".git", "config");
        string before = File.ReadAllText(configPath);
        CommandOutput result = await RunCliAsync(temp.Path, ["--install-hooks"], new()
        {
            ["PATH"] = bin + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH")
        });
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("will not substitute", result.Error);
        Assert.False(File.Exists(marker));
        Assert.Equal(before, File.ReadAllText(configPath));
    }

    [Fact]
    public async Task GitInspectionIsBatchedAndUninstallDoesNotNeedNewRevParseOptions()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The Git tracing fixture is a shell script.");
        using TempDirectory temp = new();
        await GitHooksTests.InitAsync(temp.Path);
        await GitHooksTests.RequireNamedHooksAsync(temp.Path);
        string git = CommandResolver.ResolveFromEnvironment("git", temp.Path)!;
        string bin = Path.Combine(temp.Path, "bin");
        Directory.CreateDirectory(bin);
        string log = Path.Combine(temp.Path, "git-calls");
        string wrapper = Path.Combine(bin, "git");
        WriteShell(wrapper, $"printf '%s\\n' \"$*\" >> {GitHooks.ShellQuote(log)}\nexec {GitHooks.ShellQuote(git)} \"$@\"\n");
        Dictionary<string, string> environment = new() { ["PATH"] = bin + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH") };
        HookConfigurationTests.WriteConfig(temp.Path, """{"version":1,"tasks":{}}""");
        Assert.Equal(0, (await RunCliAsync(temp.Path, ["--check"], environment)).ExitCode);
        Assert.Equal(2, File.ReadAllLines(log).Length);

        Dictionary<string, string> hooks = new() { ["pre-commit"] = "check" };
        HookConfigurationTests.WriteConfig(temp.Path, """{"version":1,"hooks":{"pre-commit":"check"},"tasks":{"check":{"command":"dotnet"}}}""");
        await GitHookInstallation.UpdateAsync(temp.Path, hooks, "runx", TextWriter.Null, CancellationToken.None);
        File.WriteAllText(log, string.Empty);
        Assert.Equal(0, (await RunCliAsync(temp.Path, ["--check"], environment)).ExitCode);
        int oneBinding = File.ReadAllLines(log).Length;
        hooks["post-commit"] = "check";
        hooks["pre-push"] = "check";
        HookConfigurationTests.WriteConfig(temp.Path, """{"version":1,"hooks":{"pre-commit":"check","post-commit":"check","pre-push":"check"},"tasks":{"check":{"command":"dotnet"}}}""");
        await GitHookInstallation.UpdateAsync(temp.Path, hooks, "runx", TextWriter.Null, CancellationToken.None);
        File.WriteAllText(log, string.Empty);
        Assert.Equal(0, (await RunCliAsync(temp.Path, ["--check"], environment)).ExitCode);
        Assert.Equal(oneBinding, File.ReadAllLines(log).Length);

        WriteShell(wrapper, $"for arg do\n case \"$arg\" in --path-format=*) exit 77;; esac\ndone\nif [ \"$1\" = --version ]; then printf 'git version 2.30.0\\n'; exit; fi\nexec {GitHooks.ShellQuote(git)} \"$@\"\n");
        string child = Path.Combine(temp.Path, "child");
        Directory.CreateDirectory(child);
        CommandOutput removed = await RunCliAsync(child, ["--uninstall-hooks"], environment);
        Assert.True(removed.ExitCode == 0, removed.Error);
        Assert.DoesNotContain("runxowner", File.ReadAllText(Path.Combine(temp.Path, ".git", "config")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CapturedCommandsRespectTimeoutAndCallerCancellation(bool cancel)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The waiting process fixture uses a Unix shell.");
        using TempDirectory temp = new();
        using CancellationTokenSource cancellation = new();
        if (cancel)
        {
            cancellation.CancelAfter(TimeSpan.FromMilliseconds(100));
        }

        Task<CommandOutput> process = CapturedProcess.RunAsync(temp.Path, "/bin/sh", ["-c", "sleep 30"],
            cancel ? TimeSpan.FromSeconds(20) : TimeSpan.FromMilliseconds(100), cancellation.Token);
        if (cancel)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => process);
        }
        else
        {
            await Assert.ThrowsAsync<TimeoutException>(() => process);
        }
    }

    private static async Task<CommandOutput> RunCliAsync(string directory, string[] arguments, Dictionary<string, string> environment)
    {
        environment["RUNX_HOME"] = Path.Combine(directory, "empty-personal-home");
        string dotnet = CommandResolver.ResolveFromEnvironment("dotnet", directory)!;
        return await GitExecutableResolutionTests.RunAsync(dotnet, [typeof(RunxCli).Assembly.Location, .. arguments], directory, environment);
    }

    private static void WriteShell(string path, string body)
    {
        File.WriteAllText(path, "#!/bin/sh\n" + body);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }
}
