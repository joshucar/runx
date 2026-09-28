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

        CommandLineModel commandLine = CreateCommandLineModel();
        ParseResult parseResult = commandLine.RootCommand.Parse(parserArgs);
        if (parseResult.Errors.Count > 0)
        {
            return CliParseResult.Failed(parseResult.Errors.Select(static parseError => parseError.Message).ToArray());
        }

        string? taskName = parseResult.GetValue(commandLine.TaskArgument);
        string? filePath = parseResult.GetValue(commandLine.FileOption);

        bool list = parseResult.GetValue(commandLine.ListOption);
        bool init = parseResult.GetValue(commandLine.InitOption);
        bool force = parseResult.GetValue(commandLine.ForceOption);
        bool allowParentSearch = parseResult.GetValue(commandLine.AllowParentSearchOption);
        bool dryRun = parseResult.GetValue(commandLine.DryRunOption);
        bool personal = parseResult.GetValue(commandLine.PersonalOption);
        bool where = parseResult.GetValue(commandLine.WhereOption);
        string? scope = parseResult.GetValue(commandLine.ScopeOption);

        List<string> errors = [];
        int actionCount = CountTrue(list, init, where);
        if (actionCount > 1)
        {
            errors.Add("Only one tool action can be used at a time.");
        }

        if (taskName is not null && (list || init || where))
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
            Force: force,
            FilePath: filePath,
            AllowParentSearch: allowParentSearch,
            DryRun: dryRun,
            TaskName: taskName,
            TaskArguments: taskArguments,
            Errors: [])
        {
            Personal = personal,
            Where = where,
            Scope = scope
        };
    }

    private static CommandLineModel CreateCommandLineModel()
    {
        Option<bool> listOption = new("--list")
        {
            Description = "List tasks without prompting."
        };

        Option<bool> initOption = new("--init")
        {
            Description = "Create a starter runx.json."
        };

        Option<bool> forceOption = new("--force")
        {
            Description = "Overwrite an existing runx.json with --init."
        };

        Option<string?> fileOption = new("--file")
        {
            Description = "Use an explicit runx.json path."
        };

        Option<bool> allowParentSearchOption = new("--allow-parent-search")
        {
            Description = "Allow config discovery beyond the safe search boundary."
        };

        Option<bool> dryRunOption = new("--dry-run")
        {
            Description = "Print the resolved task without executing it."
        };

        Option<bool> personalOption = new("--personal")
        {
            Description = "With --init, create your personal tasks file for this repo."
        };

        Option<bool> whereOption = new("--where")
        {
            Description = "Show which task files apply here."
        };

        Option<string?> scopeOption = new("--scope")
        {
            Description = "With --init --personal: global, a remote such as origin, or repo (the default)."
        };

        Argument<string?> taskArgument = new("task")
        {
            Arity = ArgumentArity.ZeroOrOne,
            Description = "Task to run."
        };

        RootCommand rootCommand = new("repo command runner for .NET projects");
        rootCommand.Options.Add(listOption);
        rootCommand.Options.Add(initOption);
        rootCommand.Options.Add(forceOption);
        rootCommand.Options.Add(fileOption);
        rootCommand.Options.Add(allowParentSearchOption);
        rootCommand.Options.Add(dryRunOption);
        rootCommand.Options.Add(personalOption);
        rootCommand.Options.Add(whereOption);
        rootCommand.Options.Add(scopeOption);
        rootCommand.Arguments.Add(taskArgument);

        return new CommandLineModel(
            rootCommand,
            listOption,
            initOption,
            forceOption,
            fileOption,
            allowParentSearchOption,
            dryRunOption,
            personalOption,
            whereOption,
            scopeOption,
            taskArgument);
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

    private static int CountTrue(params bool[] values)
    {
        int count = 0;
        foreach (bool value in values)
        {
            if (value)
            {
                count++;
            }
        }

        return count;
    }
}

internal sealed record CommandLineModel(
    RootCommand RootCommand,
    Option<bool> ListOption,
    Option<bool> InitOption,
    Option<bool> ForceOption,
    Option<string?> FileOption,
    Option<bool> AllowParentSearchOption,
    Option<bool> DryRunOption,
    Option<bool> PersonalOption,
    Option<bool> WhereOption,
    Option<string?> ScopeOption,
    Argument<string?> TaskArgument);

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
