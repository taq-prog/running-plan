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
            EnsureNoOptions(args);
            var validatePlan = PlanLoader.Load(planPath);
            Console.WriteLine($"OK: plan is valid ({validatePlan.Weeks.Count} weeks).\nPath: {Path.GetFullPath(planPath)}");
            return 0;

        case "sync":
            var syncPlan = PlanLoader.Load(planPath);
            var syncOptions = ParseSyncOptions(args, syncPlan.Meta.StartTimeLocal);
            jsonErrorOutput = syncOptions.JsonOutput;
            var mappedEvents = PlanToIntervalsMapper.Map(syncPlan);

            if (!syncOptions.JsonOutput)
            {
                PrintPreview(mappedEvents);
            }

            using (var client = CreateHttpClient())
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
            var verifyPlan = PlanLoader.Load(planPath);
            var verifyOptions = ParseSyncOptions(args, verifyPlan.Meta.StartTimeLocal);
            jsonErrorOutput = verifyOptions.JsonOutput;
            var verifyEvents = PlanToIntervalsMapper.Map(verifyPlan);

            if (!verifyOptions.JsonOutput)
            {
                PrintPreview(verifyEvents);
            }

            using (var client = CreateHttpClient())
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
            var cleanupPlan = PlanLoader.Load(planPath);
            var cleanupOptions = ParseSyncOptions(args, cleanupPlan.Meta.StartTimeLocal);
            jsonErrorOutput = cleanupOptions.JsonOutput;
            var cleanupEvents = PlanToIntervalsMapper.Map(cleanupPlan);

            using (var client = CreateHttpClient())
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

static HttpClient CreateHttpClient()
    => new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

static IntervalsOptions ParseSyncOptions(IReadOnlyList<string> args, string? planStartTimeLocal)
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
    var useApplyPlan = options.ContainsKey("apply-plan");
    var createPlanOnMissing = options.ContainsKey("create-plan-on-missing");
    var cleanupPlanBeforeApply = options.ContainsKey("cleanup-plan-before-apply");
    var startTimeLocal = GetOption(options, "start-time-local") ?? planStartTimeLocal ?? "00:00";
    var planName = GetOption(options, "plan-name") ?? "Running Plan Auto";
    var noVerify = options.ContainsKey("no-verify");
    var jsonOutput = options.ContainsKey("json");
    var folderIdRaw = GetOption(options, "folder-id");
    var folderId = 0;

    if (!string.IsNullOrWhiteSpace(folderIdRaw) && (!int.TryParse(folderIdRaw, out folderId) || folderId < 0))
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

    if (!TimeOnly.TryParseExact(startTimeLocal, "HH:mm", out _))
    {
        throw new ArgumentException("--start-time-local must be in HH:mm format, for example 00:00.");
    }

    return new IntervalsOptions
    {
        AthleteId = athleteId,
        ApiKey = apiKey,
        BaseUrl = baseUrl,
        DryRun = dryRun,
        UseApplyPlan = useApplyPlan,
        FolderId = folderId,
        StartTimeLocal = startTimeLocal,
        CreatePlanOnMissing = createPlanOnMissing,
        PlanName = planName,
        CleanupPlanBeforeApply = cleanupPlanBeforeApply,
        VerifyAfterSync = !noVerify,
        JsonOutput = jsonOutput
    };
}

static Dictionary<string, string?> ParseOptions(IReadOnlyList<string> args)
{
    var valueOptions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "athlete-id", "api-key", "base-url", "folder-id", "start-time-local", "plan-name"
    };
    var flagOptions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "dry-run", "apply-plan", "create-plan-on-missing", "cleanup-plan-before-apply", "no-verify", "json"
    };
    var options = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

    for (var i = 2; i < args.Count; i++)
    {
        var token = args[i];
        if (!token.StartsWith("--", StringComparison.Ordinal))
        {
            throw new ArgumentException($"Unexpected argument: {token}");
        }

        var key = token[2..];
        if (!valueOptions.Contains(key) && !flagOptions.Contains(key))
        {
            throw new ArgumentException($"Unknown option: --{key}");
        }

        if (!options.TryAdd(key, null))
        {
            throw new ArgumentException($"Option specified more than once: --{key}");
        }

        if (valueOptions.Contains(key))
        {
            if (i + 1 >= args.Count || args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Option --{key} requires a value.");
            }

            options[key] = args[++i];
        }
    }

    return options;
}

static void EnsureNoOptions(IReadOnlyList<string> args)
{
    if (args.Count > 2)
    {
        throw new ArgumentException($"Unexpected argument: {args[2]}");
    }
}

static string? GetOption(IReadOnlyDictionary<string, string?> options, string key)
    => options.TryGetValue(key, out var value) ? value : null;

static void PrintUsage()
{
    Console.WriteLine("Usage:");
    Console.WriteLine("  running-plan validate <plan.yaml>");
    Console.WriteLine("  running-plan sync <plan.yaml> --athlete-id <id> --api-key <key> [--base-url https://intervals.icu] [--dry-run] [--apply-plan] [--folder-id 0] [--start-time-local 00:00] [--create-plan-on-missing] [--plan-name \"Running Plan Auto\"] [--cleanup-plan-before-apply] [--no-verify] [--json]");
    Console.WriteLine("  running-plan verify <plan.yaml> --athlete-id <id> --api-key <key> [--base-url https://intervals.icu] [--json]");
    Console.WriteLine("  running-plan cleanup <plan.yaml> --athlete-id <id> --api-key <key> [--base-url https://intervals.icu] [--start-time-local 00:00] [--plan-name \"Running Plan Auto\"] [--dry-run] [--json]");
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
