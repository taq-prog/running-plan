using RunningPlan.Cli.Config;
using RunningPlan.Cli.Intervals;

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
            var syncPlan = PlanLoader.Load(planPath);
            var mappedEvents = PlanToIntervalsMapper.Map(syncPlan, syncOptions.StructuredOnly);
            PrintPreview(mappedEvents);

            using (var client = new HttpClient())
            {
                var intervalsClient = new IntervalsClient(client, syncOptions);
                await intervalsClient.UpsertEventsAsync(mappedEvents, CancellationToken.None);
            }

            Console.WriteLine(syncOptions.DryRun
                ? "Dry-run completed."
                : "Sync completed.");
            return 0;

        default:
            Console.Error.WriteLine($"Unknown command: {command}");
            PrintUsage();
            return 2;
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
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
        FolderId = folderId
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
    Console.WriteLine("  running-plan sync <plan.yaml> --athlete-id <id> --api-key <key> [--base-url https://intervals.icu] [--dry-run] [--structured-only] [--apply-plan] [--folder-id 0]");
    Console.WriteLine();
    Console.WriteLine("Environment variable fallback:");
    Console.WriteLine("  INTERVALS_ATHLETE_ID");
    Console.WriteLine("  INTERVALS_API_KEY");
    Console.WriteLine("  INTERVALS_BASE_URL");
}
