using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace Legacy.Maliev.Web.Tests;

public sealed class QuotationClientTests
{
    [Fact]
    public async Task CreateRequest_UsesAuthenticatedIdempotentLegacyContract()
    {
        var handler = new RecordingHandler(async request =>
        {
            Assert.Equal(new AuthenticationHeaderValue("Bearer", "service-token"), request.Headers.Authorization);
            Assert.Equal("quote-submit-123", Assert.Single(request.Headers.GetValues("Idempotency-Key")));
            var body = await request.Content!.ReadAsStringAsync();
            Assert.Contains("\"taxIdentification\":\"0100000000000\"", body, StringComparison.Ordinal);
            Assert.Contains("\"done\":null", body, StringComparison.Ordinal);
            return JsonResponse("{\"id\":417}", HttpStatusCode.Created);
        });
        var client = new QuotationClient(
            new NamedHttpClientFactory("quotations", new HttpClient(handler) { BaseAddress = new Uri("http://quotations/") }),
            new StubTokenProvider("service-token"),
            NullLogger<QuotationClient>.Instance);

        var result = await client.CreateRequestAsync(
            Submission(),
            "quote-submit-123",
            CancellationToken.None);

        Assert.Equal(417, result.ReferenceNumber);
        Assert.True(result.ServiceAvailable);
        Assert.True(result.Authorized);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/QuotationRequests", request.RequestUri?.AbsolutePath);
    }

    [Fact]
    public async Task CreateRequest_MissingServiceTokenFailsClosed()
    {
        var handler = new RecordingHandler(_ => Task.FromException<HttpResponseMessage>(
            new InvalidOperationException("QuotationService must not be called.")));
        var client = new QuotationClient(
            new NamedHttpClientFactory("quotations", new HttpClient(handler) { BaseAddress = new Uri("http://quotations/") }),
            new StubTokenProvider(null),
            NullLogger<QuotationClient>.Instance);

        var result = await client.CreateRequestAsync(Submission(), "quote-submit-123", CancellationToken.None);

        Assert.Null(result.ReferenceNumber);
        Assert.False(result.ServiceAvailable);
        Assert.False(result.Authorized);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task UploadAndLink_StreamsMultipartThroughFileServiceAndLinksObjectMetadata()
    {
        var fileHandler = new RecordingHandler(async request =>
        {
            Assert.Equal(new AuthenticationHeaderValue("Bearer", "service-token"), request.Headers.Authorization);
            Assert.IsType<MultipartFormDataContent>(request.Content);
            var body = await request.Content!.ReadAsStringAsync();
            Assert.Contains("model.stl", body, StringComparison.Ordinal);
            Assert.Contains("solid test", body, StringComparison.Ordinal);
            return JsonResponse(
                """
                {"object":[{"bucket":"maliev.com","objectName":"quotation-request/417/submit/model.stl","uri":"https://storage.invalid/signed"}]}
                """,
                HttpStatusCode.Created);
        });
        var quotationHandler = new RecordingHandler(request =>
        {
            Assert.Equal(new AuthenticationHeaderValue("Bearer", "service-token"), request.Headers.Authorization);
            return Task.FromResult(JsonResponse("{\"id\":91}", HttpStatusCode.Created));
        });
        var client = new QuotationFileClient(
            new MultiHttpClientFactory(
                ("files", new HttpClient(fileHandler) { BaseAddress = new Uri("http://files/") }),
                ("quotations", new HttpClient(quotationHandler) { BaseAddress = new Uri("http://quotations/") })),
            new StubTokenProvider("service-token"),
            NullLogger<QuotationFileClient>.Instance);

        var result = await client.UploadAndLinkAsync(
            417,
            Guid.Parse("11111111-2222-3333-4444-555555555555"),
            [Upload("model.stl", "model/stl", "solid test")],
            CancellationToken.None);

        Assert.True(result.Completed);
        Assert.False(result.Rejected);
        var upload = Assert.Single(fileHandler.Requests);
        Assert.Equal("/Uploads", upload.RequestUri?.AbsolutePath);
        Assert.Contains("bucket=maliev.com", upload.RequestUri?.Query, StringComparison.Ordinal);
        Assert.Contains("path=quotation-request%2F417%2F11111111222233334444555555555555", upload.RequestUri?.Query, StringComparison.OrdinalIgnoreCase);
        var link = Assert.Single(quotationHandler.Requests);
        Assert.Equal("/quotationrequests/417/files", link.RequestUri?.AbsolutePath);
        Assert.Contains("objectName=quotation-request%2F417%2Fsubmit%2Fmodel.stl", link.RequestUri?.Query, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UploadAndLink_LinkFailureCompensatesUploadedObjects()
    {
        var fileHandler = new RecordingHandler(request => Task.FromResult(
            request.Method == HttpMethod.Delete
                ? new HttpResponseMessage(HttpStatusCode.NoContent)
                : JsonResponse(
                    """
                    {"object":[{"bucket":"maliev.com","objectName":"quotation-request/417/orphan.stl","uri":"https://storage.invalid/signed"}]}
                    """,
                    HttpStatusCode.Created)));
        var quotationHandler = new RecordingHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        var client = new QuotationFileClient(
            new MultiHttpClientFactory(
                ("files", new HttpClient(fileHandler) { BaseAddress = new Uri("http://files/") }),
                ("quotations", new HttpClient(quotationHandler) { BaseAddress = new Uri("http://quotations/") })),
            new StubTokenProvider("service-token"),
            NullLogger<QuotationFileClient>.Instance);

        var result = await client.UploadAndLinkAsync(
            417,
            Guid.NewGuid(),
            [Upload("orphan.stl", "model/stl", "solid test")],
            CancellationToken.None);

        Assert.False(result.Completed);
        var delete = Assert.Single(fileHandler.Requests, request => request.Method == HttpMethod.Delete);
        Assert.Equal("/Uploads", delete.RequestUri?.AbsolutePath);
        Assert.Contains("objectName=quotation-request%2F417%2Forphan.stl", delete.RequestUri?.Query, StringComparison.OrdinalIgnoreCase);
    }

    private static QuotationRequestSubmission Submission() =>
        new("Mali", "Ev", "mali@example.com", "020000000", "Thailand", "MALIEV", "0100000000000", "Need CNC parts");

    private static QuotationUpload Upload(string fileName, string contentType, string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        return new QuotationUpload(fileName, contentType, bytes.Length, () => new MemoryStream(bytes, writable: false));
    }

    private static HttpResponseMessage JsonResponse(string json, HttpStatusCode statusCode) =>
        new(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class StubTokenProvider(string? token) : IServiceAccessTokenProvider
    {
        public ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken) => ValueTask.FromResult(token);

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

    private sealed class MultiHttpClientFactory(params (string Name, HttpClient Client)[] clients) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => clients.Single(item => item.Name == name).Client;
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return await respond(request);
        }
    }
}
