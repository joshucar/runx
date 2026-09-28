using System.Text;
using System.Text.RegularExpressions;

namespace MysterNybble.Runx;

// A repo's key names its personal tasks file, e.g. "github.com/acme/api" becomes
// repos/github.com/acme/api.json. Keys are lowercase and every segment is a safe file name
// on every operating system, so personal files can be synced between machines.
internal static partial class RepoKey
{
    private const string LocalPrefix = "local";
    private const string UnsafeFileNameCharacters = "<>:\"/\\|?*";

    private static readonly HashSet<string> s_reservedFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    // Returns null when the URL is not a recognizable remote, so the caller can fall back to a path key.
    public static string? FromRemoteUrl(string remoteUrl, bool isWindows)
    {
        ArgumentNullException.ThrowIfNull(remoteUrl);

        string url = remoteUrl.Trim();
        if (url.Length == 0)
        {
            return null;
        }

        if (Path.IsPathFullyQualified(url))
        {
            return FromLocalPath(url, isWindows);
        }

        string host;
        string path;
        Match scpLike = ScpLikeRemote().Match(url);
        if (!url.Contains("://", StringComparison.Ordinal) && scpLike.Success)
        {
            // user@host:owner/repo, as used by SSH remotes.
            host = scpLike.Groups["host"].Value;
            path = scpLike.Groups["path"].Value;
        }
        else if (Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            if (uri.IsFile)
            {
                return FromLocalPath(uri.LocalPath, isWindows);
            }

            // Host and path only: user names, tokens, and ports never become part of the key.
            host = uri.Host;
            path = Uri.UnescapeDataString(uri.AbsolutePath);
        }
        else
        {
            return null;
        }

        List<string> segments = path
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        if (segments.Count > 0 && segments[^1].EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            segments[^1] = segments[^1][..^4];
        }

        // Azure DevOps SSH remotes (ssh.dev.azure.com:v3/org/project/repo) name the same repo as
        // https://dev.azure.com/org/project/_git/repo, so both map to the HTTPS form.
        if (host.Equals("ssh.dev.azure.com", StringComparison.OrdinalIgnoreCase)
            && segments.Count == 4
            && segments[0].Equals("v3", StringComparison.OrdinalIgnoreCase))
        {
            host = "dev.azure.com";
            segments = [segments[1], segments[2], "_git", segments[3]];
        }

        segments.RemoveAll(static segment => segment.Length == 0);
        if (host.Length == 0 || segments.Count == 0)
        {
            return null;
        }

        return string.Join('/', new[] { host }.Concat(segments).Select(static segment => ToSafeSegment(segment, lowercase: true)));
    }

    // Keys a repo without a usable remote by the folder of its main checkout, so every worktree shares it.
    public static string FromLocalPath(string path, bool isWindows)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string fullPath = Path.GetFullPath(path).TrimEnd('\\', '/');
        StringBuilder name = new(fullPath.Length);
        foreach (char character in fullPath)
        {
            name.Append(character is ':' or '\\' or '/' ? '-' : character);
        }

        // Windows paths are case-insensitive, so D:\Src and d:\src are the same checkout.
        return $"{LocalPrefix}/{ToSafeSegment(name.ToString().Trim('-'), lowercase: isWindows)}";
    }

    public static string ToFilePath(string runxHome, string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runxHome);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        return Path.Combine([runxHome, "repos", .. key.Split('/')]) + ".json";
    }

    private static string ToSafeSegment(string segment, bool lowercase)
    {
        StringBuilder safe = new(segment.Length);
        foreach (char character in segment)
        {
            if (char.IsControl(character) || UnsafeFileNameCharacters.Contains(character, StringComparison.Ordinal))
            {
                safe.Append('_');
            }
            else
            {
                safe.Append(lowercase ? char.ToLowerInvariant(character) : character);
            }
        }

        string result = safe.ToString().TrimEnd('.', ' ');
        if (result.Length == 0 || result is "." or "..")
        {
            return "_";
        }

        string stem = result.Split('.')[0];
        return s_reservedFileNames.Contains(stem) ? $"_{result}" : result;
    }

    [GeneratedRegex(@"^(?:[^@/\s]+@)?(?<host>[^:/\s]{2,}):(?<path>.+)$")]
    private static partial Regex ScpLikeRemote();
}
