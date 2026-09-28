using System.Text;

namespace MysterNybble.Runx;

internal static class WindowsCommandLine
{
    private const string UnquotedSymbols = @"#$*+-./:?@\_";

    // Placed before each '%', this expands to an empty substring of %cd%, so %NAME% is never expanded.
    private const string EmptyPercentExpansion = "%%cd:~,";

    // Command extensions and delayed expansion are pinned so registry settings cannot change parsing.
    public static IReadOnlyList<string> CmdSwitches { get; } = ["/d", "/e:ON", "/v:OFF", "/s", "/c"];

    // cmd.exe comes from ComSpec rather than a PATH lookup, so a task's PATH cannot hide or replace it.
    public static string CmdPath =>
        Environment.GetEnvironmentVariable("ComSpec") ?? Path.Combine(Environment.SystemDirectory, "cmd.exe");

    public static bool IsBatchFile(string path)
    {
        string extension = Path.GetExtension(path);
        return extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".bat", StringComparison.OrdinalIgnoreCase);
    }

    public static bool CanPassToBatchFile(string argument)
    {
        return !argument.Contains('\r', StringComparison.Ordinal) && !argument.Contains('\n', StringComparison.Ordinal);
    }

    // Windows runs batch files through cmd.exe, which parses the command line again, and batch files
    // usually forward %* to another program that parses it once more. This follows Rust's fix for
    // CVE-2024-24576: quote anything unusual, escape embedded quotes as "" so cmd.exe's quote state
    // stays balanced at every level, and break up %VAR% expansion with an empty %cd:~,% substring.
    public static string CreateBatchFileArguments(string batchFilePath, IReadOnlyList<string> arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(batchFilePath);
        ArgumentNullException.ThrowIfNull(arguments);

        StringBuilder commandLine = new();
        commandLine.Append('"');
        AppendEscapingPercent(commandLine, batchFilePath);
        commandLine.Append('"');
        foreach (string argument in arguments)
        {
            if (!CanPassToBatchFile(argument))
            {
                throw new ArgumentException("Batch file arguments cannot contain line breaks.", nameof(arguments));
            }

            commandLine.Append(' ');
            AppendBatchArgument(commandLine, argument);
        }

        return CreateCmdArguments(commandLine.ToString());
    }

    // With /s, cmd.exe strips the outer quotes and runs the rest of the command line verbatim.
    public static string CreateCmdArguments(string commandLine)
    {
        ArgumentNullException.ThrowIfNull(commandLine);
        return $"{string.Join(' ', CmdSwitches)} \"{commandLine}\"";
    }

    private static void AppendBatchArgument(StringBuilder commandLine, string argument)
    {
        bool quote = argument.Length == 0 || argument.EndsWith('\\') || argument.Any(NeedsQuotes);
        if (quote)
        {
            commandLine.Append('"');
        }

        int backslashCount = 0;
        foreach (char character in argument)
        {
            if (character == '\\')
            {
                backslashCount++;
            }
            else
            {
                if (character == '"')
                {
                    commandLine.Append('\\', backslashCount).Append('"');
                }
                else if (character == '%')
                {
                    commandLine.Append(EmptyPercentExpansion);
                }

                backslashCount = 0;
            }

            commandLine.Append(character);
        }

        if (quote)
        {
            commandLine.Append('\\', backslashCount).Append('"');
        }
    }

    private static void AppendEscapingPercent(StringBuilder commandLine, string value)
    {
        foreach (char character in value)
        {
            if (character == '%')
            {
                commandLine.Append(EmptyPercentExpansion);
            }

            commandLine.Append(character);
        }
    }

    private static bool NeedsQuotes(char character)
    {
        return char.IsControl(character)
            || (char.IsAscii(character) && !char.IsAsciiLetterOrDigit(character) && !UnquotedSymbols.Contains(character, StringComparison.Ordinal));
    }
}
