namespace RunningPlan.Cli.Intervals;

public sealed class IntervalsWorkoutDoc
{
    public string Schema { get; init; } = "running-plan.workout-doc.v1";
    public string Sport { get; init; } = "Run";
    public int Version { get; init; } = 1;
    public List<IntervalsWorkoutStepDoc> Steps { get; init; } = [];
}

public sealed class IntervalsWorkoutStepDoc
{
    public required string Kind { get; init; }
    public int? Repeats { get; init; }
    public int? DistanceKm { get; init; }
    public int? DurationMin { get; init; }
    public IntervalsHeartRateTargetDoc? TargetHr { get; init; }
    public string? Note { get; init; }
    public List<IntervalsWorkoutStepDoc> Steps { get; init; } = [];
}

public sealed class IntervalsHeartRateTargetDoc
{
    public required int Min { get; init; }
    public required int Max { get; init; }
}
