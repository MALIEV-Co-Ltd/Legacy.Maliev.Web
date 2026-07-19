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
    public async Task CreateSession_SendsOnlyServiceJwtAndAcceptsExactCreatedContract()
    {
        var handler = new RecordingHandler(request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/file/v1/instant-quotation/sessions", request.RequestUri?.AbsolutePath);
            Assert.Equal(new AuthenticationHeaderValue("Bearer", "service-jwt"), request.Headers.Authorization);
            Assert.DoesNotContain(
                request.Headers,
                header => header.Key.StartsWith("X-", StringComparison.OrdinalIgnoreCase));
            Assert.Null(request.Content);

            var response = Json(
                HttpStatusCode.Created,
                $$"""
                {
                  "sessionId":"{{SessionId}}",
                  "sessionToken":"opaque-capability-000000000000000",
                  "expiresAt":"2026-07-20T12:00:00+00:00",
                  "maxUploadBytes":209715200,
                  "maxFilesPerSession":100,
                  "supportedExtensions":[".stl",".obj",".3mf",".step",".stp",".iges",".igs",".glb",".gltf"]
                }
                """);
            response.Headers.Location = new Uri($"/file/v1/instant-quotation/sessions/{SessionId}", UriKind.Relative);
            return Task.FromResult(response);
        });

        var result = await Create(handler, "service-jwt").CreateSessionAsync(CancellationToken.None);

        Assert.Equal(InstantQuotationOperationStatus.Succeeded, result.Status);
        Assert.Equal(SessionId, result.Capability?.SessionId);
        Assert.Equal("opaque-capability-000000000000000", result.Capability?.SessionToken);
        Assert.Equal(209715200, result.Capability?.MaxUploadBytes);
        Assert.Equal(100, result.Capability?.MaxFilesPerSession);
        Assert.Equal([".stl", ".obj", ".3mf", ".step", ".stp", ".iges", ".igs", ".glb", ".gltf"], result.Capability?.SupportedExtensions);
    }

    [Fact]
    public async Task CreateSession_RejectsUnknownFieldsOrWrongLocation()
    {
        var response = Json(
            HttpStatusCode.Created,
            $$"""{"sessionId":"{{SessionId}}","sessionToken":"opaque-capability-000000000000000","expiresAt":"2026-07-20T12:00:00Z","maxUploadBytes":209715200,"maxFilesPerSession":100,"supportedExtensions":[".stl",".obj",".3mf",".step",".stp",".iges",".igs",".glb",".gltf"],"storageCredential":"forbidden"}""");
        response.Headers.Location = new Uri("/file/v1/instant-quotation/sessions/wrong", UriKind.Relative);

        var result = await Create(new RecordingHandler(_ => Task.FromResult(response)), "service-jwt")
            .CreateSessionAsync(CancellationToken.None);

        Assert.Equal(InstantQuotationProblemCategory.Unexpected, result.ProblemCategory);
        Assert.Null(result.Capability);
    }

    [Fact]
    public async Task CreateSession_RejectsAbsoluteLocationEvenWhenPathMatches()
    {
        var response = Json(
            HttpStatusCode.Created,
            $$"""{"sessionId":"{{SessionId}}","sessionToken":"opaque-capability-000000000000000","expiresAt":"2026-07-20T12:00:00Z","maxUploadBytes":209715200,"maxFilesPerSession":100,"supportedExtensions":[".stl",".obj",".3mf",".step",".stp",".iges",".igs",".glb",".gltf"]}""");
        response.Headers.Location = new Uri(
            $"https://attacker.invalid/file/v1/instant-quotation/sessions/{SessionId}?token=forbidden#fragment");

        var result = await Create(new RecordingHandler(_ => Task.FromResult(response)), "service-jwt")
            .CreateSessionAsync(CancellationToken.None);

        Assert.Equal(InstantQuotationProblemCategory.Unexpected, result.ProblemCategory);
        Assert.Null(result.Capability);
    }

    [Fact]
    public async Task Upload_HashesBoundedRepeatableInputAndSendsExactMultipartContract()
    {
        var bytes = Encoding.UTF8.GetBytes("solid example\nendsolid example\n");
        var opens = 0;
        var upload = new InstantQuotationFileServiceUpload(
            "customer-part.stl",
            "model/stl",
            bytes.Length,
            _ =>
            {
                opens++;
                return ValueTask.FromResult<Stream>(new MemoryStream(bytes, writable: false));
            });
        var handler = new RecordingHandler(async request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal($"/file/v1/instant-quotation/sessions/{SessionId}/files", request.RequestUri?.AbsolutePath);
            Assert.Equal(new AuthenticationHeaderValue("Bearer", "service-jwt"), request.Headers.Authorization);
            Assert.Equal("opaque-capability-000000000000000", Assert.Single(request.Headers.GetValues("X-Quote-Session-Token")));
            Assert.Equal("upload-2222222222222222", Assert.Single(request.Headers.GetValues("Idempotency-Key")));
            Assert.Equal("dd75bf848e9a50028634377f2fad2b571fdd40b0461ee62359a95e27bbc62498", Assert.Single(request.Headers.GetValues("X-Content-SHA256")));
            var multipart = Assert.IsType<MultipartFormDataContent>(request.Content);
            var part = Assert.Single(multipart);
            Assert.Equal("files", part.Headers.ContentDisposition?.Name?.Trim('"'));
            Assert.Equal("customer-part.stl", part.Headers.ContentDisposition?.FileName?.Trim('"'));
            Assert.Equal("model/stl", part.Headers.ContentType?.MediaType);
            Assert.Equal(bytes, await part.ReadAsByteArrayAsync());

            var response = Json(
                HttpStatusCode.Created,
                $$"""{"fileId":"{{FileId}}","fileName":"customer-part.stl","contentType":"model/stl","sizeBytes":{{bytes.Length}},"sha256":"dd75bf848e9a50028634377f2fad2b571fdd40b0461ee62359a95e27bbc62498","status":"clean"}""");
            response.Headers.Location = new Uri($"/file/v1/instant-quotation/sessions/{SessionId}/files/{FileId}", UriKind.Relative);
            return response;
        });

        var result = await Create(handler, "service-jwt").UploadAsync(
            Capability(), upload, "upload-2222222222222222", CancellationToken.None);

        Assert.Equal(2, opens);
        Assert.Equal(InstantQuotationOperationStatus.Succeeded, result.Status);
        Assert.Equal(FileId, result.File?.FileId);
        Assert.Equal("clean", result.File?.Status);
    }

    [Fact]
    public async Task Upload_NormalizesParameterizedDeclaredMediaTypeToFileServiceResponse()
    {
        var response = Json(
            HttpStatusCode.Created,
            $$"""{"fileId":"{{FileId}}","fileName":"customer-part.stl","contentType":"model/stl","sizeBytes":3,"sha256":"039058c6f2c0cb492c533b0a4d14ef77cc0f78abccced5287d84a1a2011cfb81","status":"clean"}""");
        response.Headers.Location = new Uri(
            $"/file/v1/instant-quotation/sessions/{SessionId}/files/{FileId}",
            UriKind.Relative);
        var upload = new InstantQuotationFileServiceUpload(
            "customer-part.stl",
            "model/stl; charset=utf-8",
            3,
            _ => ValueTask.FromResult<Stream>(new MemoryStream([1, 2, 3], writable: false)));

        var result = await Create(new RecordingHandler(_ => Task.FromResult(response)), "service-jwt")
            .UploadAsync(Capability(), upload, "upload-2222222222222222", CancellationToken.None);

        Assert.Equal(InstantQuotationOperationStatus.Succeeded, result.Status);
        Assert.Equal("model/stl", result.File?.ContentType);
    }

    [Fact]
    public async Task Upload_RejectsLengthMismatchBeforeHttpIo()
    {
        var bytes = Encoding.UTF8.GetBytes("short");
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("FileService must not be called."));
        var upload = new InstantQuotationFileServiceUpload(
            "customer-part.stl",
            "model/stl",
            bytes.Length + 1,
            _ => ValueTask.FromResult<Stream>(new MemoryStream(bytes, writable: false)));

        var result = await Create(handler, "service-jwt").UploadAsync(
            Capability(), upload, "upload-2222222222222222", CancellationToken.None);

        Assert.Equal(InstantQuotationProblemCategory.Validation, result.ProblemCategory);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Upload_RejectsChangedSecondReadBeforeHttpIo()
    {
        var first = true;
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("FileService must not be called."));
        var upload = new InstantQuotationFileServiceUpload(
            "customer-part.stl",
            "model/stl",
            3,
            _ =>
            {
                var bytes = first ? new byte[] { 1, 2, 3 } : new byte[] { 1, 2, 4 };
                first = false;
                return ValueTask.FromResult<Stream>(new MemoryStream(bytes, writable: false));
            });

        var result = await Create(handler, "service-jwt").UploadAsync(
            Capability(), upload, "upload-2222222222222222", CancellationToken.None);

        Assert.Equal(InstantQuotationProblemCategory.Validation, result.ProblemCategory);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, "/file/v1/instant-quotation/sessions/11111111-1111-1111-1111-111111111111/files/22222222-2222-2222-2222-222222222222", false)]
    [InlineData(HttpStatusCode.Created, "/file/v1/instant-quotation/sessions/11111111-1111-1111-1111-111111111111/files/wrong", false)]
    [InlineData(HttpStatusCode.Created, "/file/v1/instant-quotation/sessions/11111111-1111-1111-1111-111111111111/files/22222222-2222-2222-2222-222222222222", true)]
    public async Task Upload_RejectsNonContractStatusLocationOrUnknownSuccessField(
        HttpStatusCode status,
        string location,
        bool includeUnknownField)
    {
        var body = $$"""
            {"fileId":"{{FileId}}","fileName":"customer-part.stl","contentType":"application/octet-stream","sizeBytes":3,"sha256":"039058c6f2c0cb492c533b0a4d14ef77cc0f78abccced5287d84a1a2011cfb81","status":"clean"{{(includeUnknownField ? ",\"bucket\":\"forbidden\"" : string.Empty)}}}
            """;
        var response = Json(status, body);
        response.Headers.Location = new Uri(location, UriKind.Relative);

        var result = await Create(new RecordingHandler(_ => Task.FromResult(response)), "service-jwt")
            .UploadAsync(Capability(), Upload([1, 2, 3]), "upload-2222222222222222", CancellationToken.None);

        Assert.Equal(InstantQuotationProblemCategory.Unexpected, result.ProblemCategory);
        Assert.Null(result.File);
    }

    [Fact]
    public async Task Remove_SendsTokenOnlyAndAcceptsExactNoContent()
    {
        var handler = new RecordingHandler(request =>
        {
            Assert.Equal(HttpMethod.Delete, request.Method);
            Assert.Equal($"/file/v1/instant-quotation/sessions/{SessionId}/files/{FileId}", request.RequestUri?.AbsolutePath);
            Assert.Equal(new AuthenticationHeaderValue("Bearer", "service-jwt"), request.Headers.Authorization);
            Assert.Equal("opaque-capability-000000000000000", Assert.Single(request.Headers.GetValues("X-Quote-Session-Token")));
            Assert.False(request.Headers.Contains("Idempotency-Key"));
            Assert.False(request.Headers.Contains("X-Content-SHA256"));
            Assert.Null(request.Content);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        });

        var result = await Create(handler, "service-jwt").RemoveAsync(
            Capability(), FileId, CancellationToken.None);

        Assert.Equal(InstantQuotationOperationStatus.Succeeded, result.Status);
        Assert.Equal(InstantQuotationProblemCategory.None, result.ProblemCategory);
    }

    [Fact]
    public async Task Remove_RejectsSuccessfulStatusWithBody()
    {
        var response = Json(HttpStatusCode.OK, "{}");
        var result = await Create(new RecordingHandler(_ => Task.FromResult(response)), "service-jwt")
            .RemoveAsync(Capability(), FileId, CancellationToken.None);

        Assert.Equal(InstantQuotationProblemCategory.Unexpected, result.ProblemCategory);
        Assert.Equal(InstantQuotationOperationStatus.Failed, result.Status);
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestEntityTooLarge, "payload_too_large", InstantQuotationProblemCategory.Validation, 0)]
    [InlineData(HttpStatusCode.UnsupportedMediaType, "unsupported_media_type", InstantQuotationProblemCategory.Validation, 0)]
    [InlineData(HttpStatusCode.UnprocessableEntity, "unsafe_content", InstantQuotationProblemCategory.Validation, 0)]
    [InlineData(HttpStatusCode.Conflict, "upload_in_progress", InstantQuotationProblemCategory.Conflict, 1)]
    [InlineData(HttpStatusCode.ServiceUnavailable, "outcome_unknown", InstantQuotationProblemCategory.DependencyUnavailable, 1)]
    [InlineData(HttpStatusCode.ServiceUnavailable, "dependency_unavailable", InstantQuotationProblemCategory.DependencyUnavailable, 2)]
    public async Task Upload_MapsExactProblemAndKeepsRetryMetadataInternal(
        HttpStatusCode status,
        string code,
        InstantQuotationProblemCategory expectedCategory,
        int expectedRetry)
    {
        var upload = Upload([1, 2, 3]);
        var result = await Create(
                new RecordingHandler(_ => Task.FromResult(Problem(status, code))),
                "service-jwt")
            .UploadAsync(Capability(), upload, "upload-2222222222222222", CancellationToken.None);

        Assert.Equal(expectedCategory, result.ProblemCategory);
        Assert.Equal(code, result.InternalProblemCode);
        Assert.Equal(expectedRetry, (int)result.RetryDisposition);
    }

    [Fact]
    public async Task Operations_RejectValidProblemPairsNotAdvertisedForThatOperation()
    {
        var create = await Create(
                new RecordingHandler(_ => Task.FromResult(Problem(HttpStatusCode.Conflict, "upload_in_progress"))),
                "service-jwt")
            .CreateSessionAsync(CancellationToken.None);
        AssertRejectedCrossOperationProblem(
            create.ProblemCategory,
            create.InternalProblemCode,
            create.RetryDisposition);

        var finalization = await Create(
                new RecordingHandler(_ => Task.FromResult(Problem(HttpStatusCode.RequestEntityTooLarge, "payload_too_large"))),
                "service-jwt")
            .FinalizeAsync(
                Capability(),
                417,
                [FileId],
                "finalize-2222222222222222",
                CancellationToken.None);
        AssertRejectedCrossOperationProblem(
            finalization.ProblemCategory,
            finalization.InternalProblemCode,
            finalization.RetryDisposition);

        var remove = await Create(
                new RecordingHandler(_ => Task.FromResult(Problem(HttpStatusCode.Conflict, "idempotency_conflict"))),
                "service-jwt")
            .RemoveAsync(Capability(), FileId, CancellationToken.None);
        AssertRejectedCrossOperationProblem(
            remove.ProblemCategory,
            remove.InternalProblemCode,
            remove.RetryDisposition);
    }

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

    private static InstantQuotationFileCapability Capability() => new(
        SessionId,
        "opaque-capability-000000000000000",
        DateTimeOffset.Parse("2026-07-20T12:00:00Z"),
        209715200,
        100,
        [".stl", ".obj", ".3mf", ".step", ".stp", ".iges", ".igs", ".glb", ".gltf"]);

    private static InstantQuotationFileServiceUpload Upload(byte[] bytes) => new(
        "customer-part.stl",
        "application/octet-stream",
        bytes.Length,
        _ => ValueTask.FromResult<Stream>(new MemoryStream(bytes, writable: false)));

    private static void AssertRejectedCrossOperationProblem(
        InstantQuotationProblemCategory category,
        string? code,
        InstantQuotationFileServiceRetryDisposition retryDisposition)
    {
        Assert.Equal(InstantQuotationProblemCategory.Unexpected, category);
        Assert.Null(code);
        Assert.Equal(InstantQuotationFileServiceRetryDisposition.None, retryDisposition);
    }

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
            "payload_too_large" => ("Upload is too large", "The uploaded file exceeds 209715200 bytes."),
            "unsupported_media_type" => ("Upload media type is unsupported", "The declared media type or file extension is not supported."),
            "unsafe_content" => ("Uploaded content is unsafe", "The uploaded content could not be accepted."),
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
