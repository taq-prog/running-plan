using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace RunningPlan.Cli.Intervals;

public sealed class IntervalsClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly HttpClient _httpClient;
    private readonly IntervalsOptions _options;
    private readonly string _baseUrl;

    public IntervalsClient(HttpClient httpClient, IntervalsOptions options)
    {
        _httpClient = httpClient;
        _options = options;
        _baseUrl = _options.BaseUrl.EndsWith('/') ? _options.BaseUrl : _options.BaseUrl + "/";

        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"API_KEY:{_options.ApiKey}"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }

    public async Task UpsertEventsAsync(IReadOnlyCollection<IntervalsEvent> events, CancellationToken cancellationToken)
    {
        if (_options.UseApplyPlan)
        {
            await ApplyPlanAsync(events, cancellationToken);
        }
        else
        {
            foreach (var plannedEvent in events)
            {
                var payload = new Dictionary<string, object?>
                {
                    ["uid"] = plannedEvent.Uid,
                    ["external_id"] = plannedEvent.ExternalId,
                    ["start_date_local"] = plannedEvent.Date.ToString("yyyy-MM-dd"),
                    ["category"] = plannedEvent.Category,
                    ["type"] = plannedEvent.Type,
                    ["name"] = plannedEvent.Name,
                    ["description"] = plannedEvent.Description,
                    ["tags"] = plannedEvent.Tags
                };

                if (plannedEvent.DistanceMeters.HasValue)
                {
                    payload["distance"] = plannedEvent.DistanceMeters.Value;
                }

                if (plannedEvent.MovingTimeSeconds.HasValue)
                {
                    payload["moving_time"] = plannedEvent.MovingTimeSeconds.Value;
                }

                if (plannedEvent.WorkoutDoc is not null)
                {
                    payload["workout_doc"] = plannedEvent.WorkoutDoc;
                }

                var content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");

                if (_options.DryRun)
                {
                    Console.WriteLine($"[DRY-RUN] {plannedEvent.Date:yyyy-MM-dd} {plannedEvent.Name}");
                    Console.WriteLine(JsonSerializer.Serialize(payload, JsonOptions));
                    Console.WriteLine();
                    continue;
                }

                var endpoint = $"{_baseUrl}api/v1/athlete/{_options.AthleteId}/events?upsertOnUid=true";
                using var response = await _httpClient.PostAsync(endpoint, content, cancellationToken);
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    throw new HttpRequestException(
                        $"Intervals API request failed ({(int)response.StatusCode} {response.StatusCode}) for '{plannedEvent.Uid}': {responseBody}");
                }

                Console.WriteLine($"[SYNCED] {plannedEvent.Date:yyyy-MM-dd} {plannedEvent.Name}");
            }
        }

        if (!_options.DryRun && _options.VerifyAfterSync)
        {
            var report = await VerifyEventsAsync(events, cancellationToken);
            if (!report.Success)
            {
                throw new InvalidOperationException("Post-sync verification detected missing or mismatched events.");
            }
        }
    }

    public async Task<VerificationReport> VerifyEventsAsync(IReadOnlyCollection<IntervalsEvent> events, CancellationToken cancellationToken)
    {
        if (events.Count == 0)
        {
            return new VerificationReport
            {
                OldestDate = DateOnly.MinValue,
                NewestDate = DateOnly.MinValue,
                ExpectedCount = 0,
                FoundCount = 0,
                MissingExternalIds = [],
                DateMismatches = []
            };
        }

        var minDate = events.MinBy(x => x.Date)!.Date;
        var maxDate = events.MaxBy(x => x.Date)!.Date;

        var endpoint =
            $"{_baseUrl}api/v1/athlete/{_options.AthleteId}/events" +
            $"?oldest={minDate:yyyy-MM-dd}" +
            $"&newest={maxDate:yyyy-MM-dd}" +
            "&category=WORKOUT&limit=500";

        using var response = await _httpClient.GetAsync(endpoint, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Verification failed ({(int)response.StatusCode} {response.StatusCode}): {responseBody}");
        }

        using var document = JsonDocument.Parse(responseBody);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("Verification response is not an array of events.");
        }

        var expectedDateByExternalId = events.ToDictionary(x => x.ExternalId, x => x.Date, StringComparer.OrdinalIgnoreCase);
        var expectedIds = new HashSet<string>(expectedDateByExternalId.Keys, StringComparer.OrdinalIgnoreCase);
        var foundIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var dateMismatches = new List<string>();

        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (!item.TryGetProperty("external_id", out var externalIdElement) || externalIdElement.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var externalId = externalIdElement.GetString();
            if (string.IsNullOrWhiteSpace(externalId) || !expectedIds.Contains(externalId))
            {
                continue;
            }

            foundIds.Add(externalId);
            if (!item.TryGetProperty("start_date_local", out var dateElement) || dateElement.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            if (DateOnly.TryParse(dateElement.GetString(), out var actualDate) && expectedDateByExternalId.TryGetValue(externalId, out var expectedDate))
            {
                if (actualDate != expectedDate)
                {
                    dateMismatches.Add($"{externalId}: expected {expectedDate:yyyy-MM-dd}, got {actualDate:yyyy-MM-dd}");
                }
            }
        }

        var missing = expectedIds.Where(x => !foundIds.Contains(x)).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();

        var report = new VerificationReport
        {
            OldestDate = minDate,
            NewestDate = maxDate,
            ExpectedCount = expectedIds.Count,
            FoundCount = foundIds.Count,
            MissingExternalIds = missing,
            DateMismatches = dateMismatches
        };

        if (!_options.JsonOutput)
        {
            Console.WriteLine($"[VERIFY] expected={report.ExpectedCount} found={report.FoundCount} missing={report.MissingExternalIds.Count} date_mismatches={report.DateMismatches.Count}");
        }

        if (!_options.JsonOutput && missing.Count > 0)
        {
            Console.WriteLine("[VERIFY] Missing external_id entries:");
            foreach (var id in missing.Take(10))
            {
                Console.WriteLine($"- {id}");
            }

            if (missing.Count > 10)
            {
                Console.WriteLine($"- ... and {missing.Count - 10} more");
            }
        }

        if (!_options.JsonOutput && dateMismatches.Count > 0)
        {
            Console.WriteLine("[VERIFY] Date mismatches:");
            foreach (var mismatch in dateMismatches.Take(10))
            {
                Console.WriteLine($"- {mismatch}");
            }

            if (dateMismatches.Count > 10)
            {
                Console.WriteLine($"- ... and {dateMismatches.Count - 10} more");
            }
        }

        if (!report.Success && !_options.JsonOutput)
        {
            Console.WriteLine("[VERIFY] Verification detected missing or mismatched events.");
        }

        return report;
    }

    private async Task ApplyPlanAsync(IReadOnlyCollection<IntervalsEvent> events, CancellationToken cancellationToken)
    {
        if (events.Count == 0)
        {
            return;
        }

        var ordered = events.OrderBy(x => x.Date).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
        var startDate = ordered[0].Date;

        var extraWorkouts = ordered.Select(evt =>
        {
            var workout = new Dictionary<string, object?>
            {
                ["name"] = evt.Name,
                ["description"] = evt.Description,
                ["type"] = evt.Type,
                ["day"] = evt.Date.DayNumber - startDate.DayNumber,
                ["days"] = 1,
                ["tags"] = evt.Tags,
                ["external_id"] = evt.ExternalId
            };

            if (evt.WorkoutDoc is not null)
            {
                workout["workout_doc"] = evt.WorkoutDoc;
            }

            if (evt.DistanceMeters.HasValue)
            {
                workout["distance"] = evt.DistanceMeters.Value;
            }

            if (evt.MovingTimeSeconds.HasValue)
            {
                workout["moving_time"] = evt.MovingTimeSeconds.Value;
            }

            return workout;
        }).ToList();

        var payload = new Dictionary<string, object?>
        {
            ["start_date_local"] = startDate.ToString("yyyy-MM-dd"),
            ["folder_id"] = _options.FolderId,
            ["extra_workouts"] = extraWorkouts
        };

        if (_options.DryRun)
        {
            Console.WriteLine($"[DRY-RUN apply-plan] start_date={startDate:yyyy-MM-dd} workouts={extraWorkouts.Count} folder_id={_options.FolderId}");
            Console.WriteLine(JsonSerializer.Serialize(payload, JsonOptions));
            Console.WriteLine();
            return;
        }

        var endpoint = $"{_baseUrl}api/v1/athlete/{_options.AthleteId}/events/apply-plan";
        var content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync(endpoint, content, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Intervals apply-plan failed ({(int)response.StatusCode} {response.StatusCode}): {responseBody}");
        }

        Console.WriteLine($"[SYNCED apply-plan] start_date={startDate:yyyy-MM-dd} workouts={extraWorkouts.Count} folder_id={_options.FolderId}");
    }
}
