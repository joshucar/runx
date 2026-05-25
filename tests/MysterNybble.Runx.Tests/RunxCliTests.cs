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

        int exitCode = await RunxCli.RunAsync([], output, error, processRunner, tempDirectory.Path, CancellationToken.None);

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
            tempDirectory.Path,
            CancellationToken.None);

        string configPath = System.IO.Path.Combine(tempDirectory.Path, "runx.json");
        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(configPath));
        Assert.Contains("\"$schema\": \"https://schemas.mysternybble.com/runx/v1/runx.schema.json\"", File.ReadAllText(configPath));
        Assert.Contains("\"dotnet-version\"", File.ReadAllText(configPath));
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
              "$schema": "https://schemas.mysternybble.com/runx/v1/runx.schema.json",
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
            tempDirectory.Path,
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Contains("build", output.ToString());
        Assert.Contains("Build the repo.", output.ToString());
        Assert.Contains("test", output.ToString());
        Assert.Contains("Run tests.", output.ToString());
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
              "$schema": "https://schemas.mysternybble.com/runx/v1/runx.schema.json",
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
              "$schema": "https://schemas.mysternybble.com/runx/v1/runx.schema.json",
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
              "$schema": "https://schemas.mysternybble.com/runx/v1/runx.schema.json",
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
              "$schema": "https://schemas.mysternybble.com/runx/v1/runx.schema.json",
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
            childPath,
            CancellationToken.None);

        StringWriter secondOutput = new();
        StringWriter secondError = new();
        int secondExitCode = await RunxCli.RunAsync(
            ["--allow-parent-search", "--list"],
            secondOutput,
            secondError,
            new RecordingProcessRunner(),
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
              "$schema": "https://schemas.mysternybble.com/runx/v1/runx.schema.json",
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
              "$schema": "https://schemas.mysternybble.com/runx/v1/runx.schema.json",
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
            tempDirectory.Path,
            CancellationToken.None);

        ProcessRequest request = Assert.Single(processRunner.Requests);
        Assert.Equal(0, exitCode);
        Assert.Equal("shell:pwsh", request.Mode);
        Assert.Equal("pwsh", request.FileName);
        Assert.Equal(["-NoLogo", "-NoProfile", "-NonInteractive", "-Command", "Write-Output hello"], request.Arguments);
    }

    private static void WriteConfig(string directory, string json)
    {
        File.WriteAllText(System.IO.Path.Combine(directory, "runx.json"), json);
    }

    private static void WriteGitDirectory(string directory)
    {
        Directory.CreateDirectory(System.IO.Path.Combine(directory, ".git"));
    }

    private sealed class RecordingProcessRunner : IProcessRunner
    {
        public List<ProcessRequest> Requests { get; } = [];

        public int ExitCode { get; set; }

        public Task<int> RunAsync(
            ProcessRequest request,
            TextWriter output,
            TextWriter error,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(ExitCode);
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "runx-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
