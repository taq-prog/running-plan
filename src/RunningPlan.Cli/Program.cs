using System.Text.Json;
using RunningPlan.Cli.Config;
using RunningPlan.Cli.Intervals;

LoadDotEnv();

var jsonErrorOutput = false;

if (args.Length < 2)
{
    PrintUsage();
    return 1;
}

var command = args[0].Trim().ToLowerInvariant();
var planPath = args[1].Trim();

try
{
    switch (command)
    {
        case "validate":
            var validatePlan = PlanLoader.Load(planPath);
            Console.WriteLine($"OK: plan is valid ({validatePlan.Weeks.Count} weeks).\nPath: {Path.GetFullPath(planPath)}");
            return 0;

        case "sync":
            var syncOptions = ParseSyncOptions(args);
            jsonErrorOutput = syncOptions.JsonOutput;
            var syncPlan = PlanLoader.Load(planPath);
            var mappedEvents = PlanToIntervalsMapper.Map(syncPlan, syncOptions.StructuredOnly);

            if (!syncOptions.JsonOutput)
            {
                PrintPreview(mappedEvents);
            }

            using (var client = new HttpClient())
            {
                var intervalsClient = new IntervalsClient(client, syncOptions);
                var report = await intervalsClient.UpsertEventsAsync(mappedEvents, CancellationToken.None);

                if (syncOptions.JsonOutput)
                {
                    Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
                }
            }

            if (!syncOptions.JsonOutput)
            {
                Console.WriteLine(syncOptions.DryRun
                    ? "Dry-run completed."
                    : "Sync completed.");
            }

            return 0;

        case "verify":
            var verifyOptions = ParseSyncOptions(args);
            jsonErrorOutput = verifyOptions.JsonOutput;
            var verifyPlan = PlanLoader.Load(planPath);
            var verifyEvents = PlanToIntervalsMapper.Map(verifyPlan, verifyOptions.StructuredOnly);

            if (!verifyOptions.JsonOutput)
            {
                PrintPreview(verifyEvents);
            }

            using (var client = new HttpClient())
            {
                var intervalsClient = new IntervalsClient(client, verifyOptions);
                var report = await intervalsClient.VerifyEventsAsync(verifyEvents, verifyOptions.UseApplyPlan, CancellationToken.None);

                if (verifyOptions.JsonOutput)
                {
                    Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
                }

                if (!report.Success)
                {
                    return 1;
                }
            }

            if (!verifyOptions.JsonOutput)
            {
                Console.WriteLine("Verification completed.");
            }

            return 0;

        case "cleanup":
            var cleanupOptions = ParseSyncOptions(args);
            jsonErrorOutput = cleanupOptions.JsonOutput;
            var cleanupPlan = PlanLoader.Load(planPath);
            var cleanupEvents = PlanToIntervalsMapper.Map(cleanupPlan, cleanupOptions.StructuredOnly);

            using (var client = new HttpClient())
            {
                var intervalsClient = new IntervalsClient(client, cleanupOptions);
                var report = await intervalsClient.CleanupPlanEventsAsync(cleanupEvents, CancellationToken.None);

                if (cleanupOptions.JsonOutput)
                {
                    Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
                }
                else
                {
                    Console.WriteLine(cleanupOptions.DryRun
                        ? $"[DRY-RUN cleanup] Plan '{report.PlanName}' candidates={report.CandidateCount} in range {report.OldestDate:yyyy-MM-dd}..{report.NewestDate:yyyy-MM-dd}"
                        : $"Cleanup completed. Plan '{report.PlanName}' deleted={report.DeletedCount} candidates={report.CandidateCount} in range {report.OldestDate:yyyy-MM-dd}..{report.NewestDate:yyyy-MM-dd}");
                }
            }

            return 0;

        default:
            Console.Error.WriteLine($"Unknown command: {command}");
            PrintUsage();
            return 2;
    }
}
catch (Exception ex)
{
    if (jsonErrorOutput)
    {
        if (ex is VerificationFailedException verificationError)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                success = false,
                error = ex.Message,
                verification = verificationError.Report
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        else
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                success = false,
                error = ex.Message
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
    else
    {
        Console.Error.WriteLine(ex.Message);
    }

    return 1;
}

static void PrintPreview(IReadOnlyList<IntervalsEvent> events)
{
    Console.WriteLine($"Planned events: {events.Count}");
    foreach (var item in events)
    {
        Console.WriteLine($"- {item.Date:yyyy-MM-dd} | {item.Name}");
    }

    Console.WriteLine();
}

static IntervalsOptions ParseSyncOptions(IReadOnlyList<string> args)
{
    var options = ParseOptions(args);

    var athleteId = GetOption(options, "athlete-id")
        ?? Environment.GetEnvironmentVariable("INTERVALS_ATHLETE_ID");
    var apiKey = GetOption(options, "api-key")
        ?? Environment.GetEnvironmentVariable("INTERVALS_API_KEY");
    var baseUrl = GetOption(options, "base-url")
        ?? Environment.GetEnvironmentVariable("INTERVALS_BASE_URL")
        ?? "https://intervals.icu";
    var dryRun = options.ContainsKey("dry-run");
    var structuredOnly = options.ContainsKey("structured-only");
    var useApplyPlan = options.ContainsKey("apply-plan");
    var createPlanOnMissing = options.ContainsKey("create-plan-on-missing");
    var cleanupPlanBeforeApply = options.ContainsKey("cleanup-plan-before-apply");
    var planName = GetOption(options, "plan-name") ?? "Running Plan Auto";
    var noVerify = options.ContainsKey("no-verify");
    var jsonOutput = options.ContainsKey("json");
    var folderIdRaw = GetOption(options, "folder-id");
    var folderId = 0;

    if (!string.IsNullOrWhiteSpace(folderIdRaw) && !int.TryParse(folderIdRaw, out folderId))
    {
        throw new ArgumentException("--folder-id must be an integer.");
    }

    if (string.IsNullOrWhiteSpace(athleteId))
    {
        throw new ArgumentException("Missing --athlete-id (or INTERVALS_ATHLETE_ID).");
    }

    if (string.IsNullOrWhiteSpace(apiKey))
    {
        throw new ArgumentException("Missing --api-key (or INTERVALS_API_KEY).");
    }

    return new IntervalsOptions
    {
        AthleteId = athleteId,
        ApiKey = apiKey,
        BaseUrl = baseUrl,
        DryRun = dryRun,
        StructuredOnly = structuredOnly,
        UseApplyPlan = useApplyPlan,
        FolderId = folderId,
        CreatePlanOnMissing = createPlanOnMissing,
        PlanName = planName,
        CleanupPlanBeforeApply = cleanupPlanBeforeApply,
        VerifyAfterSync = !noVerify,
        JsonOutput = jsonOutput
    };
}

static Dictionary<string, string?> ParseOptions(IReadOnlyList<string> args)
{
    var options = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

    for (var i = 2; i < args.Count; i++)
    {
        var token = args[i];
        if (!token.StartsWith("--", StringComparison.Ordinal))
        {
            continue;
        }

        var key = token[2..];
        if (i + 1 < args.Count && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
        {
            options[key] = args[i + 1];
            i++;
        }
        else
        {
            options[key] = null;
        }
    }

    return options;
}

static string? GetOption(IReadOnlyDictionary<string, string?> options, string key)
    => options.TryGetValue(key, out var value) ? value : null;

static void PrintUsage()
{
    Console.WriteLine("Usage:");
    Console.WriteLine("  running-plan validate <plan.yaml>");
    Console.WriteLine("  running-plan sync <plan.yaml> --athlete-id <id> --api-key <key> [--base-url https://intervals.icu] [--dry-run] [--structured-only] [--apply-plan] [--folder-id 0] [--create-plan-on-missing] [--plan-name \"Running Plan Auto\"] [--cleanup-plan-before-apply] [--no-verify] [--json]");
    Console.WriteLine("  running-plan verify <plan.yaml> --athlete-id <id> --api-key <key> [--base-url https://intervals.icu] [--structured-only] [--json]");
    Console.WriteLine("  running-plan cleanup <plan.yaml> --athlete-id <id> --api-key <key> [--base-url https://intervals.icu] [--plan-name \"Running Plan Auto\"] [--dry-run] [--json]");
    Console.WriteLine();
    Console.WriteLine("Environment variable fallback:");
    Console.WriteLine("  INTERVALS_ATHLETE_ID");
    Console.WriteLine("  INTERVALS_API_KEY");
    Console.WriteLine("  INTERVALS_BASE_URL");
    Console.WriteLine("  (.env is auto-loaded from current directory if present)");
}

static void LoadDotEnv()
{
    var path = Path.Combine(Environment.CurrentDirectory, ".env");
    if (!File.Exists(path))
    {
        return;
    }

    foreach (var rawLine in File.ReadAllLines(path))
    {
        var line = rawLine.Trim();
        if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
        {
            continue;
        }

        if (line.StartsWith("export ", StringComparison.OrdinalIgnoreCase))
        {
            line = line[7..].Trim();
        }

        var separator = line.IndexOf('=');
        if (separator <= 0)
        {
            continue;
        }

        var key = line[..separator].Trim();
        var value = line[(separator + 1)..].Trim();

        if (string.IsNullOrWhiteSpace(key))
        {
            continue;
        }

        if ((value.StartsWith('"') && value.EndsWith('"')) || (value.StartsWith('\'') && value.EndsWith('\'')))
        {
            value = value[1..^1];
        }

        // ponytail: do not override explicitly set shell/OS env vars.
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
        {
            Environment.SetEnvironmentVariable(key, value);
        }
    }
}
