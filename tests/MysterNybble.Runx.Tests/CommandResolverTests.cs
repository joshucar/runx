namespace MysterNybble.Runx.Tests;

public sealed class CommandResolverTests
{
    [Fact]
    public void WindowsBareNameResolvesCmdShimOnPath()
    {
        using TempDirectory tempDirectory = new();
        string emptyPath = CreateDirectory(tempDirectory.Path, "empty");
        string nodePath = CreateDirectory(tempDirectory.Path, "nodejs");
        CreateFile(nodePath, "npm");
        string npmCmd = CreateFile(nodePath, "npm.cmd");

        string? resolved = CommandResolver.Resolve(
            "npm",
            tempDirectory.Path,
            JoinPath(emptyPath, nodePath),
            ".com;.exe;.bat;.cmd",
            isWindows: true);

        Assert.Equal(npmCmd, resolved);
    }

    [Fact]
    public void WindowsBareNameHonorsPathExtOrder()
    {
        using TempDirectory tempDirectory = new();
        CreateFile(tempDirectory.Path, "tool.cmd");
        string toolExe = CreateFile(tempDirectory.Path, "tool.exe");

        string? resolved = CommandResolver.Resolve(
            "tool",
            tempDirectory.Path,
            tempDirectory.Path,
            ".exe;.cmd",
            isWindows: true);

        Assert.Equal(toolExe, resolved);
    }

    [Fact]
    public void WindowsRelativeCommandResolvesFromWorkingDirectoryWithPathExt()
    {
        using TempDirectory tempDirectory = new();
        string toolsPath = CreateDirectory(tempDirectory.Path, "tools");
        string buildCmd = CreateFile(toolsPath, "build.cmd");

        string? resolved = CommandResolver.Resolve(
            "tools/build",
            tempDirectory.Path,
            pathVariable: null,
            ".exe;.cmd",
            isWindows: true);

        Assert.Equal(buildCmd, resolved);
    }

    [Fact]
    public void RelativeCommandResolvesFromWorkingDirectory()
    {
        using TempDirectory tempDirectory = new();

        string? resolved = CommandResolver.Resolve(
            "./build.sh",
            tempDirectory.Path,
            pathVariable: null,
            pathExtVariable: null,
            isWindows: false);

        Assert.Equal(System.IO.Path.Combine(tempDirectory.Path, "build.sh"), resolved);
    }

    [Fact]
    public void UnresolvedBareNameIsNotFound()
    {
        using TempDirectory tempDirectory = new();

        string? resolved = CommandResolver.Resolve(
            "not-a-real-tool",
            tempDirectory.Path,
            tempDirectory.Path,
            ".exe;.cmd",
            isWindows: true);

        Assert.Null(resolved);
    }

    [Fact]
    public void UnixBareNameResolvesOnTaskPath()
    {
        using TempDirectory tempDirectory = new();
        string emptyPath = CreateDirectory(tempDirectory.Path, "empty");
        string toolsPath = CreateDirectory(tempDirectory.Path, "tools");
        string tool = CreateFile(toolsPath, "tool");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(tool, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        string? resolved = CommandResolver.Resolve(
            "tool",
            tempDirectory.Path,
            JoinPath(emptyPath, toolsPath),
            pathExtVariable: null,
            isWindows: false);

        Assert.Equal(tool, resolved);
    }

    [Fact]
    public void UnixSkipsFilesWithoutExecutePermission()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows files have no execute bits.");

        using TempDirectory tempDirectory = new();
        CreateFile(tempDirectory.Path, "tool");

        string? resolved = CommandResolver.Resolve(
            "tool",
            tempDirectory.Path,
            tempDirectory.Path,
            pathExtVariable: null,
            isWindows: false);

        Assert.Null(resolved);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RelativePathEntriesAreSkipped(bool isWindows)
    {
        using TempDirectory tempDirectory = new();
        CreateFile(tempDirectory.Path, "tool.cmd");
        CreateFile(tempDirectory.Path, "tool");

        string? resolved = CommandResolver.Resolve(
            "tool",
            tempDirectory.Path,
            JoinPath(".", "tools"),
            ".exe;.cmd",
            isWindows);

        Assert.Null(resolved);
    }

    private static string CreateDirectory(string parent, string name)
    {
        string path = System.IO.Path.Combine(parent, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private static string CreateFile(string directory, string name)
    {
        string path = System.IO.Path.Combine(directory, name);
        File.WriteAllText(path, string.Empty);
        return path;
    }

    private static string JoinPath(params string[] directories)
    {
        return string.Join(System.IO.Path.PathSeparator, directories);
    }
}
