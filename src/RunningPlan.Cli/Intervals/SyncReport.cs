namespace RunningPlan.Cli.Intervals;

public sealed class SyncReport
{
    public required bool Success { get; init; }
    public required bool DryRun { get; init; }
    public required bool ApplyPlan { get; init; }
    public required int PlannedCount { get; init; }
    public required int SyncedCount { get; init; }
    public required bool VerificationAttempted { get; init; }
    public VerificationReport? Verification { get; init; }
}
