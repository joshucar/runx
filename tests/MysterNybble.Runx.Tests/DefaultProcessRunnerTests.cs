namespace MysterNybble.Runx.Tests;

public sealed class DefaultProcessRunnerTests
{
    [Fact]
    public async Task RunsProcessInWorkingDirectoryWithEnvironmentAndReturnsExitCode()
    {
        using TempDirectory tempDirectory = new();
        ProcessRequest request = OperatingSystem.IsWindows()
            ? CreateRequest(tempDirectory.Path, "cmd.exe", ["/d", "/s", "/c", "echo %RUNX_TEST_VALUE%> out.txt& exit 3"])
            : CreateRequest(tempDirectory.Path, "sh", ["-c", "printf %s \"$RUNX_TEST_VALUE\" > out.txt; exit 3"]);

        StringWriter error = new();

        int exitCode = (await new DefaultProcessRunner().RunAsync(request, error, CancellationToken.None)).ExitCode;

        Assert.Equal(3, exitCode);
        Assert.Equal("from-runx", File.ReadAllText(System.IO.Path.Combine(tempDirectory.Path, "out.txt")).Trim());
        Assert.Empty(error.ToString());
    }

    [Fact]
    public async Task MissingCommandReportsStartFailure()
    {
        using TempDirectory tempDirectory = new();
        ProcessRequest request = CreateRequest(tempDirectory.Path, "runx-command-that-does-not-exist", []);
        StringWriter error = new();

        int exitCode = (await new DefaultProcessRunner().RunAsync(request, error, CancellationToken.None)).ExitCode;

        Assert.Equal(1, exitCode);
        Assert.Contains("Failed to start 'runx-command-that-does-not-exist'", error.ToString());
    }

    [Fact]
    public async Task CommandsAreOnlyLookedUpOnTheTaskPath()
    {
        using TempDirectory tempDirectory = new();
        string emptyPath = System.IO.Path.Combine(tempDirectory.Path, "empty");
        Directory.CreateDirectory(emptyPath);
        ProcessRequest request = CreateRequest(tempDirectory.Path, "dotnet", ["--version"]) with
        {
            Environment = new Dictionary<string, string>(StringComparer.Ordinal) { ["PATH"] = emptyPath }
        };
        StringWriter error = new();

        int exitCode = (await new DefaultProcessRunner().RunAsync(request, error, CancellationToken.None)).ExitCode;

        Assert.Equal(1, exitCode);
        Assert.Contains("Failed to start 'dotnet': it was not found on PATH.", error.ToString());
    }

    private static ProcessRequest CreateRequest(string workingDirectory, string fileName, IReadOnlyList<string> arguments)
    {
        return new ProcessRequest(
            "test",
            System.IO.Path.Combine(workingDirectory, "runx.json"),
            "process",
            fileName,
            arguments,
            workingDirectory,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["RUNX_TEST_VALUE"] = "from-runx"
            });
    }
}
