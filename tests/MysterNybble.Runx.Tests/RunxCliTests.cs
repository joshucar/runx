namespace MysterNybble.Runx.Tests;

public sealed class RunxCliTests
{
    [Fact]
    public void NoArgumentsPrintsHelp()
    {
        StringWriter output = new();
        StringWriter error = new();

        int exitCode = RunxCli.Run([], output, error);

        Assert.Equal(0, exitCode);
        Assert.Contains("Usage:", output.ToString());
        Assert.Empty(error.ToString());
    }

    [Fact]
    public void VersionPrintsInformationalVersion()
    {
        StringWriter output = new();
        StringWriter error = new();

        int exitCode = RunxCli.Run(["--version"], output, error);

        Assert.Equal(0, exitCode);
        Assert.NotEmpty(output.ToString().Trim());
        Assert.Empty(error.ToString());
    }

    [Fact]
    public void UnknownCommandReturnsFailure()
    {
        StringWriter output = new();
        StringWriter error = new();

        int exitCode = RunxCli.Run(["missing"], output, error);

        Assert.Equal(1, exitCode);
        Assert.Empty(output.ToString());
        Assert.Contains("Unknown command 'missing'.", error.ToString());
    }
}
