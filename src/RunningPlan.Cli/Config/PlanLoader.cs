using System.ComponentModel.DataAnnotations;
using RunningPlan.Cli.Domain;
using YamlDotNet.Core;
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
            .Build();

        TrainingPlan? plan;
        try
        {
            plan = deserializer.Deserialize<TrainingPlan>(yaml);
        }
        catch (YamlException exception)
        {
            throw new InvalidOperationException($"Could not parse training plan YAML: {exception.Message}", exception);
        }

        if (plan is null)
        {
            throw new InvalidOperationException("Could not parse training plan YAML.");
        }

        Validate(plan);

        return plan;
    }

    private static void Validate(TrainingPlan plan)
    {
        var results = new List<ValidationResult>();
        var context = new ValidationContext(plan);
        Validator.TryValidateObject(plan, context, results, validateAllProperties: true);
        if (plan.Meta is null)
        {
            results.Add(new ValidationResult("meta is required."));
        }
        else
        {
            Validator.TryValidateObject(plan.Meta, new ValidationContext(plan.Meta), results, validateAllProperties: true);
            ValidateMeta(plan.Meta, results);
        }

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

                ValidateHrRange($"Week {week.Number} workout {workout.Id}: workout HR", workout.TargetHr, results);
                ValidateHrMaximum($"Week {week.Number} workout {workout.Id}: workout HR", workout.TargetHr, plan.Meta?.HrProfile?.Max, results);

                ValidateWorkoutDistance(week.Number, workout, results);

                ValidateStepTree(week.Number, workout.Id, workout.Steps, plan.Meta?.HrProfile?.Max, results);
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

    private static void ValidateStepTree(int weekNumber, string workoutId, IReadOnlyCollection<WorkoutStep> steps, int? profileMaximum, List<ValidationResult> results)
    {
        foreach (var step in steps)
        {
            Validator.TryValidateObject(step, new ValidationContext(step), results, true);

            if (step.Kind == WorkoutStepKind.Unknown)
            {
                results.Add(new ValidationResult($"Week {weekNumber} workout {workoutId}: step kind is required."));
                continue;
            }

            var hasMetric = step.DistanceKm.HasValue || step.DurationMin.HasValue || step.DurationSec.HasValue || step.Kind == WorkoutStepKind.Repeat;
            if (!hasMetric)
            {
                results.Add(new ValidationResult($"Week {weekNumber} workout {workoutId}: step '{step.Kind}' must define distance_km, duration_min, duration_sec, or be repeat."));
            }

            if (step.Kind == WorkoutStepKind.Repeat)
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
            else if (step.Repeats.HasValue)
            {
                results.Add(new ValidationResult($"Week {weekNumber} workout {workoutId}: repeats is only allowed for repeat steps."));
            }

            ValidateHrRange($"Week {weekNumber} workout {workoutId}: step HR", step.TargetHr, results);
            ValidateHrMaximum($"Week {weekNumber} workout {workoutId}: step HR", step.TargetHr, profileMaximum, results);

            if (step.Steps.Count > 0)
            {
                ValidateStepTree(weekNumber, workoutId, step.Steps, profileMaximum, results);
            }
        }
    }

    private static void ValidateMeta(PlanMeta meta, List<ValidationResult> results)
    {
        if (meta.DefaultTargets is null)
        {
            results.Add(new ValidationResult("meta.default_targets is required."));
        }
        else
        {
            Validator.TryValidateObject(meta.DefaultTargets, new ValidationContext(meta.DefaultTargets), results, true);
            ValidateRequiredHrRange("Meta default_targets.easy_hr", meta.DefaultTargets.EasyHr, results);
            ValidateRequiredHrRange("Meta default_targets.steady_hr", meta.DefaultTargets.SteadyHr, results);
            ValidateRequiredHrRange("Meta default_targets.tempo_hr", meta.DefaultTargets.TempoHr, results);
        }

        if (meta.HrProfile is null)
        {
            results.Add(new ValidationResult("meta.hr_profile is required."));
            return;
        }

        Validator.TryValidateObject(meta.HrProfile, new ValidationContext(meta.HrProfile), results, true);
        if (meta.HrProfile.Zones is null)
        {
            results.Add(new ValidationResult("meta.hr_profile.zones is required."));
            return;
        }

        ValidateHeartRateProfile(meta.HrProfile, results);
        ValidateHrMaximum("Meta default_targets.easy_hr", meta.DefaultTargets?.EasyHr, meta.HrProfile.Max, results);
        ValidateHrMaximum("Meta default_targets.steady_hr", meta.DefaultTargets?.SteadyHr, meta.HrProfile.Max, results);
        ValidateHrMaximum("Meta default_targets.tempo_hr", meta.DefaultTargets?.TempoHr, meta.HrProfile.Max, results);
    }

    private static void ValidateHeartRateProfile(HeartRateProfile profile, List<ValidationResult> results)
    {
        ValidateRequiredHrRange("Meta hr_profile.zones.z1", profile.Zones.Z1, results);
        ValidateRequiredHrRange("Meta hr_profile.zones.z2", profile.Zones.Z2, results);
        ValidateRequiredHrRange("Meta hr_profile.zones.z3", profile.Zones.Z3, results);
        ValidateRequiredHrRange("Meta hr_profile.zones.z4", profile.Zones.Z4, results);
        ValidateRequiredHrRange("Meta hr_profile.zones.z5", profile.Zones.Z5, results);
        ValidateRequiredHrRange("Meta hr_profile.zones.z6", profile.Zones.Z6, results);
        ValidateRequiredHrRange("Meta hr_profile.zones.z7", profile.Zones.Z7, results);

        var zones = new[] { profile.Zones.Z1, profile.Zones.Z2, profile.Zones.Z3, profile.Zones.Z4, profile.Zones.Z5, profile.Zones.Z6, profile.Zones.Z7 };
        for (var index = 1; index < zones.Length; index++)
        {
            if (zones[index - 1] is not null && zones[index] is not null && zones[index - 1].Max >= zones[index].Min)
            {
                results.Add(new ValidationResult($"Meta hr_profile zones must be strictly ordered: z{index}.max must be below z{index + 1}.min."));
            }
        }

        if (profile.HrrcMin > profile.Threshold)
        {
            results.Add(new ValidationResult("Meta hr_profile.hrrc_min cannot be above threshold."));
        }

        if (profile.Threshold > profile.Max)
        {
            results.Add(new ValidationResult("Meta hr_profile.threshold cannot be above max."));
        }
    }

    private static void ValidateHrRange(string scope, HeartRateRange? range, List<ValidationResult> results)
    {
        if (range is null)
        {
            return;
        }

        if (range.Min > range.Max)
        {
            results.Add(new ValidationResult($"{scope}: min cannot be above max."));
        }
    }

    private static void ValidateRequiredHrRange(string scope, HeartRateRange? range, List<ValidationResult> results)
    {
        if (range is null)
        {
            results.Add(new ValidationResult($"{scope} is required."));
            return;
        }

        ValidateHrRange(scope, range, results);
    }

    private static void ValidateHrMaximum(string scope, HeartRateRange? range, int? maximum, List<ValidationResult> results)
    {
        if (range is not null && maximum.HasValue && range.Max > maximum.Value)
        {
            results.Add(new ValidationResult($"{scope}: max cannot be above hr_profile.max ({maximum.Value})."));
        }
    }
}
