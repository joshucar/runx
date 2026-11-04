using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MysterNybble.Runx;

internal sealed record HookEvent(int MinArguments, int MaxArguments, bool HasStandardInput = false);

// Repo-owned bindings use Git's named hooks, leaving core.hooksPath and hook files alone.
internal static partial class GitHooks
{
    internal const string ProtocolVersion = "1";
    internal const string Owner = "runx-v1";
    internal const string Prefix = "hook.runx-";

    public static IReadOnlyDictionary<string, HookEvent> Events { get; } = new Dictionary<string, HookEvent>(StringComparer.Ordinal)
    {
        ["applypatch-msg"] = new(1, 1),
        ["pre-applypatch"] = new(0, 0),
        ["post-applypatch"] = new(0, 0),
        ["pre-commit"] = new(0, 0),
        ["pre-merge-commit"] = new(0, 0),
        ["prepare-commit-msg"] = new(1, 3),
        ["commit-msg"] = new(1, 1),
        ["post-commit"] = new(0, 0),
        ["pre-rebase"] = new(1, 2),
        ["post-checkout"] = new(3, 3),
        ["post-merge"] = new(1, 1),
        ["pre-push"] = new(2, 2, true),
        ["post-rewrite"] = new(1, 1, true)
    };

    public static string? ValidateInvocation(string name, int argumentCount)
    {
        if (!Events.TryGetValue(name, out HookEvent? hook))
        {
            return $"Unknown or unsupported Git hook '{name}'.";
        }

        return argumentCount < hook.MinArguments || argumentCount > hook.MaxArguments
            ? $"Git hook '{name}' expects {hook.MinArguments}..{hook.MaxArguments} arguments after '--'; received {argumentCount}."
            : null;
    }

    public static async Task RequireSupportedGitAsync(string directory, CancellationToken cancellationToken)
    {
        CommandOutput result = await GitAsync(directory, ["--version"], cancellationToken);
        Match match = GitVersion().Match(result.Output);
        if (result.ExitCode != 0 || !match.Success
            || !Version.TryParse(match.Groups[1].Value, out Version? version) || version < new Version(2, 54, 0))
        {
            throw new InvalidOperationException("Installing Git hooks requires Git 2.54 or newer (named hook registrations). Ordinary runx tasks do not require this version.");
        }
    }

    // Resolve a stable launcher, preferring a manifest visible from Git's working directory.
    // Never silently fall back to a global version when a local tool needs restoring.
    public static async Task<string> ResolveLauncherAsync(string root, CancellationToken cancellationToken)
    {
        bool local = HasLocalManifest(root);
        string executable;
        string[] arguments;
        if (local)
        {
            executable = CommandResolver.ResolveFromEnvironment("dotnet", root)
                ?? throw new InvalidOperationException("dotnet was not found on PATH. Install the .NET SDK and run 'dotnet tool restore'.");
            arguments = ["tool", "run", "runx", "--"];
        }
        else
        {
            string? current = Environment.ProcessPath;
            executable = current is not null && Path.GetFileNameWithoutExtension(current).Equals("runx", StringComparison.OrdinalIgnoreCase)
                ? current
                : throw new InvalidOperationException("This invocation has no stable runx launcher at the worktree root. Install runx in a root tool manifest and restore it, or invoke an installed runx executable directly. Hook installation will not substitute a different runx from PATH.");
            arguments = [];
        }

        CommandOutput probe = await RunCommandAsync(root, executable, [.. arguments, "--hook-protocol"], cancellationToken);
        if (probe.ExitCode != 0 || probe.Output.Trim() != ProtocolVersion)
        {
            string message = local
                ? "The manifest's runx tool is unavailable or lacks Git hook support. Update its version if needed, run 'dotnet tool restore', and retry."
                : "The installed runx tool lacks Git hook support. Update or install this build as a tool, then retry.";
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(probe.Error) ? message : $"{message}{Environment.NewLine}{probe.Error.Trim()}");
        }

        return string.Join(' ', new[] { ShellQuote(OperatingSystem.IsWindows() ? executable.Replace('\\', '/') : executable) }.Concat(arguments));
    }

    internal static bool HasLocalManifest(string root)
    {
        for (DirectoryInfo? directory = new(root); directory is not null; directory = directory.Parent)
        {
            // Match the SDK's root-directory manifest opt-in (off by default on Windows).
            string? allowRoot = Environment.GetEnvironmentVariable("DOTNET_TOOLS_ALLOW_MANIFEST_IN_ROOT");
            if (directory.Parent is null && (string.IsNullOrWhiteSpace(allowRoot)
                ? OperatingSystem.IsWindows()
                : !(allowRoot == "1" || allowRoot.Equals("true", StringComparison.OrdinalIgnoreCase))))
            {
                break;
            }

            foreach (string path in new[] { Path.Combine(directory.FullName, ".config", "dotnet-tools.json"), Path.Combine(directory.FullName, "dotnet-tools.json") })
            {
                if (!File.Exists(path))
                {
                    continue;
                }

                using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(path));
                JsonElement document = manifest.RootElement;
                if (document.TryGetProperty("tools", out JsonElement tools))
                {
                    foreach (JsonProperty tool in tools.EnumerateObject())
                    {
                        if (tool.Name.Equals("MysterNybble.Runx", StringComparison.OrdinalIgnoreCase)
                            || (tool.Value.TryGetProperty("commands", out JsonElement commands)
                                && commands.EnumerateArray().Any(static command => command.GetString() == "runx")))
                        {
                            return true;
                        }
                    }
                }

                if (document.TryGetProperty("isRoot", out JsonElement isRoot) && isRoot.GetBoolean())
                {
                    return false;
                }
            }
        }

        return false;
    }

    internal static string HookCommand(string launcher, string name) => $"{launcher} --hook {name} --";

    internal static string ShellQuote(string value) => "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

    // Decode only the first argument of the launcher format we generate; never evaluate shell text.
    internal static string? LauncherExecutable(string launcher)
    {
        if (!launcher.StartsWith('\''))
        {
            return launcher.Split(' ', 2)[0];
        }

        StringBuilder path = new();
        for (int i = 1; i < launcher.Length; i++)
        {
            if (launcher[i] != '\'')
            {
                path.Append(launcher[i]);
            }
            else if (launcher.AsSpan(i).StartsWith("'\\''", StringComparison.Ordinal))
            {
                path.Append('\'');
                i += 3;
            }
            else
            {
                return path.ToString();
            }
        }

        return null;
    }

    public static Task<CommandOutput> GitAsync(string root, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        string executable = CommandResolver.ResolveFromEnvironment("git", root)
            ?? throw new InvalidOperationException("Git was not found on PATH.");
        return RunCommandAsync(root, executable, arguments, cancellationToken);
    }

    private static async Task<CommandOutput> RunCommandAsync(string root, string executable, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        try
        {
            return await CapturedProcess.RunAsync(root, executable, arguments, TimeSpan.FromSeconds(30), cancellationToken);
        }
        catch (Exception ex) when (ex is Win32Exception or TimeoutException)
        {
            throw new InvalidOperationException($"Could not run '{executable}' while managing Git hooks: {ex.Message}", ex);
        }
    }

    [GeneratedRegex(@"git version (\d+\.\d+\.\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex GitVersion();
}
