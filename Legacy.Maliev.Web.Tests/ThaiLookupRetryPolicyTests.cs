using System.Net;
using System.Net.Http.Headers;
using Legacy.Maliev.Web.Infrastructure;
using Maliev.Aspire.ServiceDefaults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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
