namespace MysterNybble.Runx.Tests;

public sealed class PersonalTasksTests
{
    private const string OriginUrl = "git@github.com:acme/api.git";

    [Fact]
    public async Task IdentifyPrefersOriginThenTheOnlyRemoteThenTheMainCheckout()
    {
        using TempDirectory tempDirectory = new();
        string root = tempDirectory.Path;
        bool isWindows = OperatingSystem.IsWindows();

        RepoIdentity origin = await PersonalTasks.IdentifyAsync(
            root, new FakeGitClient().Returns("config --get remote.origin.url", OriginUrl), isWindows, CancellationToken.None);

        RepoIdentity upstream = await PersonalTasks.IdentifyAsync(
            root,
            new FakeGitClient()
                .Returns("remote", "upstream")
                .Returns("config --get remote.upstream.url", "https://github.com/acme/api"),
            isWindows,
            CancellationToken.None);

        string mainCheckout = System.IO.Path.Combine(root, "main");
        RepoIdentity local = await PersonalTasks.IdentifyAsync(
            System.IO.Path.Combine(root, "worktree"),
            new FakeGitClient().Returns("rev-parse --path-format=absolute --git-common-dir", System.IO.Path.Combine(mainCheckout, ".git")),
            isWindows,
            CancellationToken.None);

        Assert.Equal(("github.com/acme/api", "remote 'origin'"), (origin.Key, origin.KeySource));
        Assert.Equal(("github.com/acme/api", "remote 'upstream'"), (upstream.Key, upstream.KeySource));
        Assert.Equal(RepoKey.FromLocalPath(mainCheckout, isWindows), local.Key);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RealWorktreesShareTheMainCheckoutsKey(bool throughSymlink)
    {
        Assert.SkipWhen(throughSymlink && OperatingSystem.IsWindows(), "Creating Windows symlinks requires additional permissions.");

        GitCli git = new();
        using TempDirectory tempDirectory = new();
        Assert.SkipWhen(await git.RunAsync(tempDirectory.Path, ["--version"], CancellationToken.None) is null, "Git is not installed.");

        string main = System.IO.Path.Combine(tempDirectory.Path, "main");
        string worktree = System.IO.Path.Combine(tempDirectory.Path, "feature");
        Directory.CreateDirectory(main);
        if (throughSymlink)
        {
            main = Directory.CreateSymbolicLink(System.IO.Path.Combine(tempDirectory.Path, "main-link"), main).FullName;
        }

        await git.RunAsync(main, ["init", "--quiet"], CancellationToken.None);
        await git.RunAsync(main, ["-c", "user.name=runx", "-c", "user.email=runx@example.com", "commit", "--allow-empty", "--quiet", "-m", "init"], CancellationToken.None);
        await git.RunAsync(main, ["worktree", "add", "--quiet", "-b", "feature", worktree], CancellationToken.None);
        bool isWindows = OperatingSystem.IsWindows();

        RepoIdentity mainWithoutRemote = await PersonalTasks.IdentifyAsync(main, git, isWindows, CancellationToken.None);
        RepoIdentity worktreeWithoutRemote = await PersonalTasks.IdentifyAsync(worktree, git, isWindows, CancellationToken.None);
        // Git resolves symlinks in the checkout path, including macOS's /var -> /private/var.
        string? mainGitRoot = await git.RunAsync(main, ["rev-parse", "--show-toplevel"], CancellationToken.None);

        await git.RunAsync(main, ["remote", "add", "origin", OriginUrl], CancellationToken.None);
        RepoIdentity worktreeWithRemote = await PersonalTasks.IdentifyAsync(worktree, git, isWindows, CancellationToken.None);

        Assert.True(File.Exists(System.IO.Path.Combine(worktree, ".git")), "The worktree was not created.");
        Assert.NotNull(mainGitRoot);
        Assert.Equal(RepoKey.FromLocalPath(mainGitRoot, isWindows), mainWithoutRemote.Key);
        Assert.Equal(mainWithoutRemote.Key, worktreeWithoutRemote.Key);
        Assert.Equal("github.com/acme/api", worktreeWithRemote.Key);
    }

    [Fact]
    public async Task PersonalTasksRunFromEachWorktreeRootWithoutARepoConfig()
    {
        using TempDirectory tempDirectory = new();
        string home = System.IO.Path.Combine(tempDirectory.Path, "home");
        string mainRoot = CreateRepo(tempDirectory.Path, "main");
        string worktreeRoot = CreateRepo(tempDirectory.Path, "feature");
        string subdirectory = System.IO.Path.Combine(worktreeRoot, "src", "App");
        Directory.CreateDirectory(subdirectory);
        string personalPath = WritePersonalFile(
            home,
            """
            {
              "version": 1,
              "tasks": {
                "hello": { "command": "dotnet", "args": ["--info"], "cwd": "tools" }
              }
            }
            """);

        RecordingProcessRunner processRunner = new();
        PersonalTasksSettings settings = CreateSettings(home);

        int mainExitCode = await RunAsync(["hello"], mainRoot, settings, processRunner);
        int worktreeExitCode = await RunAsync(["hello"], subdirectory, settings, processRunner);

        Assert.Equal((0, 0), (mainExitCode, worktreeExitCode));
        Assert.Equal(
            [System.IO.Path.Combine(mainRoot, "tools"), System.IO.Path.Combine(worktreeRoot, "tools")],
            processRunner.Requests.Select(static request => request.WorkingDirectory));
        Assert.All(processRunner.Requests, request => Assert.Equal(personalPath, request.ConfigPath));
    }

    [Fact]
    public async Task PersonalTasksMergeWithRepoTasksAndCanDependOnThem()
    {
        using TempDirectory tempDirectory = new();
        string home = System.IO.Path.Combine(tempDirectory.Path, "home");
        string root = CreateRepo(tempDirectory.Path, "repo");
        WriteRepoConfig(root, """{ "version": 1, "tasks": { "build": { "description": "Build it.", "command": "dotnet", "args": ["build"] } } }""");
        string personalPath = WritePersonalFile(
            home,
            """{ "version": 1, "tasks": { "deploy": { "description": "Deploy to my box.", "command": "dotnet", "args": ["publish"], "dependsOn": ["build"] }, "scratch": { "command": "dotnet" } } }""");

        RecordingProcessRunner processRunner = new();
        StringWriter listOutput = new();

        int runExitCode = await RunAsync(["deploy"], root, CreateSettings(home), processRunner);
        int listExitCode = await RunAsync(["--list"], root, CreateSettings(home), new RecordingProcessRunner(), listOutput);

        string[] lines = listOutput.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal((0, 0), (runExitCode, listExitCode));
        Assert.Equal(["build", "deploy"], processRunner.Requests.Select(static request => request.TaskName));
        Assert.Equal(
            [
                "build    Build it.",
                "deploy   Deploy to my box.  (personal)",
                "scratch  (personal)",
                $"Personal tasks from {personalPath}"
            ],
            lines);
    }

    [Fact]
    public async Task ConflictsBetweenRepoAndPersonalTasksAreErrors()
    {
        using TempDirectory tempDirectory = new();
        string home = System.IO.Path.Combine(tempDirectory.Path, "home");
        string root = CreateRepo(tempDirectory.Path, "repo");
        string repoPath = WriteRepoConfig(
            root,
            """{ "version": 1, "tasks": { "build": { "command": "dotnet" }, "ci": { "dependsOn": ["mine"] } } }""");
        string personalPath = WritePersonalFile(
            home,
            """{ "version": 1, "tasks": { "build": { "command": "dotnet" }, "mine": { "command": "dotnet" }, "broken": { "command": "" } } }""");

        StringWriter error = new();

        int exitCode = await RunAsync(["--list"], root, CreateSettings(home), new RecordingProcessRunner(), error: error);

        Assert.Equal(1, exitCode);
        Assert.Contains($"Task 'build' is defined in both {repoPath} and {personalPath}.", error.ToString());
        Assert.Contains($"Task 'ci' in {repoPath} cannot depend on personal task 'mine'.", error.ToString());
        Assert.Contains($"In personal tasks file {personalPath}:{Environment.NewLine}  Task 'broken' command must not be empty.", error.ToString());
    }

    [Fact]
    public async Task FileOptionIgnoresPersonalTasks()
    {
        using TempDirectory tempDirectory = new();
        string home = System.IO.Path.Combine(tempDirectory.Path, "home");
        string root = CreateRepo(tempDirectory.Path, "repo");
        string repoPath = WriteRepoConfig(root, """{ "version": 1, "tasks": { "build": { "command": "dotnet" } } }""");
        WritePersonalFile(home, """{ "version": 1, "tasks": { "mine": { "command": "dotnet" } } }""");

        StringWriter output = new();

        int exitCode = await RunAsync(["--file", repoPath, "--list"], root, CreateSettings(home), new RecordingProcessRunner(), output);

        Assert.Equal(0, exitCode);
        Assert.Equal("build", output.ToString().Trim());
    }

    [Fact]
    public async Task GitIsNotConsultedUntilAPersonalFileExists()
    {
        using TempDirectory tempDirectory = new();
        string home = System.IO.Path.Combine(tempDirectory.Path, "home");
        string root = CreateRepo(tempDirectory.Path, "repo");
        WriteRepoConfig(root, """{ "version": 1, "tasks": { "build": { "command": "dotnet" } } }""");
        FakeGitClient git = new FakeGitClient().Returns("config --get remote.origin.url", OriginUrl);

        int exitCode = await RunAsync(["build"], root, new PersonalTasksSettings(home, git), new RecordingProcessRunner());

        Assert.Equal(0, exitCode);
        Assert.Empty(git.Calls);
    }

    [Fact]
    public async Task InitPersonalCreatesTheRepoFileAndWhereReportsIt()
    {
        using TempDirectory tempDirectory = new();
        string home = System.IO.Path.Combine(tempDirectory.Path, "home");
        string root = CreateRepo(tempDirectory.Path, "repo");
        string expectedPath = RepoKey.ToFilePath(home, "github.com/acme/api");
        PersonalTasksSettings settings = CreateSettings(home);

        StringWriter whereBefore = new();
        StringWriter initOutput = new();
        StringWriter secondInitError = new();
        StringWriter fileInitError = new();
        StringWriter whereAfter = new();
        StringWriter listOutput = new();

        await RunAsync(["--where"], root, settings, new RecordingProcessRunner(), whereBefore);
        int initExitCode = await RunAsync(["--init", "--personal"], root, settings, new RecordingProcessRunner(), initOutput);
        int secondInitExitCode = await RunAsync(["--init", "--personal"], root, settings, new RecordingProcessRunner(), error: secondInitError);
        int fileInitExitCode = await RunAsync(["--init", "--personal", "--file", "x.json"], root, settings, new RecordingProcessRunner(), error: fileInitError);
        await RunAsync(["--where"], root, settings, new RecordingProcessRunner(), whereAfter);
        int listExitCode = await RunAsync(["--list"], root, settings, new RecordingProcessRunner(), listOutput);

        Assert.Contains($"  repo         {expectedPath} (not created)", whereBefore.ToString());
        Assert.Contains($"  origin/acme  {RepoKey.ToFilePath(home, "github.com/acme")} (not created)", whereBefore.ToString());
        Assert.Contains("Repo key:        github.com/acme/api (from remote 'origin')", whereBefore.ToString());
        Assert.Equal(0, initExitCode);
        Assert.Contains($"Created {expectedPath}.", initOutput.ToString());
        Assert.True(File.Exists(expectedPath));
        Assert.Equal(1, secondInitExitCode);
        Assert.Contains("Use --force to overwrite it.", secondInitError.ToString());
        Assert.Equal(1, fileInitExitCode);
        Assert.Contains("--personal cannot be combined with --file.", fileInitError.ToString());
        Assert.Contains($"  repo         {expectedPath}{Environment.NewLine}", whereAfter.ToString());
        Assert.Equal(0, listExitCode);
        Assert.Contains("my:status", listOutput.ToString());
    }

    [Fact]
    public async Task InitPersonalOutsideAGitRepoFails()
    {
        using TempDirectory tempDirectory = new();
        StringWriter error = new();

        int exitCode = await RunAsync(
            ["--init", "--personal"],
            tempDirectory.Path,
            CreateSettings(System.IO.Path.Combine(tempDirectory.Path, "home")),
            new RecordingProcessRunner(),
            error: error);

        Assert.Equal(1, exitCode);
        Assert.Contains("is not inside one", error.ToString());
    }

    [Fact]
    public void LayersRunFromGlobalThroughTheRemoteToTheRepo()
    {
        PersonalTasksSettings settings = new("home", new FakeGitClient());

        IReadOnlyList<PersonalLayer> remoteLayers = PersonalTasks.GetLayers(
            settings,
            new RepoIdentity("repo", "github.com/acme/api", "remote 'upstream'", "upstream"));
        IReadOnlyList<PersonalLayer> localLayers = PersonalTasks.GetLayers(
            settings,
            new RepoIdentity("repo", "local/d--src-legacy", "main checkout folder, no remote"));

        Assert.Equal(["global", "upstream", "upstream/acme", "repo"], remoteLayers.Select(static layer => layer.Scope));
        Assert.Equal(
            [
                System.IO.Path.Combine("home", "global.json"),
                RepoKey.ToFilePath("home", "github.com"),
                RepoKey.ToFilePath("home", "github.com/acme"),
                RepoKey.ToFilePath("home", "github.com/acme/api")
            ],
            remoteLayers.Select(static layer => layer.Path));
        Assert.Equal(["global", "upstream", "upstream/acme", "personal"], remoteLayers.Select(static layer => layer.Tag));
        Assert.Equal(["global", "local", "repo"], localLayers.Select(static layer => layer.Scope));
    }

    [Fact]
    public async Task SpecificFilesOverrideBroaderOnesAndBroadFilesYieldToTheRepo()
    {
        using TempDirectory tempDirectory = new();
        string home = System.IO.Path.Combine(tempDirectory.Path, "home");
        string root = CreateRepo(tempDirectory.Path, "repo");
        WriteRepoConfig(root, """{ "version": 1, "tasks": { "build": { "description": "Repo build.", "command": "dotnet" } } }""");
        WriteFile(
            System.IO.Path.Combine(home, "global.json"),
            """{ "version": 1, "tasks": { "build": { "command": "make" }, "clean": { "command": "git" }, "hello": { "command": "echo" } } }""");
        string originPath = WriteFile(
            RepoKey.ToFilePath(home, "github.com"),
            """{ "version": 1, "tasks": { "clean": { "description": "GitHub clean.", "command": "gh" } } }""");
        WritePersonalFile(home, """{ "version": 1, "tasks": { "deploy": { "command": "dotnet" } } }""");

        RecordingProcessRunner processRunner = new();
        StringWriter listOutput = new();

        int listExitCode = await RunAsync(["--list"], root, CreateSettings(home), new RecordingProcessRunner(), listOutput);
        int runExitCode = await RunAsync(["clean"], root, CreateSettings(home), processRunner);

        string[] lines = listOutput.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        ProcessRequest request = Assert.Single(processRunner.Requests);
        Assert.Equal((0, 0), (listExitCode, runExitCode));
        Assert.Equal(
            ["build   Repo build.", "clean   GitHub clean.  (origin)", "deploy  (personal)", "hello   (global)"],
            lines.Take(4));
        Assert.Equal(
            [
                $"Personal tasks from {System.IO.Path.Combine(home, "global.json")}",
                $"Personal tasks from {originPath}",
                $"Personal tasks from {RepoKey.ToFilePath(home, "github.com/acme/api")}"
            ],
            lines.Skip(4));
        Assert.Equal(originPath, request.ConfigPath);
        Assert.Equal("gh", request.FileName);
    }

    [Fact]
    public async Task BroadTasksWithMissingDependenciesAreHiddenAndExplained()
    {
        using TempDirectory tempDirectory = new();
        string home = System.IO.Path.Combine(tempDirectory.Path, "home");
        string partial = CreateRepo(tempDirectory.Path, "partial");
        string complete = CreateRepo(tempDirectory.Path, "complete");
        WriteRepoConfig(partial, """{ "version": 1, "tasks": { "build": { "command": "dotnet" } } }""");
        WriteRepoConfig(complete, """{ "version": 1, "tasks": { "build": { "command": "dotnet" }, "test": { "command": "dotnet" } } }""");
        string globalPath = WriteFile(
            System.IO.Path.Combine(home, "global.json"),
            """{ "version": 1, "tasks": { "ship": { "command": "gh", "dependsOn": ["build", "test"] }, "announce": { "command": "gh", "dependsOn": ["ship"] } } }""");

        StringWriter partialList = new();
        StringWriter shipError = new();
        StringWriter announceError = new();
        RecordingProcessRunner completeRunner = new();

        await RunAsync(["--list"], partial, CreateSettings(home), new RecordingProcessRunner(), partialList);
        int shipExitCode = await RunAsync(["ship"], partial, CreateSettings(home), new RecordingProcessRunner(), error: shipError);
        int announceExitCode = await RunAsync(["announce"], partial, CreateSettings(home), new RecordingProcessRunner(), error: announceError);
        int completeExitCode = await RunAsync(["announce"], complete, CreateSettings(home), completeRunner);

        Assert.Equal("build", partialList.ToString().Trim());
        Assert.Equal((1, 1, 0), (shipExitCode, announceExitCode, completeExitCode));
        Assert.Contains(
            $"Task 'ship' from {globalPath} is not available in this repo because it depends on 'test', which this repo does not define.",
            shipError.ToString());
        Assert.Contains("because it depends on 'ship', which is not available here either.", announceError.ToString());
        Assert.Equal(["build", "test", "ship", "announce"], completeRunner.Requests.Select(static request => request.TaskName));
    }

    [Fact]
    public async Task GlobalTasksNeedNoGitWhenNoRepoFilesExist()
    {
        using TempDirectory tempDirectory = new();
        string home = System.IO.Path.Combine(tempDirectory.Path, "home");
        string root = CreateRepo(tempDirectory.Path, "repo");
        WriteFile(System.IO.Path.Combine(home, "global.json"), """{ "version": 1, "tasks": { "hello": { "command": "echo" } } }""");
        FakeGitClient git = new FakeGitClient().Returns("config --get remote.origin.url", OriginUrl);
        RecordingProcessRunner processRunner = new();

        int exitCode = await RunAsync(["hello"], root, new PersonalTasksSettings(home, git), processRunner);

        Assert.Equal(0, exitCode);
        Assert.Equal(root, Assert.Single(processRunner.Requests).WorkingDirectory);
        Assert.Empty(git.Calls);
    }

    [Theory]
    [InlineData("global", "global.json")]
    [InlineData("origin", "github.com")]
    [InlineData("origin/acme", "github.com/acme")]
    [InlineData("repo", "github.com/acme/api")]
    public async Task InitPersonalScopesCreateTheMatchingFile(string scope, string expectedKey)
    {
        using TempDirectory tempDirectory = new();
        string home = System.IO.Path.Combine(tempDirectory.Path, "home");
        string root = CreateRepo(tempDirectory.Path, "repo");
        string expectedPath = expectedKey == "global.json"
            ? System.IO.Path.Combine(home, "global.json")
            : RepoKey.ToFilePath(home, expectedKey);

        int exitCode = await RunAsync(["--init", "--personal", "--scope", scope], root, CreateSettings(home), new RecordingProcessRunner());

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(expectedPath));
    }

    [Fact]
    public async Task InvalidScopesAreRejected()
    {
        using TempDirectory tempDirectory = new();
        string home = System.IO.Path.Combine(tempDirectory.Path, "home");
        string root = CreateRepo(tempDirectory.Path, "repo");
        StringWriter hostError = new();
        StringWriter withoutInitError = new();

        int hostExitCode = await RunAsync(["--init", "--personal", "--scope", "github.com"], root, CreateSettings(home), new RecordingProcessRunner(), error: hostError);
        int withoutInitExitCode = await RunAsync(["--list", "--scope", "origin"], root, CreateSettings(home), new RecordingProcessRunner(), error: withoutInitError);

        Assert.Equal((1, 1), (hostExitCode, withoutInitExitCode));
        Assert.Contains("Unknown scope 'github.com'. Scopes for this repo, broadest first: global, origin, origin/acme, repo.", hostError.ToString());
        Assert.Contains("--scope can only be used with --init --personal.", withoutInitError.ToString());
    }

    private static string WriteFile(string path, string json)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);
        return path;
    }

    private static async Task<int> RunAsync(
        string[] args,
        string currentDirectory,
        PersonalTasksSettings settings,
        RecordingProcessRunner processRunner,
        StringWriter? output = null,
        StringWriter? error = null)
    {
        return await RunxCli.RunAsync(
            args,
            output ?? new StringWriter(),
            error ?? new StringWriter(),
            processRunner,
            new FakeTaskSelector(),
            currentDirectory,
            settings,
            CancellationToken.None);
    }

    private static PersonalTasksSettings CreateSettings(string home)
    {
        return new PersonalTasksSettings(home, new FakeGitClient().Returns("config --get remote.origin.url", OriginUrl));
    }

    private static string CreateRepo(string parent, string name)
    {
        string root = System.IO.Path.Combine(parent, name);
        Directory.CreateDirectory(System.IO.Path.Combine(root, ".git"));
        return root;
    }

    private static string WriteRepoConfig(string root, string json)
    {
        string path = System.IO.Path.Combine(root, "runx.json");
        File.WriteAllText(path, json);
        return path;
    }

    private static string WritePersonalFile(string home, string json)
    {
        string path = RepoKey.ToFilePath(home, "github.com/acme/api");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);
        return path;
    }
}
