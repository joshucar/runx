using Spectre.Console;
using Spectre.Console.Rendering;

namespace MysterNybble.Runx;

internal interface ITaskSelector
{
    bool IsInteractive { get; }

    Task<string?> SelectTaskAsync(LoadedRunxConfig config, CancellationToken cancellationToken);
}

internal sealed class SpectreTaskSelector(IAnsiConsole console) : ITaskSelector
{
    private const int MaxPageSize = 15;
    private const int MinPageSize = 3;

    // Title, blank line, paging hint, status bar rule, and up to four preview lines.
    private const int ChromeLineCount = 8;

    private static readonly Style s_mutedStyle = new(Color.Grey);

    // Redrawing the picker in place needs ANSI escape codes; without them it would scroll a new copy per key.
    public bool IsInteractive =>
        console.Profile.Capabilities.Interactive
        && console.Profile.Capabilities.Ansi
        && console.Profile.Out.IsTerminal;

    public async Task<string?> SelectTaskAsync(LoadedRunxConfig config, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(config);

        IReadOnlyDictionary<string, RunxTask> visibleTasks = config.VisibleTasks;
        if (visibleTasks.Count == 0)
        {
            return null;
        }

        TaskPickerState state = new(visibleTasks);
        int pageSize = Math.Clamp(console.Profile.Height - ChromeLineCount, MinPageSize, MaxPageSize);
        int longestNameLength = visibleTasks.Keys.Max(static taskName => taskName.Length);

        IRenderable Render() => RenderPicker(state, config, pageSize, longestNameLength);

        // The picker hides the cursor; put it back if Ctrl+C ends the process mid-prompt.
        void RestoreCursor(object? sender, ConsoleCancelEventArgs e) => console.Cursor.Show();
        Console.CancelKeyPress += RestoreCursor;
        console.Cursor.Hide();
        try
        {
            string? selected = await console
                .Live(Render())
                .AutoClear(true)
                .StartAsync(async context =>
                {
                    context.Refresh();
                    while (true)
                    {
                        ConsoleKeyInfo? key = await console.Input.ReadKeyAsync(intercept: true, cancellationToken);
                        if (key is null)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            continue;
                        }

                        TaskPickerAction action = state.HandleKey(key.Value, pageSize);
                        if (action == TaskPickerAction.Select)
                        {
                            return state.CurrentTaskName;
                        }

                        if (action == TaskPickerAction.Cancel)
                        {
                            return null;
                        }

                        context.UpdateTarget(Render());
                        context.Refresh();
                    }
                });

            if (selected is not null)
            {
                console.MarkupLine($"[grey]>[/] runx {Markup.Escape(selected)}");
            }

            return selected;
        }
        finally
        {
            console.Cursor.Show();
            Console.CancelKeyPress -= RestoreCursor;
        }
    }

    private Rows RenderPicker(TaskPickerState state, LoadedRunxConfig config, int pageSize, int longestNameLength)
    {
        int width = Math.Max(console.Profile.Width - 1, 20);
        List<IRenderable> lines = [];

        string search = state.SearchText.Length == 0
            ? "[grey](type to search)[/]"
            : Markup.Escape(state.SearchText);
        lines.Add(new Markup($"Select a task to run: {search}"));
        lines.Add(new Text(string.Empty));

        if (state.Matches.Count == 0)
        {
            lines.Add(new Text("  No matching tasks.", s_mutedStyle));
        }

        int endIndex = Math.Min(state.TopIndex + pageSize, state.Matches.Count);
        for (int i = state.TopIndex; i < endIndex; i++)
        {
            string taskName = state.Matches[i];
            lines.Add(RenderChoice(
                taskName,
                config.Tasks[taskName].Description,
                i == state.CursorIndex,
                config.Tag(taskName),
                longestNameLength,
                width));
        }

        if (state.Matches.Count > pageSize)
        {
            lines.Add(new Text(
                $"({state.CursorIndex + 1}/{state.Matches.Count}, move up and down to reveal more tasks)",
                s_mutedStyle));
        }

        if (state.CurrentTaskName is string currentTaskName)
        {
            lines.Add(new Rule().RuleStyle(s_mutedStyle));
            lines.AddRange(RenderStatusBar(config.Tasks[currentTaskName], width));
        }

        return new Rows(lines);
    }

    private static Markup RenderChoice(
        string taskName,
        string? description,
        bool isSelected,
        string? tag,
        int longestNameLength,
        int width)
    {
        string label = (isSelected ? "> " : "  ") + taskName;
        if (string.IsNullOrWhiteSpace(description) && tag is null)
        {
            return new Markup(Highlight(Markup.Escape(Truncate(label, width)), isSelected));
        }

        string paddedLabel = Truncate(label.PadRight(longestNameLength + 2), width);
        string tagText = tag is null ? string.Empty : $"  ({tag})";
        string truncatedDescription = Truncate(description ?? string.Empty, width - paddedLabel.Length - 2 - tagText.Length);

        string markup = Highlight(Markup.Escape(paddedLabel), isSelected);
        if (truncatedDescription.Length > 0)
        {
            markup += $"  [grey]{Markup.Escape(truncatedDescription)}[/]";
        }

        if (tag is not null && paddedLabel.Length + (truncatedDescription.Length > 0 ? truncatedDescription.Length + 2 : 0) + tagText.Length <= width)
        {
            markup += $"  [grey italic]({Markup.Escape(tag)})[/]";
        }

        return new Markup(markup);
    }

    private static IEnumerable<IRenderable> RenderStatusBar(RunxTask task, int width)
    {
        if (!string.IsNullOrWhiteSpace(task.Command))
        {
            yield return new Text(Truncate(TaskPreview.FormatCommandLine(task.Command, task.Args), width));
        }
        else if (!string.IsNullOrWhiteSpace(task.Script))
        {
            string shellLabel = $"{task.Shell}: ";
            IReadOnlyList<string> scriptLines = TaskPreview.GetScriptLines(task.Script);
            for (int i = 0; i < scriptLines.Count; i++)
            {
                string prefix = i == 0 ? shellLabel : new string(' ', shellLabel.Length);
                string line = Truncate(scriptLines[i], width - prefix.Length);
                yield return new Markup($"[grey]{Markup.Escape(prefix)}[/]{Markup.Escape(line)}");
            }
        }

        string? details = TaskPreview.FormatDetails(task);
        if (details is not null)
        {
            yield return new Text(Truncate(details, width), s_mutedStyle);
        }
    }

    private static string Highlight(string markup, bool isSelected)
    {
        return isSelected ? $"[blue]{markup}[/]" : markup;
    }

    private static string Truncate(string value, int maxLength)
    {
        if (maxLength <= 0)
        {
            return string.Empty;
        }

        if (value.Length <= maxLength)
        {
            return value;
        }

        return maxLength <= 3 ? value[..maxLength] : string.Concat(value.AsSpan(0, maxLength - 3), "...");
    }
}
