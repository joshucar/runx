namespace MysterNybble.Runx;

internal static class TaskPlanner
{
    // Dependencies run first, in the order they are listed, and each task runs at most once.
    // The config validator has already rejected unknown dependencies and cycles.
    public static IReadOnlyList<string> GetExecutionOrder(IReadOnlyDictionary<string, RunxTask> tasks, string taskName)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        ArgumentException.ThrowIfNullOrWhiteSpace(taskName);

        List<string> order = [];
        HashSet<string> visited = new(StringComparer.Ordinal);
        Visit(taskName);
        return order;

        void Visit(string name)
        {
            if (!visited.Add(name))
            {
                return;
            }

            foreach (string? dependency in tasks[name].DependsOn ?? [])
            {
                if (dependency is not null)
                {
                    Visit(dependency);
                }
            }

            order.Add(name);
        }
    }
}
