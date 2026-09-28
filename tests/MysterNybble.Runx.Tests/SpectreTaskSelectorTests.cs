using Spectre.Console.Testing;

namespace MysterNybble.Runx.Tests;

public sealed class SpectreTaskSelectorTests
{
    [Fact]
    public async Task ArrowKeysAndEnterSelectTask()
    {
        using TestConsole console = new TestConsole().Interactive();
        console.Input.PushKey(ConsoleKey.DownArrow);
        console.Input.PushKey(ConsoleKey.DownArrow);
        console.Input.PushKey(ConsoleKey.Enter);

        string? selected = await new SpectreTaskSelector(console).SelectTaskAsync(CreateConfig(), CancellationToken.None);

        Assert.Equal("test", selected);
        Assert.Contains("> runx test", console.Output);
    }

    [Fact]
    public async Task TypingSearchesTasks()
    {
        using TestConsole console = new TestConsole().Interactive();
        console.Input.PushTextWithEnter("pub");

        string? selected = await new SpectreTaskSelector(console).SelectTaskAsync(CreateConfig(), CancellationToken.None);

        Assert.Equal("publish:nuget", selected);
    }

    [Fact]
    public async Task EscapeCancels()
    {
        using TestConsole console = new TestConsole().Interactive();
        console.Input.PushKey(ConsoleKey.Escape);

        string? selected = await new SpectreTaskSelector(console).SelectTaskAsync(CreateConfig(), CancellationToken.None);

        Assert.Null(selected);
        Assert.DoesNotContain("> runx", console.Output);
    }

    [Fact]
    public async Task EscapeClearsSearchBeforeCancelling()
    {
        using TestConsole console = new TestConsole().Interactive();
        console.Input.PushText("zzz");
        console.Input.PushKey(ConsoleKey.Enter);
        console.Input.PushKey(ConsoleKey.Escape);
        console.Input.PushKey(ConsoleKey.Enter);

        string? selected = await new SpectreTaskSelector(console).SelectTaskAsync(CreateConfig(), CancellationToken.None);

        Assert.Equal("build", selected);
        Assert.Contains("No matching tasks.", console.Output);
    }

    [Fact]
    public async Task ShowsDescriptionsWithoutInterpretingMarkup()
    {
        using TestConsole console = new TestConsole().Interactive();
        console.Input.PushKey(ConsoleKey.Enter);

        string? selected = await new SpectreTaskSelector(console).SelectTaskAsync(CreateConfig(), CancellationToken.None);

        Assert.Equal("build", selected);
        Assert.Contains("Build [Release] output.", console.Output);
    }

    [Fact]
    public async Task StatusBarShowsHighlightedTaskCommand()
    {
        using TestConsole console = new TestConsole().Interactive();
        console.Input.PushKey(ConsoleKey.DownArrow);
        console.Input.PushKey(ConsoleKey.Escape);

        await new SpectreTaskSelector(console).SelectTaskAsync(CreateConfig(), CancellationToken.None);

        Assert.Contains("dotnet build --configuration Release", console.Output);
        Assert.Contains("pwsh: dotnet nuget push *.nupkg", console.Output);
        Assert.Contains("       --source nuget.org", console.Output);
        Assert.Contains("cwd: artifacts   env: NUGET_API_KEY", console.Output);
        Assert.DoesNotContain("dotnet test", console.Output);
    }

    [Fact]
    public async Task PersonalTasksAreTagged()
    {
        using TestConsole console = new TestConsole().Interactive();
        console.Input.PushKey(ConsoleKey.Escape);
        Dictionary<string, RunxTask> tasks = new(StringComparer.Ordinal)
        {
            ["build"] = new RunxTask { Description = "Build it.", Command = "dotnet" },
            ["deploy"] = new RunxTask { Description = "Deploy to my box.", Command = "dotnet" }
        };
        Dictionary<string, TaskSource> sources = new(StringComparer.Ordinal)
        {
            ["build"] = new TaskSource("runx.json", "."),
            ["deploy"] = new TaskSource("personal.json", ".", PersonalTag: "personal")
        };

        await new SpectreTaskSelector(console).SelectTaskAsync(new LoadedRunxConfig(tasks, sources, "runx.json"), CancellationToken.None);

        string[] lines = console.Output.Split('\n');
        Assert.Contains(lines, static line => line.Contains("deploy", StringComparison.Ordinal) && line.Contains("(personal)", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, static line => line.Contains("build", StringComparison.Ordinal) && line.Contains("(personal)", StringComparison.Ordinal));
    }

    private static LoadedRunxConfig CreateConfig()
    {
        return new LoadedRunxConfig(
            "runx.json",
            ".",
            new Dictionary<string, RunxTask>(StringComparer.Ordinal)
            {
                ["test"] = new RunxTask { Description = "Run tests.", Command = "dotnet", Args = ["test"] },
                ["build"] = new RunxTask
                {
                    Description = "Build [Release] output.",
                    Command = "dotnet",
                    Args = ["build", "--configuration", "Release"]
                },
                ["publish:nuget"] = new RunxTask
                {
                    Script = "dotnet nuget push *.nupkg\n  --source nuget.org",
                    Shell = "pwsh",
                    Cwd = "artifacts",
                    Env = new Dictionary<string, string?> { ["NUGET_API_KEY"] = "secret" }
                }
            });
    }
}
