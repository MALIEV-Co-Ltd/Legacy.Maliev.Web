using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Infrastructure;
using Polly.Timeout;

namespace Legacy.Maliev.Web.Tests;

public sealed class InstantQuotationFileServiceTransportTests
{
    private static readonly Guid SessionId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid FileId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task Finalize_SendsExactAuthenticatedContractAndAcceptsMatchingLegacyRequestId()
    {
        var handler = new RecordingHandler(async request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal(
                $"/file/v1/instant-quotation/sessions/{SessionId}/finalizations",
                request.RequestUri?.AbsolutePath);
            Assert.Equal(new AuthenticationHeaderValue("Bearer", "service-jwt"), request.Headers.Authorization);
            Assert.Equal("opaque-capability-000000000000000", Assert.Single(request.Headers.GetValues("X-Quote-Session-Token")));
            Assert.Equal("finalize-2222222222222222", Assert.Single(request.Headers.GetValues("Idempotency-Key")));

            using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            var properties = document.RootElement.EnumerateObject()
                .ToDictionary(property => property.Name, property => property.Value, StringComparer.Ordinal);
            Assert.Equal(["fileIds", "quotationRequestId"], properties.Keys.Order(StringComparer.Ordinal));
            Assert.Equal(417, properties["quotationRequestId"].GetInt32());
            Assert.Equal(FileId, Assert.Single(properties["fileIds"].EnumerateArray()).GetGuid());
            return Json(
                HttpStatusCode.OK,
                $$"""
                {
                  "quotationRequestId": 417,
                  "files": [{
                    "fileId": "{{FileId}}",
                    "bucket": "private-upload-bucket",
                    "objectName": "instant-quotation/417/file.stl",
                    "fileName": "customer.stl",
                    "contentType": "model/stl",
                    "sizeBytes": 123,
                    "sha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                    "status": "finalized"
                  }]
                }
                """);
        });
        var client = Create(handler, "service-jwt");

        var result = await client.FinalizeAsync(
            Capability(),
            417,
            [FileId],
            "finalize-2222222222222222",
            CancellationToken.None);

        Assert.Equal(InstantQuotationServiceStatus.Available, result.ServiceStatus);
        Assert.Equal(InstantQuotationAuthorizationStatus.Authorized, result.AuthorizationStatus);
        Assert.Equal(InstantQuotationOperationStatus.Succeeded, result.Status);
        Assert.Equal(InstantQuotationProblemCategory.None, result.ProblemCategory);
        Assert.Equal(417, result.QuotationRequestId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Finalize_NonpositiveLegacyRequestIdFailsValidationWithoutIo(int quotationRequestId)
    {
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("FileService must not be called."));
        var client = Create(handler, "service-jwt");

        var result = await client.FinalizeAsync(
            Capability(),
            quotationRequestId,
            [FileId],
            "finalize-2222222222222222",
            CancellationToken.None);

        Assert.Equal(InstantQuotationProblemCategory.Validation, result.ProblemCategory);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Finalize_NullOperationIdFailsValidationWithoutIo()
    {
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("FileService must not be called."));
        var client = Create(handler, "service-jwt");

        var result = await client.FinalizeAsync(
            Capability(),
            417,
            [FileId],
            null!,
            CancellationToken.None);

        Assert.Equal(InstantQuotationProblemCategory.Validation, result.ProblemCategory);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Finalize_MissingServiceJwtFailsClosedWithoutIo()
    {
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("FileService must not be called."));
        var client = Create(handler, token: null);

        var result = await client.FinalizeAsync(
            Capability(),
            417,
            [FileId],
            "finalize-2222222222222222",
            CancellationToken.None);

        Assert.Equal(InstantQuotationServiceStatus.Unavailable, result.ServiceStatus);
        Assert.Equal(InstantQuotationAuthorizationStatus.NotEvaluated, result.AuthorizationStatus);
        Assert.Equal(InstantQuotationProblemCategory.DependencyUnavailable, result.ProblemCategory);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Finalize_MismatchedEchoFailsClosedAsUnexpected()
    {
        var handler = new RecordingHandler(_ => Task.FromResult(Json(
            HttpStatusCode.OK,
            $$"""{"quotationRequestId":418,"files":[]}""")));
        var client = Create(handler, "service-jwt");

        var result = await client.FinalizeAsync(
            Capability(),
            417,
            [FileId],
            "finalize-2222222222222222",
            CancellationToken.None);

        Assert.Equal(InstantQuotationProblemCategory.Unexpected, result.ProblemCategory);
        Assert.Equal(InstantQuotationOperationStatus.Failed, result.Status);
        Assert.Null(result.QuotationRequestId);
    }

    [Fact]
    public async Task Finalize_UnknownSuccessFieldFailsClosed()
    {
        var handler = new RecordingHandler(_ => Task.FromResult(Json(
            HttpStatusCode.OK,
            $$"""
            {
              "quotationRequestId":417,
              "files":[{
                "fileId":"{{FileId}}",
                "bucket":"private",
                "objectName":"instant-quotation/417/file.stl",
                "fileName":"file.stl",
                "contentType":"model/stl",
                "sizeBytes":123,
                "sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                "status":"finalized",
                "downloadUrl":"https://forbidden.invalid"
              }]
            }
            """)));
        var client = Create(handler, "service-jwt");

        var result = await client.FinalizeAsync(
            Capability(),
            417,
            [FileId],
            "finalize-2222222222222222",
            CancellationToken.None);

        Assert.Equal(InstantQuotationProblemCategory.Unexpected, result.ProblemCategory);
        Assert.Empty(result.Files);
    }

    [Fact]
    public async Task Finalize_NullSuccessHashFailsClosedWithoutThrowing()
    {
        var response = Json(
            HttpStatusCode.OK,
            $$"""
            {"quotationRequestId":417,"files":[{"fileId":"{{FileId}}","bucket":"private","objectName":"instant-quotation/417/file.stl","fileName":"file.stl","contentType":"model/stl","sizeBytes":123,"sha256":null,"status":"finalized"}]}
            """);
        var client = Create(new RecordingHandler(_ => Task.FromResult(response)), "service-jwt");

        var result = await client.FinalizeAsync(
            Capability(),
            417,
            [FileId],
            "finalize-2222222222222222",
            CancellationToken.None);

        Assert.Equal(InstantQuotationProblemCategory.Unexpected, result.ProblemCategory);
    }

    [Fact]
    public async Task Finalize_NullFinalizedFileFailsClosedWithoutThrowing()
    {
        var response = Json(HttpStatusCode.OK, """{"quotationRequestId":417,"files":[null]}""");
        var client = Create(new RecordingHandler(_ => Task.FromResult(response)), "service-jwt");

        var result = await client.FinalizeAsync(
            Capability(),
            417,
            [FileId],
            "finalize-2222222222222222",
            CancellationToken.None);

        Assert.Equal(InstantQuotationProblemCategory.Unexpected, result.ProblemCategory);
    }

    [Fact]
    public async Task Finalize_UnknownProblemFieldFailsClosedInsteadOfTrustingKnownCode()
    {
        var response = Problem(HttpStatusCode.Conflict, "idempotency_conflict");
        response.Content = new StringContent(
            """
            {"type":"https://docs.maliev.com/problems/idempotency_conflict","title":"Safe","status":409,"detail":"Safe","code":"idempotency_conflict","providerCode":"secret"}
            """,
            Encoding.UTF8,
            "application/problem+json");
        var client = Create(new RecordingHandler(_ => Task.FromResult(response)), "service-jwt");

        var result = await client.FinalizeAsync(
            Capability(),
            417,
            [FileId],
            "finalize-2222222222222222",
            CancellationToken.None);

        Assert.Equal(InstantQuotationProblemCategory.Unexpected, result.ProblemCategory);
    }

    [Fact]
    public async Task Finalize_WrongSuccessContentTypeFailsClosed()
    {
        var response = Json(HttpStatusCode.OK, $$"""{"quotationRequestId":417,"files":[]}""");
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        var client = Create(new RecordingHandler(_ => Task.FromResult(response)), "service-jwt");

        var result = await client.FinalizeAsync(
            Capability(),
            417,
            [FileId],
            "finalize-2222222222222222",
            CancellationToken.None);

        Assert.Equal(InstantQuotationProblemCategory.Unexpected, result.ProblemCategory);
    }

    [Fact]
    public async Task Finalize_NonContractSuccessStatusFailsClosed()
    {
        var response = Json(
            HttpStatusCode.Created,
            $$"""
            {"quotationRequestId":417,"files":[{"fileId":"{{FileId}}","bucket":"private","objectName":"instant-quotation/417/file.stl","fileName":"file.stl","contentType":"model/stl","sizeBytes":123,"sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","status":"finalized"}]}
            """);
        var client = Create(new RecordingHandler(_ => Task.FromResult(response)), "service-jwt");

        var result = await client.FinalizeAsync(
            Capability(),
            417,
            [FileId],
            "finalize-2222222222222222",
            CancellationToken.None);

        Assert.Equal(InstantQuotationProblemCategory.Unexpected, result.ProblemCategory);
        Assert.Equal(InstantQuotationOperationStatus.Failed, result.Status);
    }

    [Fact]
    public async Task Finalize_OnlyUnauthorizedInvalidatesServiceJwt()
    {
        var unauthorizedToken = new StubTokenProvider("service-jwt");
        var unauthorized = Create(
            new RecordingHandler(_ => Task.FromResult(Problem(
                HttpStatusCode.Unauthorized,
                "platform_authentication_required"))),
            unauthorizedToken);
        await unauthorized.FinalizeAsync(
            Capability(), 417, [FileId], "finalize-2222222222222222", CancellationToken.None);

        var forbiddenToken = new StubTokenProvider("service-jwt");
        var forbidden = Create(
            new RecordingHandler(_ => Task.FromResult(Problem(
                HttpStatusCode.Forbidden,
                "permission_forbidden"))),
            forbiddenToken);
        await forbidden.FinalizeAsync(
            Capability(), 417, [FileId], "finalize-2222222222222222", CancellationToken.None);

        Assert.Equal(["service-jwt"], unauthorizedToken.Invalidated);
        Assert.Empty(forbiddenToken.Invalidated);
    }

    [Fact]
    public async Task Finalize_WrongFrozenProblemTextFailsClosed()
    {
        var response = Problem(HttpStatusCode.Conflict, "idempotency_conflict");
        response.Content = new StringContent(
            """
            {"type":"https://docs.maliev.com/problems/idempotency_conflict","title":"Changed title","status":409,"detail":"Changed detail","code":"idempotency_conflict"}
            """,
            Encoding.UTF8,
            "application/problem+json");
        var client = Create(new RecordingHandler(_ => Task.FromResult(response)), "service-jwt");

        var result = await client.FinalizeAsync(
            Capability(), 417, [FileId], "finalize-2222222222222222", CancellationToken.None);

        Assert.Equal(InstantQuotationProblemCategory.Unexpected, result.ProblemCategory);
    }

    [Fact]
    public async Task Finalize_UnauthorizedWithoutBearerChallengeFailsClosed()
    {
        var response = Problem(HttpStatusCode.Unauthorized, "platform_authentication_required");
        response.Headers.WwwAuthenticate.Clear();
        var client = Create(new RecordingHandler(_ => Task.FromResult(response)), "service-jwt");

        var result = await client.FinalizeAsync(
            Capability(), 417, [FileId], "finalize-2222222222222222", CancellationToken.None);

        Assert.Equal(InstantQuotationProblemCategory.Unexpected, result.ProblemCategory);
    }

    [Fact]
    public async Task Finalize_IdenticalReplayPreservesRequestIdSelectionAndIdempotencyKey()
    {
        var calls = new List<(string IdempotencyKey, string Body)>();
        var handler = new RecordingHandler(async request =>
        {
            calls.Add((
                Assert.Single(request.Headers.GetValues("Idempotency-Key")),
                await request.Content!.ReadAsStringAsync()));
            return ValidFinalization();
        });
        var client = Create(handler, "service-jwt");

        await client.FinalizeAsync(
            Capability(), 417, [FileId], "finalize-2222222222222222", CancellationToken.None);
        await client.FinalizeAsync(
            Capability(), 417, [FileId], "finalize-2222222222222222", CancellationToken.None);

        Assert.Equal(2, calls.Count);
        Assert.Equal(calls[0], calls[1]);
    }

    [Fact]
    public async Task Finalize_TransientFailureIsUnavailableButCallerCancellationPropagates()
    {
        var unavailable = Create(
            new RecordingHandler(_ => Task.FromException<HttpResponseMessage>(new HttpRequestException("offline"))),
            "service-jwt");
        var unavailableResult = await unavailable.FinalizeAsync(
            Capability(), 417, [FileId], "finalize-2222222222222222", CancellationToken.None);
        Assert.Equal(InstantQuotationServiceStatus.Unavailable, unavailableResult.ServiceStatus);
        Assert.Equal(InstantQuotationProblemCategory.DependencyUnavailable, unavailableResult.ProblemCategory);

        var rejected = Create(
            new RecordingHandler(_ => Task.FromException<HttpResponseMessage>(
                new TimeoutRejectedException("resilience pipeline rejected execution"))),
            "service-jwt");
        var rejectedResult = await rejected.FinalizeAsync(
            Capability(), 417, [FileId], "finalize-2222222222222222", CancellationToken.None);
        Assert.Equal(InstantQuotationServiceStatus.Unavailable, rejectedResult.ServiceStatus);
        Assert.Equal(InstantQuotationProblemCategory.DependencyUnavailable, rejectedResult.ProblemCategory);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => unavailable.FinalizeAsync(
            Capability(), 417, [FileId], "finalize-2222222222222222", cancellation.Token));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "validation_error", InstantQuotationProblemCategory.Validation)]
    [InlineData(HttpStatusCode.Unauthorized, "platform_authentication_required", InstantQuotationProblemCategory.Authorization)]
    [InlineData(HttpStatusCode.Forbidden, "permission_forbidden", InstantQuotationProblemCategory.Authorization)]
    [InlineData(HttpStatusCode.Forbidden, "session_forbidden", InstantQuotationProblemCategory.Authorization)]
    [InlineData(HttpStatusCode.Conflict, "idempotency_conflict", InstantQuotationProblemCategory.Conflict)]
    [InlineData(HttpStatusCode.Conflict, "upload_in_progress", InstantQuotationProblemCategory.Conflict)]
    [InlineData(HttpStatusCode.ServiceUnavailable, "dependency_unavailable", InstantQuotationProblemCategory.DependencyUnavailable)]
    [InlineData(HttpStatusCode.ServiceUnavailable, "outcome_unknown", InstantQuotationProblemCategory.DependencyUnavailable)]
    public async Task Finalize_MapsOnlyStableProblemCodes(
        HttpStatusCode status,
        string code,
        InstantQuotationProblemCategory expected)
    {
        var handler = new RecordingHandler(_ => Task.FromResult(Problem(status, code)));
        var client = Create(handler, "service-jwt");

        var result = await client.FinalizeAsync(
            Capability(),
            417,
            [FileId],
            "finalize-2222222222222222",
            CancellationToken.None);

        Assert.Equal(InstantQuotationServiceStatus.Available, result.ServiceStatus);
        Assert.Equal(expected, result.ProblemCategory);
        Assert.Equal(InstantQuotationOperationStatus.Failed, result.Status);
    }

    private static InstantQuotationFileServiceCapability Capability() => new(
        SessionId,
        "opaque-capability-000000000000000");

    private static InstantQuotationFileServiceTransport Create(RecordingHandler handler, string? token) => Create(
        handler,
        new StubTokenProvider(token));

    private static InstantQuotationFileServiceTransport Create(
        RecordingHandler handler,
        StubTokenProvider tokenProvider) => new(
        new NamedHttpClientFactory(new HttpClient(handler) { BaseAddress = new Uri("http://files/") }),
        tokenProvider);

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    private static HttpResponseMessage Problem(HttpStatusCode status, string code)
    {
        var (title, detail) = code switch
        {
            "validation_error" => ("Instant quotation request is invalid", "One or more request values are invalid."),
            "platform_authentication_required" => ("Platform authentication is required", "The caller must provide an accepted platform identity."),
            "permission_forbidden" => ("File operation is not permitted", "The caller does not have permission to perform this file operation."),
            "session_forbidden" => ("Upload session is not accessible", "The upload session could not be authorized."),
            "idempotency_conflict" => ("Idempotency replay conflict", "The idempotency key is already associated with a different request."),
            "upload_in_progress" => ("Instant quotation operation is in progress", "Retry the identical request with the same idempotency key."),
            "dependency_unavailable" => ("Instant quotation upload is unavailable", "A required upload dependency is temporarily unavailable."),
            "outcome_unknown" => ("Instant quotation outcome is unknown", "Retry the identical request with the same idempotency key."),
            _ => ("Safe title", "Safe detail"),
        };
        var response = new HttpResponseMessage(status)
        {
            Content = new StringContent(
                $$"""{"type":"https://docs.maliev.com/problems/{{code}}","title":"{{title}}","status":{{(int)status}},"detail":"{{detail}}","code":"{{code}}"}""",
                Encoding.UTF8,
                "application/problem+json"),
        };
        if (status == HttpStatusCode.Unauthorized)
        {
            response.Headers.WwwAuthenticate.Add(new AuthenticationHeaderValue("Bearer"));
        }

        return response;
    }

    private static HttpResponseMessage ValidFinalization() => Json(
        HttpStatusCode.OK,
        $$"""
        {"quotationRequestId":417,"files":[{"fileId":"{{FileId}}","bucket":"private","objectName":"instant-quotation/417/file.stl","fileName":"file.stl","contentType":"model/stl","sizeBytes":123,"sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","status":"finalized"}]}
        """);

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
            Assert.Equal("files", name);
            return client;
        }
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
