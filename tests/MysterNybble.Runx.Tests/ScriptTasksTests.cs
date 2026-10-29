namespace MysterNybble.Runx.Tests;

public sealed class ScriptTasksTests
{
    [Fact]
    public void FoldersBecomePrefixesAndHelpersAreSkipped()
    {
        using TempDirectory tempDirectory = new();
        string folder = System.IO.Path.Combine(tempDirectory.Path, "eng", "tasks");
        WriteScript(folder, "build.ps1");
        WriteScript(folder, "release/pack.ps1");
        WriteScript(folder, "db/_default.ps1");
        WriteScript(folder, "_default.ps1");
        WriteScript(folder, "lib/_helper.ps1");
        WriteScript(folder, "_internal/tool.ps1");
        WriteScript(folder, ".cache/tool.ps1");
        WriteScript(folder, "bad name.ps1");
        WriteScript(folder, "README.md", "# Notes");

        IReadOnlyList<ScriptTask> tasks = ScriptTasks.Discover(tempDirectory.Path, [folder], isWindows: true);

        Assert.Equal(["build", "db", "default", "release:pack"], tasks.Select(static task => task.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void HeadersSetDescriptionDependenciesAndHide()
    {
        using TempDirectory tempDirectory = new();
        string folder = System.IO.Path.Combine(tempDirectory.Path, "tasks");
        WriteScript(folder, "pack.ps1", "#Requires -Version 7\n# runx description=\"Pack \\\"it\\\"\"\n#RUNX depends=[\"build\", 'test']\n#runx   hide=true\n");
        WriteScript(folder, "deploy.ps1", "# runx description='Deploy C:\\temp'\n# runx depends=\"pack\"\n# runxy hide=true\n# runx is great\n");
        WriteScript(folder, "hello.cs", "// runx description=\"Say hello\"\nConsole.WriteLine(\"hello\");\n");

        Dictionary<string, RunxTask> tasks = ScriptTasks.Discover(tempDirectory.Path, [folder], isWindows: true)
            .ToDictionary(static task => task.Name, static task => task.Task);

        Assert.Equal("Pack \"it\"", tasks["pack"].Description);
        Assert.Equal(["build", "test"], tasks["pack"].DependsOn);
        Assert.True(tasks["pack"].Hide);
        Assert.Equal("Deploy C:\\temp", tasks["deploy"].Description);
        Assert.Equal(["pack"], tasks["deploy"].DependsOn);
        Assert.Null(tasks["deploy"].Hide);
        Assert.Equal("Say hello", tasks["hello"].Description);
    }

    [Fact]
    public void BatchFileHeadersUseCmdComments()
    {
        using TempDirectory tempDirectory = new();
        string folder = System.IO.Path.Combine(tempDirectory.Path, "tasks");
        WriteScript(folder, "one.cmd", "@echo off\r\n:: runx description=\"One\"\r\n");
        WriteScript(folder, "two.bat", "@echo off\r\nrem runx description=\"Two\"\r\n");

        IReadOnlyList<ScriptTask> windowsTasks = ScriptTasks.Discover(tempDirectory.Path, [folder], isWindows: true);
        IReadOnlyList<ScriptTask> unixTasks = ScriptTasks.Discover(tempDirectory.Path, [folder], isWindows: false);

        Assert.Equal(["One", "Two"], windowsTasks.Select(static task => task.Task.Description));
        Assert.Empty(unixTasks);
    }

    [Fact]
    public void EachOperatingSystemPicksItsBestScript()
    {
        using TempDirectory tempDirectory = new();
        string folder = System.IO.Path.Combine(tempDirectory.Path, "eng", "tasks");
        WriteScript(folder, "build.ps1");
        WriteScript(folder, "build.cmd", "@echo off\r\n");
        WriteScript(folder, "build.sh", "#!/bin/sh\necho build\n");
        WriteScript(folder, "hello.cs", "Console.WriteLine(\"hello\");\n");

        Dictionary<string, RunxTask> windows = ScriptTasks.Discover(tempDirectory.Path, [folder], isWindows: true)
            .ToDictionary(static task => task.Name, static task => task.Task);
        Dictionary<string, RunxTask> unix = ScriptTasks.Discover(tempDirectory.Path, [folder], isWindows: false)
            .ToDictionary(static task => task.Name, static task => task.Task);

        Assert.Equal("pwsh", windows["build"].Command);
        Assert.Equal(["-NoLogo", "-NoProfile", "-File", "eng/tasks/build.ps1"], windows["build"].Args!);
        Assert.EndsWith("build.sh", unix["build"].Args!.LastOrDefault() ?? unix["build"].Command, StringComparison.Ordinal);
        Assert.Equal("dotnet", windows["hello"].Command);
        Assert.Equal(["run", "--file", "eng/tasks/hello.cs", "--"], windows["hello"].Args!);
    }

    [Fact]
    public void EarlierFoldersWin()
    {
        using TempDirectory tempDirectory = new();
        string first = System.IO.Path.Combine(tempDirectory.Path, "eng", "tasks");
        string second = System.IO.Path.Combine(tempDirectory.Path, "build", "tasks");
        WriteScript(first, "build.ps1", "# runx description=\"From eng\"\n");
        WriteScript(second, "build.ps1", "# runx description=\"From build\"\n");
        WriteScript(second, "lint.ps1");

        IReadOnlyList<ScriptTask> tasks = ScriptTasks.Discover(tempDirectory.Path, [first, second], isWindows: true);

        Assert.Equal(["build", "lint"], tasks.Select(static task => task.Name));
        Assert.Equal("From eng", tasks[0].Task.Description);
    }

    [Theory]
    [InlineData("#!/usr/bin/env bash", false, "bash", "")]
    [InlineData("#!/usr/bin/env -S python3 -u", false, "python3", "-u")]
    [InlineData("#!/bin/bash -e", false, "/bin/bash", "-e")]
    [InlineData("#!/bin/bash -e", true, "bash", "-e")]
    public void ShebangsNameTheInterpreter(string line, bool isWindows, string command, string arguments)
    {
        (string Command, IReadOnlyList<string> Arguments)? shebang = ScriptTasks.ParseShebang(line, isWindows);

        Assert.NotNull(shebang);
        Assert.Equal(command, shebang.Value.Command);
        Assert.Equal(arguments, string.Join(' ', shebang.Value.Arguments));
    }

    [Fact]
    public void LinesWithoutShebangsAreNotInterpreters()
    {
        Assert.Null(ScriptTasks.ParseShebang("echo hi", isWindows: false));
        Assert.Null(ScriptTasks.ParseShebang("#!", isWindows: false));
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("\"tab\\tquote\\\" \\u0041\"", "tab\tquote\" A")]
    [InlineData("'C:\\literal'", "C:\\literal")]
    public void HeaderValuesParse(string text, object expected)
    {
        Assert.True(ScriptTasks.TryParseValue(text, out object value));
        Assert.Equal(expected, value);
    }

    [Fact]
    public void HeaderArraysParseAndInvalidValuesAreRejected()
    {
        Assert.True(ScriptTasks.TryParseValue("[ \"a\", 'b' ]", out object array));
        Assert.Equal(["a", "b"], Assert.IsType<List<string>>(array));
        Assert.False(ScriptTasks.TryParseValue("unquoted", out _));
        Assert.False(ScriptTasks.TryParseValue("\"unterminated", out _));
        Assert.False(ScriptTasks.TryParseValue("[\"a\", 1]", out _));
    }

    [Fact]
    public async Task ScriptsRunFromTheRepoRootWithoutAnyConfig()
    {
        using TempDirectory tempDirectory = new();
        string root = CreateRepo(tempDirectory.Path);
        string script = WriteScript(System.IO.Path.Combine(root, "eng", "tasks"), "release/pack.ps1", "# runx description=\"Pack\"\n");
        string subdirectory = System.IO.Path.Combine(root, "src", "App");
        Directory.CreateDirectory(subdirectory);
        RecordingProcessRunner processRunner = new();

        int exitCode = await RunAsync(["release:pack", "--", "-Configuration", "Debug"], subdirectory, processRunner);

        ProcessRequest request = Assert.Single(processRunner.Requests);
        Assert.Equal(0, exitCode);
        Assert.Equal("pwsh", request.FileName);
        Assert.Equal(["-NoLogo", "-NoProfile", "-File", "eng/tasks/release/pack.ps1", "-Configuration", "Debug"], request.Arguments);
        Assert.Equal(root, request.WorkingDirectory);
        Assert.Equal(script, request.ConfigPath);
    }

    [Fact]
    public async Task ScriptsAndRunxJsonTasksWorkTogether()
    {
        using TempDirectory tempDirectory = new();
        string root = CreateRepo(tempDirectory.Path);
        string folder = System.IO.Path.Combine(root, "eng", "tasks");
        WriteScript(folder, "build.ps1", "# runx description=\"Script build\"\n");
        WriteScript(folder, "pack.ps1", "# runx depends=[\"restore\"]\n");
        WriteScript(folder, "internal.ps1", "# runx hide=true\n");
        WriteConfig(
            root,
            """
            {
              "version": 1,
              "tasks": {
                "build": { "description": "Repo build.", "command": "dotnet", "args": ["build"] },
                "restore": { "command": "dotnet", "args": ["restore"] },
                "ci": { "dependsOn": ["pack", "internal"] },
                "secret": { "command": "dotnet", "hide": true }
              }
            }
            """);

        StringWriter listOutput = new();
        RecordingProcessRunner processRunner = new();

        int listExitCode = await RunAsync(["--list"], root, new RecordingProcessRunner(), listOutput);
        int runExitCode = await RunAsync(["ci"], root, processRunner);
        int hiddenExitCode = await RunAsync(["secret"], root, new RecordingProcessRunner());

        string[] lines = listOutput.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal((0, 0, 0), (listExitCode, runExitCode, hiddenExitCode));
        Assert.Equal(["build    Repo build.", "ci", "pack     (script)", "restore", $"Script tasks from {folder}"], lines);
        Assert.Equal(["restore", "pack", "internal"], processRunner.Requests.Select(static request => request.TaskName));
    }

    [Fact]
    public async Task TaskFoldersCanBeMovedPerRepoAndTurnedOff()
    {
        using TempDirectory tempDirectory = new();
        string home = System.IO.Path.Combine(tempDirectory.Path, "home");
        string root = CreateRepo(tempDirectory.Path);
        WriteScript(System.IO.Path.Combine(root, "eng", "tasks"), "default-folder.ps1");
        WriteScript(System.IO.Path.Combine(root, "build", "tasks"), "moved.ps1");
        WriteScript(System.IO.Path.Combine(root, "mine"), "personal-folder.ps1");
        string configPath = WriteConfig(root, """{ "version": 1, "taskFolders": ["build/tasks"], "tasks": {} }""");
        PersonalTasksSettings settings = new(home, new FakeGitClient().Returns("config --get remote.origin.url", "git@github.com:acme/api.git"));

        StringWriter movedList = new();
        StringWriter where = new();
        await RunAsync(["--list"], root, new RecordingProcessRunner(), movedList, settings);
        await RunAsync(["--where"], root, new RecordingProcessRunner(), where, settings);

        string personalPath = RepoKey.ToFilePath(home, "github.com/acme/api");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(personalPath)!);
        File.WriteAllText(personalPath, """{ "version": 1, "taskFolders": ["mine"], "tasks": {} }""");
        StringWriter personalList = new();
        await RunAsync(["--list"], root, new RecordingProcessRunner(), personalList, settings);

        File.WriteAllText(personalPath, """{ "version": 1, "taskFolders": [], "tasks": {} }""");
        StringWriter offList = new();
        await RunAsync(["--list"], root, new RecordingProcessRunner(), offList, settings);

        Assert.StartsWith("moved  (script)", movedList.ToString(), StringComparison.Ordinal);
        Assert.Contains($"Script tasks:    folders from taskFolders in {configPath}", where.ToString());
        Assert.StartsWith("personal-folder  (script)", personalList.ToString(), StringComparison.Ordinal);
        Assert.StartsWith("No tasks defined", offList.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ScriptsYieldToRunxJsonButBeatBroadPersonalTasks()
    {
        using TempDirectory tempDirectory = new();
        string home = System.IO.Path.Combine(tempDirectory.Path, "home");
        string root = CreateRepo(tempDirectory.Path);
        string folder = System.IO.Path.Combine(root, "eng", "tasks");
        string lintScript = WriteScript(folder, "lint.ps1");
        WriteScript(folder, "build.ps1");
        WriteConfig(root, """{ "version": 1, "tasks": { "build": { "command": "dotnet" } } }""");
        File.WriteAllText(
            WriteScript(home, "global.json"),
            """{ "version": 1, "tasks": { "lint": { "command": "global-lint" } } }""");
        PersonalTasksSettings settings = new(home, new FakeGitClient().Returns("config --get remote.origin.url", "git@github.com:acme/api.git"));
        RecordingProcessRunner processRunner = new();

        await RunAsync(["build"], root, processRunner, personalTasks: settings);
        await RunAsync(["lint"], root, processRunner, personalTasks: settings);

        string personalPath = RepoKey.ToFilePath(home, "github.com/acme/api");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(personalPath)!);
        File.WriteAllText(personalPath, """{ "version": 1, "tasks": { "lint": { "command": "mine" } } }""");
        StringWriter clashError = new();
        int clashExitCode = await RunAsync(["lint"], root, new RecordingProcessRunner(), error: clashError, personalTasks: settings);

        Assert.Equal(["dotnet", "pwsh"], processRunner.Requests.Select(static request => request.FileName));
        Assert.Equal(1, clashExitCode);
        Assert.Contains($"Task 'lint' is defined in both {lintScript} and {personalPath}.", clashError.ToString());
    }

    [Fact]
    public async Task FileOptionIgnoresScripts()
    {
        using TempDirectory tempDirectory = new();
        string root = CreateRepo(tempDirectory.Path);
        WriteScript(System.IO.Path.Combine(root, "eng", "tasks"), "script.ps1");
        string configPath = WriteConfig(root, """{ "version": 1, "tasks": { "build": { "command": "dotnet" } } }""");
        StringWriter output = new();

        await RunAsync(["--file", configPath, "--list"], root, new RecordingProcessRunner(), output);

        Assert.Equal("build", output.ToString().Trim());
    }

    [Fact]
    public async Task InvalidTaskFoldersAreConfigErrors()
    {
        using TempDirectory tempDirectory = new();
        string root = CreateRepo(tempDirectory.Path);
        WriteConfig(root, """{ "version": 1, "taskFolders": [" ", null], "tasks": {} }""");
        StringWriter error = new();

        int exitCode = await RunAsync(["--list"], root, new RecordingProcessRunner(), error: error);

        Assert.Equal(1, exitCode);
        Assert.Contains("Config property 'taskFolders[0]' must be a non-empty string.", error.ToString());
        Assert.Contains("Config property 'taskFolders[1]' must be a non-empty string.", error.ToString());
    }

    [Fact]
    public async Task RealScriptTasksRunWithPassthroughArguments()
    {
        using TempDirectory tempDirectory = new();
        string root = CreateRepo(tempDirectory.Path);
        string folder = System.IO.Path.Combine(root, "eng", "tasks");
        WriteScript(
            folder,
            "hello.cs",
            """
            File.WriteAllText("cs.txt", string.Join(",", args));
            return args.Length;
            """);

        int exitCode = await RunxCli.RunAsync(
            ["hello", "--", "a", "b c"],
            new StringWriter(),
            new StringWriter(),
            new DefaultProcessRunner(),
            new FakeTaskSelector(),
            root,
            CancellationToken.None);

        Assert.Equal(2, exitCode);
        // The relative output file also verifies the working directory, even through a symlink.
        Assert.Equal("a,b c", File.ReadAllText(System.IO.Path.Combine(root, "cs.txt")));
    }

    private static async Task<int> RunAsync(
        string[] args,
        string currentDirectory,
        RecordingProcessRunner processRunner,
        StringWriter? output = null,
        PersonalTasksSettings? personalTasks = null,
        StringWriter? error = null)
    {
        return await RunxCli.RunAsync(
            args,
            output ?? new StringWriter(),
            error ?? new StringWriter(),
            processRunner,
            new FakeTaskSelector(),
            currentDirectory,
            personalTasks,
            CancellationToken.None);
    }

    private static string CreateRepo(string parent)
    {
        string root = System.IO.Path.Combine(parent, "repo");
        Directory.CreateDirectory(System.IO.Path.Combine(root, ".git"));
        return root;
    }

    private static string WriteConfig(string root, string json)
    {
        string path = System.IO.Path.Combine(root, "runx.json");
        File.WriteAllText(path, json);
        return path;
    }

    private static string WriteScript(string folder, string relativePath, string contents = "")
    {
        string path = System.IO.Path.GetFullPath(System.IO.Path.Combine(folder, relativePath));
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
        return path;
    }
}
