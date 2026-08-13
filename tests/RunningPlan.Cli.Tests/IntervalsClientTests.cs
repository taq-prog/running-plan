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
        Assert.Equal("2026-08-11T04:30:00", json.RootElement.GetProperty("start_date_local").GetString());
        Assert.Equal("running-plan", json.RootElement.GetProperty("tags")[0].GetString());
        Assert.Equal(5000, json.RootElement.GetProperty("distance").GetInt32());
    }

    [Fact]
    public async Task UpsertEventsAsync_RetriesTransientResponse()
    {
        var calls = 0;
        var handler = new StubHandler(_ =>
            calls++ == 0
                ? JsonResponse("busy", HttpStatusCode.ServiceUnavailable)
                : JsonResponse("{}"));
        var client = CreateClient(handler, new IntervalsOptions
        {
            AthleteId = "athlete",
            ApiKey = "secret",
            BaseUrl = "https://intervals.test",
            VerifyAfterSync = false
        });

        var report = await client.UpsertEventsAsync([CreateEvent()], CancellationToken.None);

        Assert.Equal(1, report.SyncedCount);
        Assert.Equal(2, handler.Requests.Count);
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
    public async Task ApplyPlanUnavailable_FallsBackToIndividualSyncAndReportsFallback()
    {
        var handler = new StubHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/events/apply-plan", StringComparison.Ordinal))
            {
                return JsonResponse("Plan not found", HttpStatusCode.NotFound);
            }

            if (request.RequestUri.AbsolutePath.EndsWith("/events", StringComparison.Ordinal))
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
            UseApplyPlan = true,
            VerifyAfterSync = false
        });

        var report = await client.UpsertEventsAsync([CreateEvent()], CancellationToken.None);

        Assert.True(report.ApplyPlanRequested);
        Assert.False(report.ApplyPlan);
        Assert.True(report.ApplyPlanFallback);
        Assert.Equal(1, report.SyncedCount);
    }

    [Fact]
    public async Task ApplyPlanFailureAfterDestructiveCleanup_StopsWithoutFallback()
    {
        var handler = new StubHandler(request =>
        {
            if (request.Method == HttpMethod.Get)
            {
                return JsonResponse("[{\"id\":1,\"uid\":\"uid-1\",\"external_id\":\"external-1\",\"name\":\"Easy\",\"start_date_local\":\"2026-08-11\",\"tags\":[\"running-plan\"]}]");
            }

            if (request.Method == HttpMethod.Delete)
            {
                return JsonResponse("{}");
            }

            if (request.RequestUri!.AbsolutePath.EndsWith("/events/apply-plan", StringComparison.Ordinal))
            {
                return JsonResponse("Plan not found", HttpStatusCode.NotFound);
            }

            throw new InvalidOperationException($"Unexpected request: {request.Method} {request.RequestUri}");
        });
        var client = CreateClient(handler, new IntervalsOptions
        {
            AthleteId = "athlete",
            ApiKey = "secret",
            BaseUrl = "https://intervals.test",
            UseApplyPlan = true,
            CleanupPlanBeforeApply = true,
            VerifyAfterSync = false
        });

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => client.UpsertEventsAsync([CreateEvent()], CancellationToken.None));

        Assert.Contains("destructive cleanup", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(handler.Requests, x => x.Uri.EndsWith("/events?upsertOnUid=true", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UpsertEventsAsync_DoesNotRetryBadRequest()
    {
        var handler = new StubHandler(_ => JsonResponse("bad request", HttpStatusCode.BadRequest));
        var client = CreateClient(handler, new IntervalsOptions
        {
            AthleteId = "athlete",
            ApiKey = "secret",
            BaseUrl = "https://intervals.test",
            VerifyAfterSync = false
        });

        await Assert.ThrowsAsync<HttpRequestException>(() => client.UpsertEventsAsync([CreateEvent()], CancellationToken.None));

        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData(408)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    [InlineData(504)]
    public async Task UpsertEventsAsync_RetriesTransientStatusCodes(int statusCode)
    {
        var calls = 0;
        var handler = new StubHandler(_ =>
            calls++ == 0
                ? JsonResponse("temporary", (HttpStatusCode)statusCode)
                : JsonResponse("{}"));
        var client = CreateClient(handler, new IntervalsOptions
        {
            AthleteId = "athlete",
            ApiKey = "secret",
            BaseUrl = "https://intervals.test",
            VerifyAfterSync = false
        });

        var report = await client.UpsertEventsAsync([CreateEvent()], CancellationToken.None);

        Assert.Equal(1, report.SyncedCount);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task UpsertEventsAsync_DoesNotRetryUnauthorized()
    {
        var handler = new StubHandler(_ => JsonResponse("unauthorized", HttpStatusCode.Unauthorized));
        var client = CreateClient(handler, new IntervalsOptions
        {
            AthleteId = "athlete",
            ApiKey = "secret",
            BaseUrl = "https://intervals.test",
            VerifyAfterSync = false
        });

        await Assert.ThrowsAsync<HttpRequestException>(() => client.UpsertEventsAsync([CreateEvent()], CancellationToken.None));

        Assert.Single(handler.Requests);
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
    public async Task VerifyApplyPlan_DoesNotUseOneDuplicateSignatureForTwoExpectedEvents()
    {
        var handler = new StubHandler(_ => JsonResponse("[{\"start_date_local\":\"2026-08-11T04:30:00\",\"name\":\"Easy\",\"description\":\"- 5km\"}]"));
        var client = CreateClient(handler, new IntervalsOptions
        {
            AthleteId = "athlete",
            ApiKey = "secret",
            BaseUrl = "https://intervals.test",
            PlanName = "Test Plan",
            JsonOutput = true
        });

        var first = CreateEvent();
        var second = CreateEvent("uid-2", "external-2");
        var report = await client.VerifyEventsAsync([first, second], useApplyPlanVerification: true, CancellationToken.None);

        Assert.Equal(2, report.ExpectedCount);
        Assert.Equal(1, report.FoundCount);
        Assert.Single(report.MissingExternalIds);
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
                    "{\"id\":3,\"uid\":\"rp-w01-copy\",\"external_id\":\"running-plan:w01:copy\",\"name\":\"Easy\",\"start_date_local\":\"2026-08-11\",\"plan_name\":\"Test Plan\",\"tags\":[\"running-plan\"]}," +
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

        Assert.Equal(2, report.DeletedCount);
        Assert.Equal(2, handler.Requests.Count(x => x.Method == HttpMethod.Delete));
        Assert.Contains(handler.Requests, x => x.Method == HttpMethod.Delete && x.Uri.EndsWith("/events/1", StringComparison.Ordinal));
        Assert.Contains(handler.Requests, x => x.Method == HttpMethod.Delete && x.Uri.EndsWith("/events/3", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Cleanup_UsesStableIdentityWhenDateAndNameChanged()
    {
        var handler = new StubHandler(request => request.Method == HttpMethod.Get
            ? JsonResponse("[{\"id\":9,\"uid\":\"uid-1\",\"external_id\":\"external-1\",\"name\":\"Old Name\",\"start_date_local\":\"2026-08-10\"}]")
            : JsonResponse("{}"));
        var client = CreateClient(handler, new IntervalsOptions
        {
            AthleteId = "athlete",
            ApiKey = "secret",
            BaseUrl = "https://intervals.test",
            PlanName = "Test Plan"
        });

        var report = await client.CleanupPlanEventsAsync([CreateEvent()], CancellationToken.None);

        Assert.Equal(1, report.DeletedCount);
        Assert.Contains(handler.Requests, x => x.Method == HttpMethod.Delete && x.Uri.EndsWith("/events/9", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UpsertEventsAsync_PropagatesCancellation()
    {
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();
        var handler = new CancellationHandler();
        var client = CreateClient(handler, new IntervalsOptions
        {
            AthleteId = "athlete",
            ApiKey = "secret",
            BaseUrl = "https://intervals.test",
            VerifyAfterSync = false
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.UpsertEventsAsync([CreateEvent()], cancellationSource.Token));
        Assert.Equal(1, handler.RequestCount);
    }

    private static IntervalsClient CreateClient(HttpMessageHandler handler, IntervalsOptions options)
        => new(new HttpClient(handler), options);

    private static IntervalsEvent CreateEvent(string uid = "uid-1", string externalId = "external-1") => new()
    {
        Uid = uid,
        ExternalId = externalId,
        Date = new DateOnly(2026, 8, 11),
        StartDateLocal = new DateTime(2026, 8, 11, 4, 30, 0),
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

    private sealed class CancellationHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromCanceled<HttpResponseMessage>(cancellationToken);
        }
    }

    private sealed record RecordedRequest(HttpMethod Method, string Uri, string Body);
}
