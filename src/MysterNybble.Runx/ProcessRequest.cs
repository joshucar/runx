namespace MysterNybble.Runx;

internal sealed record ProcessRequest(
    string TaskName,
    string ConfigPath,
    string Mode,
    string FileName,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    IReadOnlyDictionary<string, string> Environment);

internal sealed record ProcessRequestBuildResult(ProcessRequest? Request, IReadOnlyList<string> Errors)
{
    public static ProcessRequestBuildResult Failed(string error)
    {
        return new ProcessRequestBuildResult(Request: null, [error]);
    }
}
