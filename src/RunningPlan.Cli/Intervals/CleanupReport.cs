namespace RunningPlan.Cli.Intervals;

public sealed class CleanupReport
{
    public required bool Success { get; init; }
    public required bool DryRun { get; init; }
    public required string PlanName { get; init; }
    public required DateOnly OldestDate { get; init; }
    public required DateOnly NewestDate { get; init; }
    public required int CandidateCount { get; init; }
    public required int DeletedCount { get; init; }
}
