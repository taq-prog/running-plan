using System.ComponentModel.DataAnnotations;
using YamlDotNet.Serialization;

namespace RunningPlan.Cli.Domain;

public sealed class TrainingPlan
{
    public PlanMeta Meta { get; init; } = null!;

    [Required]
    [MinLength(1)]
    public List<TrainingWeek> Weeks { get; init; } = [];
}

public sealed class PlanMeta
{
    [Required]
    [MinLength(3)]
    public string Name { get; init; } = string.Empty;

    [Required]
    [YamlMember(Alias = "start_date")]
    public DateOnly StartDate { get; init; }

    [Required]
    [YamlMember(Alias = "timezone")]
    public string TimeZone { get; init; } = string.Empty;

    [YamlMember(Alias = "start_time_local")]
    [RegularExpression("^([01]\\d|2[0-3]):[0-5]\\d$")]
    public string StartTimeLocal { get; init; } = "00:00";

    [YamlMember(Alias = "default_targets")]
    public DefaultTargets DefaultTargets { get; init; } = null!;

    [YamlMember(Alias = "hr_profile")]
    public HeartRateProfile HrProfile { get; init; } = null!;
}

public sealed class DefaultTargets
{
    public HeartRateRange EasyHr { get; init; } = null!;
    public HeartRateRange SteadyHr { get; init; } = null!;
    public HeartRateRange TempoHr { get; init; } = null!;
}

public sealed class HeartRateProfile
{
    [Range(50, 230)]
    public int Threshold { get; init; }

    [Range(50, 230)]
    public int Max { get; init; }

    [Range(50, 230)]
    public int HrrcMin { get; init; }

    public HeartRateZones Zones { get; init; } = null!;
}

public sealed class HeartRateZones
{
    public HeartRateRange Z1 { get; init; } = null!;
    public HeartRateRange Z2 { get; init; } = null!;
    public HeartRateRange Z3 { get; init; } = null!;
    public HeartRateRange Z4 { get; init; } = null!;
    public HeartRateRange Z5 { get; init; } = null!;
    public HeartRateRange Z6 { get; init; } = null!;
    public HeartRateRange Z7 { get; init; } = null!;
}

public sealed class TrainingWeek
{
    [Range(1, 53)]
    public int Number { get; init; }

    [Required]
    [MinLength(1)]
    public List<PlannedWorkout> Workouts { get; init; } = [];
}

public sealed class PlannedWorkout
{
    [Required]
    [RegularExpression("^[a-z0-9_-]+$")]
    public string Id { get; init; } = string.Empty;

    [Required]
    [MinLength(3)]
    public string Name { get; init; } = string.Empty;

    [Required]
    public WeekDay? Day { get; init; }

    [Required]
    public string Type { get; init; } = string.Empty;

    [Required]
    public string Category { get; init; } = string.Empty;

    [Range(1, int.MaxValue)]
    public int? DistanceKm { get; init; }

    [Range(1, int.MaxValue)]
    public int? DurationMin { get; init; }

    [YamlMember(Alias = "duration_sec")]
    [Range(1, int.MaxValue)]
    public int? DurationSec { get; init; }

    public HeartRateRange? TargetHr { get; init; }

    public List<WorkoutStep> Steps { get; init; } = [];

    public List<string> Tags { get; init; } = [];
}

public sealed class WorkoutStep
{
    public WorkoutStepKind Kind { get; init; } = WorkoutStepKind.Unknown;

    [Range(1, int.MaxValue)]
    public int? DistanceKm { get; init; }

    [Range(1, int.MaxValue)]
    public int? DurationMin { get; init; }

    [YamlMember(Alias = "duration_sec")]
    [Range(1, int.MaxValue)]
    public int? DurationSec { get; init; }

    [Range(1, int.MaxValue)]
    public int? Repeats { get; init; }

    public HeartRateRange? TargetHr { get; init; }

    public List<WorkoutStep> Steps { get; init; } = [];

    public string? Note { get; init; }
}

public sealed class HeartRateRange
{
    [Range(0, 230)]
    public int Min { get; init; }

    [Range(50, 230)]
    public int Max { get; init; }
}

public enum WorkoutStepKind
{
    Unknown = 0,
    Easy,
    Warmup,
    Stride,
    Tempo,
    Steady,
    Moderate,
    Marathon,
    Recovery,
    Cooldown,
    Repeat
}