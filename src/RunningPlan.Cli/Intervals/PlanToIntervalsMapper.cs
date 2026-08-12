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
                var description = BuildDescription(workout);
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
                    Tags = workout.Tags
                });
            }
        }

        return mapped.OrderBy(x => x.Date).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static DateOnly ComputeDate(DateOnly startDate, int weekNumber, WeekDay day)
    {
        var baseDate = startDate.AddDays((weekNumber - 1) * 7);
        var requestedDay = day switch
        {
            WeekDay.Monday => DayOfWeek.Monday,
            WeekDay.Tuesday => DayOfWeek.Tuesday,
            WeekDay.Wednesday => DayOfWeek.Wednesday,
            WeekDay.Thursday => DayOfWeek.Thursday,
            WeekDay.Friday => DayOfWeek.Friday,
            WeekDay.Saturday => DayOfWeek.Saturday,
            WeekDay.Sunday => DayOfWeek.Sunday,
            _ => throw new ArgumentOutOfRangeException(nameof(day), day, null)
        };

        var offset = ((int)requestedDay - (int)baseDate.DayOfWeek + 7) % 7;
        return baseDate.AddDays(offset);
    }

    private static string BuildDescription(PlannedWorkout workout)
    {
        var sb = new StringBuilder();
        if (workout.Steps.Count == 0)
        {
            AppendBuilderStep(sb, workout.DistanceKm, workout.DurationMin, workout.TargetHr);
            return sb.ToString().TrimEnd();
        }

        AppendBuilderSteps(sb, workout.Steps);
        return sb.ToString().TrimEnd();
    }

    private static void AppendBuilderSteps(StringBuilder sb, IReadOnlyList<WorkoutStep> steps)
    {
        foreach (var step in steps)
        {
            if (step.Kind.Equals("repeat", StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine($"{step.Repeats}x");
                AppendBuilderSteps(sb, step.Steps);
                sb.AppendLine();
                continue;
            }

            AppendBuilderStep(sb, step.DistanceKm, step.DurationMin, step.TargetHr);
        }
    }

    private static void AppendBuilderStep(StringBuilder sb, int? distanceKm, int? durationMin, HeartRateRange? target)
    {
        var measurement = distanceKm.HasValue ? $"{distanceKm.Value}km" : $"{durationMin ?? 0}m";
        var targetText = target is null ? string.Empty : $" {target.Min}-{target.Max} HR";
        sb.AppendLine($"- {measurement}{targetText}");
    }
}
