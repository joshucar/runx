namespace MysterNybble.Runx;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        return await RunxCli.RunAsync(args, Console.Out, Console.Error, CancellationToken.None);
    }
}
