namespace MysterNybble.Runx.Tests;

internal sealed class FakeTaskSelector : ITaskSelector
{
    public bool IsInteractive { get; init; }

    public string? Selection { get; init; }

    public int PromptCount { get; private set; }

    public Task<string?> SelectTaskAsync(LoadedRunxConfig config, CancellationToken cancellationToken)
    {
        PromptCount++;
        return Task.FromResult(Selection);
    }
}

internal sealed class RecordingProcessRunner : IProcessRunner
{
    public List<ProcessRequest> Requests { get; } = [];

    public int ExitCode { get; set; }

    public string? InterruptedTaskName { get; init; }

    public Task<ProcessResult> RunAsync(
        ProcessRequest request,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(new ProcessResult(ExitCode, request.TaskName == InterruptedTaskName));
    }
}

internal sealed class FakeGitClient : IGitClient
{
    private readonly Dictionary<string, string> _responses = new(StringComparer.Ordinal);

    public List<string> Calls { get; } = [];

    public FakeGitClient Returns(string arguments, string output)
    {
        _responses[arguments] = output;
        return this;
    }

    public Task<string?> RunAsync(string repoRoot, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        string key = string.Join(' ', arguments);
        Calls.Add(key);
        return Task.FromResult(_responses.GetValueOrDefault(key));
    }
}
