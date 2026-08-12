using System.Text;
using RunningPlan.Cli.Domain;

namespace RunningPlan.Cli.Intervals;

public static class PlanToIntervalsMapper
{
    public static IReadOnlyList<IntervalsEvent> Map(TrainingPlan plan, bool structuredOnly)
    {
        var mapped = new List<IntervalsEvent>();

        foreach (var week in plan.Weeks.OrderBy(x => x.Number))
        {
            foreach (var workout in week.Workouts)
            {
                var date = ComputeDate(plan.Meta.StartDate, week.Number, workout.Day);
                var description = BuildDescription(week.Number, workout);
                var workoutDoc = structuredOnly && workout.Steps.Count == 0
                    ? null
                    : BuildWorkoutDoc(workout);
                mapped.Add(new IntervalsEvent
                {
                    Uid = $"rp-w{week.Number:D2}-{workout.Id}",
                    ExternalId = $"running-plan:w{week.Number:D2}:{workout.Id}",
                    Date = date,
                    Name = workout.Name,
                    Description = description,
                    Type = workout.Type,
                    Category = workout.Category,
                    DistanceMeters = workout.DistanceKm.HasValue ? workout.DistanceKm.Value * 1000 : null,
                    MovingTimeSeconds = workout.DurationMin.HasValue ? workout.DurationMin.Value * 60 : null,
                    WorkoutDoc = workoutDoc,
                    Tags = workout.Tags
                });
            }
        }

        return mapped.OrderBy(x => x.Date).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static DateOnly ComputeDate(DateOnly startMonday, int weekNumber, WeekDay day)
    {
        var baseDate = startMonday.AddDays((weekNumber - 1) * 7);
        return baseDate.AddDays(day switch
        {
            WeekDay.Monday => 0,
            WeekDay.Tuesday => 1,
            WeekDay.Wednesday => 2,
            WeekDay.Thursday => 3,
            WeekDay.Friday => 4,
            WeekDay.Saturday => 5,
            WeekDay.Sunday => 6,
            _ => throw new ArgumentOutOfRangeException(nameof(day), day, null)
        });
    }

    private static string BuildDescription(int weekNumber, PlannedWorkout workout)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Week {weekNumber} / {workout.Day}");

        if (workout.TargetHr is not null)
        {
            sb.AppendLine($"Target HR: {workout.TargetHr.Min}-{workout.TargetHr.Max} bpm");
        }

        if (workout.DistanceKm.HasValue)
        {
            sb.AppendLine($"Distance: {workout.DistanceKm.Value} km");
        }

        if (workout.DurationMin.HasValue)
        {
            sb.AppendLine($"Duration: {workout.DurationMin.Value} min");
        }

        if (workout.Steps.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Steps:");
            AppendSteps(sb, workout.Steps, 0);
        }

        return sb.ToString().TrimEnd();
    }

    private static IntervalsWorkoutDoc BuildWorkoutDoc(PlannedWorkout workout)
    {
        var doc = new IntervalsWorkoutDoc();

        if (workout.Steps.Count > 0)
        {
            doc.Steps.AddRange(workout.Steps.Select(MapStep));
            return doc;
        }

        var baseStep = new IntervalsWorkoutStepDoc
        {
            Kind = "main",
            DistanceKm = workout.DistanceKm,
            DurationMin = workout.DurationMin,
            TargetHr = MapTarget(workout.TargetHr),
            Note = "Auto-generated from simple workout"
        };

        doc.Steps.Add(baseStep);
        return doc;
    }

    private static IntervalsWorkoutStepDoc MapStep(WorkoutStep step)
        => new()
        {
            Kind = step.Kind,
            Repeats = step.Repeats,
            DistanceKm = step.DistanceKm,
            DurationMin = step.DurationMin,
            TargetHr = MapTarget(step.TargetHr),
            Note = step.Note,
            Steps = step.Steps.Select(MapStep).ToList()
        };

    private static IntervalsHeartRateTargetDoc? MapTarget(HeartRateRange? target)
        => target is null
            ? null
            : new IntervalsHeartRateTargetDoc
            {
                Min = target.Min,
                Max = target.Max
            };

    private static void AppendSteps(StringBuilder sb, IReadOnlyList<WorkoutStep> steps, int level)
    {
        var indent = new string(' ', level * 2);
        foreach (var step in steps)
        {
            if (step.Kind.Equals("repeat", StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine($"{indent}- Repeat x{step.Repeats}");
                AppendSteps(sb, step.Steps, level + 1);
                continue;
            }

            var metrics = new List<string>();
            if (step.DistanceKm.HasValue)
            {
                metrics.Add($"{step.DistanceKm.Value} km");
            }

            if (step.DurationMin.HasValue)
            {
                metrics.Add($"{step.DurationMin.Value} min");
            }

            if (step.TargetHr is not null)
            {
                metrics.Add($"HR {step.TargetHr.Min}-{step.TargetHr.Max}");
            }

            if (!string.IsNullOrWhiteSpace(step.Note))
            {
                metrics.Add(step.Note.Trim());
            }

            var summary = metrics.Count == 0 ? step.Kind : $"{step.Kind}: {string.Join(" | ", metrics)}";
            sb.AppendLine($"{indent}- {summary}");

            if (step.Steps.Count > 0)
            {
                AppendSteps(sb, step.Steps, level + 1);
            }
        }
    }
}
