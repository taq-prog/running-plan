using System.ComponentModel.DataAnnotations;
using RunningPlan.Cli.Config;
using Xunit;

namespace RunningPlan.Cli.Tests;

public sealed class PlanLoaderTests
{
    [Fact]
    public void Load_ValidPlan_Succeeds()
    {
        var plan = PlanLoader.Load(TestPaths.PlanPath);

        Assert.Equal(12, plan.Weeks.Count);
        Assert.Equal(36, plan.Weeks.SelectMany(x => x.Workouts).Count());
    }

    [Fact]
    public void Load_RejectsUnknownYamlProperty()
    {
        var yaml = """
meta:
  name: "Unknown key"
  start_date: 2026-08-11
  timezone: "Asia/Almaty"
weeks:
  - number: 1
    workouts:
      - id: w01-tu-easy
        name: "W01 Tue Easy"
        day: Tuesday
        type: Run
        category: WORKOUT
        distance_km: 4
        target_hrr:
          min: 125
          max: 145
""";

        var path = WriteTempYaml(yaml);

        var error = Assert.ThrowsAny<Exception>(() => PlanLoader.Load(path));

        Assert.Contains("target_hrr", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Load_RejectsInvertedHeartRateRanges()
    {
        var yaml = """
meta:
  name: "Bad HR"
  start_date: 2026-08-11
  timezone: "Asia/Almaty"
  default_targets:
    easy_hr: { min: 150, max: 140 }
    steady_hr: { min: 145, max: 160 }
    tempo_hr: { min: 165, max: 175 }
  hr_profile:
    threshold: 181
    max: 199
    hrrc_min: 181
    zones:
      z1: { min: 0, max: 152 }
      z2: { min: 153, max: 161 }
      z3: { min: 162, max: 170 }
      z4: { min: 171, max: 180 }
      z5: { min: 181, max: 185 }
      z6: { min: 186, max: 190 }
      z7: { min: 199, max: 191 }
weeks:
  - number: 1
    workouts:
      - id: w01-tu-easy
        name: "W01 Tue Easy"
        day: Tuesday
        type: Run
        category: WORKOUT
        distance_km: 4
        target_hr: { min: 175, max: 160 }
""";

        var path = WriteTempYaml(yaml);

        var error = Assert.Throws<ValidationException>(() => PlanLoader.Load(path));

        Assert.Contains("Meta default_targets.easy_hr", error.Message, StringComparison.Ordinal);
        Assert.Contains("Meta hr_profile.zones.z7", error.Message, StringComparison.Ordinal);
        Assert.Contains("workout HR", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_RejectsWorkoutWithoutMetrics()
    {
        var yaml = """
meta:
  name: "Bad workout"
  start_date: 2026-08-11
  timezone: "Asia/Almaty"
weeks:
  - number: 1
    workouts:
      - id: w01-tu-easy
        name: "W01 Tue Easy"
        day: Tuesday
        type: Run
        category: WORKOUT
""";

        var path = WriteTempYaml(yaml);

        var error = Assert.Throws<ValidationException>(() => PlanLoader.Load(path));

        Assert.Contains("must define distance_km, duration_min, duration_sec, or steps", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Load_RejectsNonRepeatStepWithRepeats()
    {
        var yaml = """
meta:
  name: "Bad step"
  start_date: 2026-08-11
  timezone: "Asia/Almaty"
weeks:
  - number: 1
    workouts:
      - id: w01-tu-easy
        name: "W01 Tue Easy"
        day: Tuesday
        type: Run
        category: WORKOUT
        distance_km: 4
        steps:
          - kind: easy
            repeats: 2
            distance_km: 4
""";

        var path = WriteTempYaml(yaml);

        var error = Assert.Throws<ValidationException>(() => PlanLoader.Load(path));

        Assert.Contains("repeats is only allowed for repeat steps", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Load_RejectsStructuredDistanceThatExceedsDeclaredDistance()
    {
        var yaml = """
meta:
  name: "Distance overflow"
  start_date: 2026-08-11
  timezone: "Asia/Almaty"
weeks:
  - number: 1
    workouts:
      - id: w01-tu-easy
        name: "W01 Tue Easy"
        day: Tuesday
        type: Run
        category: WORKOUT
        distance_km: 5
        steps:
          - kind: easy
            distance_km: 3
          - kind: easy
            distance_km: 3
""";

        var path = WriteTempYaml(yaml);

        var error = Assert.Throws<ValidationException>(() => PlanLoader.Load(path));

        Assert.Contains("structured distance", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("exceeds declared distance", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Load_RejectsTimeBasedWorkoutWithoutFinalDistanceStep()
    {
        var yaml = """
meta:
  name: "Time based without final distance"
  start_date: 2026-08-11
  timezone: "Asia/Almaty"
weeks:
  - number: 1
    workouts:
      - id: w01-tu-tempo
        name: "W01 Tue Tempo"
        day: Tuesday
        type: Run
        category: WORKOUT
        distance_km: 6
        steps:
          - kind: warmup
            distance_km: 2
          - kind: repeat
            repeats: 3
            steps:
              - kind: tempo
                duration_min: 6
              - kind: recovery
                duration_min: 2
          - kind: cooldown
            duration_min: 8
""";

        var path = WriteTempYaml(yaml);

        var error = Assert.Throws<ValidationException>(() => PlanLoader.Load(path));

        Assert.Contains("add a final distance step", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static string WriteTempYaml(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"running-plan-test-{Guid.NewGuid():N}.yaml");
        File.WriteAllText(path, content);
        return path;
    }
}
