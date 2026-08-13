using System.ComponentModel.DataAnnotations;
using YamlDotNet.Serialization;

namespace RunningPlan.Cli.Domain;

public sealed class TrainingPlan
{
    [Required]
    public PlanMeta Meta { get; init; } = new();

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
    public string TimeZone { get; init; } = "Europe/Moscow";

    [YamlMember(Alias = "start_time_local")]
    [RegularExpression("^([01]\\d|2[0-3]):[0-5]\\d$")]
    public string StartTimeLocal { get; init; } = "00:00";

    [YamlMember(Alias = "default_targets")]
    public DefaultTargets DefaultTargets { get; init; } = new();

    [YamlMember(Alias = "hr_profile")]
    public HeartRateProfile HrProfile { get; init; } = new();
}

public sealed class DefaultTargets
{
    public HeartRateRange EasyHr { get; init; } = new() { Min = 125, Max = 145 };
    public HeartRateRange SteadyHr { get; init; } = new() { Min = 145, Max = 160 };
    public HeartRateRange TempoHr { get; init; } = new() { Min = 165, Max = 175 };
}

public sealed class HeartRateProfile
{
    public int Threshold { get; init; } = 181;
    public int Max { get; init; } = 199;
    public int HrrcMin { get; init; } = 181;
    public HeartRateZones Zones { get; init; } = new();
}

public sealed class HeartRateZones
{
    public HeartRateRange Z1 { get; init; } = new() { Min = 0, Max = 152 };
    public HeartRateRange Z2 { get; init; } = new() { Min = 153, Max = 161 };
    public HeartRateRange Z3 { get; init; } = new() { Min = 162, Max = 170 };
    public HeartRateRange Z4 { get; init; } = new() { Min = 171, Max = 180 };
    public HeartRateRange Z5 { get; init; } = new() { Min = 181, Max = 185 };
    public HeartRateRange Z6 { get; init; } = new() { Min = 186, Max = 190 };
    public HeartRateRange Z7 { get; init; } = new() { Min = 191, Max = 199 };
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
    public WeekDay Day { get; init; }

    [Required]
    public string Type { get; init; } = "Run";

    [Required]
    public string Category { get; init; } = "WORKOUT";

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