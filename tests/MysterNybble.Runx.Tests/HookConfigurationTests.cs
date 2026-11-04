using System.Text.Json;

namespace MysterNybble.Runx.Tests;

public sealed class HookConfigurationTests
{
    [Fact]
    public async Task ExplicitFileAndPersonalFoldersDoNotChangeHookValidation()
    {
        using TempDirectory temp = new();
        await GitHooksTests.InitAsync(temp.Path);
        WriteConfig(temp.Path, """{"version":1,"hooks":{"commit-msg":"git:message"},"tasks":{"ok":{"command":"dotnet"}}}""");
        string folder = Path.Combine(temp.Path, "eng", "tasks", "git");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "message.cs"), "Console.WriteLine(args[0]);");
        RecordingProcessRunner runner = new();
        Assert.Equal(0, await GitHooksTests.RunAsync(temp.Path, ["--file", "runx.json", "ok"], runner));
        Assert.Equal("ok", Assert.Single(runner.Requests).TaskName);
        Assert.Equal(0, await GitHooksTests.RunAsync(temp.Path, ["--file", "runx.json", "--check"], new()));

        string personal = Path.Combine(temp.Path, "personal");
        Directory.CreateDirectory(personal);
        File.WriteAllText(Path.Combine(personal, "global.json"), """{"version":1,"taskFolders":[],"tasks":{}}""");
        StringWriter output = new();
        Assert.Equal(0, await RunxCli.RunAsync(["--check", "--json"], output, TextWriter.Null, new RecordingProcessRunner(), new FakeTaskSelector(), temp.Path,
            new PersonalTasksSettings(personal, new FakeGitClient()), CancellationToken.None));
        using JsonDocument report = JsonDocument.Parse(output.ToString());
        Assert.True(report.RootElement.GetProperty("valid").GetBoolean());
        Assert.Equal(1, report.RootElement.GetProperty("taskCount").GetInt32());
        Assert.Equal(0, await GitHooksTests.RunAsync(temp.Path, ["--hook", "commit-msg", "--", "message.txt"], new()));
    }

    [Fact]
    public async Task NewCanCreateTheMissingTargetOfAnExistingBinding()
    {
        using TempDirectory temp = new();
        await GitHooksTests.InitAsync(temp.Path);
        WriteConfig(temp.Path, """{"version":1,"hooks":{"commit-msg":"git:message"},"tasks":{}}""");
        Assert.Equal(1, await GitHooksTests.RunAsync(temp.Path, ["--check"], new()));
        StringWriter error = new();
        Assert.Equal(0, await GitHooksTests.RunAsync(temp.Path, ["--new", "git:message", "--type", "cs"], new(), error: error));
        Assert.Empty(error.ToString());
        Assert.True(File.Exists(Path.Combine(temp.Path, "eng", "tasks", "git", "message.cs")));
        Assert.Equal(0, await GitHooksTests.RunAsync(temp.Path, ["--check"], new()));
    }

    [Fact]
    public async Task UnboundEventsIgnoreUnrelatedInvalidTasksButNotUnparseableJson()
    {
        using TempDirectory temp = new();
        await GitHooksTests.InitAsync(temp.Path);
        WriteConfig(temp.Path, """{"version":1,"hooks":{"post-commit":"missing"},"tasks":{"bad":{"command":"dotnet","script":"bad","shell":"sh"}}}""");
        RecordingProcessRunner runner = new();
        Assert.Equal(0, await GitHooksTests.RunAsync(temp.Path, ["--hook", "pre-commit"], runner));
        Assert.Equal(1, await GitHooksTests.RunAsync(temp.Path, ["--hook", "post-commit"], runner));
        WriteConfig(temp.Path, "{");
        Assert.Equal(1, await GitHooksTests.RunAsync(temp.Path, ["--hook", "pre-commit"], runner));
        Assert.Empty(runner.Requests);
    }

    [Fact]
    public async Task RootHookErrorsFailNestedCheckWithoutBlockingNestedTasks()
    {
        using TempDirectory temp = new();
        await GitHooksTests.InitAsync(temp.Path);
        WriteConfig(temp.Path, """{"version":1,"hooks":{"pre-commit":"missing"},"tasks":{}}""");
        string child = Path.Combine(temp.Path, "child");
        Directory.CreateDirectory(child);
        WriteConfig(child, """{"version":1,"tasks":{"ok":{"command":"dotnet"}}}""");
        StringWriter output = new();
        Assert.Equal(1, await GitHooksTests.RunAsync(child, ["--check", "--json"], new(), output));
        using JsonDocument report = JsonDocument.Parse(output.ToString());
        Assert.False(report.RootElement.GetProperty("valid").GetBoolean());
        Assert.Contains("unknown task", report.RootElement.GetProperty("errors")[0].GetString());
        Assert.Equal(0, await GitHooksTests.RunAsync(child, ["ok"], new()));
        output.GetStringBuilder().Clear();
        Assert.Equal(0, await GitHooksTests.RunAsync(child, ["--info", "ok", "--json"], new(), output));
        using JsonDocument info = JsonDocument.Parse(output.ToString());
        Assert.NotEqual(0, info.RootElement.GetProperty("hookErrors").GetArrayLength());
    }

    [Fact]
    public async Task DiscoveryReportsRootBindingsWithoutLabellingASameNamedNestedTask()
    {
        using TempDirectory temp = new();
        await GitHooksTests.InitAsync(temp.Path);
        WriteConfig(temp.Path, """{"version":1,"hooks":{"pre-commit":"check"},"tasks":{"check":{"command":"dotnet"}}}""");
        string child = Path.Combine(temp.Path, "child");
        Directory.CreateDirectory(child);
        WriteConfig(child, """{"version":1,"hooks":{"post-commit":"check"},"tasks":{"check":{"command":"another-tool"}}}""");
        StringWriter output = new();
        Assert.Equal(0, await GitHooksTests.RunAsync(child, ["--list", "--json"], new(), output));
        using JsonDocument list = JsonDocument.Parse(output.ToString());
        Assert.Equal("check", list.RootElement.GetProperty("hooks").GetProperty("pre-commit").GetString());
        Assert.False(list.RootElement.GetProperty("hooks").TryGetProperty("post-commit", out _));
        Assert.Equal(0, list.RootElement.GetProperty("tasks")[0].GetProperty("hooks").GetArrayLength());
        Assert.EndsWith("runx.json", list.RootElement.GetProperty("hookConfig").GetString());
        output.GetStringBuilder().Clear();
        Assert.Equal(0, await GitHooksTests.RunAsync(child, ["--info", "check", "--json"], new(), output));
        using JsonDocument info = JsonDocument.Parse(output.ToString());
        Assert.Equal(0, info.RootElement.GetProperty("hooks").GetArrayLength());
        output.GetStringBuilder().Clear();
        Assert.Equal(0, await GitHooksTests.RunAsync(temp.Path, ["--info", "check", "--json"], new(), output));
        using JsonDocument rootInfo = JsonDocument.Parse(output.ToString());
        Assert.Equal("pre-commit", rootInfo.RootElement.GetProperty("hooks")[0].GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EmptyInstallPreservesSharedRegistrationsUntilExplicitlyPruned(bool hasConfig)
    {
        using TempDirectory temp = new();
        await GitHooksTests.InitAsync(temp.Path);
        await GitHooksTests.RequireNamedHooksAsync(temp.Path);
        await GitHookInstallation.UpdateAsync(temp.Path, new Dictionary<string, string> { ["pre-commit"] = "check" }, "runx", TextWriter.Null, CancellationToken.None);
        if (hasConfig)
        {
            WriteConfig(temp.Path, """{"version":1,"hooks":{},"tasks":{}}""");
        }

        string path = Path.Combine(temp.Path, ".git", "config");
        string before = File.ReadAllText(path);
        Assert.Equal(0, await GitHooksTests.RunAsync(temp.Path, ["--install-hooks"], new()));
        Assert.Equal(before, File.ReadAllText(path));
        Assert.Equal(0, await GitHooksTests.RunAsync(temp.Path, ["--install-hooks", "--prune"], new()));
        Assert.DoesNotContain("runxowner", File.ReadAllText(path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EnablementPreferencesOutsideSharedConfigSurviveInstallAndUninstall(bool worktree)
    {
        using TempDirectory temp = new();
        await GitHooksTests.InitAsync(temp.Path);
        await GitHooksTests.RequireNamedHooksAsync(temp.Path);
        Dictionary<string, string> hooks = new() { ["pre-commit"] = "check" };
        string preferences;
        if (worktree)
        {
            await GitHooksTests.GitAsync(temp.Path, "config", "extensions.worktreeConfig", "true");
            await GitHooksTests.GitAsync(temp.Path, "config", "--worktree", "hook.runx-pre-commit.enabled", "off");
            preferences = Path.Combine(temp.Path, ".git", "config.worktree");
        }
        else
        {
            preferences = Path.Combine(temp.Path, "preferences.config");
            File.WriteAllText(preferences, "[hook \"runx-pre-commit\"]\n enabled = off\n");
            await GitHooksTests.GitAsync(temp.Path, "config", "include.path", preferences);
        }

        string before = File.ReadAllText(preferences);
        await GitHookInstallation.UpdateAsync(temp.Path, hooks, "runx", TextWriter.Null, CancellationToken.None);
        IReadOnlyList<string> warnings = await GitHookInstallation.CheckAsync(temp.Path, hooks, CancellationToken.None);
        Assert.Contains(warnings, static warning => warning.Contains("disabled", StringComparison.Ordinal));
        Assert.DoesNotContain(warnings, static warning => warning.Contains("overrides", StringComparison.Ordinal));
        await GitHookInstallation.UpdateAsync(temp.Path, hooks, null, TextWriter.Null, CancellationToken.None);
        Assert.Equal(before, File.ReadAllText(preferences));
    }

    [Fact]
    public async Task HookProtocolDoesNotDependOnHelpOrRepositoryConfiguration()
    {
        using TempDirectory temp = new();
        WriteConfig(temp.Path, "broken");
        StringWriter output = new();
        RecordingProcessRunner runner = new();
        Assert.Equal(0, await GitHooksTests.RunAsync(temp.Path, ["--hook-protocol"], runner, output));
        Assert.Equal("1", output.ToString().Trim());
        Assert.Empty(runner.Requests);
    }

    internal static void WriteConfig(string directory, string json) => File.WriteAllText(Path.Combine(directory, "runx.json"), json);
}
