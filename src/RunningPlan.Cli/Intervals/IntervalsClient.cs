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

    private sealed class ApplyPlanResult
    {
        public required bool Applied { get; init; }
        public required int CleanupDeletedCount { get; init; }
    }

    public IntervalsClient(HttpClient httpClient, IntervalsOptions options)
    {
        _httpClient = httpClient;
        _options = options;
        _baseUrl = _options.BaseUrl.EndsWith('/') ? _options.BaseUrl : _options.BaseUrl + "/";

        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"API_KEY:{_options.ApiKey}"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }

    public async Task<SyncReport> UpsertEventsAsync(IReadOnlyCollection<IntervalsEvent> events, CancellationToken cancellationToken)
    {
        var syncedCount = 0;
        var cleanupDeletedCount = 0;
        VerificationReport? verificationReport = null;
        var usedApplyPlan = _options.UseApplyPlan;

        if (_options.UseApplyPlan)
        {
            var applyPlanResult = await ApplyPlanAsync(events, cancellationToken);
            cleanupDeletedCount = applyPlanResult.CleanupDeletedCount;

            if (applyPlanResult.Applied)
            {
                syncedCount = events.Count;
            }
            else
            {
                // ponytail: fallback keeps user flow working when apply-plan is unavailable for the account/folder.
                usedApplyPlan = false;
                syncedCount = await SyncEventsIndividuallyAsync(events, cancellationToken);
            }
        }
        else
        {
            syncedCount = await SyncEventsIndividuallyAsync(events, cancellationToken);
        }

        if (!_options.DryRun && _options.VerifyAfterSync)
        {
            verificationReport = await VerifyWithRetriesAsync(events, usedApplyPlan, cancellationToken);
            if (!verificationReport.Success)
            {
                throw new VerificationFailedException(verificationReport);
            }
        }

        return new SyncReport
        {
            Success = true,
            DryRun = _options.DryRun,
            ApplyPlan = usedApplyPlan,
            CleanupDeletedCount = cleanupDeletedCount,
            PlannedCount = events.Count,
            SyncedCount = syncedCount,
            VerificationAttempted = !_options.DryRun && _options.VerifyAfterSync,
            Verification = verificationReport
        };
    }

    private async Task<int> SyncEventsIndividuallyAsync(IReadOnlyCollection<IntervalsEvent> events, CancellationToken cancellationToken)
    {
        var syncedCount = 0;

        foreach (var plannedEvent in events)
        {
            var payload = new Dictionary<string, object?>
            {
                ["uid"] = plannedEvent.Uid,
                ["external_id"] = plannedEvent.ExternalId,
                ["start_date_local"] = FormatEventStartDate(plannedEvent.Date),
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
                if (!_options.JsonOutput)
                {
                    Console.WriteLine($"[DRY-RUN] {plannedEvent.Date:yyyy-MM-dd} {plannedEvent.Name}");
                    Console.WriteLine(JsonSerializer.Serialize(payload, JsonOptions));
                    Console.WriteLine();
                }

                syncedCount++;
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

            if (!_options.JsonOutput)
            {
                Console.WriteLine($"[SYNCED] {plannedEvent.Date:yyyy-MM-dd} {plannedEvent.Name}");
            }

            syncedCount++;
        }

        return syncedCount;
    }

    public async Task<VerificationReport> VerifyEventsAsync(IReadOnlyCollection<IntervalsEvent> events, CancellationToken cancellationToken)
        => await VerifyEventsAsync(events, _options.UseApplyPlan, cancellationToken);

    public async Task<VerificationReport> VerifyEventsAsync(IReadOnlyCollection<IntervalsEvent> events, bool useApplyPlanVerification, CancellationToken cancellationToken)
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
            "&limit=1000";

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
        var externalIdByUid = events.ToDictionary(x => x.Uid, x => x.ExternalId, StringComparer.OrdinalIgnoreCase);
        var expectedSignatureByExternalId = events.ToDictionary(x => x.ExternalId, x => BuildEventSignature(x.Date, x.Name), StringComparer.OrdinalIgnoreCase);
        var expectedIds = new HashSet<string>(expectedDateByExternalId.Keys, StringComparer.OrdinalIgnoreCase);
        var foundIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var dateMismatches = new List<string>();
        var foundSignatures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (useApplyPlanVerification)
            {
                if (!string.IsNullOrWhiteSpace(_options.PlanName)
                    && item.TryGetProperty("plan_name", out var planNameElement)
                    && planNameElement.ValueKind == JsonValueKind.String)
                {
                    var planName = planNameElement.GetString();
                    if (!string.Equals(planName, _options.PlanName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                }

                if (!item.TryGetProperty("start_date_local", out var startElement) || startElement.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                if (!TryParseEventDate(startElement.GetString(), out var actualDate))
                {
                    continue;
                }

                if (!item.TryGetProperty("name", out var nameElement) || nameElement.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var actualName = nameElement.GetString();
                if (string.IsNullOrWhiteSpace(actualName))
                {
                    continue;
                }

                foundSignatures.Add(BuildEventSignature(actualDate, actualName));
                continue;
            }

            string? matchedExternalId = null;

            if (item.TryGetProperty("external_id", out var externalIdElement) && externalIdElement.ValueKind == JsonValueKind.String)
            {
                var externalId = externalIdElement.GetString();
                if (!string.IsNullOrWhiteSpace(externalId) && expectedIds.Contains(externalId))
                {
                    matchedExternalId = externalId;
                }
            }

            if (matchedExternalId is null && item.TryGetProperty("uid", out var uidElement) && uidElement.ValueKind == JsonValueKind.String)
            {
                var uid = uidElement.GetString();
                if (!string.IsNullOrWhiteSpace(uid) && externalIdByUid.TryGetValue(uid, out var mappedExternalId))
                {
                    matchedExternalId = mappedExternalId;
                }
            }

            if (matchedExternalId is null)
            {
                continue;
            }

            foundIds.Add(matchedExternalId);
            if (!item.TryGetProperty("start_date_local", out var dateElement) || dateElement.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            if (TryParseEventDate(dateElement.GetString(), out var parsedDate) && expectedDateByExternalId.TryGetValue(matchedExternalId, out var expectedDate))
            {
                if (parsedDate != expectedDate)
                {
                    dateMismatches.Add($"{matchedExternalId}: expected {expectedDate:yyyy-MM-dd}, got {parsedDate:yyyy-MM-dd}");
                }
            }
        }

        List<string> missing;
        var foundCount = foundIds.Count;

        if (useApplyPlanVerification)
        {
            missing = expectedSignatureByExternalId
                .Where(pair => !foundSignatures.Contains(pair.Value))
                .Select(pair => pair.Key)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
            foundCount = expectedIds.Count - missing.Count;
        }
        else
        {
            missing = expectedIds.Where(x => !foundIds.Contains(x)).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        }

        var report = new VerificationReport
        {
            OldestDate = minDate,
            NewestDate = maxDate,
            ExpectedCount = expectedIds.Count,
            FoundCount = foundCount,
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

    private async Task<VerificationReport> VerifyWithRetriesAsync(IReadOnlyCollection<IntervalsEvent> events, bool useApplyPlanVerification, CancellationToken cancellationToken)
    {
        const int maxAttempts = 4;
        const int delayMs = 1500;

        VerificationReport? lastReport = null;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            lastReport = await VerifyEventsAsync(events, useApplyPlanVerification, cancellationToken);
            if (lastReport.Success)
            {
                return lastReport;
            }

            if (attempt < maxAttempts)
            {
                if (!_options.JsonOutput)
                {
                    Console.WriteLine($"[VERIFY] attempt {attempt}/{maxAttempts} not ready yet, retrying...");
                }

                await Task.Delay(delayMs, cancellationToken);
            }
        }

        return lastReport ?? new VerificationReport
        {
            OldestDate = DateOnly.MinValue,
            NewestDate = DateOnly.MinValue,
            ExpectedCount = 0,
            FoundCount = 0,
            MissingExternalIds = [],
            DateMismatches = []
        };
    }

    private async Task<ApplyPlanResult> ApplyPlanAsync(IReadOnlyCollection<IntervalsEvent> events, CancellationToken cancellationToken)
    {
        if (events.Count == 0)
        {
            return new ApplyPlanResult
            {
                Applied = true,
                CleanupDeletedCount = 0
            };
        }

        var ordered = events.OrderBy(x => x.Date).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
        var startDate = ordered[0].Date;
        var endDate = ordered[^1].Date;

        var extraWorkouts = ordered.Select(evt =>
        {
            var workout = new Dictionary<string, object?>
            {
                ["uid"] = evt.Uid,
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

        var folderId = _options.FolderId;

        var payload = new Dictionary<string, object?>
        {
            ["start_date_local"] = FormatEventStartDate(startDate),
            ["folder_id"] = folderId,
            ["extra_workouts"] = extraWorkouts
        };

        if (_options.DryRun)
        {
            if (!_options.JsonOutput)
            {
                Console.WriteLine($"[DRY-RUN apply-plan] start_date={startDate:yyyy-MM-dd} workouts={extraWorkouts.Count} folder_id={folderId}");
                Console.WriteLine(JsonSerializer.Serialize(payload, JsonOptions));
                Console.WriteLine();
            }

            return new ApplyPlanResult
            {
                Applied = true,
                CleanupDeletedCount = 0
            };
        }

        var cleanupDeletedCount = 0;
        if (_options.CleanupPlanBeforeApply)
        {
            cleanupDeletedCount = await CleanupExistingPlanEventsAsync(startDate, endDate, cancellationToken);
            if (!_options.JsonOutput)
            {
                Console.WriteLine($"[CLEANUP] Removed {cleanupDeletedCount} existing events for plan '{_options.PlanName}' before apply-plan.");
            }
        }

        var endpoint = $"{_baseUrl}api/v1/athlete/{_options.AthleteId}/events/apply-plan";
        var content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync(endpoint, content, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound && responseBody.Contains("Plan not found", StringComparison.OrdinalIgnoreCase))
        {
            if (_options.CreatePlanOnMissing)
            {
                var createdFolderId = await CreatePlanFolderAsync(cancellationToken);
                payload["folder_id"] = createdFolderId;

                if (!_options.JsonOutput)
                {
                    Console.WriteLine($"[INFO] Created plan folder {createdFolderId}. Retrying apply-plan.");
                }

                using var retryContent = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");
                using var retryResponse = await _httpClient.PostAsync(endpoint, retryContent, cancellationToken);
                var retryBody = await retryResponse.Content.ReadAsStringAsync(cancellationToken);

                if (retryResponse.IsSuccessStatusCode)
                {
                    if (!_options.JsonOutput)
                    {
                        Console.WriteLine($"[SYNCED apply-plan] start_date={startDate:yyyy-MM-dd} workouts={extraWorkouts.Count} folder_id={createdFolderId}");
                    }

                    return new ApplyPlanResult
                    {
                        Applied = true,
                        CleanupDeletedCount = cleanupDeletedCount
                    };
                }

                if (retryResponse.StatusCode != System.Net.HttpStatusCode.NotFound || !retryBody.Contains("Plan not found", StringComparison.OrdinalIgnoreCase))
                {
                    throw new HttpRequestException(
                        $"Intervals apply-plan retry failed ({(int)retryResponse.StatusCode} {retryResponse.StatusCode}): {retryBody}");
                }
            }

            if (!_options.JsonOutput)
            {
                Console.WriteLine("[INFO] apply-plan not available for this folder/account, falling back to per-event sync.");
            }

            return new ApplyPlanResult
            {
                Applied = false,
                CleanupDeletedCount = cleanupDeletedCount
            };
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Intervals apply-plan failed ({(int)response.StatusCode} {response.StatusCode}): {responseBody}");
        }

        if (!_options.JsonOutput)
        {
            Console.WriteLine($"[SYNCED apply-plan] start_date={startDate:yyyy-MM-dd} workouts={extraWorkouts.Count} folder_id={folderId}");
        }

        return new ApplyPlanResult
        {
            Applied = true,
            CleanupDeletedCount = cleanupDeletedCount
        };
    }

    private async Task<int> CreatePlanFolderAsync(CancellationToken cancellationToken)
    {
        var endpoint = $"{_baseUrl}api/v1/athlete/{_options.AthleteId}/folders";
        var payload = new Dictionary<string, object?>
        {
            ["type"] = "PLAN",
            ["name"] = _options.PlanName,
            ["visibility"] = "PRIVATE"
        };

        using var content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync(endpoint, content, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Create plan folder failed ({(int)response.StatusCode} {response.StatusCode}): {responseBody}");
        }

        using var doc = JsonDocument.Parse(responseBody);
        if (!doc.RootElement.TryGetProperty("id", out var idElement) || idElement.ValueKind != JsonValueKind.Number || !idElement.TryGetInt32(out var id))
        {
            throw new InvalidOperationException($"Create plan folder response does not contain numeric id: {responseBody}");
        }

        return id;
    }

    private async Task<int> CleanupExistingPlanEventsAsync(DateOnly oldestDate, DateOnly newestDate, CancellationToken cancellationToken)
    {
        var listEndpoint =
            $"{_baseUrl}api/v1/athlete/{_options.AthleteId}/events" +
            $"?oldest={oldestDate:yyyy-MM-dd}" +
            $"&newest={newestDate:yyyy-MM-dd}" +
            "&limit=1000";

        using var listResponse = await _httpClient.GetAsync(listEndpoint, cancellationToken);
        var listBody = await listResponse.Content.ReadAsStringAsync(cancellationToken);

        if (!listResponse.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Cleanup preflight failed ({(int)listResponse.StatusCode} {listResponse.StatusCode}): {listBody}");
        }

        using var document = JsonDocument.Parse(listBody);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("Cleanup preflight response is not an array of events.");
        }

        var eventIdsToDelete = new List<long>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (!item.TryGetProperty("plan_name", out var planNameElement) || planNameElement.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var planName = planNameElement.GetString();
            if (!string.Equals(planName, _options.PlanName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (item.TryGetProperty("id", out var idElement) && idElement.ValueKind == JsonValueKind.Number && idElement.TryGetInt64(out var eventId))
            {
                eventIdsToDelete.Add(eventId);
            }
        }

        var deleted = 0;
        foreach (var eventId in eventIdsToDelete.Distinct())
        {
            var deleteEndpoint = $"{_baseUrl}api/v1/athlete/{_options.AthleteId}/events/{eventId}";
            using var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, deleteEndpoint);
            using var deleteResponse = await _httpClient.SendAsync(deleteRequest, cancellationToken);
            var deleteBody = await deleteResponse.Content.ReadAsStringAsync(cancellationToken);

            if (!deleteResponse.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"Cleanup delete failed for event {eventId} ({(int)deleteResponse.StatusCode} {deleteResponse.StatusCode}): {deleteBody}");
            }

            deleted++;
        }

        return deleted;
    }

    private static string FormatEventStartDate(DateOnly date)
        => $"{date:yyyy-MM-dd}T00:00:00";

    private static bool TryParseEventDate(string? value, out DateOnly date)
    {
        if (DateOnly.TryParse(value, out date))
        {
            return true;
        }

        if (DateTime.TryParse(value, out var dateTime))
        {
            date = DateOnly.FromDateTime(dateTime);
            return true;
        }

        date = default;
        return false;
    }

    private static string BuildEventSignature(DateOnly date, string name)
        => $"{date:yyyy-MM-dd}|{name.Trim()}";
}
