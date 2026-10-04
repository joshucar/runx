using System.CommandLine;
using System.CommandLine.Parsing;

namespace MysterNybble.Runx;

internal static class RunxCommandLine
{
    public static CliParseResult Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        int delimiterIndex = FindDelimiter(args);
        string[] parserArgs = delimiterIndex >= 0
            ? args.Take(delimiterIndex).ToArray()
            : args.ToArray();

        string[] taskArguments = delimiterIndex >= 0
            ? args.Skip(delimiterIndex + 1).ToArray()
            : [];

        if (HasFlagBeforeDelimiter(args, "--help") || HasFlagBeforeDelimiter(args, "-h"))
        {
            return CliParseResult.Help();
        }

        if (HasFlagBeforeDelimiter(args, "--version"))
        {
            return CliParseResult.Version();
        }

        CommandLineModel commandLine = new();
        ParseResult parseResult = commandLine.RootCommand.Parse(parserArgs);
        if (parseResult.Errors.Count > 0)
        {
            return CliParseResult.Failed(parseResult.Errors.Select(static parseError => parseError.Message).ToArray());
        }

        string? taskName = parseResult.GetValue(commandLine.Task);
        bool list = parseResult.GetValue(commandLine.List);
        bool init = parseResult.GetValue(commandLine.Init);
        bool where = parseResult.GetValue(commandLine.Where);
        bool guide = parseResult.GetValue(commandLine.Guide);
        bool installSkill = parseResult.GetValue(commandLine.InstallSkill);
        bool check = parseResult.GetValue(commandLine.Check);
        string? newTaskName = parseResult.GetValue(commandLine.New);
        string? infoTaskName = parseResult.GetValue(commandLine.Info);

        List<string> errors = [];
        bool[] actions = [list, init, where, guide, installSkill, check, newTaskName is not null, infoTaskName is not null];
        int actionCount = actions.Count(static action => action);
        if (actionCount > 1)
        {
            errors.Add("Only one tool action can be used at a time.");
        }

        if (taskName is not null && actionCount > 0)
        {
            errors.Add("Tool action flags cannot be combined with a task name.");
        }

        if (delimiterIndex >= 0 && taskName is null)
        {
            errors.Add("Task arguments after '--' require a task name.");
        }

        if (errors.Count > 0)
        {
            return CliParseResult.Failed(errors);
        }

        return new CliParseResult(
            ShowHelp: false,
            ShowVersion: false,
            List: list,
            Init: init,
            Force: parseResult.GetValue(commandLine.Force),
            FilePath: parseResult.GetValue(commandLine.File),
            AllowParentSearch: parseResult.GetValue(commandLine.AllowParentSearch),
            DryRun: parseResult.GetValue(commandLine.DryRun),
            TaskName: taskName,
            TaskArguments: taskArguments,
            Errors: [])
        {
            Personal = parseResult.GetValue(commandLine.Personal),
            Where = where,
            Scope = parseResult.GetValue(commandLine.Scope),
            Guide = guide,
            InstallSkill = installSkill,
            Check = check,
            Json = parseResult.GetValue(commandLine.Json),
            InfoTaskName = infoTaskName,
            NewTaskName = newTaskName,
            ScriptType = parseResult.GetValue(commandLine.Type),
            Description = parseResult.GetValue(commandLine.Description),
            Depends = (parseResult.GetValue(commandLine.Depends) ?? [])
                .SelectMany(static value => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .ToArray(),
            Hide = parseResult.GetValue(commandLine.Hide)
        };
    }

    private static int FindDelimiter(IReadOnlyList<string> args)
    {
        for (int i = 0; i < args.Count; i++)
        {
            if (args[i] == "--")
            {
                return i;
            }
        }

        return -1;
    }

    private static bool HasFlagBeforeDelimiter(IReadOnlyList<string> args, string flag)
    {
        for (int i = 0; i < args.Count; i++)
        {
            if (args[i] == "--")
            {
                return false;
            }

            if (StringComparer.Ordinal.Equals(args[i], flag))
            {
                return true;
            }
        }

        return false;
    }
}

internal sealed class CommandLineModel
{
    public CommandLineModel()
    {
        RootCommand = new RootCommand("repo command runner for .NET projects");
        foreach (Option option in (Option[])
            [List, Init, Force, File, AllowParentSearch, DryRun, Personal, Where, Scope, Guide, InstallSkill, Check, Json, Info, New, Type, Description, Depends, Hide])
        {
            RootCommand.Options.Add(option);
        }

        RootCommand.Arguments.Add(Task);
    }

    public RootCommand RootCommand { get; }

    public Option<bool> List { get; } = new("--list") { Description = "List tasks without prompting." };

    public Option<bool> Init { get; } = new("--init") { Description = "Create a starter runx.json." };

    public Option<bool> Force { get; } = new("--force") { Description = "Overwrite an existing file with --init or --new." };

    public Option<string?> File { get; } = new("--file") { Description = "Use an explicit runx.json path." };

    public Option<bool> AllowParentSearch { get; } = new("--allow-parent-search")
    {
        Description = "Allow config discovery beyond the safe search boundary."
    };

    public Option<bool> DryRun { get; } = new("--dry-run") { Description = "Print the resolved task without executing it." };

    public Option<bool> Personal { get; } = new("--personal") { Description = "With --init, create your personal tasks file for this repo." };

    public Option<bool> Where { get; } = new("--where") { Description = "Show which task files apply here." };

    public Option<string?> Scope { get; } = new("--scope")
    {
        Description = "With --init --personal: global, a remote such as origin, or repo (the default)."
    };

    public Option<bool> Guide { get; } = new("--guide") { Description = "Explain how to add tasks, for people and AI agents." };

    public Option<bool> InstallSkill { get; } = new("--install-skill") { Description = "Install the runx skill for Claude Code." };

    public Option<bool> Check { get; } = new("--check") { Description = "Validate tasks without running anything." };

    public Option<bool> Json { get; } = new("--json") { Description = "With --list, --info, or --check, write JSON." };

    public Option<string?> Info { get; } = new("--info") { Description = "Describe one task." };

    public Option<string?> New { get; } = new("--new") { Description = "Create a script task in the task folder." };

    public Option<string?> Type { get; } = new("--type") { Description = "With --new: ps1 (the default), cs, sh, or cmd." };

    public Option<string?> Description { get; } = new("--description") { Description = "With --new: the task's description." };

    public Option<string[]?> Depends { get; } = new("--depends") { Description = "With --new: a task to run first. Repeat or comma-separate." };

    public Option<bool> Hide { get; } = new("--hide") { Description = "With --new: keep the task out of the picker and --list." };

    public Argument<string?> Task { get; } = new("task") { Arity = ArgumentArity.ZeroOrOne, Description = "Task to run." };
}

internal sealed record CliParseResult(
    bool ShowHelp,
    bool ShowVersion,
    bool List,
    bool Init,
    bool Force,
    string? FilePath,
    bool AllowParentSearch,
    bool DryRun,
    string? TaskName,
    IReadOnlyList<string> TaskArguments,
    IReadOnlyList<string> Errors)
{
    public bool Personal { get; init; }

    public bool Where { get; init; }

    public string? Scope { get; init; }

    public bool Guide { get; init; }

    public bool InstallSkill { get; init; }

    public bool Check { get; init; }

    public bool Json { get; init; }

    public string? InfoTaskName { get; init; }

    public string? NewTaskName { get; init; }

    public string? ScriptType { get; init; }

    public string? Description { get; init; }

    public IReadOnlyList<string> Depends { get; init; } = [];

    public bool Hide { get; init; }

    public static CliParseResult Help()
    {
        return new CliParseResult(
            ShowHelp: true,
            ShowVersion: false,
            List: false,
            Init: false,
            Force: false,
            FilePath: null,
            AllowParentSearch: false,
            DryRun: false,
            TaskName: null,
            TaskArguments: [],
            Errors: []);
    }

    public static CliParseResult Version()
    {
        return new CliParseResult(
            ShowHelp: false,
            ShowVersion: true,
            List: false,
            Init: false,
            Force: false,
            FilePath: null,
            AllowParentSearch: false,
            DryRun: false,
            TaskName: null,
            TaskArguments: [],
            Errors: []);
    }

    public static CliParseResult Failed(IReadOnlyList<string> errors)
    {
        return new CliParseResult(
            ShowHelp: false,
            ShowVersion: false,
            List: false,
            Init: false,
            Force: false,
            FilePath: null,
            AllowParentSearch: false,
            DryRun: false,
            TaskName: null,
            TaskArguments: [],
            Errors: errors);
    }
}
