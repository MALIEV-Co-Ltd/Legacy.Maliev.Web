using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace Legacy.Maliev.Web.Tests;

public sealed class ContactClientTests
{
    [Fact]
    public async Task Countries_UsesAnonymousLegacyContract()
    {
        var handler = new RecordingHandler(request =>
        {
            Assert.Null(request.Authorization);
            return JsonResponse(
                """
                [{"id":764,"name":"Thailand","continent":"Asia","countryCode":"66","iso2":"TH","iso3":"THA"}]
                """);
        });
        var client = new CountryClient(
            new NamedHttpClientFactory("countries", new HttpClient(handler) { BaseAddress = new Uri("http://countries/") }),
            NullLogger<CountryClient>.Instance);

        var result = await client.GetCountriesAsync(CancellationToken.None);

        Assert.True(result.ServiceAvailable);
        var countries = Assert.IsAssignableFrom<IReadOnlyList<Country>>(result.Value);
        Assert.Equal("Thailand", Assert.Single(countries).Name);
        Assert.Equal("/Countries", Assert.Single(handler.Requests).RequestUri?.AbsolutePath);
    }

    [Fact]
    public async Task Submit_AddsServerSideBearerTokenAndPreservesWireShape()
    {
        var handler = new RecordingHandler(request =>
        {
            Assert.Equal(new AuthenticationHeaderValue("Bearer", "service-token"), request.Authorization);
            return JsonResponse(
                """
                {"id":81,"firstName":"Mali","lastName":"Ev","company":"MALIEV","email":"info@example.com","telephone":"020000000","country":"Thailand","messageContent":"Please call me"}
                """,
                HttpStatusCode.Created);
        });
        var client = new ContactClient(
            new NamedHttpClientFactory("contacts", new HttpClient(handler) { BaseAddress = new Uri("http://contacts/") }),
            new StubServiceAccessTokenProvider("service-token"),
            NullLogger<ContactClient>.Instance);

        var result = await client.SubmitAsync(
            new ContactSubmission("Mali", "Ev", "MALIEV", "info@example.com", "020000000", "Thailand", "Please call me"),
            CancellationToken.None);

        Assert.True(result.ServiceAvailable);
        Assert.True(result.Authorized);
        Assert.Equal(81, result.ReferenceNumber);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/Messages", request.RequestUri?.AbsolutePath);
        Assert.Contains("\"messageContent\":\"Please call me\"", request.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("service-token", request.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Submit_MissingServiceTokenFailsClosedWithoutCallingContactService()
    {
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("ContactService must not be called."));
        var client = new ContactClient(
            new NamedHttpClientFactory("contacts", new HttpClient(handler) { BaseAddress = new Uri("http://contacts/") }),
            new StubServiceAccessTokenProvider(null),
            NullLogger<ContactClient>.Instance);

        var result = await client.SubmitAsync(
            new ContactSubmission("Mali", "Ev", null, "info@example.com", null, "Thailand", "Hello"),
            CancellationToken.None);

        Assert.False(result.Authorized);
        Assert.False(result.ServiceAvailable);
        Assert.Null(result.ReferenceNumber);
        Assert.Empty(handler.Requests);
    }

    private static HttpResponseMessage JsonResponse(string json, HttpStatusCode statusCode = HttpStatusCode.OK) =>
        new(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class StubServiceAccessTokenProvider(string? token) : IServiceAccessTokenProvider
    {
        public ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(token);

        public void Invalidate(string token)
        {
        }
    }

    private sealed class NamedHttpClientFactory(string expectedName, HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            Assert.Equal(expectedName, name);
            return client;
        }
    }

    private sealed class RecordingHandler(Func<RecordedRequest, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var recorded = new RecordedRequest(
                request.Method,
                request.RequestUri,
                request.Headers.Authorization,
                request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
            Requests.Add(recorded);
            return respond(recorded);
        }
    }

    private sealed record RecordedRequest(
        HttpMethod Method,
        Uri? RequestUri,
        AuthenticationHeaderValue? Authorization,
        string Body);
}
