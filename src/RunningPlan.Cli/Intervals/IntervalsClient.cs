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
}
