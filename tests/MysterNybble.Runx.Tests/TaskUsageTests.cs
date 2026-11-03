using System.Text.Json;

namespace MysterNybble.Runx.Tests;

public sealed class TaskUsageTests
{
    [Fact]
    public async Task UsageIsFreeFormAndReportedWithoutExecutingOrChangingTheTask()
    {
        using TempDirectory temp = new();
        File.WriteAllText(Path.Combine(temp.Path, "runx.json"), """{"version":1,"tasks":{"help":{"command":"missing-command","usage":"runx help -- <anything>\nAn example: --not-a-runx-option"}}}""");
        RecordingProcessRunner runner = new();
        StringWriter output = new();
        Assert.Equal(0, await GitHooksTests.RunAsync(temp.Path, ["--info", "help"], runner, output));
        Assert.Contains("Usage:        runx help -- <anything>", output.ToString());
        Assert.Contains("              An example: --not-a-runx-option", output.ToString());
        output.GetStringBuilder().Clear();
        Assert.Equal(0, await GitHooksTests.RunAsync(temp.Path, ["--info", "help", "--json"], runner, output));
        using JsonDocument json = JsonDocument.Parse(output.ToString());
        Assert.Equal("runx help -- <anything>\nAn example: --not-a-runx-option", json.RootElement.GetProperty("usage").GetString());
        Assert.Empty(runner.Requests);
        Assert.Equal(0, await GitHooksTests.RunAsync(temp.Path, ["help", "--", "undocumented"], runner));
        Assert.Equal(["undocumented"], Assert.Single(runner.Requests).Arguments);
    }

    [Fact]
    public void ScriptUsageSupportsMultilineTextAndWarnsOnWrongTypes()
    {
        using TempDirectory temp = new();
        File.WriteAllText(Path.Combine(temp.Path, "hello.cs"), "// runx usage=\"runx hello -- <name>\\nNames can contain spaces.\"\n");
        File.WriteAllText(Path.Combine(temp.Path, "bad.cs"), "// runx usage=true\n");
        List<string> warnings = [];
        IReadOnlyList<ScriptTask> tasks = ScriptTasks.Discover(temp.Path, [temp.Path], OperatingSystem.IsWindows(), warnings);
        Assert.Equal("runx hello -- <name>\nNames can contain spaces.", tasks.Single(static task => task.Name == "hello").Task.Usage);
        Assert.Contains(warnings, static message => message.Contains("'usage' must be a quoted string", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("true")]
    public async Task JsonUsageRejectsNonStringValues(string value)
    {
        using TempDirectory temp = new();
        string path = Path.Combine(temp.Path, "runx.json");
        File.WriteAllText(path, "{\"version\":1,\"tasks\":{\"check\":{\"command\":\"dotnet\",\"usage\":" + value + "}}}");
        ConfigLoadResult result = await RunxConfigLoader.LoadAsync(path, CancellationToken.None);
        Assert.NotEmpty(result.Errors);
    }
}
