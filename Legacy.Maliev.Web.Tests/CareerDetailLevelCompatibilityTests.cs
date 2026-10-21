using System.Net;
using System.Text;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Components.Pages.Career;
using Legacy.Maliev.Web.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace Legacy.Maliev.Web.Tests;

public sealed class CareerDetailLevelCompatibilityTests
{
    [Fact]
    public async Task OmittedDetailLevelUsesExistingMetadataForBothSharedDetailPresentations()
    {
        using var handler = new DetailHandler(Offer(3), "[{\"id\":3,\"name\":\"Senior\"}]");
        using var http = Http(handler);
        var client = Client(http);
        var response = await client.GetOfferAsync(42, default);
        Assert.True(response.ServiceAvailable);
        var offer = Assert.IsType<CareerOffer>(response.Value);
        Assert.Equal("Senior", CareerDetailContentModel.Create(offer).LevelName);
        Assert.Equal("Engineer", offer.Title);
        Assert.Equal(3, offer.LevelId);
        Assert.Equal(new[] { "/Jobs/42", "/jobs/levels" }, handler.Requests.Select(uri => uri.AbsolutePath));
    }

    [Fact]
    public async Task ProvidedLevelDoesNotRequestMetadata()
    {
        using var handler = new DetailHandler(
            "{\"id\":42,\"levelId\":3,\"title\":\"Engineer\",\"level\":{\"id\":3,\"name\":\"Already provided\"}}", "must-not-read");
        using var http = Http(handler);
        var response = await Client(http).GetOfferAsync(42, default);
        Assert.True(response.ServiceAvailable);
        Assert.Equal("Already provided", CareerDetailContentModel.Create(Assert.IsType<CareerOffer>(response.Value)).LevelName);
        Assert.Equal("/Jobs/42", Assert.Single(handler.Requests).AbsolutePath);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task InvalidLevelReferenceKeepsDisplayFallbackWithoutMetadataRequest(int levelId)
    {
        using var handler = new DetailHandler(Offer(levelId), "must-not-read");
        using var http = Http(handler);
        var response = await Client(http).GetOfferAsync(42, default);
        Assert.True(response.ServiceAvailable);
        Assert.Equal("not specified", CareerDetailContentModel.Create(Assert.IsType<CareerOffer>(response.Value)).LevelName);
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, "[]")]
    [InlineData(HttpStatusCode.NotFound, "not-read")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "not-read")]
    [InlineData(HttpStatusCode.OK, "bad-json")]
    [InlineData(HttpStatusCode.OK, "{}")]
    [InlineData(HttpStatusCode.OK, "[{\"id\":\"bad\",\"name\":\"Invalid\"}]")]
    [InlineData(HttpStatusCode.OK, "null")]
    [InlineData(HttpStatusCode.OK, "[null]")]
    [InlineData(HttpStatusCode.OK, "[{\"id\":4,\"name\":\"Other\"}]")]
    [InlineData(HttpStatusCode.OK, "[{\"id\":3,\"name\":\"A\"},{\"id\":3,\"name\":\"B\"}]")]
    [InlineData(HttpStatusCode.OK, "[{\"id\":3,\"name\":null}]")]
    public async Task MissingUnavailableMalformedOrAmbiguousMetadataKeepsOfferAndExistingFallback(HttpStatusCode status, string metadata)
    {
        using var handler = new DetailHandler(Offer(3), metadata, status);
        using var http = Http(handler);
        var response = await Client(http).GetOfferAsync(42, default);
        Assert.True(response.ServiceAvailable);
        var offer = Assert.IsType<CareerOffer>(response.Value);
        Assert.Equal(42, offer.Id);
        Assert.Equal("Engineer", offer.Title);
        Assert.Equal("not specified", CareerDetailContentModel.Create(offer).LevelName);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task InvalidMetadataCharsetKeepsOfferAndDisplayFallback()
    {
        using var handler = new DetailHandler(Offer(3), "[{\"id\":3,\"name\":\"Senior\"}]", metadataCharset: "not-an-encoding");
        using var http = Http(handler);
        var response = await Client(http).GetOfferAsync(42, default);
        Assert.True(response.ServiceAvailable);
        Assert.Equal("not specified", CareerDetailContentModel.Create(Assert.IsType<CareerOffer>(response.Value)).LevelName);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MetadataTransportFailureKeepsOfferAvailable(bool timeout)
    {
        using var handler = new DetailHandler(Offer(3), "unused", metadataFailure: () =>
            timeout ? new TaskCanceledException("Controlled metadata timeout") : new HttpRequestException("Controlled metadata failure"));
        using var http = Http(handler);
        var response = await Client(http).GetOfferAsync(42, default);
        Assert.True(response.ServiceAvailable);
        Assert.Equal("not specified", CareerDetailContentModel.Create(Assert.IsType<CareerOffer>(response.Value)).LevelName);
    }

    [Fact]
    public async Task CallerCancellationDuringMetadataLookupPropagates()
    {
        using var cancellation = new CancellationTokenSource();
        using var handler = new DetailHandler(Offer(3), "unused", onMetadata: cancellation.Cancel);
        using var http = Http(handler);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Client(http).GetOfferAsync(42, cancellation.Token));
        Assert.True(cancellation.IsCancellationRequested);
        Assert.Equal(2, handler.Requests.Count);
    }

    private static string Offer(int levelId) =>
        "{\"id\":42,\"levelId\":" + levelId + ",\"title\":\"Engineer\",\"isFilled\":false}";

    private static HttpClient Http(HttpMessageHandler handler) => new(handler) { BaseAddress = new Uri("http://careers/") };

    private static CareerClient Client(HttpClient http) =>
        new(new ClientFactory(http), NullLogger<CareerClient>.Instance);

    private sealed class ClientFactory(HttpClient http) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            Assert.Equal("careers", name);
            return http;
        }
    }

    private sealed class DetailHandler(
        string offer,
        string metadata,
        HttpStatusCode metadataStatus = HttpStatusCode.OK,
        Func<Exception>? metadataFailure = null,
        Action? onMetadata = null,
        string? metadataCharset = null) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri ?? throw new InvalidOperationException("Request URI is missing.");
            Requests.Add(uri);
            if (uri.AbsolutePath == "/Jobs/42")
                return Task.FromResult(Json(offer, HttpStatusCode.OK));
            Assert.Equal("/jobs/levels", uri.AbsolutePath);
            onMetadata?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            if (metadataFailure is not null) throw metadataFailure();
            var response = Json(metadata, metadataStatus);
            if (metadataCharset is not null) response.Content.Headers.ContentType!.CharSet = metadataCharset;
            return Task.FromResult(response);
        }

        private static HttpResponseMessage Json(string payload, HttpStatusCode status) =>
            new(status) { Content = new StringContent(payload, Encoding.UTF8, "application/json") };
    }
}
