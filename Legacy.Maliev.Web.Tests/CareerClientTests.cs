using System.Net;
using System.Text;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace Legacy.Maliev.Web.Tests;

public sealed class CareerClientTests
{
    [Fact]
    public async Task Listing_UsesLegacyCompatibleCareerRoutesAndWireShape()
    {
        var handler = new RecordingHandler(request =>
        {
            var path = request.RequestUri?.AbsolutePath;
            return path switch
            {
                "/Jobs" => JsonResponse(
                    """
                    {"items":[{"id":42,"levelId":3,"title":"Engineer","isFilled":false}],"pageIndex":2,"totalPages":4,"totalItems":7,"hasPreviousPage":true,"hasNextPage":true}
                    """),
                "/jobs/levels" => JsonResponse("[{\"id\":3,\"name\":\"Senior\"}]"),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            };
        });
        var client = CreateClient(handler);

        var listing = await client.GetListingAsync(
            CareerSort.JobCreatedDate_Descending,
            "CNC engineer",
            2,
            25,
            CancellationToken.None);

        Assert.True(listing.ServiceAvailable);
        Assert.Single(listing.Offers.Items);
        Assert.Equal(42, listing.Offers.Items[0].Id);
        Assert.Equal("Senior", Assert.Single(listing.Levels).Name);
        var listingRequest = Assert.Single(handler.Requests, uri => uri.AbsolutePath == "/Jobs");
        Assert.Contains("sort=JobCreatedDate_Descending", listingRequest.Query, StringComparison.Ordinal);
        Assert.Contains("search=CNC%20engineer", listingRequest.Query, StringComparison.Ordinal);
        Assert.Contains("index=2", listingRequest.Query, StringComparison.Ordinal);
        Assert.Contains("size=25", listingRequest.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Offer_NotFoundIsAvailableWithoutInventingData()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var client = CreateClient(handler);

        var response = await client.GetOfferAsync(404, CancellationToken.None);

        Assert.True(response.ServiceAvailable);
        Assert.Null(response.Value);
        Assert.Equal("/Jobs/404", Assert.Single(handler.Requests).AbsolutePath);
    }

    private static CareerClient CreateClient(RecordingHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://careers/") };
        return new CareerClient(new StubHttpClientFactory(httpClient), NullLogger<CareerClient>.Instance);
    }

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class StubHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            Assert.Equal("careers", name);
            return client;
        }
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri ?? throw new InvalidOperationException("Request URI was missing."));
            return Task.FromResult(respond(request));
        }
    }
}
