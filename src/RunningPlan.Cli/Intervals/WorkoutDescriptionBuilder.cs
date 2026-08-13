using System.Text;
using RunningPlan.Cli.Domain;

namespace RunningPlan.Cli.Intervals;

public static class WorkoutDescriptionBuilder
{
    public static string Build(PlannedWorkout workout)
    {
        var builder = new StringBuilder();
        if (workout.Steps.Count == 0)
        {
            AppendStep(builder, workout.DistanceKm, workout.DurationMin, workout.DurationSec, workout.TargetHr, null, null);
            return builder.ToString().TrimEnd();
        }

        AppendSteps(builder, workout.Steps);
        return builder.ToString().TrimEnd();
    }

    private static void AppendSteps(StringBuilder builder, IReadOnlyList<WorkoutStep> steps)
    {
        foreach (var step in steps)
        {
            if (step.Kind == WorkoutStepKind.Repeat)
            {
                builder.AppendLine($"{step.Repeats}x");
                AppendSteps(builder, step.Steps);
                builder.AppendLine();
                continue;
            }

            AppendStep(builder, step.DistanceKm, step.DurationMin, step.DurationSec, step.TargetHr, step.Note, step.Kind);
        }
    }

    private static void AppendStep(StringBuilder builder, decimal? distanceKm, int? durationMin, int? durationSec, HeartRateRange? target, string? note, WorkoutStepKind? kind)
    {
        var measurement = distanceKm.HasValue
            ? $"{distanceKm.Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}km"
            : durationSec.HasValue
                ? $"{durationSec.Value}s"
                : $"{durationMin ?? 0}m";
        var targetText = target is null ? string.Empty : $" {target.Min}-{target.Max} HR";
        var cue = string.IsNullOrWhiteSpace(note) ? string.Empty : $"{note.Trim()} ";
        var intensity = kind switch
        {
            WorkoutStepKind.Warmup => " intensity=warmup",
            WorkoutStepKind.Stride or WorkoutStepKind.Tempo or WorkoutStepKind.Steady or WorkoutStepKind.Moderate or WorkoutStepKind.Marathon => " intensity=active",
            WorkoutStepKind.Recovery => " intensity=recovery",
            WorkoutStepKind.Cooldown => " intensity=cooldown",
            _ => string.Empty
        };
        builder.AppendLine($"- {cue}{measurement}{targetText}{intensity}");
    }
}
