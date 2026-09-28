namespace MysterNybble.Runx.Tests;

public sealed class RepoKeyTests
{
    [Theory]
    [InlineData("https://github.com/Acme/API.git", "github.com/acme/api")]
    [InlineData("git@github.com:acme/api.git", "github.com/acme/api")]
    [InlineData("ssh://git@github.com:22/acme/api", "github.com/acme/api")]
    [InlineData("https://user:secret-token@github.com/acme/api/", "github.com/acme/api")]
    [InlineData("https://org@dev.azure.com/org/proj/_git/web", "dev.azure.com/org/proj/_git/web")]
    [InlineData("git@ssh.dev.azure.com:v3/org/proj/web", "dev.azure.com/org/proj/_git/web")]
    [InlineData("https://gitlab.example.com/group/sub/proj.git", "gitlab.example.com/group/sub/proj")]
    [InlineData("https://example.com/con/aux.git", "example.com/_con/_aux")]
    [InlineData("https://example.com/a%3Ab/c.", "example.com/a_b/c")]
    public void RemoteUrlsBecomeReadableKeys(string remoteUrl, string expected)
    {
        Assert.Equal(expected, RepoKey.FromRemoteUrl(remoteUrl, isWindows: OperatingSystem.IsWindows()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("../sibling/repo")]
    [InlineData("https://github.com/")]
    public void UnrecognizedRemotesHaveNoKey(string remoteUrl)
    {
        Assert.Null(RepoKey.FromRemoteUrl(remoteUrl, isWindows: OperatingSystem.IsWindows()));
    }

    [Fact]
    public void WindowsLocalPathsAreCaseInsensitive()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows paths only parse on Windows.");

        Assert.Equal("local/d--src-legacy", RepoKey.FromLocalPath(@"D:\Src\Legacy\", isWindows: true));
        Assert.Equal("local/d--src-legacy", RepoKey.FromRemoteUrl(@"D:\Src\Legacy", isWindows: true));
    }

    [Fact]
    public void UnixLocalPathsKeepTheirCase()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix paths only parse on Unix.");

        Assert.Equal("local/home-Josh-src", RepoKey.FromLocalPath("/home/Josh/src/", isWindows: false));
        Assert.Equal("local/srv-git-api.git", RepoKey.FromRemoteUrl("file:///srv/git/api.git", isWindows: false));
    }

    [Fact]
    public void KeysMapToFilesUnderReposFolder()
    {
        string home = System.IO.Path.Combine(System.IO.Path.GetTempPath(), ".runx");

        string path = RepoKey.ToFilePath(home, "github.com/acme/api");

        Assert.Equal(System.IO.Path.Combine(home, "repos", "github.com", "acme", "api.json"), path);
    }
}
