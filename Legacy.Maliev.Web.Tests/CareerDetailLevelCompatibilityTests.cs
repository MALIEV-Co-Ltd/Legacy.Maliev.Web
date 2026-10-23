using System.Net;
using System.Text;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace Legacy.Maliev.Web.Tests;

public sealed class CareerDetailLevelCompatibilityTests
{
    [Theory]
    [InlineData(null, false, null)]
    [InlineData("null", false, null)]
    [InlineData("{}", true, null)]
    [InlineData("{\"id\":3,\"name\":\"Provided level\"}", true, "Provided level")]
    public async Task RawDetailPreservesUpstreamLevelWithoutDuplicatingThePresentationLookup(
        string? levelJson, bool hasLevel, string? levelName)
    {
        using var handler = new DetailHandler(levelJson);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://careers/") };
        var client = new CareerClient(new ClientFactory(http), NullLogger<CareerClient>.Instance);

        var response = await client.GetOfferAsync(42, default);

        Assert.True(response.ServiceAvailable);
        var offer = Assert.IsType<CareerOffer>(response.Value);
        Assert.Equal(42, offer.Id);
        Assert.Equal(3, offer.LevelId);
        Assert.Equal("Engineer", offer.Title);
        Assert.Equal(hasLevel, offer.Level is not null);
        Assert.Equal(levelName, offer.Level?.Name);
        Assert.Equal("/Jobs/42", Assert.Single(handler.Requests).AbsolutePath);
    }

    private sealed class ClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            Assert.Equal("careers", name);
            return client;
        }
    }

    private sealed class DetailHandler(string? levelJson) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var uri = request.RequestUri ?? throw new InvalidOperationException("Missing detail URI.");
            Requests.Add(uri);
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Null(request.Headers.Authorization);
            Assert.Equal("/Jobs/42", uri.AbsolutePath);
            var payload = "{\"id\":42,\"levelId\":3,\"title\":\"Engineer\",\"isFilled\":false"
                + (levelJson is null ? string.Empty : ",\"level\":" + levelJson) + "}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            });
        }
    }
}
