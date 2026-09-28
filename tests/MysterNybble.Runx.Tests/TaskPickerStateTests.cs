namespace MysterNybble.Runx.Tests;

public sealed class TaskPickerStateTests
{
    [Fact]
    public void ArrowKeysWrapAround()
    {
        TaskPickerState state = new(CreateTasks("a", "b", "c"));

        state.HandleKey(Key(ConsoleKey.UpArrow), pageSize: 10);
        Assert.Equal("c", state.CurrentTaskName);

        state.HandleKey(Key(ConsoleKey.DownArrow), pageSize: 10);
        Assert.Equal("a", state.CurrentTaskName);
    }

    [Fact]
    public void PagingKeepsCursorVisible()
    {
        TaskPickerState state = new(CreateTasks("a", "b", "c", "d", "e", "f", "g"));

        state.HandleKey(Key(ConsoleKey.PageDown), pageSize: 3);
        Assert.Equal("d", state.CurrentTaskName);
        Assert.Equal(1, state.TopIndex);

        state.HandleKey(Key(ConsoleKey.End), pageSize: 3);
        Assert.Equal("g", state.CurrentTaskName);
        Assert.Equal(4, state.TopIndex);

        state.HandleKey(Key(ConsoleKey.Home), pageSize: 3);
        Assert.Equal("a", state.CurrentTaskName);
        Assert.Equal(0, state.TopIndex);
    }

    [Fact]
    public void SearchFiltersByNameOrDescriptionAndKeepsCurrentTask()
    {
        Dictionary<string, RunxTask> tasks = CreateTasks("build", "pack", "test");
        tasks["pack"].Description = "Create the NuGet package.";
        TaskPickerState state = new(tasks);
        state.HandleKey(Key(ConsoleKey.DownArrow), pageSize: 10);

        state.HandleKey(Key('n'), pageSize: 10);
        state.HandleKey(Key('u'), pageSize: 10);

        Assert.Equal("nu", state.SearchText);
        Assert.Equal(["build", "pack"], state.Matches);
        Assert.Equal("pack", state.CurrentTaskName);

        state.HandleKey(Key(ConsoleKey.Backspace), pageSize: 10);
        Assert.Equal("n", state.SearchText);
    }

    [Fact]
    public void EnterIsIgnoredWithoutMatches()
    {
        TaskPickerState state = new(CreateTasks("build"));

        state.HandleKey(Key('x'), pageSize: 10);

        Assert.Empty(state.Matches);
        Assert.Null(state.CurrentTaskName);
        Assert.Equal(TaskPickerAction.Continue, state.HandleKey(Key(ConsoleKey.Enter), pageSize: 10));
        Assert.Equal(TaskPickerAction.Continue, state.HandleKey(Key(ConsoleKey.Escape), pageSize: 10));
        Assert.Equal(TaskPickerAction.Select, state.HandleKey(Key(ConsoleKey.Enter), pageSize: 10));
    }

    private static Dictionary<string, RunxTask> CreateTasks(params string[] names)
    {
        return names.ToDictionary(
            static name => name,
            static name => new RunxTask { Command = name, Description = name == "build" ? "Build a NuGet-ready output." : null },
            StringComparer.Ordinal);
    }

    private static ConsoleKeyInfo Key(ConsoleKey key)
    {
        return new ConsoleKeyInfo('\0', key, shift: false, alt: false, control: false);
    }

    private static ConsoleKeyInfo Key(char character)
    {
        return new ConsoleKeyInfo(character, ConsoleKey.NoName, shift: false, alt: false, control: false);
    }
}
