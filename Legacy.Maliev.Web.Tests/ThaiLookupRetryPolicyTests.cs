using System.Net;
using System.Net.Http.Headers;
using Legacy.Maliev.Web.Infrastructure;
using Maliev.Aspire.ServiceDefaults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;

namespace Legacy.Maliev.Web.Tests;

// Production registration controls use a synthetic terminal transport; Catalog journeys are separate.
public sealed class ThaiLookupRetryPolicyTests
{
    [Theory]
    [InlineData("catalog", "GET", 429, "true", true, false, false)]
    [InlineData("catalog", "GET", 429, "missing", true, false, true)]
    [InlineData("catalog", "GET", 429, "false", true, false, true)]
    [InlineData("catalog", "GET", 429, "wrong-type", true, false, true)]
    [InlineData("catalog", "GET", 503, "true", true, false, true)]
    [InlineData("catalog", "POST", 503, "true", true, false, false)]
    [InlineData("catalog", "POST", 429, "missing", true, false, false)]
    [InlineData("catalog", "GET", 429, "true", false, false, true)]
    [InlineData("catalog", "GET", 200, "true", true, false, false)]
    [InlineData("catalog", "GET", 503, "true", true, true, true)]
    [InlineData("customers", "GET", 429, "true", true, false, true)]
    public async Task ProductionPipelinePreservesAllOutcomesExceptTaggedInteractive429(
        string clientName, string method, int firstStatus, string tag,
        bool attachRequest, bool firstThrows, bool expectedRetry)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] = null;
        builder.AddServiceDefaults();
        builder.Services.AddLegacyServiceClients(builder.Configuration);
        using var primary = new CountingPrimary(firstStatus, attachRequest, firstThrows);
        builder.Services.AddHttpClient(clientName).ConfigurePrimaryHttpMessageHandler(() => primary);
        using var host = builder.Build();
        using var client = host.Services.GetRequiredService<IHttpClientFactory>().CreateClient(clientName);
        using var request = new HttpRequestMessage(new HttpMethod(method), "/retry-unit-control");
        var key = new HttpRequestOptionsKey<bool>("maliev-thai-lookup-interactive");
        if (tag == "true") request.Options.Set(key, true);
        else if (tag == "false") request.Options.Set(key, false);
        else if (tag == "wrong-type")
            request.Options.Set(new HttpRequestOptionsKey<string>(key.Key), "true");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var response = await client.SendAsync(request, deadline.Token);
        Assert.Equal(expectedRetry ? 2 : 1, primary.Attempts);
        Assert.Equal(expectedRetry ? HttpStatusCode.OK : (HttpStatusCode)firstStatus, response.StatusCode);
    }

    [Theory]
    [InlineData("object")]
    [InlineData("string")]
    [InlineData("tuple")]
    public async Task ForeignMarkerCannotSuppressOriginalOuterRetry(string markerType)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] = null;
        builder.AddServiceDefaults();
        builder.Services.AddLegacyServiceClients(builder.Configuration);
        using var primary = new CountingPrimary(429, true, false);
        builder.Services.AddHttpClient("foreign-marker-control").ConfigurePrimaryHttpMessageHandler(() => primary);
        using var host = builder.Build();
        using var client = host.Services.GetRequiredService<IHttpClientFactory>().CreateClient("foreign-marker-control");
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://marker-control.example.test/retry-unit-control");
        request.Options.Set(new HttpRequestOptionsKey<bool>("maliev-thai-lookup-interactive"), true);
        const string markerKey = "maliev-catalog-interactive429-refusal";
        if (markerType == "object") request.Options.Set(new HttpRequestOptionsKey<object>(markerKey), new object());
        else if (markerType == "string") request.Options.Set(new HttpRequestOptionsKey<string>(markerKey), "foreign");
        else request.Options.Set(new HttpRequestOptionsKey<(object Token, HttpRequestMessage Request)>(markerKey), (new object(), request));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var response = await client.SendAsync(request, deadline.Token);
        Assert.Equal(2, primary.Attempts);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task CopiedActualRefusalCapabilityCannotSuppressAnotherRequest()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] = null;
        builder.AddServiceDefaults();
        builder.Services.AddLegacyServiceClients(builder.Configuration);
        using var catalogPrimary = new CountingPrimary(429, true, false);
        using var foreignPrimary = new CountingPrimary(429, true, false);
        builder.Services.AddHttpClient("catalog").ConfigurePrimaryHttpMessageHandler(() => catalogPrimary);
        // Only the existing global Defaults handler applies here, so the outer decision is directly exercised.
        builder.Services.AddHttpClient("copied-marker-control").ConfigurePrimaryHttpMessageHandler(() => foreignPrimary);
        using var host = builder.Build();
        using var catalog = host.Services.GetRequiredService<IHttpClientFactory>().CreateClient("catalog");
        using var other = host.Services.GetRequiredService<IHttpClientFactory>().CreateClient("copied-marker-control");
        using var request = new HttpRequestMessage(HttpMethod.Get, "/retry-unit-control");
        request.Options.Set(new HttpRequestOptionsKey<bool>("maliev-thai-lookup-interactive"), true);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var refused = await catalog.SendAsync(request, deadline.Token);
        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.Equal(1, catalogPrimary.Attempts);
        var key = new HttpRequestOptionsKey<(object Token, HttpRequestMessage Request)>("maliev-catalog-interactive429-refusal");
        Assert.NotNull(refused.RequestMessage);
        Assert.True(refused.RequestMessage.Options.TryGetValue(key, out var actual));
        using var copied = new HttpRequestMessage(HttpMethod.Get, "https://marker-control.example.test/retry-unit-control");
        copied.Options.Set(new HttpRequestOptionsKey<bool>("maliev-thai-lookup-interactive"), true);
        copied.Options.Set(key, actual);
        using var response = await other.SendAsync(copied, deadline.Token);
        Assert.Equal(2, foreignPrimary.Attempts);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActualFactoryPipelineNamesAndRepeatedRegistrationPreserveRefusal(bool repeatRegistration)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] = null;
        builder.AddServiceDefaults();
        builder.Services.AddLegacyServiceClients(builder.Configuration);
        if (repeatRegistration) builder.Services.AddLegacyServiceClients(builder.Configuration);
        var observed = new PipelineCount();
        builder.Services.AddSingleton<IHttpMessageHandlerBuilderFilter>(observed);
        using var primary = new CountingPrimary(429, true, false);
        builder.Services.AddHttpClient("catalog").ConfigurePrimaryHttpMessageHandler(() => primary);
        using var host = builder.Build();
        using var client = host.Services.GetRequiredService<IHttpClientFactory>().CreateClient("catalog");
        Assert.Equal(repeatRegistration ? 3 : 2, observed.Count);
        var options = host.Services.GetRequiredService<IOptionsMonitor<HttpStandardResilienceOptions>>();
        Assert.Equal(3, options.Get("-standard").Retry.MaxRetryAttempts);
        Assert.Equal(3, options.Get("catalog-standard").Retry.MaxRetryAttempts);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/retry-unit-control");
        request.Options.Set(new HttpRequestOptionsKey<bool>("maliev-thai-lookup-interactive"), true);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var response = await client.SendAsync(request, deadline.Token);
        Assert.Equal(1, primary.Attempts);
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
    }

    private sealed class PipelineCount : IHttpMessageHandlerBuilderFilter
    {
        internal int Count { get; private set; }
        public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next) => builder =>
        {
            next(builder);
            if (builder.Name == "catalog") Count = builder.AdditionalHandlers.Count(handler => handler is ResilienceHandler);
        };
    }

    private sealed class CountingPrimary(int firstStatus, bool attachRequest, bool firstThrows) : HttpMessageHandler
    {
        private int attempts;
        public int Attempts => Volatile.Read(ref attempts);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var first = Interlocked.Increment(ref attempts) == 1;
            if (first && firstThrows) throw new HttpRequestException("Synthetic retry unit control.");
            var response = new HttpResponseMessage(first ? (HttpStatusCode)firstStatus : HttpStatusCode.OK);
            if (attachRequest) response.RequestMessage = request;
            // A response header exercises the original Retry-After policy; no retry options are overridden.
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero);
            return Task.FromResult(response);
        }
    }
}
