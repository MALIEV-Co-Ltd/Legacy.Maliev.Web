using System.Net;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace Legacy.Maliev.Web.Tests;

public sealed class CareerPageCountCompatibilityTests
{
    [Theory]
    [InlineData("\"totalRecords\":7")]
    [InlineData("\"totalItems\":7")]
    [InlineData("\"totalRecords\":7,\"totalItems\":7")]
    [InlineData("\"TotalRecords\":7")]
    [InlineData("\"totalItems\":\"7\"")]
    public async Task CanonicalAndStagedCountsKeepCurrentClrFieldsAndRequestContract(string count)
    {
        using var handler = new PageHandler(Page(count));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://careers/") };
        var client = Client(http);
        var listing = await client.GetListingAsync(CareerSort.JobCreatedDate_Descending, "CNC engineer", 2, 25, default);
        Assert.True(listing.ServiceAvailable);
        Assert.Equal(7, listing.Offers.TotalItems);
        Assert.Equal(2, listing.Offers.PageIndex);
        Assert.Equal(4, listing.Offers.TotalPages);
        Assert.True(listing.Offers.HasPreviousPage);
        Assert.True(listing.Offers.HasNextPage);
        Assert.Equal(42, Assert.Single(listing.Offers.Items).Id);
        Assert.Equal("Engineer", Assert.Single(listing.Offers.Items).Title);
        Assert.Equal("Senior", Assert.Single(listing.Levels).Name);
        var request = Assert.Single(handler.Requests, uri => uri.AbsolutePath == "/Jobs");
        Assert.Contains("sort=JobCreatedDate_Descending", request.Query, StringComparison.Ordinal);
        Assert.Contains("search=CNC%20engineer", request.Query, StringComparison.Ordinal);
        Assert.Contains("index=2", request.Query, StringComparison.Ordinal);
        Assert.Contains("size=25", request.Query, StringComparison.Ordinal);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("\"totalRecords\":null")]
    [InlineData("\"totalItems\":null")]
    [InlineData("\"totalRecords\":true")]
    [InlineData("\"totalRecords\":\"bad-count\"")]
    [InlineData("\"totalRecords\":[]")]
    [InlineData("\"totalRecords\":{}")]
    [InlineData("\"totalRecords\":-1")]
    [InlineData("\"totalRecords\":2147483648")]
    [InlineData("\"totalRecords\":1.5")]
    [InlineData("\"totalRecords\":7,\"totalItems\":8")]
    [InlineData("\"totalRecords\":null,\"totalItems\":7")]
    [InlineData("\"totalRecords\":7,\"totalItems\":\"bad-count\"")]
    [InlineData("\"totalRecords\":7,\"TotalRecords\":7")]
    [InlineData("\"totalItems\":7,\"TotalItems\":7")]
    public async Task MissingConflictingAndMalformedCountsRetainJsonFailureInsteadOfInventingZero(string count)
    {
        using var handler = new PageHandler(Page(count));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://careers/") };
        var client = Client(http);
        await Assert.ThrowsAsync<JsonException>(() =>
            client.GetListingAsync(CareerSort.JobId_Ascending, null, 2, 25, default));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, false)]
    public async Task ExistingNotFoundAndTransientStatusSemanticsRemain(HttpStatusCode status, bool available)
    {
        using var handler = new PageHandler("not-read", status);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://careers/") };
        var client = Client(http);
        var listing = await client.GetListingAsync(CareerSort.JobId_Ascending, null, 2, 25, default);
        Assert.Equal(available, listing.ServiceAvailable);
        Assert.Equal(2, listing.Offers.PageIndex);
        Assert.Equal(0, listing.Offers.TotalItems);
        Assert.Empty(listing.Offers.Items);
    }

    [Fact]
    public async Task CallerCancellationStillPropagates()
    {
        using var handler = new PageHandler(Page("\"totalRecords\":7"));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://careers/") };
        var client = Client(http);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.GetListingAsync(CareerSort.JobId_Ascending, null, 2, 25, cancellation.Token));
    }

    private static CareerClient Client(HttpClient http) =>
        new(new ClientFactory(http), NullLogger<CareerClient>.Instance);

    private static string Page(string count) =>
        "{\"items\":[{\"id\":42,\"levelId\":3,\"title\":\"Engineer\",\"isFilled\":false}],\"pageIndex\":2,\"totalPages\":4,\"hasPreviousPage\":true,\"hasNextPage\":true"
        + (count.Length == 0 ? string.Empty : "," + count) + "}";

    private sealed class ClientFactory(HttpClient http) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            Assert.Equal("careers", name);
            return http;
        }
    }

    private sealed class PageHandler(string payload, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request.RequestUri ?? throw new InvalidOperationException("Request URI is missing."));
            return Task.FromResult(request.RequestUri!.AbsolutePath == "/Jobs"
                ? new HttpResponseMessage(status) { Content = new StringContent(payload, Encoding.UTF8, "application/json") }
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("[{\"id\":3,\"name\":\"Senior\"}]", Encoding.UTF8, "application/json"),
                });
        }
    }
}
