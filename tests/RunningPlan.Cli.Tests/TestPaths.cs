namespace RunningPlan.Cli.Tests;

internal static class TestPaths
{
    public static string PlanPath => Path.Combine(RepositoryRoot, "plans", "plan-12-weeks.yaml");

    private static string RepositoryRoot => _repositoryRoot ??= FindRepositoryRoot();
    private static string? _repositoryRoot;

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            var solutionPath = Path.Combine(current.FullName, "RunningPlan.slnx");
            if (File.Exists(solutionPath))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root from test runtime directory.");
    }
}
