namespace MysterNybble.Runx.Tests;

public sealed class TaskRunSummaryTests
{
    [Theory]
    [InlineData(null, null, 0, "OK", 3)]
    [InlineData("second", null, 7, "FAILED (exit code 7)", 2)]
    [InlineData(null, "second", 130, "INTERRUPTED", 2)]
    [InlineData(null, "third", 0, "INTERRUPTED", 3)]
    public async Task SummaryReportsCompletedAndSkippedSteps(string? failure, string? interrupted, int expectedCode, string expectedStatus, int expectedRuns)
    {
        using TempDirectory directory = new();
        WriteConfig(directory.Path);
        RecordingProcessRunner runner = new() { InterruptedTaskName = interrupted };
        if (failure is not null)
        {
            runner.TaskExitCodes.Add(failure, expectedCode);
        }

        StringWriter output = new();
        StringWriter error = new();

        int code = await RunAsync(directory.Path, ["all"], runner, output, error);

        Assert.Equal(expectedCode, code);
        Assert.Equal(expectedRuns, runner.Requests.Count);
        Assert.Empty(output.ToString());
        string report = error.ToString();
        Assert.Contains("> runx first (1/3)", report);
        Assert.Matches(@"1/3 first  \S+  OK", report);
        Assert.Contains("Summary:", report);
        Assert.Contains("Total:", report);
        string step = failure ?? interrupted ?? "third";
        Assert.Matches($@"{step}  \S+  {System.Text.RegularExpressions.Regex.Escape(expectedStatus)}", report);
        if (expectedRuns < 3)
        {
            Assert.Contains("3/3 third  -  SKIPPED", report);
            Assert.DoesNotContain("> runx third", report);
        }
    }

    [Fact]
    public async Task StandaloneTasksKeepTheirOutputUnadorned()
    {
        using TempDirectory directory = new();
        WriteConfig(directory.Path);
        StringWriter output = new();
        StringWriter error = new();
        RecordingProcessRunner runner = new() { ExitCode = 9 };

        int code = await RunAsync(directory.Path, ["first"], runner, output, error);

        Assert.Equal(9, code);
        Assert.Empty(output.ToString());
        Assert.Empty(error.ToString());
    }

    [Fact]
    public async Task CancellationSummarizesTheInterruptedStepAndDoesNotStartTheNext()
    {
        using TempDirectory directory = new();
        WriteConfig(directory.Path);
        using CancellationTokenSource cancellation = new();
        CancellingProcessRunner runner = new(cancellation);
        StringWriter error = new();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RunxCli.RunAsync(
            ["all"], new StringWriter(), error, runner, new FakeTaskSelector(), directory.Path, cancellation.Token));

        Assert.Equal(1, runner.Runs);
        Assert.Matches(@"1/3 first  \S+  INTERRUPTED", error.ToString());
        Assert.Contains("2/3 second  -  SKIPPED", error.ToString());
        Assert.Contains("3/3 third  -  SKIPPED", error.ToString());
    }

    [Fact]
    public void SummaryFormatsDurationsWithoutWrappingOrTerminalControlCodes()
    {
        ProcessRequest[] requests = [
            new("build", "runx.json", "process", "tool", [], ".", new Dictionary<string, string>()),
            new("deploy", "runx.json", "process", "tool", [], ".", new Dictionary<string, string>())
        ];
        TaskRunSummary summary = new(requests);
        summary.Add(new ProcessResult(0), TimeSpan.FromSeconds(252));
        summary.Add(new ProcessResult(3), TimeSpan.FromSeconds(3672));
        StringWriter error = new();

        summary.Write(error);

        Assert.Contains("1/2 build  4m12s  OK", error.ToString());
        Assert.Contains("2/2 deploy  1h01m12s  FAILED (exit code 3)", error.ToString());
        Assert.DoesNotContain('\u001b', error.ToString());
    }

    private sealed class CancellingProcessRunner(CancellationTokenSource cancellation) : IProcessRunner
    {
        public int Runs { get; private set; }

        public Task<ProcessResult> RunAsync(ProcessRequest request, TextWriter error, CancellationToken cancellationToken)
        {
            Runs++;
            cancellation.Cancel();
            return Task.FromCanceled<ProcessResult>(cancellationToken);
        }
    }

    private static void WriteConfig(string root) => File.WriteAllText(System.IO.Path.Combine(root, "runx.json"), """
        { "version": 1, "tasks": {
          "first": { "command": "tool" },
          "second": { "command": "tool" },
          "third": { "command": "tool" },
          "all": { "dependsOn": ["first", "second", "third"] }
        } }
        """);

    private static Task<int> RunAsync(string root, string[] args, IProcessRunner runner, StringWriter output, StringWriter error) =>
        RunxCli.RunAsync(args, output, error, runner, new FakeTaskSelector(), root, CancellationToken.None);
}
