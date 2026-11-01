using System.Net;
using System.Reflection;
using System.Text;
using CrowdSecManager.Models;
using CrowdSecManager.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CrowdSecManager.Tests;

public sealed class CrowdSecApiServiceTests
{
    [Fact]
    public async Task GetDecisions_UsesVersionedRouteAndBouncerApiKeyHeader()
    {
        HttpRequestMessage? sentRequest = null;
        var handler = new StubHandler(request =>
        {
            sentRequest = request;
            return JsonResponse("[{\"id\":23,\"value\":\"203.0.113.4\"}]");
        });
        using var http = new HttpClient(handler);
        var (service, _) = CreateService(http);

        var decisions = await service.GetDecisionsAsync();

        Assert.Single(decisions);
        Assert.Equal(23, decisions[0].Id);
        Assert.Equal("http://lapi.test/v1/decisions", sentRequest!.RequestUri!.ToString());
        Assert.True(sentRequest.Headers.TryGetValues("X-Api-Key", out var apiKeys));
        Assert.Equal("bouncer-token", Assert.Single(apiKeys));
    }

    [Fact]
    public async Task IsReachable_RequestsHealthEndpoint()
    {
        string? requestedPath = null;
        var handler = new StubHandler(request =>
        {
            requestedPath = request.RequestUri!.AbsolutePath;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var http = new HttpClient(handler);
        var (service, _) = CreateService(http);

        Assert.True(await service.IsReachableAsync());
        Assert.Equal("/health", requestedPath);
    }

    [Fact]
    public async Task IsReachable_PropagatesCallerCancellationInsteadOfReportingOffline()
    {
        using var http = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)));
        var (service, _) = CreateService(http);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.IsReachableAsync(cancellation.Token));
    }

    [Fact]
    public async Task GetSnapshotStats_PropagatesCallerCancellation()
    {
        using var http = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)));
        var (service, cli) = CreateService(http);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GetSnapshotStatsAsync(cancellation.Token));
        Assert.Equal(0, cli.DecisionCalls);
    }

    [Fact]
    public async Task GetDecisions_FallsBackToCliWhenLapiRequestFails()
    {
        var fallback = new[] { new CsDecision { Id = 71 } };
        using var http = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)));
        var (service, cli) = CreateService(http, fallback);

        var decisions = await service.GetDecisionsAsync();

        Assert.Single(decisions);
        Assert.Equal(71, decisions[0].Id);
        Assert.Equal(1, cli.DecisionCalls);
    }

    private static (CrowdSecApiService Service, CliProxy CliState) CreateService(
        HttpClient http, IReadOnlyList<CsDecision>? fallbackDecisions = null)
    {
        var cliProxy = DispatchProxy.Create<ICrowdSecCliService, CliProxy>();
        var cliState = (CliProxy)(object)cliProxy;
        cliState.FallbackDecisions = fallbackDecisions ?? Array.Empty<CsDecision>();
        var service = new CrowdSecApiService(http, Options.Create(new CrowdSecOptions
        {
            LocalApiUrl = "http://lapi.test",
            BouncerApiKey = "bouncer-token"
        }), NullLogger<CrowdSecApiService>.Instance, cliProxy);
        return (service, cliState);
    }

    private static HttpResponseMessage JsonResponse(string json)
        => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(responseFactory(request));
    }

    public class CliProxy : DispatchProxy
    {
        public IReadOnlyList<CsDecision> FallbackDecisions { get; set; } = Array.Empty<CsDecision>();
        public int DecisionCalls { get; private set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(ICrowdSecCliService.ListDecisionsAsync))
            {
                DecisionCalls++;
                return Task.FromResult(FallbackDecisions);
            }
            throw new NotSupportedException($"Unexpected CLI call: {targetMethod?.Name}");
        }
    }
}
