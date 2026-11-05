using System.Diagnostics;
using System.Globalization;

namespace MysterNybble.Runx;

// Runner-owned progress stays on stderr; child processes keep their inherited console streams.
internal sealed class TaskRunSummary(IReadOnlyList<ProcessRequest> requests)
{
    private readonly long _started = Stopwatch.GetTimestamp();
    private readonly List<(ProcessResult Result, TimeSpan Elapsed)> _results = [];

    public void Add(ProcessResult result, TimeSpan elapsed) => _results.Add((result, elapsed));

    public void Write(TextWriter error)
    {
        TimeSpan total = Stopwatch.GetElapsedTime(_started);
        error.WriteLine();
        error.WriteLine("Summary:");
        for (int i = 0; i < requests.Count; i++)
        {
            string status = "SKIPPED";
            string duration = "-";
            if (i < _results.Count)
            {
                (ProcessResult result, TimeSpan elapsed) = _results[i];
                status = result.Interrupted ? "INTERRUPTED"
                    : result.ExitCode == 0 ? "OK" : $"FAILED (exit code {result.ExitCode})";
                duration = FormatDuration(elapsed);
            }

            error.WriteLine($"  {i + 1}/{requests.Count} {requests[i].TaskName}  {duration}  {status}");
        }

        error.WriteLine($"Total: {FormatDuration(total)}");
    }

    private static string FormatDuration(TimeSpan elapsed)
    {
        if (elapsed.TotalHours >= 1)
        {
            return FormattableString.Invariant($"{(long)elapsed.TotalHours}h{elapsed.Minutes:00}m{elapsed.Seconds:00}s");
        }

        if (elapsed.TotalMinutes >= 1)
        {
            return FormattableString.Invariant($"{(int)elapsed.TotalMinutes}m{elapsed.Seconds:00}s");
        }

        return elapsed.TotalSeconds >= 1
            ? elapsed.TotalSeconds.ToString("0.0's'", CultureInfo.InvariantCulture)
            : elapsed.TotalMilliseconds.ToString("0'ms'", CultureInfo.InvariantCulture);
    }
}
