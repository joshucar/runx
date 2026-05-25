using System.Reflection;

namespace MysterNybble.Runx;

public static class RunxCli
{
    public static int Run(IReadOnlyList<string> args, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        if (args.Count == 0 || IsHelp(args[0]))
        {
            WriteHelp(output);
            return 0;
        }

        if (args[0] is "--version")
        {
            output.WriteLine(GetVersion());
            return 0;
        }

        error.WriteLine($"Unknown command '{args[0]}'.");
        error.WriteLine("Run 'runx --help' for usage.");
        return 1;
    }

    private static bool IsHelp(string value) => value is "-h" or "--help" or "help";

    private static void WriteHelp(TextWriter output)
    {
        output.WriteLine("runx - repo command runner for .NET projects");
        output.WriteLine();
        output.WriteLine("Usage:");
        output.WriteLine("  runx [command]");
        output.WriteLine();
        output.WriteLine("Commands:");
        output.WriteLine("  help        Show help.");
        output.WriteLine();
        output.WriteLine("Options:");
        output.WriteLine("  --version   Show version.");
    }

    private static string GetVersion()
    {
        Assembly assembly = typeof(RunxCli).Assembly;
        AssemblyInformationalVersionAttribute? versionAttribute =
            assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();

        return versionAttribute?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "0.0.0";
    }
}
