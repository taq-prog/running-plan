namespace RunningPlan.Cli.Intervals;

public sealed class IntervalsOptions
{
    public required string AthleteId { get; init; }
    public required string ApiKey { get; init; }
    public string BaseUrl { get; init; } = "https://intervals.icu";
    public bool DryRun { get; init; }
    public bool UseApplyPlan { get; init; }
    public int FolderId { get; init; }
    public string StartTimeLocal { get; init; } = "00:00";
    public bool CreatePlanOnMissing { get; init; }
    public string PlanName { get; init; } = "Running Plan Auto";
    public bool CleanupPlanBeforeApply { get; init; }
    public bool VerifyAfterSync { get; init; } = true;
    public bool JsonOutput { get; init; }
}
