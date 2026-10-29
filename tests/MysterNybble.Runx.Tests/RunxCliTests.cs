namespace MysterNybble.Runx.Tests;

public sealed class RunxCliTests
{
    [Fact]
    public async Task NoArgumentsWithoutConfigPrintsHelpAndInitGuidance()
    {
        using TempDirectory tempDirectory = new();
        RecordingProcessRunner processRunner = new();
        StringWriter output = new();
        StringWriter error = new();

        int exitCode = await RunxCli.RunAsync([], output, error, processRunner, new FakeTaskSelector(), tempDirectory.Path, CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Contains("Usage:", output.ToString());
        Assert.Contains("runx --init", output.ToString());
        Assert.Empty(error.ToString());
        Assert.Empty(processRunner.Requests);
    }

    [Fact]
    public async Task VersionPrintsInformationalVersion()
    {
        using TempDirectory tempDirectory = new();
        StringWriter output = new();
        StringWriter error = new();

        int exitCode = await RunxCli.RunAsync(
            ["--version"],
            output,
            error,
            new RecordingProcessRunner(),
            new FakeTaskSelector(),
            tempDirectory.Path,
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.NotEmpty(output.ToString().Trim());
        Assert.Empty(error.ToString());
    }

    [Fact]
    public async Task InitCreatesStarterConfig()
    {
        using TempDirectory tempDirectory = new();
        StringWriter output = new();
        StringWriter error = new();

        int exitCode = await RunxCli.RunAsync(
            ["--init"],
            output,
            error,
            new RecordingProcessRunner(),
            new FakeTaskSelector(),
            tempDirectory.Path,
            CancellationToken.None);

        string configPath = System.IO.Path.Combine(tempDirectory.Path, "runx.json");
        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(configPath));
        Assert.Contains("\"$schema\": \"https://raw.githubusercontent.com/joshucar/runx/master/schemas/v1/runx.schema.json\"", File.ReadAllText(configPath));
        Assert.Contains("\"dotnet-version\"", File.ReadAllText(configPath));
        Assert.DoesNotContain("null", File.ReadAllText(configPath));
        Assert.Contains($"Created {configPath}.", output.ToString());
        Assert.Empty(error.ToString());
    }

    [Fact]
    public async Task InitRequiresForceForExistingConfig()
    {
        using TempDirectory tempDirectory = new();
        string configPath = System.IO.Path.Combine(tempDirectory.Path, "runx.json");
        File.WriteAllText(configPath, "{}");

        StringWriter output = new();
        StringWriter error = new();

        int exitCode = await RunxCli.RunAsync(
            ["--init"],
            output,
            error,
            new RecordingProcessRunner(),
            new FakeTaskSelector(),
            tempDirectory.Path,
            CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Equal("{}", File.ReadAllText(configPath));
        Assert.Empty(output.ToString());
        Assert.Contains("Use --force to overwrite it.", error.ToString());
    }

    [Fact]
    public async Task DryRunCannotBeCombinedWithInit()
    {
        using TempDirectory tempDirectory = new();
        StringWriter output = new();
        StringWriter error = new();

        int exitCode = await RunxCli.RunAsync(
            ["--init", "--dry-run"],
            output,
            error,
            new RecordingProcessRunner(),
            new FakeTaskSelector(),
            tempDirectory.Path,
            CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.False(File.Exists(System.IO.Path.Combine(tempDirectory.Path, "runx.json")));
        Assert.Empty(output.ToString());
        Assert.Contains("--dry-run can only be used with a task.", error.ToString());
    }

    [Fact]
    public async Task ListPrintsTaskNamesAndDescriptions()
    {
        using TempDirectory tempDirectory = new();
        WriteGitDirectory(tempDirectory.Path);
        WriteConfig(
            tempDirectory.Path,
            """
            {
              "$schema": "https://raw.githubusercontent.com/joshucar/runx/master/schemas/v1/runx.schema.json",
              "version": 1,
              "tasks": {
                "test": {
                  "description": "Run tests.",
                  "command": "dotnet",
                  "args": ["test"]
                },
                "build": {
                  "description": "Build the repo.",
                  "command": "dotnet",
                  "args": ["build"]
                },
                "clean": {
                  "command": "dotnet",
                  "args": ["clean"]
                }
              }
            }
            """);

        StringWriter output = new();
        StringWriter error = new();

        int exitCode = await RunxCli.RunAsync(
            ["--list"],
            output,
            error,
            new RecordingProcessRunner(),
            new FakeTaskSelector(),
            tempDirectory.Path,
            CancellationToken.None);

        string[] lines = output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(0, exitCode);
        Assert.Equal(["build  Build the repo.", "clean", "test   Run tests."], lines);
        Assert.Empty(error.ToString());
    }

    [Fact]
    public async Task RunsStructuredTaskFromConfigDirectoryWithPassthroughArgs()
    {
        using TempDirectory tempDirectory = new();
        string repoPath = tempDirectory.Path;
        string childPath = System.IO.Path.Combine(repoPath, "src", "App");
        Directory.CreateDirectory(childPath);
        Directory.CreateDirectory(System.IO.Path.Combine(repoPath, "artifacts"));
        WriteGitDirectory(repoPath);
        WriteConfig(
            repoPath,
            """
            {
              "$schema": "https://raw.githubusercontent.com/joshucar/runx/master/schemas/v1/runx.schema.json",
              "version": 1,
              "tasks": {
                "test": {
                  "description": "Run tests.",
                  "command": "dotnet",
                  "args": ["test"],
                  "cwd": "artifacts",
                  "env": {
                    "RUNX_TEST": "1"
                  }
                }
              }
            }
            """);

        RecordingProcessRunner processRunner = new();
        processRunner.ExitCode = 7;
        StringWriter output = new();
        StringWriter error = new();

        int exitCode = await RunxCli.RunAsync(
            ["test", "--", "--filter", "Smoke"],
            output,
            error,
            processRunner,
            new FakeTaskSelector(),
            childPath,
            CancellationToken.None);

        ProcessRequest request = Assert.Single(processRunner.Requests);
        Assert.Equal(7, exitCode);
        Assert.Equal("test", request.TaskName);
        Assert.Equal("dotnet", request.FileName);
        Assert.Equal(["test", "--filter", "Smoke"], request.Arguments);
        Assert.Equal(System.IO.Path.Combine(repoPath, "artifacts"), request.WorkingDirectory);
        Assert.Equal("1", request.Environment["RUNX_TEST"]);
    }

    [Fact]
    public async Task DryRunShowsEnvKeysButNotValues()
    {
        using TempDirectory tempDirectory = new();
        WriteGitDirectory(tempDirectory.Path);
        WriteConfig(
            tempDirectory.Path,
            """
            {
              "$schema": "https://raw.githubusercontent.com/joshucar/runx/master/schemas/v1/runx.schema.json",
              "version": 1,
              "tasks": {
                "deploy": {
                  "description": "Deploy.",
                  "command": "dotnet",
                  "args": ["publish"],
                  "env": {
                    "SECRET_TOKEN": "super-secret"
                  }
                }
              }
            }
            """);

        RecordingProcessRunner processRunner = new();
        StringWriter output = new();
        StringWriter error = new();

        int exitCode = await RunxCli.RunAsync(
            ["--dry-run", "deploy"],
            output,
            error,
            processRunner,
            new FakeTaskSelector(),
            tempDirectory.Path,
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Contains("SECRET_TOKEN", output.ToString());
        Assert.DoesNotContain("super-secret", output.ToString());
        Assert.Empty(error.ToString());
        Assert.Empty(processRunner.Requests);
    }

    [Fact]
    public async Task FileOptionBypassesDiscovery()
    {
        using TempDirectory tempDirectory = new();
        string repoPath = System.IO.Path.Combine(tempDirectory.Path, "repo");
        string otherPath = System.IO.Path.Combine(tempDirectory.Path, "other");
        Directory.CreateDirectory(repoPath);
        Directory.CreateDirectory(otherPath);
        WriteGitDirectory(repoPath);
        WriteConfig(
            otherPath,
            """
            {
              "$schema": "https://raw.githubusercontent.com/joshucar/runx/master/schemas/v1/runx.schema.json",
              "version": 1,
              "tasks": {
                "build": {
                  "command": "dotnet",
                  "args": ["build"]
                }
              }
            }
            """);

        RecordingProcessRunner processRunner = new();
        StringWriter output = new();
        StringWriter error = new();

        int exitCode = await RunxCli.RunAsync(
            ["--file", System.IO.Path.Combine(otherPath, "runx.json"), "build"],
            output,
            error,
            processRunner,
            new FakeTaskSelector(),
            repoPath,
            CancellationToken.None);

        ProcessRequest request = Assert.Single(processRunner.Requests);
        Assert.Equal(0, exitCode);
        Assert.Equal(System.IO.Path.Combine(otherPath, "runx.json"), request.ConfigPath);
        Assert.Empty(error.ToString());
    }

    [Fact]
    public async Task DiscoveryStopsAtGitRootUnlessParentSearchIsAllowed()
    {
        using TempDirectory tempDirectory = new();
        WriteConfig(
            tempDirectory.Path,
            """
            {
              "$schema": "https://raw.githubusercontent.com/joshucar/runx/master/schemas/v1/runx.schema.json",
              "version": 1,
              "tasks": {
                "build": {
                  "command": "dotnet",
                  "args": ["build"]
                }
              }
            }
            """);

        string repoPath = System.IO.Path.Combine(tempDirectory.Path, "repo");
        string childPath = System.IO.Path.Combine(repoPath, "src");
        Directory.CreateDirectory(childPath);
        WriteGitDirectory(repoPath);

        StringWriter firstOutput = new();
        StringWriter firstError = new();
        int firstExitCode = await RunxCli.RunAsync(
            ["--list"],
            firstOutput,
            firstError,
            new RecordingProcessRunner(),
            new FakeTaskSelector(),
            childPath,
            CancellationToken.None);

        StringWriter secondOutput = new();
        StringWriter secondError = new();
        int secondExitCode = await RunxCli.RunAsync(
            ["--allow-parent-search", "--list"],
            secondOutput,
            secondError,
            new RecordingProcessRunner(),
            new FakeTaskSelector(),
            childPath,
            CancellationToken.None);

        Assert.Equal(1, firstExitCode);
        Assert.Contains("Git repo root", firstError.ToString());
        Assert.Equal(0, secondExitCode);
        Assert.Contains("build", secondOutput.ToString());
        Assert.Empty(secondError.ToString());
    }

    [Fact]
    public async Task StrictValidationRejectsUnknownProperties()
    {
        using TempDirectory tempDirectory = new();
        WriteGitDirectory(tempDirectory.Path);
        WriteConfig(
            tempDirectory.Path,
            """
            {
              "$schema": "https://raw.githubusercontent.com/joshucar/runx/master/schemas/v1/runx.schema.json",
              "version": 1,
              "unknown": true,
              "tasks": {
                "build": {
                  "command": "dotnet",
                  "args": ["build"],
                  "extra": "nope"
                }
              }
            }
            """);

        StringWriter output = new();
        StringWriter error = new();

        int exitCode = await RunxCli.RunAsync(
            ["--list"],
            output,
            error,
            new RecordingProcessRunner(),
            new FakeTaskSelector(),
            tempDirectory.Path,
            CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Empty(output.ToString());
        Assert.Contains("Unknown config property '$.unknown'.", error.ToString());
        Assert.Contains("Unknown config property 'tasks.build.extra'.", error.ToString());
    }

    [Fact]
    public async Task ShellTaskUsesPwshPreset()
    {
        using TempDirectory tempDirectory = new();
        WriteGitDirectory(tempDirectory.Path);
        WriteConfig(
            tempDirectory.Path,
            """
            {
              "$schema": "https://raw.githubusercontent.com/joshucar/runx/master/schemas/v1/runx.schema.json",
              "version": 1,
              "tasks": {
                "hello": {
                  "script": "Write-Output hello",
                  "shell": "pwsh"
                }
              }
            }
            """);

        RecordingProcessRunner processRunner = new();
        StringWriter output = new();
        StringWriter error = new();

        int exitCode = await RunxCli.RunAsync(
            ["hello"],
            output,
            error,
            processRunner,
            new FakeTaskSelector(),
            tempDirectory.Path,
            CancellationToken.None);

        ProcessRequest request = Assert.Single(processRunner.Requests);
        Assert.Equal(0, exitCode);
        Assert.Equal("shell:pwsh", request.Mode);
        Assert.Equal("pwsh", request.FileName);
        Assert.Equal(["-NoLogo", "-NoProfile", "-Command", "Write-Output hello"], request.Arguments);
    }

    [Fact]
    public async Task NoArgumentsPromptsAndRunsSelectedTaskWhenInteractive()
    {
        using TempDirectory tempDirectory = new();
        WriteTwoTaskConfig(tempDirectory.Path);

        FakeTaskSelector taskSelector = new() { IsInteractive = true, Selection = "test" };
        RecordingProcessRunner processRunner = new() { ExitCode = 4 };
        StringWriter output = new();
        StringWriter error = new();

        int exitCode = await RunxCli.RunAsync([], output, error, processRunner, taskSelector, tempDirectory.Path, CancellationToken.None);

        ProcessRequest request = Assert.Single(processRunner.Requests);
        Assert.Equal(4, exitCode);
        Assert.Equal(1, taskSelector.PromptCount);
        Assert.Equal("test", request.TaskName);
        Assert.Equal(["test"], request.Arguments);
        Assert.Empty(error.ToString());
    }

    [Fact]
    public async Task NoArgumentsListsTasksWhenNotInteractive()
    {
        using TempDirectory tempDirectory = new();
        WriteTwoTaskConfig(tempDirectory.Path);

        FakeTaskSelector taskSelector = new() { IsInteractive = false, Selection = "test" };
        RecordingProcessRunner processRunner = new();
        StringWriter output = new();
        StringWriter error = new();

        int exitCode = await RunxCli.RunAsync([], output, error, processRunner, taskSelector, tempDirectory.Path, CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Equal(0, taskSelector.PromptCount);
        Assert.Contains("Build the repo.", output.ToString());
        Assert.Empty(processRunner.Requests);
    }

    [Fact]
    public async Task ListOptionNeverPrompts()
    {
        using TempDirectory tempDirectory = new();
        WriteTwoTaskConfig(tempDirectory.Path);

        FakeTaskSelector taskSelector = new() { IsInteractive = true, Selection = "test" };
        RecordingProcessRunner processRunner = new();
        StringWriter output = new();
        StringWriter error = new();

        int exitCode = await RunxCli.RunAsync(["--list"], output, error, processRunner, taskSelector, tempDirectory.Path, CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Equal(0, taskSelector.PromptCount);
        Assert.Contains("Run tests.", output.ToString());
        Assert.Empty(processRunner.Requests);
    }

    [Fact]
    public async Task CancelledPromptRunsNothing()
    {
        using TempDirectory tempDirectory = new();
        WriteTwoTaskConfig(tempDirectory.Path);

        FakeTaskSelector taskSelector = new() { IsInteractive = true, Selection = null };
        RecordingProcessRunner processRunner = new();
        StringWriter output = new();
        StringWriter error = new();

        int exitCode = await RunxCli.RunAsync([], output, error, processRunner, taskSelector, tempDirectory.Path, CancellationToken.None);

        Assert.Equal(130, exitCode);
        Assert.Empty(processRunner.Requests);
        Assert.Empty(error.ToString());
    }

    [Fact]
    public async Task DryRunWithoutTaskPromptsWhenInteractive()
    {
        using TempDirectory tempDirectory = new();
        WriteTwoTaskConfig(tempDirectory.Path);

        FakeTaskSelector taskSelector = new() { IsInteractive = true, Selection = "build" };
        RecordingProcessRunner processRunner = new();
        StringWriter output = new();
        StringWriter error = new();

        int exitCode = await RunxCli.RunAsync(["--dry-run"], output, error, processRunner, taskSelector, tempDirectory.Path, CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Contains("Task: build", output.ToString());
        Assert.Empty(processRunner.Requests);
    }

    [Fact]
    public async Task DryRunWithoutTaskFailsWhenNotInteractive()
    {
        using TempDirectory tempDirectory = new();
        WriteTwoTaskConfig(tempDirectory.Path);

        FakeTaskSelector taskSelector = new() { IsInteractive = false };
        StringWriter output = new();
        StringWriter error = new();

        int exitCode = await RunxCli.RunAsync(["--dry-run"], output, error, new RecordingProcessRunner(), taskSelector, tempDirectory.Path, CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Contains("--dry-run requires a task name.", error.ToString());
    }

    [Fact]
    public async Task DependsOnRunsDependenciesFirstAndOnlyOnce()
    {
        using TempDirectory tempDirectory = new();
        WriteDependencyConfig(tempDirectory.Path);

        RecordingProcessRunner processRunner = new();
        StringWriter output = new();
        StringWriter error = new();

        int exitCode = await RunxCli.RunAsync(
            ["test", "--", "--filter", "Smoke"],
            output,
            error,
            processRunner,
            new FakeTaskSelector(),
            tempDirectory.Path,
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Equal(["build", "lint", "test"], processRunner.Requests.Select(static request => request.TaskName));
        Assert.Equal(["build"], processRunner.Requests[0].Arguments);
        Assert.Equal(["test", "--filter", "Smoke"], processRunner.Requests[2].Arguments);
        Assert.Contains("> runx build (1/3)", error.ToString());
        Assert.Contains("> runx test (3/3)", error.ToString());
    }

    [Fact]
    public async Task FailedDependencyStopsTheRun()
    {
        using TempDirectory tempDirectory = new();
        WriteDependencyConfig(tempDirectory.Path);

        RecordingProcessRunner processRunner = new() { ExitCode = 2 };
        StringWriter output = new();
        StringWriter error = new();

        int exitCode = await RunxCli.RunAsync(["test"], output, error, processRunner, new FakeTaskSelector(), tempDirectory.Path, CancellationToken.None);

        ProcessRequest request = Assert.Single(processRunner.Requests);
        Assert.Equal(2, exitCode);
        Assert.Equal("build", request.TaskName);
        Assert.Contains("Task 'build' failed with exit code 2.", error.ToString());
    }

    [Fact]
    public async Task DependsOnOnlyTaskRunsItsDependencies()
    {
        using TempDirectory tempDirectory = new();
        WriteDependencyConfig(tempDirectory.Path);

        RecordingProcessRunner processRunner = new();
        StringWriter output = new();
        StringWriter error = new();

        int exitCode = await RunxCli.RunAsync(["ci"], output, error, processRunner, new FakeTaskSelector(), tempDirectory.Path, CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Equal(["build", "lint", "test", "pack"], processRunner.Requests.Select(static request => request.TaskName));
    }

    [Fact]
    public async Task DependsOnOnlyTaskRejectsPassthroughArguments()
    {
        using TempDirectory tempDirectory = new();
        WriteDependencyConfig(tempDirectory.Path);

        RecordingProcessRunner processRunner = new();
        StringWriter output = new();
        StringWriter error = new();

        int exitCode = await RunxCli.RunAsync(["ci", "--", "x"], output, error, processRunner, new FakeTaskSelector(), tempDirectory.Path, CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Empty(processRunner.Requests);
        Assert.Contains("Task 'ci' only runs its dependencies.", error.ToString());
    }

    [Fact]
    public async Task DryRunShowsDependencyPlan()
    {
        using TempDirectory tempDirectory = new();
        WriteDependencyConfig(tempDirectory.Path);

        RecordingProcessRunner processRunner = new();
        StringWriter output = new();
        StringWriter error = new();

        int exitCode = await RunxCli.RunAsync(["--dry-run", "test"], output, error, processRunner, new FakeTaskSelector(), tempDirectory.Path, CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.StartsWith("Plan: build -> lint -> test", output.ToString());
        Assert.Contains("Task: lint", output.ToString());
        Assert.Empty(processRunner.Requests);
    }

    [Fact]
    public async Task ValidationRejectsBadDependencies()
    {
        using TempDirectory tempDirectory = new();
        WriteGitDirectory(tempDirectory.Path);
        WriteConfig(
            tempDirectory.Path,
            """
            {
              "version": 1,
              "tasks": {
                "a": { "command": "dotnet", "dependsOn": ["missing", "b", "b"] },
                "b": { "dependsOn": [], "cwd": "src" },
                "c": { "command": "dotnet" }
              }
            }
            """);

        StringWriter output = new();
        StringWriter error = new();

        int exitCode = await RunxCli.RunAsync(["--list"], output, error, new RecordingProcessRunner(), new FakeTaskSelector(), tempDirectory.Path, CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Contains("Task 'a' depends on unknown task 'missing'.", error.ToString());
        Assert.Contains("Task 'a' lists 'b' in dependsOn more than once.", error.ToString());
        Assert.Contains("Task 'b' dependsOn must not be empty.", error.ToString());
        Assert.Contains("Task 'b' cannot define 'cwd' without a 'command' or 'script'.", error.ToString());
    }

    [Fact]
    public async Task ValidationRejectsDependencyCycles()
    {
        using TempDirectory tempDirectory = new();
        WriteGitDirectory(tempDirectory.Path);
        WriteConfig(
            tempDirectory.Path,
            """
            {
              "version": 1,
              "tasks": {
                "a": { "command": "dotnet", "dependsOn": ["b"] },
                "b": { "dependsOn": ["c"] },
                "c": { "command": "dotnet", "dependsOn": ["a"] },
                "self": { "command": "dotnet", "dependsOn": ["self"] }
              }
            }
            """);

        StringWriter output = new();
        StringWriter error = new();

        int exitCode = await RunxCli.RunAsync(["--list"], output, error, new RecordingProcessRunner(), new FakeTaskSelector(), tempDirectory.Path, CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Contains("Task dependencies form a cycle: a -> b -> c -> a.", error.ToString());
        Assert.Contains("Task dependencies form a cycle: self -> self.", error.ToString());
    }

    [Fact]
    public async Task InterruptStopsRemainingTasksEvenWhenTheChildSucceeds()
    {
        using TempDirectory tempDirectory = new();
        WriteDependencyConfig(tempDirectory.Path);

        RecordingProcessRunner processRunner = new() { InterruptedTaskName = "build" };
        StringWriter output = new();
        StringWriter error = new();

        int exitCode = await RunxCli.RunAsync(["ci"], output, error, processRunner, new FakeTaskSelector(), tempDirectory.Path, CancellationToken.None);

        ProcessRequest request = Assert.Single(processRunner.Requests);
        Assert.Equal(130, exitCode);
        Assert.Equal("build", request.TaskName);
        Assert.Contains("Interrupted during task 'build'. Remaining tasks were skipped.", error.ToString());
    }

    [Fact]
    public async Task DependencyChainRunsRealProcessesAndStopsAtFailure()
    {
        using TempDirectory tempDirectory = new();
        WriteGitDirectory(tempDirectory.Path);
        string shell = OperatingSystem.IsWindows() ? "cmd" : "sh";
        string exit = OperatingSystem.IsWindows() ? "exit /b" : "exit";
        WriteConfig(
            tempDirectory.Path,
            $$"""
            {
              "version": 1,
              "tasks": {
                "first": { "script": "echo first>> log.txt", "shell": "{{shell}}" },
                "second": { "script": "echo second>> log.txt\n{{exit}} 7", "shell": "{{shell}}", "dependsOn": ["first"] },
                "third": { "script": "echo third>> log.txt", "shell": "{{shell}}", "dependsOn": ["second"] }
              }
            }
            """);

        StringWriter output = new();
        StringWriter error = new();

        int exitCode = await RunxCli.RunAsync(
            ["third"],
            output,
            error,
            new DefaultProcessRunner(),
            new FakeTaskSelector(),
            tempDirectory.Path,
            CancellationToken.None);

        string[] log = File.ReadAllLines(System.IO.Path.Combine(tempDirectory.Path, "log.txt"));
        Assert.Equal(7, exitCode);
        Assert.Equal(["first", "second"], log.Select(static line => line.Trim()));
        Assert.Contains("Task 'second' failed with exit code 7.", error.ToString());
    }

    [Fact]
    public async Task DryRunShowsBatchFileForMultiLineCmdScripts()
    {
        using TempDirectory tempDirectory = new();
        WriteGitDirectory(tempDirectory.Path);
        WriteConfig(
            tempDirectory.Path,
            """
            {
              "version": 1,
              "tasks": {
                "multi": { "script": "echo one\r\necho two", "shell": "cmd" }
              }
            }
            """);

        StringWriter output = new();
        StringWriter error = new();

        int exitCode = await RunxCli.RunAsync(["--dry-run", "multi"], output, error, new RecordingProcessRunner(), new FakeTaskSelector(), tempDirectory.Path, CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Contains($"Batch file:{Environment.NewLine}  @echo off{Environment.NewLine}  echo one{Environment.NewLine}  echo two", output.ToString());
    }

    [Fact]
    public async Task ExplicitMissingConfigFileFails()
    {
        using TempDirectory tempDirectory = new();
        StringWriter output = new();
        StringWriter error = new();

        int exitCode = await RunxCli.RunAsync(["--file", "missing.json"], output, error, new RecordingProcessRunner(), new FakeTaskSelector(), tempDirectory.Path, CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Empty(output.ToString());
        Assert.Contains("Config file not found", error.ToString());
    }

    [Fact]
    public async Task InitIntoDirectoryFails()
    {
        using TempDirectory tempDirectory = new();
        Directory.CreateDirectory(System.IO.Path.Combine(tempDirectory.Path, "folder"));
        StringWriter output = new();
        StringWriter error = new();

        int exitCode = await RunxCli.RunAsync(["--init", "--file", "folder"], output, error, new RecordingProcessRunner(), new FakeTaskSelector(), tempDirectory.Path, CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Contains("because it is a directory", error.ToString());
    }

    [Fact]
    public async Task ValidationRejectsEmptyCommandsScriptsAndUnsafeCharacters()
    {
        using TempDirectory tempDirectory = new();
        WriteGitDirectory(tempDirectory.Path);
        WriteConfig(
            tempDirectory.Path,
            """
            {
              "version": 1,
              "tasks": {
                "empty-command": { "command": "", "dependsOn": ["ok"] },
                "blank-script": { "script": "  ", "shell": "sh" },
                "nul": {
                  "command": "dotnet",
                  "args": ["before\u0000after"],
                  "cwd": "src\u0000",
                  "env": { "A=B": "1", "TOKEN": "x\u0000y" }
                },
                "ok": { "command": "dotnet" }
              }
            }
            """);

        StringWriter output = new();
        StringWriter error = new();

        int exitCode = await RunxCli.RunAsync(["--list"], output, error, new RecordingProcessRunner(), new FakeTaskSelector(), tempDirectory.Path, CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Contains("Task 'empty-command' command must not be empty.", error.ToString());
        Assert.Contains("Task 'blank-script' script must not be empty.", error.ToString());
        Assert.Contains("Task 'nul' args[0] must not contain NUL characters.", error.ToString());
        Assert.Contains("Task 'nul' cwd must not contain NUL characters.", error.ToString());
        Assert.Contains("Task 'nul' environment variable 'TOKEN' must not contain NUL characters.", error.ToString());
        Assert.Contains("Task 'nul' environment variable name 'A=B' cannot contain '='.", error.ToString());
    }

    [Fact]
    public async Task ExplicitNullTaskPropertiesAreRejected()
    {
        using TempDirectory tempDirectory = new();
        WriteGitDirectory(tempDirectory.Path);
        WriteConfig(
            tempDirectory.Path,
            """
            {
              "version": 1,
              "tasks": {
                "null-command": { "command": null, "dependsOn": ["ok"] },
                "null-cwd": { "command": "dotnet", "cwd": null },
                "ok": { "command": "dotnet" }
              }
            }
            """);

        RecordingProcessRunner processRunner = new();
        StringWriter output = new();
        StringWriter error = new();

        int exitCode = await RunxCli.RunAsync(["null-command"], output, error, processRunner, new FakeTaskSelector(), tempDirectory.Path, CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Empty(processRunner.Requests);
        Assert.Contains("Task 'null-command' command must not be null. Omit it instead.", error.ToString());
        Assert.Contains("Task 'null-cwd' cwd must not be null. Omit it instead.", error.ToString());
    }

    [Fact]
    public async Task DuplicateTaskNamesAreRejected()
    {
        using TempDirectory tempDirectory = new();
        WriteGitDirectory(tempDirectory.Path);
        WriteConfig(
            tempDirectory.Path,
            """
            {
              "version": 1,
              "tasks": {
                "build": { "command": "dotnet", "args": ["build"] },
                "build": { "command": "dotnet", "args": ["test"] }
              }
            }
            """);

        StringWriter output = new();
        StringWriter error = new();

        int exitCode = await RunxCli.RunAsync(["--list"], output, error, new RecordingProcessRunner(), new FakeTaskSelector(), tempDirectory.Path, CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Contains("Duplicate properties", error.ToString());
        Assert.Contains("$.tasks.build", error.ToString());
    }

    private static void WriteDependencyConfig(string directory)
    {
        WriteGitDirectory(directory);
        WriteConfig(
            directory,
            """
            {
              "version": 1,
              "tasks": {
                "build": { "command": "dotnet", "args": ["build"] },
                "lint": { "command": "dotnet", "args": ["format", "--verify-no-changes"], "dependsOn": ["build"] },
                "test": { "command": "dotnet", "args": ["test"], "dependsOn": ["build", "lint"] },
                "pack": { "command": "dotnet", "args": ["pack"], "dependsOn": ["build"] },
                "ci": { "dependsOn": ["test", "pack"] }
              }
            }
            """);
    }

    private static void WriteTwoTaskConfig(string directory)
    {
        WriteGitDirectory(directory);
        WriteConfig(
            directory,
            """
            {
              "version": 1,
              "tasks": {
                "build": {
                  "description": "Build the repo.",
                  "command": "dotnet",
                  "args": ["build"]
                },
                "test": {
                  "description": "Run tests.",
                  "command": "dotnet",
                  "args": ["test"]
                }
              }
            }
            """);
    }

    private static void WriteConfig(string directory, string json)
    {
        File.WriteAllText(System.IO.Path.Combine(directory, "runx.json"), json);
    }

    private static void WriteGitDirectory(string directory)
    {
        Directory.CreateDirectory(System.IO.Path.Combine(directory, ".git"));
    }
}
