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
    public async Task Listing_DeserializesOriginalOfferLevelOffersWireContractWithoutAuthorization()
    {
        var handler = new RecordingHandler(request =>
        {
            Assert.Null(request.Headers.Authorization);
            return request.RequestUri?.AbsolutePath == "/Jobs"
                ? JsonResponse("""
                    {"items":[{"id":42,"levelId":3,"title":"Engineer","introduction":"Introduction","description":"Description","prerequisites":"Prerequisites","whatWeOffer":"Benefits","location":"Nonthaburi","isFilled":false,"createdDate":"2026-01-02T03:04:05Z","modifiedDate":"2026-02-03T04:05:06Z","level":{"id":3,"name":"Senior","description":"Level description","createdDate":"2025-01-02T03:04:05Z","modifiedDate":"2025-02-03T04:05:06Z","offers":[]}}],"pageIndex":2,"totalPages":4,"totalItems":7,"hasPreviousPage":true,"hasNextPage":true}
                    """)
                : JsonResponse("[]");
        });

        var listing = await CreateClient(handler).GetListingAsync(
            CareerSort.JobId_Ascending, null, 2, 25, CancellationToken.None);

        Assert.True(listing.ServiceAvailable);
        var offer = Assert.Single(listing.Offers.Items);
        Assert.Equal(42, offer.Id);
        Assert.Equal(3, offer.LevelId);
        Assert.Equal("Engineer", offer.Title);
        Assert.Equal("Introduction", offer.Introduction);
        Assert.Equal("Description", offer.Description);
        Assert.Equal("Prerequisites", offer.Prerequisites);
        Assert.Equal("Benefits", offer.WhatWeOffer);
        Assert.Equal("Nonthaburi", offer.Location);
        Assert.Equal(false, offer.IsFilled);
        Assert.Equal(new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc), offer.CreatedDate);
        Assert.Equal(new DateTime(2026, 2, 3, 4, 5, 6, DateTimeKind.Utc), offer.ModifiedDate);
        var level = Assert.IsType<CareerLevel>(offer.Level);
        Assert.Equal(3, level.Id);
        var levelJson = System.Text.Json.JsonSerializer.SerializeToElement(
            level, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        Assert.Equal(0, levelJson.GetProperty("offers").GetArrayLength());
        Assert.Equal("Senior", level.Name);
        Assert.Equal("Level description", level.Description);
        Assert.Equal(new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc), level.CreatedDate);
        Assert.Equal(new DateTime(2025, 2, 3, 4, 5, 6, DateTimeKind.Utc), level.ModifiedDate);
        Assert.Equal(2, listing.Offers.PageIndex);
        Assert.Equal(4, listing.Offers.TotalPages);
        Assert.Equal(7, listing.Offers.TotalItems);
        Assert.True(listing.Offers.HasPreviousPage);
        Assert.True(listing.Offers.HasNextPage);
    }

    [Fact]
    public async Task Listing_PreservesUnknownOfferStateAndNullableProducerFields()
    {
        var handler = new RecordingHandler(request => request.RequestUri?.AbsolutePath == "/Jobs"
            ? JsonResponse("""
                {"items":[{"id":42,"levelId":3,"title":null,"introduction":null,"description":null,"prerequisites":null,"whatWeOffer":null,"location":null,"isFilled":null,"createdDate":null,"modifiedDate":null,"level":null}],"pageIndex":1,"totalPages":1,"totalItems":1,"hasPreviousPage":false,"hasNextPage":false}
                """)
            : JsonResponse("[]"));

        var listing = await CreateClient(handler).GetListingAsync(
            CareerSort.JobId_Ascending, null, 1, 25, CancellationToken.None);

        Assert.True(listing.ServiceAvailable);
        var offer = Assert.Single(listing.Offers.Items);
        Assert.Null(offer.Title);
        Assert.Null(offer.Introduction);
        Assert.Null(offer.Description);
        Assert.Null(offer.Prerequisites);
        Assert.Null(offer.WhatWeOffer);
        Assert.Null(offer.Location);
        Assert.Null(offer.IsFilled);
        Assert.Null(offer.CreatedDate);
        Assert.Null(offer.ModifiedDate);
        Assert.Null(offer.Level);
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

    [Theory]
    [InlineData(null, -2)]
    [InlineData("", 0)]
    [InlineData("  ", 1)]
    [InlineData(" engineer ", 1)]
    public async Task OffersOnly_PreservesQueryWithoutFetchingLevels(string? search, int index)
    {
        var handler = new RecordingHandler(request =>
        {
            Assert.Equal("/Jobs", request.RequestUri?.AbsolutePath);
            Assert.Null(request.Headers.Authorization);
            return JsonResponse("""
                {"items":[],"pageIndex":1,"totalPages":0,"totalItems":0,"hasPreviousPage":false,"hasNextPage":false}
                """);
        });

        var result = await InvokeOfferReadAsync(CreateClient(handler), search, index);

        Assert.True(result.ServiceAvailable);
        var request = Assert.Single(handler.Requests);
        Assert.Contains($"search={Uri.EscapeDataString(search ?? string.Empty)}", request.Query, StringComparison.Ordinal);
        Assert.Contains($"index={index}", request.Query, StringComparison.Ordinal);
        Assert.Contains("sort=JobId_Ascending", request.Query, StringComparison.Ordinal);
    }

    private static Task<ServiceResponse<CareerOfferPage>> InvokeOfferReadAsync(
        CareerClient client, string? search, int index)
    {
        // The unchanged transport method is private on the protected baseline and public on the candidate.
        // Keep a tests-only baseline overlay compilable so failures establish behavior, not missing symbols.
        var method = typeof(CareerClient).GetMethod("GetOffersAsync",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(method);
        return Assert.IsAssignableFrom<Task<ServiceResponse<CareerOfferPage>>>(method.Invoke(client,
            [CareerSort.JobId_Ascending, search, index, 25, CancellationToken.None]));
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
