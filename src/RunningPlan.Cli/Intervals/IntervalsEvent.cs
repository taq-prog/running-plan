namespace RunningPlan.Cli.Intervals;

public sealed class IntervalsEvent
{
    public required string Uid { get; init; }
    public required string ExternalId { get; init; }
    public required DateOnly Date { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string Type { get; init; }
    public required string Category { get; init; }
    public int? DistanceMeters { get; init; }
    public int? MovingTimeSeconds { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
}
