namespace RunningPlan.Cli.Intervals;

public sealed class VerificationReport
{
    public required DateOnly OldestDate { get; init; }
    public required DateOnly NewestDate { get; init; }
    public required int ExpectedCount { get; init; }
    public required int FoundCount { get; init; }
    public required IReadOnlyList<string> MissingExternalIds { get; init; }
    public required IReadOnlyList<string> DateMismatches { get; init; }
    public bool Success => MissingExternalIds.Count == 0 && DateMismatches.Count == 0;
}
