using System.Text.Json;

namespace MysterNybble.Runx.Tests;

public sealed class DependencyArgumentsTests
{
    [Fact]
    public async Task FixedArgumentsBelongOnlyToTheirDependencyCall()
    {
        using TempDirectory directory = new();
        WriteConfig(directory.Path, """
            {
              "version": 1,
              "tasks": {
                "prepare": { "command": "tool", "args": ["prepare"] },
                "lint": { "command": "tool", "args": ["lint"] },
                "deploy": {
                  "command": "tool", "args": ["deploy"],
                  "dependsOn": [{ "task": "prepare", "args": ["--warmup"] }]
                },
                "release": {
                  "command": "tool", "args": ["release"],
                  "dependsOn": ["lint", { "task": "deploy", "args": ["--standby", "two words", "", "a\"b", "&|%!", "--now=false"] }]
                }
              }
            }
            """);
        RecordingProcessRunner runner = new();

        int code = await RunAsync(directory.Path, ["release", "--", "--yes"], runner);

        Assert.Equal(0, code);
        Assert.Equal(["lint", "prepare", "deploy", "release"], runner.Requests.Select(static request => request.TaskName));
        Assert.Equal(["lint"], runner.Requests[0].Arguments);
        Assert.Equal(["prepare", "--warmup"], runner.Requests[1].Arguments);
        Assert.Equal(["deploy", "--standby", "two words", "", "a\"b", "&|%!", "--now=false"], runner.Requests[2].Arguments);
        Assert.Equal(["release", "--yes"], runner.Requests[3].Arguments);

        RecordingProcessRunner direct = new();
        await RunAsync(directory.Path, ["deploy"], direct);
        Assert.Equal(["deploy"], direct.Requests[^1].Arguments);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("[\"--standby\"]")]
    public async Task IdenticalCallsAcrossBranchesRunOnce(string args)
    {
        using TempDirectory directory = new();
        WriteDiamond(directory.Path, args, args);
        RecordingProcessRunner runner = new();

        int code = await RunAsync(directory.Path, ["release"], runner);

        Assert.Equal(0, code);
        Assert.Equal("deploy", Assert.Single(runner.Requests).TaskName);
    }

    [Theory]
    [InlineData("[\"--standby\"]", "[\"--active\"]")]
    [InlineData("[]", "[\"--standby\"]")]
    [InlineData("[\"a\", \"b\"]", "[\"b\", \"a\"]")]
    public async Task ConflictingCallsAreRejectedBeforeExecution(string first, string second)
    {
        using TempDirectory directory = new();
        WriteDiamond(directory.Path, first, second);
        RecordingProcessRunner runner = new();
        foreach (string[] args in new[] { new[] { "release" }, ["--dry-run", "release"], ["--info", "release"], ["--check"] })
        {
            StringWriter output = new();
            StringWriter error = new();
            int code = await RunAsync(directory.Path, args, runner, output, error);
            Assert.Equal(1, code);
            Assert.Contains("Task 'release' reaches 'deploy' with conflicting dependency arguments.", output.ToString() + error);
        }

        Assert.Empty(runner.Requests);
    }

    [Theory]
    [InlineData("42", "A dependency must be a task name")]
    [InlineData("null", "dependsOn[0] must be a task name")]
    [InlineData("{}", "Dependency object requires 'task'")]
    [InlineData("{\"task\":null}", "Dependency 'task' must be a string")]
    [InlineData("{\"task\":\"deploy\",\"args\":null}", "Dependency 'args' must be an array")]
    [InlineData("{\"task\":\"deploy\",\"args\":[null]}", "Dependency args must contain only strings")]
    [InlineData("{\"task\":\"deploy\",\"args\":[false]}", "Dependency args must contain only strings")]
    [InlineData("{\"task\":\"deploy\",\"forward\":true}", "Unknown dependency property 'forward'")]
    [InlineData("{\"task\":\"deploy\",\"task\":\"deploy\"}", "Duplicate dependency property 'task'")]
    [InlineData("{\"task\":\"deploy\",\"args\":[],\"args\":[]}", "Duplicate dependency property 'args'")]
    [InlineData("{\"task\":\"missing\",\"args\":[\"x\"]}", "depends on unknown task 'missing'")]
    [InlineData("{\"task\":\"release\",\"args\":[]}", "Task dependencies form a cycle")]
    [InlineData("{\"task\":\"deploy\",\"args\":[\"a\\u0000b\"]}", "must not contain NUL")]
    [InlineData("\"deploy\", {\"task\":\"deploy\",\"args\":[\"x\"]}", "lists 'deploy' in dependsOn more than once")]
    public async Task InvalidDependencyEntriesFailValidation(string dependency, string expected)
    {
        using TempDirectory directory = new();
        WriteConfig(directory.Path, $$"""
            { "version": 1, "tasks": {
              "deploy": { "command": "tool" },
              "release": { "dependsOn": [{{dependency}}] }
            } }
            """);
        RecordingProcessRunner runner = new();
        StringWriter error = new();

        int code = await RunAsync(directory.Path, ["release"], runner, error: error);

        Assert.Equal(1, code);
        Assert.Contains(expected, error.ToString());
        Assert.Empty(runner.Requests);
    }

    [Theory]
    [InlineData("{\"script\":\"echo hi\",\"shell\":\"sh\"}")]
    [InlineData("{\"dependsOn\":[\"build\"]}")]
    public async Task DependencyArgumentsRequireACommandOrDiscoveredScript(string target)
    {
        using TempDirectory directory = new();
        WriteConfig(directory.Path, $$"""
            { "version": 1, "tasks": {
              "build": { "command": "tool" },
              "deploy": {{target}},
              "release": { "dependsOn": [{ "task": "deploy", "args": ["--standby"] }] }
            } }
            """);
        StringWriter error = new();
        RecordingProcessRunner runner = new();

        int code = await RunAsync(directory.Path, ["release"], runner, error: error);

        Assert.Equal(1, code);
        Assert.Contains("only command tasks and discovered script tasks accept arguments", error.ToString());
        Assert.Empty(runner.Requests);
    }

    [Fact]
    public async Task InspectionShowsDependencyArgumentsWithoutRunningTasks()
    {
        using TempDirectory directory = new();
        WriteConfig(directory.Path, """
            { "version": 1, "tasks": {
              "build": { "command": "tool" },
              "deploy": { "command": "tool", "args": ["deploy"] },
              "release": { "dependsOn": ["build", { "task": "deploy", "args": ["--standby", "two words"] }] }
            } }
            """);
        RecordingProcessRunner runner = new();
        StringWriter info = new();
        StringWriter json = new();
        StringWriter list = new();
        StringWriter dryRun = new();

        Assert.Equal(0, await RunAsync(directory.Path, ["--info", "release"], runner, info));
        Assert.Equal(0, await RunAsync(directory.Path, ["--info", "release", "--json"], runner, json));
        Assert.Equal(0, await RunAsync(directory.Path, ["--list", "--json"], runner, list));
        Assert.Equal(0, await RunAsync(directory.Path, ["--dry-run", "release"], runner, dryRun));

        Assert.Contains("build -> deploy -- --standby \"two words\"", info.ToString());
        Assert.Contains("  deploy" + Environment.NewLine + "  --standby" + Environment.NewLine + "  two words", dryRun.ToString());
        using JsonDocument document = JsonDocument.Parse(json.ToString());
        Assert.Equal(["build", "deploy"], document.RootElement.GetProperty("plan").EnumerateArray().Select(static item => item.GetString()));
        Assert.Equal("build", document.RootElement.GetProperty("dependsOn")[0].GetString());
        Assert.Equal("--standby", document.RootElement.GetProperty("dependsOn")[1].GetProperty("args")[0].GetString());
        Assert.Equal(["--standby", "two words"], document.RootElement.GetProperty("steps")[1].GetProperty("args").EnumerateArray().Select(static item => item.GetString()));
        using JsonDocument listing = JsonDocument.Parse(list.ToString());
        JsonElement release = listing.RootElement.GetProperty("tasks").EnumerateArray().Single(static task => task.GetProperty("name").GetString() == "release");
        Assert.Equal("deploy", release.GetProperty("dependsOn")[1].GetProperty("task").GetString());
        Assert.Empty(runner.Requests);
    }

    [Fact]
    public async Task FixedArgumentsReachARealDiscoveredScriptIntact()
    {
        using TempDirectory directory = new();
        Directory.CreateDirectory(System.IO.Path.Combine(directory.Path, ".git"));
        string folder = System.IO.Path.Combine(directory.Path, "eng", "tasks");
        Directory.CreateDirectory(folder);
        File.WriteAllText(System.IO.Path.Combine(folder, "capture.cs"), """
            File.WriteAllText("actual.txt", string.Join("\n", args));
            """);
        WriteConfig(directory.Path, """
            { "version": 1, "tasks": {
              "release": { "dependsOn": [{ "task": "capture", "args": ["--standby", "two words", "a\"b", "&|%!", ""] }] }
            } }
            """);
        StringWriter error = new();

        int code = await RunAsync(directory.Path, ["release"], new DefaultProcessRunner(), error: error);

        Assert.Equal(0, code);
        Assert.Equal("--standby\ntwo words\na\"b\n&|%!\n", File.ReadAllText(System.IO.Path.Combine(directory.Path, "actual.txt")));
        Assert.Contains("1/1 capture", error.ToString());
    }

    private static void WriteDiamond(string root, string first, string second) => WriteConfig(root, $$"""
        { "version": 1, "tasks": {
          "deploy": { "command": "tool" },
          "first": { "dependsOn": [{ "task": "deploy", "args": {{first}} }] },
          "second": { "dependsOn": [{ "task": "deploy", "args": {{second}} }] },
          "release": { "dependsOn": ["first", "second"] }
        } }
        """);

    private static void WriteConfig(string root, string json) => File.WriteAllText(System.IO.Path.Combine(root, "runx.json"), json);

    private static Task<int> RunAsync(string root, string[] args, IProcessRunner runner, StringWriter? output = null, StringWriter? error = null) =>
        RunxCli.RunAsync(args, output ?? new StringWriter(), error ?? new StringWriter(), runner, new FakeTaskSelector(), root, CancellationToken.None);
}
