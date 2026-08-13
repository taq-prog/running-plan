using RunningPlan.Cli.Domain;

namespace RunningPlan.Cli.Intervals;

public static class PlanToIntervalsMapper
{
    public static IReadOnlyList<IntervalsEvent> Map(TrainingPlan plan, string? startTimeLocalOverride = null)
    {
        var mapped = new List<IntervalsEvent>();
        var startTimeLocal = ParseStartTime(startTimeLocalOverride ?? plan.Meta.StartTimeLocal);

        foreach (var week in plan.Weeks.OrderBy(x => x.Number))
        {
            foreach (var workout in week.Workouts)
            {
                var date = ComputeDate(plan.Meta.StartDate, week.Number, workout.Day!.Value);
                var description = WorkoutDescriptionBuilder.Build(workout);
                mapped.Add(new IntervalsEvent
                {
                    Uid = $"rp-w{week.Number:D2}-{workout.Id}",
                    ExternalId = $"running-plan:w{week.Number:D2}:{workout.Id}",
                    Date = date,
                    StartDateLocal = date.ToDateTime(startTimeLocal),
                    Name = workout.Name,
                    Description = description,
                    Type = workout.Type,
                    Category = workout.Category,
                    DistanceMeters = workout.DistanceKm.HasValue ? workout.DistanceKm.Value * 1000 : null,
                    MovingTimeSeconds = ToMovingTimeSeconds(workout.DurationMin, workout.DurationSec),
                    Tags = workout.Tags
                });
            }
        }

        return mapped.OrderBy(x => x.Date).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static TimeOnly ParseStartTime(string value)
        => TimeOnly.TryParseExact(value, "HH:mm", out var parsed)
            ? parsed
            : throw new ArgumentException("Workout start time must be in HH:mm format.", nameof(value));

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

    private static int? ToMovingTimeSeconds(int? durationMin, int? durationSec)
    {
        if (!durationMin.HasValue && !durationSec.HasValue)
        {
            return null;
        }

        return (durationMin ?? 0) * 60 + (durationSec ?? 0);
    }
}
