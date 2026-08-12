using System.ComponentModel.DataAnnotations;
using RunningPlan.Cli.Domain;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace RunningPlan.Cli.Config;

public static class PlanLoader
{
    public static TrainingPlan Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Plan file was not found: {path}");
        }

        var yaml = File.ReadAllText(path);

        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        var plan = deserializer.Deserialize<TrainingPlan>(yaml)
            ?? throw new InvalidOperationException("Could not parse training plan YAML.");

        Validate(plan);

        return plan;
    }

    private static void Validate(TrainingPlan plan)
    {
        var results = new List<ValidationResult>();
        var context = new ValidationContext(plan);
        Validator.TryValidateObject(plan, context, results, validateAllProperties: true);
        Validator.TryValidateObject(plan.Meta, new ValidationContext(plan.Meta), results, validateAllProperties: true);

        var weekNumbers = new HashSet<int>();
        foreach (var week in plan.Weeks)
        {
            Validator.TryValidateObject(week, new ValidationContext(week), results, true);
            if (!weekNumbers.Add(week.Number))
            {
                results.Add(new ValidationResult($"Duplicate week number: {week.Number}"));
            }

            var workoutIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var workout in week.Workouts)
            {
                Validator.TryValidateObject(workout, new ValidationContext(workout), results, true);

                if (!workoutIds.Add(workout.Id))
                {
                    results.Add(new ValidationResult($"Duplicate workout id in week {week.Number}: {workout.Id}"));
                }

                if (workout.DistanceKm is null && workout.DurationMin is null && workout.DurationSec is null && workout.Steps.Count == 0)
                {
                    results.Add(new ValidationResult($"Workout {workout.Id} must define distance_km, duration_min, duration_sec, or steps."));
                }

                ValidateWorkoutDistance(week.Number, workout, results);

                ValidateStepTree(week.Number, workout.Id, workout.Steps, results);
            }
        }

        if (results.Count > 0)
        {
            var message = string.Join(Environment.NewLine, results.Select(x => $"- {x.ErrorMessage}"));
            throw new ValidationException($"Plan validation failed:{Environment.NewLine}{message}");
        }
    }

    private static void ValidateWorkoutDistance(int weekNumber, PlannedWorkout workout, List<ValidationResult> results)
    {
        if (!workout.DistanceKm.HasValue || workout.Steps.Count == 0)
        {
            return;
        }

        var explicitDistanceKm = SumDistanceKm(workout.Steps, out var hasTimeBasedStep);
        if (explicitDistanceKm > workout.DistanceKm.Value)
        {
            results.Add(new ValidationResult($"Week {weekNumber} workout {workout.Id}: structured distance {explicitDistanceKm} km exceeds declared distance {workout.DistanceKm.Value} km."));
            return;
        }

        if (explicitDistanceKm == workout.DistanceKm.Value || !hasTimeBasedStep)
        {
            return;
        }

        var lastStep = workout.Steps[^1];
        if (lastStep.DistanceKm is null)
        {
            results.Add(new ValidationResult($"Week {weekNumber} workout {workout.Id}: add a final distance step to account for the declared {workout.DistanceKm.Value} km after time-based steps."));
        }
    }

    private static int SumDistanceKm(IReadOnlyCollection<WorkoutStep> steps, out bool hasTimeBasedStep)
    {
        hasTimeBasedStep = false;
        var totalDistanceKm = 0;
        foreach (var step in steps)
        {
            if (step.DistanceKm.HasValue)
            {
                totalDistanceKm += step.DistanceKm.Value * Math.Max(step.Repeats ?? 1, 1);
            }

            if (step.DurationMin.HasValue || step.DurationSec.HasValue)
            {
                hasTimeBasedStep = true;
            }

            if (step.Steps.Count > 0)
            {
                totalDistanceKm += (step.Repeats ?? 1) * SumDistanceKm(step.Steps, out var nestedHasTimeBasedStep);
                hasTimeBasedStep |= nestedHasTimeBasedStep;
            }
        }

        return totalDistanceKm;
    }

    private static void ValidateStepTree(int weekNumber, string workoutId, IReadOnlyCollection<WorkoutStep> steps, List<ValidationResult> results)
    {
        foreach (var step in steps)
        {
            Validator.TryValidateObject(step, new ValidationContext(step), results, true);

            var hasMetric = step.DistanceKm.HasValue || step.DurationMin.HasValue || step.DurationSec.HasValue || step.Kind.Equals("repeat", StringComparison.OrdinalIgnoreCase);
            if (!hasMetric)
            {
                results.Add(new ValidationResult($"Week {weekNumber} workout {workoutId}: step '{step.Kind}' must define distance_km, duration_min, duration_sec, or be repeat."));
            }

            if (step.Kind.Equals("repeat", StringComparison.OrdinalIgnoreCase))
            {
                if (!step.Repeats.HasValue || step.Repeats.Value < 1)
                {
                    results.Add(new ValidationResult($"Week {weekNumber} workout {workoutId}: repeat step must have repeats >= 1."));
                }

                if (step.Steps.Count == 0)
                {
                    results.Add(new ValidationResult($"Week {weekNumber} workout {workoutId}: repeat step must include nested steps."));
                }
            }

            if (step.TargetHr is not null && step.TargetHr.Min > step.TargetHr.Max)
            {
                results.Add(new ValidationResult($"Week {weekNumber} workout {workoutId}: step HR min cannot be above max."));
            }

            if (step.Steps.Count > 0)
            {
                ValidateStepTree(weekNumber, workoutId, step.Steps, results);
            }
        }
    }
}
