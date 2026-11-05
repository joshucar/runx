namespace MysterNybble.Runx;

internal static class TaskPlanner
{
    // Dependencies run first, in the order they are listed, and each task runs at most once.
    // The config validator has already rejected unknown dependencies and cycles.
    public static TaskPlan Build(IReadOnlyDictionary<string, RunxTask> tasks, string taskName)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        ArgumentException.ThrowIfNullOrWhiteSpace(taskName);

        List<TaskDependency> order = [];
        List<string> errors = [];
        Dictionary<string, TaskDependency> visited = new(StringComparer.Ordinal);
        Visit(new TaskDependency(taskName));
        return new TaskPlan(order, errors);

        void Visit(TaskDependency invocation)
        {
            string name = invocation.Task;
            if (visited.TryGetValue(name, out TaskDependency? previous))
            {
                if (!previous.Args.SequenceEqual(invocation.Args, StringComparer.Ordinal))
                {
                    string error = $"Task '{taskName}' reaches '{name}' with conflicting dependency arguments. Each task can run only once per invocation.";
                    if (!errors.Contains(error, StringComparer.Ordinal))
                    {
                        errors.Add(error);
                    }
                }

                return;
            }

            visited.Add(name, invocation);
            foreach (TaskDependency? dependency in tasks[name].DependsOn ?? [])
            {
                if (dependency is not null)
                {
                    Visit(dependency);
                }
            }

            order.Add(invocation);
        }
    }
}

internal sealed record TaskPlan(IReadOnlyList<TaskDependency> Steps, IReadOnlyList<string> Errors);
