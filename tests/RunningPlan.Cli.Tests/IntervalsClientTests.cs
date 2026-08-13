using System.Net;
using System.Text;
using System.Text.Json;
using RunningPlan.Cli.Intervals;
using Xunit;

namespace RunningPlan.Cli.Tests;

public sealed class IntervalsClientTests
{
    [Fact]
    public async Task UpsertEventsAsync_SendsContractPayload()
    {
        var handler = new StubHandler(_ => JsonResponse("{}"));
        var client = CreateClient(handler, new IntervalsOptions
        {
            AthleteId = "athlete",
            ApiKey = "secret",
            BaseUrl = "https://intervals.test",
            DryRun = false,
            VerifyAfterSync = false
        });

        var report = await client.UpsertEventsAsync([CreateEvent()], CancellationToken.None);

        Assert.Equal(1, report.SyncedCount);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.EndsWith("/api/v1/athlete/athlete/events?upsertOnUid=true", request.Uri, StringComparison.Ordinal);

        using var json = JsonDocument.Parse(request.Body);
        Assert.Equal("uid-1", json.RootElement.GetProperty("uid").GetString());
        Assert.Equal("running-plan", json.RootElement.GetProperty("tags")[0].GetString());
        Assert.Equal(5000, json.RootElement.GetProperty("distance").GetInt32());
    }

    [Fact]
    public async Task ApplyPlan_404CreatesFolderAndRetries()
    {
        var handlerCallCount = 0;
        var handler = new StubHandler(request =>
        {
            if (request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath.EndsWith("/events/apply-plan", StringComparison.Ordinal))
            {
                return handlerCallCount++ == 0
                    ? JsonResponse("Plan not found", HttpStatusCode.NotFound)
                    : JsonResponse("{}", HttpStatusCode.OK);
            }

            if (request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath.EndsWith("/folders", StringComparison.Ordinal))
            {
                return JsonResponse("[]");
            }

            if (request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath.EndsWith("/folders", StringComparison.Ordinal))
            {
                return JsonResponse("{\"id\": 42}");
            }

            throw new InvalidOperationException($"Unexpected request: {request.Method} {request.RequestUri}");
        });
        var client = CreateClient(handler, new IntervalsOptions
        {
            AthleteId = "athlete",
            ApiKey = "secret",
            BaseUrl = "https://intervals.test",
            UseApplyPlan = true,
            CreatePlanOnMissing = true,
            PlanName = "Test Plan",
            VerifyAfterSync = false
        });

        var report = await client.UpsertEventsAsync([CreateEvent()], CancellationToken.None);

        Assert.True(report.ApplyPlan);
        Assert.Equal(2, handler.Requests.Count(x => x.Uri.EndsWith("/events/apply-plan", StringComparison.Ordinal)));
        var retry = handler.Requests.Last(x => x.Uri.EndsWith("/events/apply-plan", StringComparison.Ordinal));
        using var retryJson = JsonDocument.Parse(retry.Body);
        Assert.Equal(42, retryJson.RootElement.GetProperty("folder_id").GetInt32());
    }

    [Fact]
    public async Task VerifyEventsAsync_MatchesExternalIdAndReportsDateMismatch()
    {
        var handler = new StubHandler(_ => JsonResponse("[{\"external_id\":\"external-1\",\"uid\":\"uid-1\",\"start_date_local\":\"2026-08-12T04:30:00\"}]"));
        var client = CreateClient(handler, new IntervalsOptions
        {
            AthleteId = "athlete",
            ApiKey = "secret",
            BaseUrl = "https://intervals.test",
            JsonOutput = true
        });

        var report = await client.VerifyEventsAsync([CreateEvent()], useApplyPlanVerification: false, CancellationToken.None);

        Assert.False(report.Success);
        Assert.Empty(report.MissingExternalIds);
        Assert.Single(report.DateMismatches);
    }

    [Fact]
    public async Task Cleanup_DoesNotDeleteUnownedSameSignatureEvent()
    {
        var handler = new StubHandler(request =>
        {
            if (request.Method == HttpMethod.Get)
            {
                return JsonResponse("[" +
                    "{\"id\":1,\"uid\":\"uid-1\",\"external_id\":\"external-1\",\"name\":\"Easy\",\"start_date_local\":\"2026-08-11\",\"tags\":[\"running-plan\"]}," +
                    "{\"id\":3,\"uid\":\"uid-1-copy\",\"external_id\":\"external-1-copy\",\"name\":\"Easy\",\"start_date_local\":\"2026-08-11\",\"tags\":[\"running-plan\"]}," +
                    "{\"id\":2,\"name\":\"Easy\",\"start_date_local\":\"2026-08-11\"}" +
                    "]");
            }

            if (request.Method == HttpMethod.Delete)
            {
                return JsonResponse("{}");
            }

            throw new InvalidOperationException($"Unexpected request: {request.Method} {request.RequestUri}");
        });
        var client = CreateClient(handler, new IntervalsOptions
        {
            AthleteId = "athlete",
            ApiKey = "secret",
            BaseUrl = "https://intervals.test",
            PlanName = "Test Plan"
        });

        var report = await client.CleanupPlanEventsAsync([CreateEvent()], CancellationToken.None);

        Assert.Equal(1, report.DeletedCount);
        var delete = Assert.Single(handler.Requests, x => x.Method == HttpMethod.Delete);
        Assert.EndsWith("/events/1", delete.Uri, StringComparison.Ordinal);
    }

    private static IntervalsClient CreateClient(StubHandler handler, IntervalsOptions options)
        => new(new HttpClient(handler), options);

    private static IntervalsEvent CreateEvent() => new()
    {
        Uid = "uid-1",
        ExternalId = "external-1",
        Date = new DateOnly(2026, 8, 11),
        Name = "Easy",
        Description = "- 5km",
        Type = "Run",
        Category = "WORKOUT",
        DistanceMeters = 5000,
        Tags = ["easy"]
    };

    private static HttpResponseMessage JsonResponse(string body, HttpStatusCode statusCode = HttpStatusCode.OK)
        => new(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new RecordedRequest(request.Method, request.RequestUri!.ToString(), body));
            return responder(request);
        }
    }

    private sealed record RecordedRequest(HttpMethod Method, string Uri, string Body);
}
