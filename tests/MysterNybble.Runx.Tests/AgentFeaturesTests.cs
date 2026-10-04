using System.Text.Json;

namespace MysterNybble.Runx.Tests;

// --new, --info, --check, --list --json, --guide, and the Claude Code skill.
public sealed class AgentFeaturesTests
{
    [Theory]
    [InlineData(new[] { "--list", "--guide" }, "Only one tool action can be used at a time.")]
    [InlineData(new[] { "--json" }, "--json can only be used with --list, --info, or --check.")]
    [InlineData(new[] { "--list", "--type", "cs" }, "--type, --description, --depends, and --hide can only be used with --new.")]
    [InlineData(new[] { "--check", "--force" }, "--force can only be used with --init or --new.")]
    [InlineData(new[] { "--new", "x", "--file", "runx.json" }, "--new cannot be combined with --file.")]
    [InlineData(new[] { "--check", "--dry-run" }, "--dry-run can only be used with a task.")]
    public async Task OptionsOnlyWorkWithTheirActions(string[] args, string expectedError)
    {
        using TempDirectory tempDirectory = new();
        StringWriter error = new();

        int exitCode = await RunAsync(args, CreateRepo(tempDirectory.Path), error: error);

        Assert.Equal(1, exitCode);
        Assert.Contains(expectedError, error.ToString());
    }

    [Theory]
    [InlineData("ps1", "# runx description=", "throw 'TODO: implement db:seed in eng/tasks/db/seed.ps1'")]
    [InlineData("cs", "// runx description=", "Console.Error.WriteLine(\"TODO: implement db:seed in eng/tasks/db/seed.cs\");")]
    [InlineData("sh", "# runx description=", "echo 'TODO: implement db:seed in eng/tasks/db/seed.sh' >&2")]
    [InlineData("cmd", ":: runx description=", "echo TODO: implement db:seed in eng/tasks/db/seed.cmd 1>&2")]
    public async Task NewCreatesAScriptWithItsHeader(string type, string headerStart, string todoLine)
    {
        using TempDirectory tempDirectory = new();
        string root = CreateRepo(tempDirectory.Path);
        WriteConfig(root, """{ "version": 1, "tasks": { "build": { "command": "dotnet" } } }""");
        StringWriter output = new();

        int exitCode = await RunAsync(
            ["--new", "db:seed", "--type", type, "--description", "Seed \"it\" C:\\data", "--depends", "build", "--hide"],
            root,
            output);

        string path = System.IO.Path.Combine(root, "eng", "tasks", "db", $"seed.{type}");
        string[] lines = File.ReadAllLines(path);
        Assert.Equal(0, exitCode);
        Assert.Contains($"Created eng/tasks/db/seed.{type} for task 'db:seed'.", output.ToString());
        Assert.Contains($"{headerStart}\"Seed \\\"it\\\" C:\\\\data\"", lines);
        Assert.Contains(todoLine, lines);

        if (OperatingSystem.IsWindows() || type != "cmd")
        {
            ScriptTask task = Assert.Single(ScriptTasks.Discover(root, [System.IO.Path.Combine(root, "eng", "tasks")], OperatingSystem.IsWindows()));
            Assert.Equal("db:seed", task.Name);
            Assert.Equal("Seed \"it\" C:\\data", task.Task.Description);
            Assert.Equal(["build"], task.Task.DependsOn);
            Assert.True(task.Task.Hide);
        }
    }

    [Fact]
    public async Task NewWorksInARepoWithNoTasksYetAndDefaultsToPowerShell()
    {
        using TempDirectory tempDirectory = new();
        string root = CreateRepo(tempDirectory.Path);
        string subdirectory = System.IO.Path.Combine(root, "src");
        Directory.CreateDirectory(subdirectory);

        int exitCode = await RunAsync(["--new", "hello"], subdirectory);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(System.IO.Path.Combine(root, "eng", "tasks", "hello.ps1")));
    }

    [Fact]
    public async Task NewRejectsNamesAndDependenciesThatWouldNotWork()
    {
        using TempDirectory tempDirectory = new();
        string root = CreateRepo(tempDirectory.Path);
        WriteConfig(root, """{ "version": 1, "tasks": { "build": { "command": "dotnet" } } }""");
        WriteScript(root, "eng/tasks/lint.cs");

        (int, string) helper = await RunWithErrorAsync(["--new", "tools:_helper"], root);
        (int, string) clash = await RunWithErrorAsync(["--new", "build"], root);
        (int, string) unknown = await RunWithErrorAsync(["--new", "deploy", "--depends", "build,ship"], root);
        (int, string) self = await RunWithErrorAsync(["--new", "deploy", "--depends", "deploy"], root);
        (int, string) type = await RunWithErrorAsync(["--new", "deploy", "--type", "py"], root);
        (int, string) exists = await RunWithErrorAsync(["--new", "lint", "--type", "cs"], root);
        (int, string) forced = await RunWithErrorAsync(["--new", "lint", "--type", "cs", "--force"], root);

        Assert.Contains("do not start a part with '_' or '.'", helper.Item2);
        Assert.Contains("already defines 'build', so a script with that name would not be used.", clash.Item2);
        Assert.Contains("Task 'ship' does not exist, so 'deploy' cannot depend on it.", unknown.Item2);
        Assert.Contains("Task 'deploy' cannot depend on itself.", self.Item2);
        Assert.Contains("Unknown script type 'py'. Use one of: ps1, cs, sh, cmd.", type.Item2);
        Assert.Contains("eng/tasks/lint.cs already exists. Use --force to overwrite it.", exists.Item2);
        Assert.Equal((1, 1, 1, 1, 1, 1, 0), (helper.Item1, clash.Item1, unknown.Item1, self.Item1, type.Item1, exists.Item1, forced.Item1));
    }

    [Fact]
    public async Task NewPairsScriptsForOtherOperatingSystemsAndFollowsTaskFolders()
    {
        using TempDirectory tempDirectory = new();
        string root = CreateRepo(tempDirectory.Path);
        WriteScript(root, "build/tasks/pack.ps1");
        WriteConfig(root, """{ "version": 1, "taskFolders": ["build/tasks"], "tasks": {} }""");
        StringWriter output = new();

        int exitCode = await RunAsync(["--new", "pack", "--type", "sh"], root, output);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(System.IO.Path.Combine(root, "build", "tasks", "pack.sh")));
        Assert.Contains("It pairs with build/tasks/pack.ps1", output.ToString());
    }

    [Fact]
    public async Task NewFailsWhenScriptTasksAreTurnedOff()
    {
        using TempDirectory tempDirectory = new();
        string root = CreateRepo(tempDirectory.Path);
        string configPath = WriteConfig(root, """{ "version": 1, "taskFolders": [], "tasks": {} }""");

        (int ExitCode, string Error) result = await RunWithErrorAsync(["--new", "hello"], root);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains($"Script tasks are turned off here: taskFolders is empty in {configPath}.", result.Error);
    }

    [Fact]
    public async Task InfoDescribesATaskAsTextAndJson()
    {
        using TempDirectory tempDirectory = new();
        string root = CreateRepo(tempDirectory.Path);
        WriteConfig(
            root,
            """
            {
              "version": 1,
              "tasks": {
                "build": { "description": "Build it.", "command": "dotnet", "args": ["build", "My App.sln"], "env": { "B": "1", "A": "2" } },
                "lint": { "script": "echo one\necho two", "shell": "pwsh", "dependsOn": ["build"], "hide": true },
                "ci": { "dependsOn": ["lint"] }
              }
            }
            """);

        StringWriter text = new();
        StringWriter json = new();
        StringWriter ciText = new();
        (int ExitCode, string Error) missing = await RunWithErrorAsync(["--info", "nope"], root);

        await RunAsync(["--info", "lint"], root, text);
        await RunAsync(["--info", "build", "--json"], root, json);
        await RunAsync(["--info", "ci"], root, ciText);

        Assert.Contains("Runs:         pwsh: echo one", text.ToString());
        Assert.Contains("                    echo two", text.ToString());
        Assert.Contains("Depends on:   build", text.ToString());
        Assert.Contains("Plan:         build -> lint", text.ToString());
        Assert.Contains("Hidden:       yes", text.ToString());
        Assert.Contains("Runs:         only its dependencies", ciText.ToString());

        using JsonDocument document = JsonDocument.Parse(json.ToString());
        JsonElement task = document.RootElement;
        Assert.Equal("build", task.GetProperty("name").GetString());
        Assert.Equal("config", task.GetProperty("source").GetString());
        Assert.Equal("command", task.GetProperty("kind").GetString());
        Assert.Equal(["build", "My App.sln"], task.GetProperty("args").EnumerateArray().Select(static arg => arg.GetString()));
        Assert.Equal(["A", "B"], task.GetProperty("env").EnumerateArray().Select(static env => env.GetString()));
        Assert.Equal(root, task.GetProperty("cwd").GetString());
        Assert.Equal(["build"], task.GetProperty("plan").EnumerateArray().Select(static step => step.GetString()));

        Assert.Equal(1, missing.ExitCode);
        Assert.Contains("Task 'nope' was not found", missing.Error);
    }

    [Fact]
    public async Task ListJsonIncludesHiddenTasksAndIsEmptyWithoutTasks()
    {
        using TempDirectory tempDirectory = new();
        string empty = CreateRepo(System.IO.Path.Combine(tempDirectory.Path, "empty"));
        string root = CreateRepo(System.IO.Path.Combine(tempDirectory.Path, "full"));
        WriteConfig(root, """{ "version": 1, "tasks": { "build": { "command": "dotnet" } } }""");
        WriteScript(root, "eng/tasks/internal.ps1", "# runx hide=true\n");

        StringWriter emptyOutput = new();
        StringWriter output = new();
        await RunAsync(["--list", "--json"], empty, emptyOutput);
        await RunAsync(["--list", "--json"], root, output);

        using JsonDocument emptyDocument = JsonDocument.Parse(emptyOutput.ToString());
        using JsonDocument document = JsonDocument.Parse(output.ToString());
        JsonElement[] tasks = document.RootElement.GetProperty("tasks").EnumerateArray().ToArray();
        Assert.Equal(0, emptyDocument.RootElement.GetProperty("tasks").GetArrayLength());
        Assert.Equal(["build", "internal"], tasks.Select(static task => task.GetProperty("name").GetString()));
        Assert.Equal(["config", "script"], tasks.Select(static task => task.GetProperty("source").GetString()));
        Assert.Equal([false, true], tasks.Select(static task => task.GetProperty("hidden").GetBoolean()));
    }

    [Fact]
    public async Task CheckReportsWarningsErrorsAndEmptyRepos()
    {
        using TempDirectory tempDirectory = new();
        string empty = CreateRepo(System.IO.Path.Combine(tempDirectory.Path, "empty"));
        string root = CreateRepo(System.IO.Path.Combine(tempDirectory.Path, "repo"));
        string configPath = WriteConfig(root, """{ "version": 1, "tasks": { "build": { "command": "dotnet" } } }""");
        WriteScript(root, "eng/tasks/build.ps1");
        WriteScript(root, "eng/tasks/typo.ps1", "# runx descripton=\"x\"\n# runx hide=maybe\n# runx depends=true\n");
        WriteScript(root, "eng/tasks/bad name.ps1");

        StringWriter emptyOutput = new();
        StringWriter output = new();
        StringWriter json = new();
        int emptyExitCode = await RunAsync(["--check"], empty, emptyOutput);
        int exitCode = await RunAsync(["--check"], root, output);
        await RunAsync(["--check", "--json"], root, json);

        WriteConfig(root, """{ "version": 1, "tasks": { "build": { "command": "" } } }""");
        StringWriter brokenOutput = new();
        int brokenExitCode = await RunAsync(["--check"], root, brokenOutput);

        Assert.Equal((0, 0, 1), (emptyExitCode, exitCode, brokenExitCode));
        Assert.Contains("No tasks here", emptyOutput.ToString());
        Assert.Contains("warning: eng/tasks/typo.ps1: unknown header key 'descripton'.", output.ToString());
        Assert.Contains("warning: eng/tasks/typo.ps1: the header value for 'hide' is not a quoted string", output.ToString());
        Assert.Contains("warning: eng/tasks/typo.ps1: the header 'depends' must be a quoted task name", output.ToString());
        Assert.Contains("warning: eng/tasks/bad name.ps1 is not a task", output.ToString());
        Assert.Contains($"is not used because {configPath} also defines 'build'.", output.ToString());
        Assert.Contains("OK: 2 tasks (1 from runx.json, 1 script).", output.ToString());
        Assert.Contains("Task 'build' command must not be empty.", brokenOutput.ToString());
        Assert.Contains("runx --check found problems.", brokenOutput.ToString());

        using JsonDocument document = JsonDocument.Parse(json.ToString());
        Assert.True(document.RootElement.GetProperty("valid").GetBoolean());
        Assert.Equal(2, document.RootElement.GetProperty("taskCount").GetInt32());
        Assert.Equal(5, document.RootElement.GetProperty("warnings").GetArrayLength());
    }

    [Fact]
    public async Task GuideExplainsTasksAndDescribesTheRepo()
    {
        using TempDirectory tempDirectory = new();
        string root = CreateRepo(tempDirectory.Path);
        string configPath = WriteConfig(root, """{ "version": 1, "taskFolders": ["build/tasks"], "tasks": { "build": { "command": "dotnet" } } }""");
        StringWriter output = new();

        int exitCode = await RunAsync(["--guide"], root, output);

        string guide = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("## Add a script task", guide);
        Assert.Contains("runx --new db:seed --type cs", guide);
        Assert.Contains("## This repo", guide);
        Assert.Contains($"- New scripts go in: build/tasks, set by taskFolders in {configPath}; runx --new creates it", guide);
        Assert.Contains($"- runx.json: {configPath}", guide);
        Assert.Contains("- Tasks: 1 task (1 from runx.json).", guide);
    }

    [Fact]
    public void SkillInstallsOnceAndUpdatesWhenChanged()
    {
        using TempDirectory tempDirectory = new();
        string skillPath = System.IO.Path.Combine(tempDirectory.Path, "skills", "runx", "SKILL.md");
        StringWriter first = new();
        StringWriter second = new();
        StringWriter third = new();

        int firstExitCode = RunxGuide.InstallSkill(tempDirectory.Path, first, new StringWriter());
        int secondExitCode = RunxGuide.InstallSkill(tempDirectory.Path, second, new StringWriter());
        File.WriteAllText(skillPath, "old");
        int thirdExitCode = RunxGuide.InstallSkill(tempDirectory.Path, third, new StringWriter());

        Assert.Equal((0, 0, 0), (firstExitCode, secondExitCode, thirdExitCode));
        Assert.StartsWith($"Installed the runx skill at {skillPath}.", first.ToString(), StringComparison.Ordinal);
        Assert.Contains("already up to date", second.ToString());
        Assert.StartsWith("Updated the runx skill", third.ToString(), StringComparison.Ordinal);
        string skill = File.ReadAllText(skillPath);
        Assert.StartsWith("---\nname: runx\ndescription: ", skill.ReplaceLineEndings("\n"), StringComparison.Ordinal);
        Assert.Contains("runx --guide", skill);
    }

    [Fact]
    public async Task HelpPointsToTheGuide()
    {
        StringWriter output = new();

        await RunAsync(["--help"], System.IO.Path.GetTempPath(), output);

        Assert.Contains("Run 'runx --guide' to learn how to add tasks.", output.ToString());
        Assert.Contains("--new <name>", output.ToString());
    }

    [Fact]
    public async Task NewPowerShellTasksFailUntilTheTodoIsReplaced()
    {
        using TempDirectory tempDirectory = new();
        string root = CreateRepo(tempDirectory.Path);
        await RunAsync(["--new", "hello"], root);
        StringWriter error = new();

        int exitCode = await RunxCli.RunAsync(
            ["hello"],
            new StringWriter(),
            error,
            new DefaultProcessRunner(),
            new FakeTaskSelector(),
            root,
            CancellationToken.None);

        Assert.NotEqual(0, exitCode);
        Assert.DoesNotContain("not found on PATH", error.ToString());
    }

    private static async Task<int> RunAsync(string[] args, string currentDirectory, StringWriter? output = null, StringWriter? error = null)
    {
        return await RunxCli.RunAsync(
            args,
            output ?? new StringWriter(),
            error ?? new StringWriter(),
            new RecordingProcessRunner(),
            new FakeTaskSelector(),
            currentDirectory,
            CancellationToken.None);
    }

    private static async Task<(int ExitCode, string Error)> RunWithErrorAsync(string[] args, string currentDirectory)
    {
        StringWriter error = new();
        int exitCode = await RunAsync(args, currentDirectory, error: error);
        return (exitCode, error.ToString());
    }

    private static string CreateRepo(string root)
    {
        Directory.CreateDirectory(System.IO.Path.Combine(root, ".git"));
        return root;
    }

    private static string WriteConfig(string root, string json)
    {
        string path = System.IO.Path.Combine(root, "runx.json");
        File.WriteAllText(path, json);
        return path;
    }

    private static void WriteScript(string root, string relativePath, string contents = "")
    {
        string path = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, relativePath));
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
    }
}
