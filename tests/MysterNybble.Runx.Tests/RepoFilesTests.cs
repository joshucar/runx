using System.Text.Json;

namespace MysterNybble.Runx.Tests;

public sealed class RepoFilesTests
{
    [Fact]
    public async Task RepoRunxJsonIsValid()
    {
        ConfigLoadResult result = await RunxConfigLoader.LoadAsync(
            System.IO.Path.Combine(FindRepoRoot(), "runx.json"),
            CancellationToken.None);

        Assert.Empty(result.Errors);
        Assert.NotNull(result.Config);
    }

    [Fact]
    public void SchemaShellEnumMatchesSupportedShells()
    {
        string schemaPath = System.IO.Path.Combine(FindRepoRoot(), "schemas", "v1", "runx.schema.json");
        using JsonDocument schema = JsonDocument.Parse(File.ReadAllText(schemaPath));

        string[] shells = schema.RootElement
            .GetProperty("$defs")
            .GetProperty("task")
            .GetProperty("properties")
            .GetProperty("shell")
            .GetProperty("enum")
            .EnumerateArray()
            .Select(static shell => shell.GetString() ?? string.Empty)
            .ToArray();

        Assert.Equal(["cmd", "pwsh", "sh"], shells);
        Assert.All(shells, static shell => Assert.True(ProcessRequestFactory.IsSupportedShell(shell)));
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(System.IO.Path.Combine(current.FullName, "runx.slnx")))
        {
            current = current.Parent;
        }

        return current?.FullName ?? throw new InvalidOperationException("Could not find the repo root.");
    }
}
