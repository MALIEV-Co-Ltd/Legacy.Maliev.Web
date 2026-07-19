using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Infrastructure;

namespace Legacy.Maliev.Web.Tests;

public sealed class InstantQuotationRequestFileClientTests
{
    private static readonly InstantQuotationFinalizedFile File = new(
        Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
        "private quotation files",
        "instant-quotation/417/a+b.stl",
        "customer-part.stl",
        "model/stl",
        123,
        new string('a', 64));

    [Fact]
    public async Task Link_UsesExactAuthenticatedReplaySafeLegacyRequestFileContract()
    {
        var handler = new RecordingHandler(request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/quotationrequests/417/files", request.RequestUri!.AbsolutePath);
            Assert.Equal(
                "?bucket=private%20quotation%20files&objectName=instant-quotation%2F417%2Fa%2Bb.stl",
                request.RequestUri.Query);
            Assert.Equal(new AuthenticationHeaderValue("Bearer", "service-token"), request.Headers.Authorization);
            Assert.Equal(new string('b', 64), Assert.Single(request.Headers.GetValues("Idempotency-Key")));
            Assert.Null(request.Content);
            var response = Json(
                HttpStatusCode.Created,
                """
                {"Id":91,"RequestId":417,"Bucket":"private quotation files","ObjectName":"instant-quotation/417/a+b.stl","CreatedDate":"2026-07-19T08:00:00Z"}
                """);
            response.Headers.Location = new Uri("/quotationrequests/files/91", UriKind.Relative);
            return Task.FromResult(response);
        });
        var client = Create(handler, new StubTokenProvider("service-token"));

        var result = await client.LinkAsync(417, File, new string('b', 64), CancellationToken.None);

        Assert.Equal(InstantQuotationServiceStatus.Available, result.ServiceStatus);
        Assert.Equal(InstantQuotationAuthorizationStatus.Authorized, result.AuthorizationStatus);
        Assert.Equal(InstantQuotationOperationStatus.Succeeded, result.Status);
        Assert.Equal(InstantQuotationProblemCategory.None, result.ProblemCategory);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "storage_coordinate_required", InstantQuotationProblemCategory.Validation)]
    [InlineData(HttpStatusCode.BadRequest, "idempotency_key_too_long", InstantQuotationProblemCategory.Validation)]
    [InlineData(HttpStatusCode.Conflict, "idempotency_key_conflict", InstantQuotationProblemCategory.Conflict)]
    [InlineData(HttpStatusCode.ServiceUnavailable, "idempotency_store_unavailable", InstantQuotationProblemCategory.DependencyUnavailable)]
    public async Task Link_MapsOnlyReviewedStableProblemCodes(
        HttpStatusCode status,
        string code,
        InstantQuotationProblemCategory expected)
    {
        var client = Create(
            new RecordingHandler(_ => Task.FromResult(Problem(status, code))),
            new StubTokenProvider("service-token"));

        var result = await client.LinkAsync(417, File, new string('b', 64), CancellationToken.None);

        Assert.Equal(InstantQuotationServiceStatus.Available, result.ServiceStatus);
        Assert.Equal(expected, result.ProblemCategory);
        Assert.Equal(InstantQuotationOperationStatus.Failed, result.Status);
    }

    [Fact]
    public async Task Link_RejectsUnknownSuccessFieldsAndMismatchedCoordinates()
    {
        var responses = new Queue<HttpResponseMessage>(
        [
            Json(
                HttpStatusCode.Created,
                """{"Id":91,"RequestId":417,"Bucket":"private quotation files","ObjectName":"instant-quotation/417/a+b.stl","CreatedDate":"2026-07-19T08:00:00Z","DownloadUrl":"https://forbidden.invalid"}"""),
            Json(
                HttpStatusCode.Created,
                """{"Id":91,"RequestId":417,"Bucket":"other","ObjectName":"instant-quotation/417/a+b.stl","CreatedDate":"2026-07-19T08:00:00Z"}"""),
        ]);
        var client = Create(
            new RecordingHandler(_ => Task.FromResult(responses.Dequeue())),
            new StubTokenProvider("service-token"));

        var unknown = await client.LinkAsync(417, File, new string('b', 64), CancellationToken.None);
        var mismatch = await client.LinkAsync(417, File, new string('c', 64), CancellationToken.None);

        Assert.Equal(InstantQuotationProblemCategory.Unexpected, unknown.ProblemCategory);
        Assert.Equal(InstantQuotationProblemCategory.Unexpected, mismatch.ProblemCategory);
    }

    [Fact]
    public async Task Link_MissingTokenAndInvalidInputFailClosedWithoutIo()
    {
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("No HTTP expected."));
        var missing = Create(handler, new StubTokenProvider(null));
        var invalid = Create(handler, new StubTokenProvider("service-token"));

        var missingResult = await missing.LinkAsync(417, File, new string('b', 64), CancellationToken.None);
        var invalidResult = await invalid.LinkAsync(0, File, "short", CancellationToken.None);
        var oversizedBucket = await invalid.LinkAsync(
            417,
            File with { Bucket = new string('b', 51) },
            new string('b', 64),
            CancellationToken.None);

        Assert.Equal(InstantQuotationServiceStatus.Unavailable, missingResult.ServiceStatus);
        Assert.Equal(InstantQuotationProblemCategory.DependencyUnavailable, missingResult.ProblemCategory);
        Assert.Equal(InstantQuotationProblemCategory.Validation, invalidResult.ProblemCategory);
        Assert.Equal(InstantQuotationProblemCategory.Validation, oversizedBucket.ProblemCategory);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Link_TimeoutIsRetryableAndUnauthorizedInvalidatesOnlyThatToken()
    {
        var unavailable = Create(
            new RecordingHandler(_ => Task.FromException<HttpResponseMessage>(new HttpRequestException("offline"))),
            new StubTokenProvider("service-token"));
        var rejected = Create(
            new RecordingHandler(_ => Task.FromException<HttpResponseMessage>(
                new Polly.Timeout.TimeoutRejectedException("timeout"))),
            new StubTokenProvider("service-token"));
        var token = new StubTokenProvider("expired-token");
        var unauthorized = Create(
            new RecordingHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized))),
            token);

        var unavailableResult = await unavailable.LinkAsync(
            417,
            File,
            new string('b', 64),
            CancellationToken.None);
        var rejectedResult = await rejected.LinkAsync(
            417,
            File,
            new string('b', 64),
            CancellationToken.None);
        var unauthorizedResult = await unauthorized.LinkAsync(
            417,
            File,
            new string('b', 64),
            CancellationToken.None);

        Assert.Equal(InstantQuotationServiceStatus.Unavailable, unavailableResult.ServiceStatus);
        Assert.Equal(InstantQuotationProblemCategory.DependencyUnavailable, unavailableResult.ProblemCategory);
        Assert.Equal(InstantQuotationServiceStatus.Unavailable, rejectedResult.ServiceStatus);
        Assert.Equal(InstantQuotationAuthorizationStatus.Denied, unauthorizedResult.AuthorizationStatus);
        Assert.Equal(["expired-token"], token.Invalidated);
    }

    private static InstantQuotationRequestFileClient Create(
        RecordingHandler handler,
        StubTokenProvider tokenProvider) => new(
            new NamedHttpClientFactory(new HttpClient(handler) { BaseAddress = new Uri("http://quotations/") }),
            tokenProvider);

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    private static HttpResponseMessage Problem(HttpStatusCode status, string code) => new(status)
    {
        Content = new StringContent(
            $$"""{"type":"https://docs.maliev.com/problems/{{code}}","title":"Safe","status":{{(int)status}},"code":"{{code}}"}""",
            Encoding.UTF8,
            "application/problem+json"),
    };

    private sealed class StubTokenProvider(string? token) : IServiceAccessTokenProvider
    {
        public List<string> Invalidated { get; } = [];

        public ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken) => ValueTask.FromResult(token);

        public void Invalidate(string value) => Invalidated.Add(value);
    }

    private sealed class NamedHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            Assert.Equal("quotations", name);
            return client;
        }
    }

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
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
