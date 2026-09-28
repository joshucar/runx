namespace MysterNybble.Runx;

internal enum TaskPickerAction
{
    Continue,
    Select,
    Cancel
}

internal sealed class TaskPickerState
{
    private readonly IReadOnlyDictionary<string, RunxTask> _tasks;
    private readonly string[] _allTaskNames;

    public TaskPickerState(IReadOnlyDictionary<string, RunxTask> tasks)
    {
        ArgumentNullException.ThrowIfNull(tasks);

        _tasks = tasks;
        _allTaskNames = tasks.Keys.Order(StringComparer.Ordinal).ToArray();
        Matches = _allTaskNames;
    }

    public string SearchText { get; private set; } = string.Empty;

    public IReadOnlyList<string> Matches { get; private set; }

    public int CursorIndex { get; private set; }

    public int TopIndex { get; private set; }

    public string? CurrentTaskName => Matches.Count == 0 ? null : Matches[CursorIndex];

    public TaskPickerAction HandleKey(ConsoleKeyInfo key, int pageSize)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);

        switch (key.Key)
        {
            case ConsoleKey.Enter:
                return CurrentTaskName is null ? TaskPickerAction.Continue : TaskPickerAction.Select;
            case ConsoleKey.Escape when SearchText.Length > 0:
                SetSearchText(string.Empty);
                break;
            case ConsoleKey.Escape:
                return TaskPickerAction.Cancel;
            case ConsoleKey.UpArrow:
                MoveWrapped(-1);
                break;
            case ConsoleKey.DownArrow:
                MoveWrapped(1);
                break;
            case ConsoleKey.PageUp:
                MoveClamped(-pageSize);
                break;
            case ConsoleKey.PageDown:
                MoveClamped(pageSize);
                break;
            case ConsoleKey.Home:
                MoveClamped(-Matches.Count);
                break;
            case ConsoleKey.End:
                MoveClamped(Matches.Count);
                break;
            case ConsoleKey.Backspace when SearchText.Length > 0:
                SetSearchText(SearchText[..^1]);
                break;
            default:
                if (!char.IsControl(key.KeyChar))
                {
                    SetSearchText(SearchText + key.KeyChar);
                }

                break;
        }

        KeepCursorVisible(pageSize);
        return TaskPickerAction.Continue;
    }

    private void SetSearchText(string searchText)
    {
        string? previousTaskName = CurrentTaskName;
        SearchText = searchText;
        Matches = searchText.Length == 0
            ? _allTaskNames
            : _allTaskNames.Where(IsMatch).ToArray();

        int previousIndex = previousTaskName is null ? -1 : IndexOf(previousTaskName);
        CursorIndex = Math.Max(previousIndex, 0);
        TopIndex = 0;
    }

    private bool IsMatch(string taskName)
    {
        return taskName.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
            || (_tasks[taskName].Description?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private int IndexOf(string taskName)
    {
        for (int i = 0; i < Matches.Count; i++)
        {
            if (StringComparer.Ordinal.Equals(Matches[i], taskName))
            {
                return i;
            }
        }

        return -1;
    }

    private void MoveWrapped(int delta)
    {
        if (Matches.Count > 0)
        {
            CursorIndex = (CursorIndex + delta + Matches.Count) % Matches.Count;
        }
    }

    private void MoveClamped(int delta)
    {
        if (Matches.Count > 0)
        {
            CursorIndex = Math.Clamp(CursorIndex + delta, 0, Matches.Count - 1);
        }
    }

    private void KeepCursorVisible(int pageSize)
    {
        if (CursorIndex < TopIndex)
        {
            TopIndex = CursorIndex;
        }
        else if (CursorIndex >= TopIndex + pageSize)
        {
            TopIndex = CursorIndex - pageSize + 1;
        }
    }
}
