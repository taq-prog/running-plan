namespace RunningPlan.Cli.Intervals;

public sealed class IntervalsOptions
{
    public required string AthleteId { get; init; }
    public required string ApiKey { get; init; }
    public string BaseUrl { get; init; } = "https://intervals.icu";
    public bool DryRun { get; init; }
    public bool StructuredOnly { get; init; }
}
