using RunningPlan.Cli.Config;
using RunningPlan.Cli.Domain;
using RunningPlan.Cli.Intervals;
using Xunit;

namespace RunningPlan.Cli.Tests;

public sealed class PlanToIntervalsMapperTests
{
    [Fact]
    public void Map_ComputesDateFromStartDateAndWeekOffset()
    {
        var plan = PlanLoader.Load(TestPaths.PlanPath);

        var mapped = PlanToIntervalsMapper.Map(plan);
        var firstWorkout = Assert.Single(mapped, x => x.Uid == "rp-w01-w01-tu-easy");

        Assert.Equal(new DateOnly(2026, 8, 11), firstWorkout.Date);
        Assert.Equal(new DateTime(2026, 8, 11, 4, 30, 0), firstWorkout.StartDateLocal);
        Assert.Equal(DateTimeKind.Unspecified, firstWorkout.StartDateLocal.Kind);
    }

    [Fact]
    public void Map_OverrideStartTimeChangesLocalDateTimeOnly()
    {
        var plan = PlanLoader.Load(TestPaths.PlanPath);

        var mapped = PlanToIntervalsMapper.Map(plan, "05:15");
        var workout = Assert.Single(mapped, x => x.Uid == "rp-w01-w01-tu-easy");

        Assert.Equal(new DateOnly(2026, 8, 11), workout.Date);
        Assert.Equal(new DateTime(2026, 8, 11, 5, 15, 0), workout.StartDateLocal);
    }

    [Fact]
    public void Map_UsesDurationSecondsForMovingTime()
    {
        var plan = new TrainingPlan
        {
            Meta = new(),
            Weeks =
            [
                new TrainingWeek
                {
                    Number = 1,
                    Workouts =
                    [
                        new PlannedWorkout
                        {
                            Id = "sec-only",
                            Name = "Seconds only",
                            Day = WeekDay.Tuesday,
                            Type = "Run",
                            Category = "WORKOUT",
                            DurationSec = 20
                        },
                        new PlannedWorkout
                        {
                            Id = "min-sec",
                            Name = "Minutes and seconds",
                            Day = WeekDay.Thursday,
                            Type = "Run",
                            Category = "WORKOUT",
                            DurationMin = 1,
                            DurationSec = 20
                        }
                    ]
                }
            ]
        };

        var mapped = PlanToIntervalsMapper.Map(plan);

        var secOnly = Assert.Single(mapped, x => x.Uid == "rp-w01-sec-only");
        var minSec = Assert.Single(mapped, x => x.Uid == "rp-w01-min-sec");

        Assert.Equal(20, secOnly.MovingTimeSeconds);
        Assert.Equal(80, minSec.MovingTimeSeconds);
    }

    [Fact]
    public void Map_BuildsStridesWithSecondDurations()
    {
        var plan = PlanLoader.Load(TestPaths.PlanPath);

        var mapped = PlanToIntervalsMapper.Map(plan);
        var workout = Assert.Single(mapped, x => x.Uid == "rp-w05-w05-tu-easy-strides");

        Assert.Contains("20s", workout.Description, StringComparison.Ordinal);
        Assert.Contains("40s", workout.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void Map_AppliesActiveIntensityForProgressiveSteps()
    {
        var plan = PlanLoader.Load(TestPaths.PlanPath);

        var mapped = PlanToIntervalsMapper.Map(plan);
        var workout = Assert.Single(mapped, x => x.Uid == "rp-w10-w10-su-progressive");

        Assert.Contains("1km 145-155 HR intensity=active", workout.Description, StringComparison.Ordinal);
        Assert.Contains("1km 155-165 HR intensity=active", workout.Description, StringComparison.Ordinal);
        Assert.Contains("1km 160-170 HR intensity=active", workout.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void Map_BuildsRepeatStructureForQualityWorkout()
    {
        var plan = PlanLoader.Load(TestPaths.PlanPath);

        var mapped = PlanToIntervalsMapper.Map(plan);
        var workout = Assert.Single(mapped, x => x.Uid == "rp-w11-w11-tu-quality");

        Assert.Contains("3x", workout.Description, StringComparison.Ordinal);
        Assert.Contains("8m 165-175 HR intensity=active", workout.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void Map_BuildsTempoAndRecoveryForW07()
    {
        var plan = PlanLoader.Load(TestPaths.PlanPath);

        var mapped = PlanToIntervalsMapper.Map(plan);
        var workout = Assert.Single(mapped, x => x.Uid == "rp-w07-w07-tu-tempo");

        Assert.Contains("6m 165-175 HR intensity=active", workout.Description, StringComparison.Ordinal);
        Assert.Contains("2m 125-145 HR intensity=recovery", workout.Description, StringComparison.Ordinal);
        Assert.Contains("1km 125-140 HR intensity=cooldown", workout.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void Map_BuildsProgressiveIntensityForW12()
    {
        var plan = PlanLoader.Load(TestPaths.PlanPath);

        var mapped = PlanToIntervalsMapper.Map(plan);
        var workout = Assert.Single(mapped, x => x.Uid == "rp-w12-w12-su-progressive");

        Assert.Contains("3km 145-155 HR intensity=active", workout.Description, StringComparison.Ordinal);
        Assert.Contains("2km 145-155 HR intensity=active", workout.Description, StringComparison.Ordinal);
        Assert.Contains("2km 160-170 HR intensity=active", workout.Description, StringComparison.Ordinal);
    }
}
