namespace MysterNybble.Runx;

// The guide printed by 'runx --guide' and the Claude Code skill installed by 'runx --install-skill'.
// Both ship inside the tool, so they always describe the installed version.
internal static class RunxGuide
{
    public const string ClaudeConfigVariable = "CLAUDE_CONFIG_DIR";

    public static string GuideText => ReadResource("MysterNybble.Runx.Guide.md");

    public static string SkillText => ReadResource("MysterNybble.Runx.SKILL.md");

    // The general guide, then what applies to the current repo, since that is what an agent needs next.
    public static void Write(
        TextWriter output,
        string repoRoot,
        bool isGitRepo,
        string? repoConfigPath,
        ConfigLoadResult? loadResult)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentException.ThrowIfNullOrWhiteSpace(repoRoot);

        output.Write(GuideText.ReplaceLineEndings(Environment.NewLine).TrimEnd());
        output.WriteLine();
        output.WriteLine();
        output.WriteLine("## This repo");
        output.WriteLine();
        output.WriteLine("This describes the repo containing the current directory. Run runx from inside the repo you are working on.");
        output.WriteLine();
        output.WriteLine(isGitRepo ? $"- Root: {repoRoot}" : $"- Root: {repoRoot} (not a Git repo, so personal tasks do not apply)");

        TaskFolderSettings? taskFolders = loadResult?.Config?.TaskFolders;
        if (taskFolders is null)
        {
            output.WriteLine($"- New scripts go in: {ScriptTasks.DefaultFolder} unless runx.json or a personal file sets taskFolders");
        }
        else if (taskFolders.Folders.Count == 0)
        {
            output.WriteLine($"- Script tasks are turned off: taskFolders is empty in {taskFolders.SetBy}.");
        }
        else
        {
            string folder = taskFolders.Folders[0];
            string exists = Directory.Exists(folder) ? string.Empty : "; runx --new creates it";
            string setBy = taskFolders.SetBy is null ? string.Empty : $", set by taskFolders in {taskFolders.SetBy}";
            output.WriteLine($"- New scripts go in: {ToDisplayPath(repoRoot, folder)}{setBy}{exists}");
        }

        output.WriteLine(repoConfigPath is null
            ? "- runx.json: none; 'runx --init' creates one"
            : $"- runx.json: {repoConfigPath}");

        IReadOnlyList<string> personalFiles = loadResult?.Config?.PersonalFiles ?? [];
        output.WriteLine(personalFiles.Count == 0
            ? "- Personal task files: none"
            : $"- Personal task files: {string.Join(", ", personalFiles)}");

        if (loadResult is { Config: LoadedRunxConfig config, Errors.Count: 0 })
        {
            output.WriteLine($"- Tasks: {TaskReports.DescribeCounts(config)}. See them with 'runx --list'.");
        }
        else if (loadResult is { Errors.Count: > 0 })
        {
            output.WriteLine("- The tasks have errors; run 'runx --check' to see them.");
        }
    }

    public static string GetDefaultClaudeDirectory()
    {
        string? configured = Environment.GetEnvironmentVariable(ClaudeConfigVariable);
        return string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude")
            : configured;
    }

    public static int InstallSkill(string claudeDirectory, TextWriter output, TextWriter error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(claudeDirectory);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        string skillPath = Path.Combine(Path.GetFullPath(claudeDirectory), "skills", "runx", "SKILL.md");
        string skill = SkillText;

        try
        {
            bool existed = File.Exists(skillPath);
            if (existed && File.ReadAllText(skillPath) == skill)
            {
                output.WriteLine($"The runx skill at {skillPath} is already up to date.");
                return 0;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(skillPath)!);
            File.WriteAllText(skillPath, skill);
            output.WriteLine($"{(existed ? "Updated" : "Installed")} the runx skill at {skillPath}.");
            output.WriteLine("Claude Code uses it when asked to add or run tasks; it tells agents to start with 'runx --guide'.");
            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error.WriteLine($"Failed to install the runx skill at {skillPath}: {ex.Message}");
            return 1;
        }
    }

    public static string ToDisplayPath(string root, string path)
    {
        string relative = Path.GetRelativePath(root, path);
        return Path.IsPathRooted(relative) || relative.StartsWith("..", StringComparison.Ordinal)
            ? path
            : relative.Replace('\\', '/');
    }

    private static string ReadResource(string name)
    {
        using Stream stream = typeof(RunxGuide).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Missing embedded resource {name}.");
        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }
}
