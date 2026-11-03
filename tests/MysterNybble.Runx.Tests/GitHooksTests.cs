using System.Text.Json;

namespace MysterNybble.Runx.Tests;

public sealed class GitHooksTests
{
    [Theory]
    [InlineData("{\"no-such-event\":\"check\"}", "unsupported Git hook")]
    [InlineData("{\"pre-commit\":\"missing\"}", "unknown task")]
    [InlineData("{\"pre-commit\":null}", "must name a task")]
    [InlineData("null", "must not be null")]
    public async Task InvalidBindingsAreReportedWithoutExecution(string hooks, string expected)
    {
        using TempDirectory temp = new();
        string path = Path.Combine(temp.Path, "runx.json");
        File.WriteAllText(path, "{\"version\":1,\"hooks\":" + hooks + ",\"tasks\":{\"check\":{\"command\":\"dotnet\"}}}");
        await InitAsync(temp.Path);
        ConfigLoadResult ordinary = await RunxConfigLoader.LoadAsync(path, CancellationToken.None);
        Assert.Empty(ordinary.Errors);
        HookConfiguration result = await HookConfiguration.LoadAsync(await GitRepository.RequireAsync(temp.Path, CancellationToken.None), CancellationToken.None);
        Assert.Contains(result.Errors, message => message.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ArgumentBearingHooksRequireAnEntryPointThatAcceptsArguments()
    {
        using TempDirectory temp = new();
        string path = Path.Combine(temp.Path, "runx.json");
        File.WriteAllText(path, """{"version":1,"hooks":{"commit-msg":"check"},"tasks":{"check":{"dependsOn":["build"]},"build":{"command":"dotnet"}}}""");
        await InitAsync(temp.Path);
        ConfigLoadResult ordinary = await RunxConfigLoader.LoadAsync(path, CancellationToken.None);
        Assert.Empty(ordinary.Errors);
        HookConfiguration result = await HookConfiguration.LoadAsync(await GitRepository.RequireAsync(temp.Path, CancellationToken.None), CancellationToken.None);
        Assert.Contains(result.Errors, static message => message.Contains("must be a command or script-file task", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("pre-push", new[] { "origin", "ssh://example/repo" }, false)]
    [InlineData("commit-msg", new[] { ".git/a message's file" }, true)]
    [InlineData("pre-commit", new string[0], true)]
    public async Task HookArgumentsAndInputBelongOnlyToTheEntryTask(string hook, string[] arguments, bool entryInputClosed)
    {
        using TempDirectory temp = new();
        await InitAsync(temp.Path);
        WriteConfig(temp.Path, hook);
        RecordingProcessRunner runner = new();
        StringWriter error = new();
        int exit = await RunAsync(temp.Path, ["--hook", hook, "--", .. arguments], runner, error: error);
        Assert.Equal(0, exit);
        Assert.Empty(error.ToString().Replace("> runx setup (1/2)", "", StringComparison.Ordinal).Replace("> runx entry (2/2)", "", StringComparison.Ordinal).Trim());
        Assert.Equal(["setup", "entry"], runner.Requests.Select(static request => request.TaskName));
        Assert.True(runner.Requests[0].CloseStandardInput);
        Assert.Empty(runner.Requests[0].Arguments);
        Assert.Equal(entryInputClosed, runner.Requests[1].CloseStandardInput);
        Assert.Equal(arguments, runner.Requests[1].Arguments);

        RecordingProcessRunner ordinary = new();
        Assert.Equal(0, await RunAsync(temp.Path, ["entry", "--", .. arguments], ordinary));
        Assert.All(ordinary.Requests, static request => Assert.False(request.CloseStandardInput));
    }

    [Fact]
    public async Task HookPreviewAndMissingBindingDoNotExecuteTasks()
    {
        using TempDirectory temp = new();
        await InitAsync(temp.Path);
        WriteConfig(temp.Path, "pre-commit");
        RecordingProcessRunner runner = new();
        StringWriter output = new();
        Assert.Equal(0, await RunAsync(temp.Path, ["--dry-run", "--hook", "pre-commit"], runner, output));
        Assert.Contains("setup -> entry", output.ToString());
        Assert.Equal(0, await RunAsync(temp.Path, ["--hook", "post-commit"], runner));
        Assert.Empty(runner.Requests);
        File.Delete(Path.Combine(temp.Path, "runx.json"));
        Assert.Equal(0, await RunAsync(temp.Path, ["--hook", "pre-commit"], runner));
        Assert.Empty(runner.Requests);
    }

    [Fact]
    public async Task MissingHookArgumentsFailBeforeDependenciesAndDependencyFailureStopsTheHook()
    {
        using TempDirectory temp = new();
        await InitAsync(temp.Path);
        WriteConfig(temp.Path, "commit-msg");
        RecordingProcessRunner runner = new() { ExitCode = 7 };
        StringWriter error = new();
        Assert.Equal(1, await RunAsync(temp.Path, ["--hook", "commit-msg"], runner, error: error));
        Assert.Contains("expects 1..1 arguments", error.ToString());
        Assert.Empty(runner.Requests);
        Assert.Equal(7, await RunAsync(temp.Path, ["--hook", "commit-msg", "--", "message"], runner));
        Assert.Equal("setup", Assert.Single(runner.Requests).TaskName);
    }

    [Theory]
    [InlineData("--install-hooks", "entry")]
    [InlineData("--uninstall-hooks", "--dry-run")]
    [InlineData("--hook", "pre-commit", "--file", "runx.json")]
    [InlineData("--hook", "pre-commit", "--personal")]
    [InlineData("--install-hooks", "--json")]
    [InlineData("--prune")]
    [InlineData("--uninstall-hooks", "--prune")]
    [InlineData("--hook-protocol", "entry")]
    public async Task HookOptionsDoNotSilentlyIgnoreOtherActions(params string[] args)
    {
        using TempDirectory temp = new();
        RecordingProcessRunner runner = new();
        Assert.Equal(1, await RunAsync(temp.Path, args, runner));
        Assert.Empty(runner.Requests);
    }

    [Fact]
    public async Task InstallIsIdempotentAndRemovalPreservesOtherHooksAndSettings()
    {
        using TempDirectory temp = new();
        await InitAsync(temp.Path);
        await RequireNamedHooksAsync(temp.Path);
        await GitAsync(temp.Path, "config", "core.hooksPath", "custom-hooks");
        await GitAsync(temp.Path, "config", "hook.other.event", "pre-commit");
        await GitAsync(temp.Path, "config", "hook.other.command", "other-tool");
        Dictionary<string, string> hooks = new() { ["pre-commit"] = "check", ["commit-msg"] = "message" };
        await GitHookInstallation.UpdateAsync(temp.Path, hooks, "runx", TextWriter.Null, CancellationToken.None);
        string configPath = Path.Combine(temp.Path, ".git", "config");
        string once = File.ReadAllText(configPath);
        await GitHookInstallation.UpdateAsync(temp.Path, hooks, "runx", TextWriter.Null, CancellationToken.None);
        Assert.Equal(once, File.ReadAllText(configPath));
        Assert.Equal("runx --hook pre-commit --", (await GitAsync(temp.Path, "config", "--get", "hook.runx-pre-commit.command")).Trim());
        hooks.Remove("commit-msg");
        await GitHookInstallation.UpdateAsync(temp.Path, hooks, "runx", TextWriter.Null, CancellationToken.None);
        Assert.Contains("runx-commit-msg", File.ReadAllText(configPath));
        await GitHookInstallation.UpdateAsync(temp.Path, hooks, "runx", TextWriter.Null, CancellationToken.None, prune: true);
        Assert.DoesNotContain("runx-commit-msg", File.ReadAllText(configPath));
        await GitAsync(temp.Path, "config", "hook.runx-pre-commit.enabled", "false");
        await GitHookInstallation.UpdateAsync(temp.Path, hooks, null, TextWriter.Null, CancellationToken.None);
        string removed = File.ReadAllText(configPath);
        Assert.DoesNotContain("runxowner", removed);
        Assert.Contains("enabled = false", removed);
        Assert.Equal("custom-hooks", (await GitAsync(temp.Path, "config", "--get", "core.hooksPath")).Trim());
        Assert.Equal("other-tool", (await GitAsync(temp.Path, "config", "--get", "hook.other.command")).Trim());
        Assert.False(File.Exists(configPath + ".lock"));
        await GitHookInstallation.UpdateAsync(temp.Path, hooks, "runx", TextWriter.Null, CancellationToken.None);
        Assert.Equal("false", (await GitAsync(temp.Path, "config", "--get", "hook.runx-pre-commit.enabled")).Trim());
        Assert.Equal("runx --hook pre-commit --", (await GitAsync(temp.Path, "config", "--get", "hook.runx-pre-commit.command")).Trim());
    }

    [Fact]
    public async Task ConflictingRegistrationAndExistingLockAreLeftUntouched()
    {
        using TempDirectory temp = new();
        await InitAsync(temp.Path);
        await RequireNamedHooksAsync(temp.Path);
        await GitAsync(temp.Path, "config", "hook.runx-pre-commit.command", "someone-else");
        string configPath = Path.Combine(temp.Path, ".git", "config");
        string original = File.ReadAllText(configPath);
        Dictionary<string, string> hooks = new() { ["pre-commit"] = "check" };
        await Assert.ThrowsAsync<InvalidOperationException>(() => GitHookInstallation.UpdateAsync(temp.Path, hooks, "runx", TextWriter.Null, CancellationToken.None));
        Assert.Equal(original, File.ReadAllText(configPath));
        Assert.False(File.Exists(configPath + ".lock"));
        File.WriteAllText(configPath + ".lock", "another process owns this");
        await Assert.ThrowsAsync<IOException>(() => GitHookInstallation.UpdateAsync(temp.Path, hooks, "runx", TextWriter.Null, CancellationToken.None));
        Assert.Equal("another process owns this", File.ReadAllText(configPath + ".lock"));
        Assert.Equal(original, File.ReadAllText(configPath));
    }

    [Fact]
    public async Task CheckReportsMissingDisabledAndStaleRegistrationsWithoutChangingThem()
    {
        using TempDirectory temp = new();
        await InitAsync(temp.Path);
        await RequireNamedHooksAsync(temp.Path);
        Dictionary<string, string> hooks = new() { ["pre-commit"] = "check" };
        IReadOnlyList<string> missing = await GitHookInstallation.CheckAsync(temp.Path, hooks, CancellationToken.None);
        Assert.Contains(missing, static message => message.Contains("not installed", StringComparison.Ordinal));
        await GitHookInstallation.UpdateAsync(temp.Path, hooks, "runx", TextWriter.Null, CancellationToken.None);
        await GitAsync(temp.Path, "config", "hook.runx-pre-commit.enabled", "false");
        string original = File.ReadAllText(Path.Combine(temp.Path, ".git", "config"));
        Assert.Contains(await GitHookInstallation.CheckAsync(temp.Path, hooks, CancellationToken.None), static message => message.Contains("disabled", StringComparison.Ordinal));
        Assert.Contains(await GitHookInstallation.CheckAsync(temp.Path, new Dictionary<string, string>(), CancellationToken.None), static message => message.Contains("no binding", StringComparison.Ordinal));
        Assert.Equal(original, File.ReadAllText(Path.Combine(temp.Path, ".git", "config")));
    }

    [Fact]
    public void LocalManifestSelectionHonorsIsRootAndAncestorTools()
    {
        using TempDirectory temp = new();
        string child = Path.Combine(temp.Path, "child");
        Directory.CreateDirectory(Path.Combine(child, ".config"));
        Directory.CreateDirectory(Path.Combine(temp.Path, ".config"));
        File.WriteAllText(Path.Combine(temp.Path, ".config", "dotnet-tools.json"), """{"isRoot":true,"tools":{"mysternybble.runx":{"version":"1.0.0","commands":["runx"]}}}""");
        Assert.True(GitHooks.HasLocalManifest(child));
        File.WriteAllText(Path.Combine(child, ".config", "dotnet-tools.json"), """{"isRoot":true,"tools":{}}""");
        Assert.False(GitHooks.HasLocalManifest(child));
    }

    [Fact]
    public async Task PersonalBindingsAreWarningsAndCannotSupplyHookTargets()
    {
        using TempDirectory temp = new();
        string repo = Path.Combine(temp.Path, "runx.json");
        string personal = Path.Combine(temp.Path, "personal.json");
        File.WriteAllText(repo, """{"version":1,"hooks":{"pre-commit":"mine"},"tasks":{}}""");
        File.WriteAllText(personal, """{"version":1,"hooks":{"post-commit":"mine"},"tasks":{"mine":{"command":"dotnet"}}}""");
        ConfigLoadResult result = await RunxConfigLoader.LoadAsync(repo, [new PersonalTasksFile(personal, temp.Path, "global", false)], null, CancellationToken.None);
        Assert.Empty(result.Errors);
        Assert.Contains(result.Warnings!, static message => message.Contains("are ignored", StringComparison.Ordinal));
        await InitAsync(temp.Path);
        HookConfiguration hooks = await HookConfiguration.LoadAsync(await GitRepository.RequireAsync(temp.Path, CancellationToken.None), CancellationToken.None);
        Assert.Contains(hooks.Errors, static message => message.Contains("unknown task 'mine'", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DispatchUsesRootConfigAndIgnoresPersonalFiles()
    {
        using TempDirectory temp = new();
        await InitAsync(temp.Path);
        WriteConfig(temp.Path, "pre-commit");
        string child = Path.Combine(temp.Path, "child");
        string personalHome = Path.Combine(temp.Path, "personal");
        Directory.CreateDirectory(child);
        Directory.CreateDirectory(personalHome);
        File.WriteAllText(Path.Combine(child, "runx.json"), "invalid nested config");
        File.WriteAllText(Path.Combine(personalHome, "global.json"), "invalid personal config");
        RecordingProcessRunner runner = new();
        Assert.Equal(0, await RunxCli.RunAsync(["--hook", "pre-commit"], TextWriter.Null, TextWriter.Null, runner, new FakeTaskSelector(), child,
            new PersonalTasksSettings(personalHome, new FakeGitClient()), CancellationToken.None));
        Assert.Equal(2, runner.Requests.Count);
        Assert.All(runner.Requests, request => Assert.Equal("runx.json", Path.GetFileName(request.ConfigPath)));
        Assert.All(runner.Requests, request => Assert.DoesNotContain("child", request.ConfigPath, StringComparison.Ordinal));
    }

    [Fact]
    public async Task CheckUsesRootBindingsEvenWhenDiscoverySelectsANestedConfig()
    {
        using TempDirectory temp = new();
        await InitAsync(temp.Path);
        await RequireNamedHooksAsync(temp.Path);
        WriteConfig(temp.Path, "pre-commit");
        Dictionary<string, string> hooks = new() { ["pre-commit"] = "entry" };
        await GitHookInstallation.UpdateAsync(temp.Path, hooks, "runx", TextWriter.Null, CancellationToken.None);
        string child = Path.Combine(temp.Path, "child");
        Directory.CreateDirectory(child);
        WriteConfig(child, "post-commit");
        StringWriter output = new();
        Assert.Equal(0, await RunAsync(child, ["--check"], new RecordingProcessRunner(), output));
        Assert.Contains("are not active", output.ToString());
        Assert.DoesNotContain("no binding", output.ToString());
        Assert.DoesNotContain("not installed", output.ToString());
    }

    [Fact]
    public async Task EmptyWorktreeCheckReportsStaleHooksAndBrokenConfigDoesNotPreventUninstall()
    {
        using TempDirectory temp = new();
        await InitAsync(temp.Path);
        await RequireNamedHooksAsync(temp.Path);
        Dictionary<string, string> hooks = new() { ["pre-commit"] = "check" };
        await GitHookInstallation.UpdateAsync(temp.Path, hooks, "runx", TextWriter.Null, CancellationToken.None);
        StringWriter output = new();
        Assert.Equal(0, await RunAsync(temp.Path, ["--check"], new RecordingProcessRunner(), output));
        Assert.Contains("no binding", output.ToString());
        File.WriteAllText(Path.Combine(temp.Path, "runx.json"), "invalid");
        Assert.Equal(0, await RunAsync(temp.Path, ["--uninstall-hooks"], new RecordingProcessRunner()));
        Assert.DoesNotContain("runxowner", File.ReadAllText(Path.Combine(temp.Path, ".git", "config")));
    }

    [Fact]
    public async Task MissingLauncherAndEditedCommandAreDiagnosedWithoutExecutingThem()
    {
        using TempDirectory temp = new();
        await InitAsync(temp.Path);
        await RequireNamedHooksAsync(temp.Path);
        Dictionary<string, string> hooks = new() { ["pre-commit"] = "check" };
        string executable = Path.Combine(temp.Path, "missing tool's executable").Replace('\\', '/');
        string launcher = GitHooks.ShellQuote(executable);
        Assert.Equal(executable, GitHooks.LauncherExecutable(launcher));
        await GitHookInstallation.UpdateAsync(temp.Path, hooks, launcher, TextWriter.Null, CancellationToken.None);
        Assert.Contains(await GitHookInstallation.CheckAsync(temp.Path, hooks, CancellationToken.None), static message => message.Contains("launcher is missing", StringComparison.Ordinal));
        await GitAsync(temp.Path, "config", "hook.runx-pre-commit.command", "other --hook pre-commit --");
        Assert.Contains(await GitHookInstallation.CheckAsync(temp.Path, hooks, CancellationToken.None), static message => message.Contains("out of date", StringComparison.Ordinal));
    }

    [Fact]
    public async Task HookBindingsAreReportedAsTextAndJson()
    {
        using TempDirectory temp = new();
        await InitAsync(temp.Path);
        WriteConfig(temp.Path, "pre-commit");
        StringWriter output = new();
        Assert.Equal(0, await RunAsync(temp.Path, ["--info", "entry"], new RecordingProcessRunner(), output));
        Assert.Contains("Git hooks:    pre-commit", output.ToString());
        output.GetStringBuilder().Clear();
        Assert.Equal(0, await RunAsync(temp.Path, ["--list", "--json"], new RecordingProcessRunner(), output));
        using JsonDocument json = JsonDocument.Parse(output.ToString());
        Assert.Equal("entry", json.RootElement.GetProperty("hooks").GetProperty("pre-commit").GetString());
        Assert.Equal("pre-commit", json.RootElement.GetProperty("tasks")[0].GetProperty("hooks")[0].GetString());
    }

    private static void WriteConfig(string root, string hook) => File.WriteAllText(Path.Combine(root, "runx.json"),
        "{\"version\":1,\"hooks\":{\"" + hook + "\":\"entry\"},\"tasks\":{\"setup\":{\"command\":\"dotnet\"},\"entry\":{\"command\":\"dotnet\",\"dependsOn\":[\"setup\"]}}}");

    internal static Task<int> RunAsync(string root, string[] args, RecordingProcessRunner runner, StringWriter? output = null, StringWriter? error = null) =>
        RunxCli.RunAsync(args, output ?? new StringWriter(), error ?? new StringWriter(), runner, new FakeTaskSelector(), root, CancellationToken.None);

    internal static Task<string> InitAsync(string root) => GitAsync(root, "init", "--quiet");

    internal static async Task<string> GitAsync(string root, params string[] args)
    {
        CommandOutput result = await GitHooks.GitAsync(root, args, CancellationToken.None);
        Assert.True(result.ExitCode == 0, result.Error);
        return result.Output;
    }

    internal static async Task RequireNamedHooksAsync(string root)
    {
        try
        {
            await GitHooks.RequireSupportedGitAsync(root, CancellationToken.None);
        }
        catch (InvalidOperationException)
        {
            Assert.Skip("Named-hook installation tests require Git 2.54 or newer.");
        }
    }
}
