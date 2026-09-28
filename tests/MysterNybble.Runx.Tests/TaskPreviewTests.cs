namespace MysterNybble.Runx.Tests;

public sealed class TaskPreviewTests
{
    [Fact]
    public void FormatCommandLineQuotesArgumentsThatNeedIt()
    {
        string commandLine = TaskPreview.FormatCommandLine("dotnet", ["test", "--filter", "Name~a b", "", "say \"hi\""]);

        Assert.Equal("dotnet test --filter \"Name~a b\" \"\" \"say \\\"hi\\\"\"", commandLine);
    }

    [Fact]
    public void GetScriptLinesSkipsBlankLinesAndSummarizesLongScripts()
    {
        Assert.Equal(["one", "two"], TaskPreview.GetScriptLines("one\r\n\r\ntwo\n"));
        Assert.Equal(["one", "two", "three"], TaskPreview.GetScriptLines("one\ntwo\nthree"));
        Assert.Equal(["one", "two", "... 3 more lines"], TaskPreview.GetScriptLines("one\ntwo\nthree\nfour\nfive"));
    }

    [Fact]
    public void FormatDetailsShowsCwdAndEnvNamesOnly()
    {
        RunxTask task = new()
        {
            Command = "dotnet",
            Cwd = "src/App",
            Env = new Dictionary<string, string?> { ["TOKEN"] = "secret", ["A"] = "1" }
        };

        Assert.Equal("cwd: src/App   env: A, TOKEN", TaskPreview.FormatDetails(task));
        Assert.Equal(
            "depends on: build, lint",
            TaskPreview.FormatDetails(new RunxTask { DependsOn = ["build", "lint"] }));
        Assert.Null(TaskPreview.FormatDetails(new RunxTask { Command = "dotnet" }));
    }
}
