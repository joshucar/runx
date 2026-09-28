namespace MysterNybble.Runx.Tests;

public sealed class WindowsCommandLineTests
{
    [Theory]
    [InlineData("install", "install")]
    [InlineData("--save-dev", "--save-dev")]
    [InlineData("a&b", "\"a&b\"")]
    [InlineData("sp ace", "\"sp ace\"")]
    [InlineData("", "\"\"")]
    [InlineData("q\"uote", "\"q\"\"uote\"")]
    [InlineData("a\\\"b", "\"a\\\\\"\"b\"")]
    [InlineData("tail\\", "\"tail\\\\\"")]
    [InlineData("%PATH%", "\"%%cd:~,%PATH%%cd:~,%\"")]
    [InlineData("C:\\dir\\file", "C:\\dir\\file")]
    public void BatchFileArgumentsAreQuotedAndEscaped(string argument, string expected)
    {
        string arguments = WindowsCommandLine.CreateBatchFileArguments(@"C:\tools\npm.cmd", [argument]);

        Assert.Equal($"/d /e:ON /v:OFF /s /c \"\"C:\\tools\\npm.cmd\" {expected}\"", arguments);
    }

    [Fact]
    public void BatchFileArgumentsRejectLineBreaks()
    {
        Assert.False(WindowsCommandLine.CanPassToBatchFile("a\nb"));
        Assert.Throws<ArgumentException>(() => WindowsCommandLine.CreateBatchFileArguments(@"C:\tools\npm.cmd", ["a\r\nb"]));
    }

    [Fact]
    public async Task BatchFileArgumentsCannotInjectCommands()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Batch files only run on Windows.");

        using TempDirectory tempDirectory = new();
        string batchFile = System.IO.Path.Combine(tempDirectory.Path, "forward args.cmd");
        File.WriteAllText(batchFile, "@echo off\r\n> \"%~dp0args.txt\" echo %*\r\n");

        ProcessRequest request = CreateRequest(
            tempDirectory.Path,
            batchFile,
            ["a&echo injected>injected.txt", "q\"&echo injected>injected.txt", "%PATH%", "car^et"]);

        int exitCode = (await new DefaultProcessRunner().RunAsync(request, new StringWriter(), CancellationToken.None)).ExitCode;

        Assert.Equal(0, exitCode);
        Assert.False(File.Exists(System.IO.Path.Combine(tempDirectory.Path, "injected.txt")));
        Assert.Equal(
            "\"a&echo injected>injected.txt\" \"q\"\"&echo injected>injected.txt\" \"%PATH%\" \"car^et\"",
            File.ReadAllText(System.IO.Path.Combine(tempDirectory.Path, "args.txt")).Trim());
    }

    [Fact]
    public async Task CmdScriptsRunVerbatim()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "cmd.exe only runs on Windows.");

        using TempDirectory tempDirectory = new();
        RunxTask task = new() { Script = "echo \"hello world\"> out.txt", Shell = "cmd" };
        ProcessRequest? request = BuildRequest(tempDirectory.Path, "hello", task);

        int exitCode = (await new DefaultProcessRunner().RunAsync(request!, new StringWriter(), CancellationToken.None)).ExitCode;

        Assert.Equal(0, exitCode);
        Assert.Equal("\"hello world\"", File.ReadAllText(System.IO.Path.Combine(tempDirectory.Path, "out.txt")).Trim());
    }

    [Fact]
    public async Task MultiLineCmdScriptsRunEveryLine()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "cmd.exe only runs on Windows.");

        using TempDirectory tempDirectory = new();
        RunxTask task = new() { Script = "echo one> out.txt\r\necho two>> out.txt\nexit /b 5", Shell = "cmd" };
        ProcessRequest? request = BuildRequest(tempDirectory.Path, "multi", task);

        int exitCode = (await new DefaultProcessRunner().RunAsync(request!, new StringWriter(), CancellationToken.None)).ExitCode;

        Assert.Equal(5, exitCode);
        Assert.Equal(["one", "two"], File.ReadAllLines(System.IO.Path.Combine(tempDirectory.Path, "out.txt")));
    }

    [Fact]
    public async Task BatchFilesRunFromDirectoriesWithPercentSigns()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Batch files only run on Windows.");

        using TempDirectory tempDirectory = new();
        string directory = System.IO.Path.Combine(tempDirectory.Path, "%RUNX_TEST_DIR%");
        Directory.CreateDirectory(directory);
        string batchFile = System.IO.Path.Combine(directory, "hello.cmd");
        File.WriteAllText(batchFile, "@echo off\r\n> \"%~dp0out.txt\" echo hello\r\n");

        ProcessRequest request = new(
            "test",
            System.IO.Path.Combine(tempDirectory.Path, "runx.json"),
            "process",
            batchFile,
            [],
            tempDirectory.Path,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["RUNX_TEST_DIR"] = "elsewhere" });

        int exitCode = (await new DefaultProcessRunner().RunAsync(request, new StringWriter(), CancellationToken.None)).ExitCode;

        Assert.Equal(0, exitCode);
        Assert.Equal("hello", File.ReadAllText(System.IO.Path.Combine(directory, "out.txt")).Trim());
    }

    private static ProcessRequest? BuildRequest(string directory, string taskName, RunxTask task)
    {
        LoadedRunxConfig config = new(
            System.IO.Path.Combine(directory, "runx.json"),
            directory,
            new Dictionary<string, RunxTask>(StringComparer.Ordinal) { [taskName] = task });

        return ProcessRequestFactory.Build(config, taskName, task, []).Request;
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
            new Dictionary<string, string>(StringComparer.Ordinal));
    }
}
