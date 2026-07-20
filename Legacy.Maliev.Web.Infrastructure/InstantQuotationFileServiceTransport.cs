using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Legacy.Maliev.Web.Application;
using Polly;

namespace Legacy.Maliev.Web.Infrastructure;

internal enum InstantQuotationFileServiceRetryDisposition
{
    None,
    RetryIdentical,
    RetryWithBackoff,
}

internal sealed record InstantQuotationFileServiceUpload(
    string FileName,
    string ContentType,
    long ContentLength,
    Func<CancellationToken, ValueTask<Stream>> OpenReadAsync);

internal sealed record InstantQuotationFileServiceSessionResult(
    InstantQuotationServiceStatus ServiceStatus,
    InstantQuotationAuthorizationStatus AuthorizationStatus,
    InstantQuotationOperationStatus Status,
    InstantQuotationProblemCategory ProblemCategory,
    InstantQuotationFileCapability? Capability,
    string? InternalProblemCode,
    InstantQuotationFileServiceRetryDisposition RetryDisposition);

internal sealed record InstantQuotationFileServiceUploadResult(
    InstantQuotationServiceStatus ServiceStatus,
    InstantQuotationAuthorizationStatus AuthorizationStatus,
    InstantQuotationOperationStatus Status,
    InstantQuotationProblemCategory ProblemCategory,
    InstantQuotationFileServiceUploadedFile? File,
    string? InternalProblemCode,
    InstantQuotationFileServiceRetryDisposition RetryDisposition);

internal sealed record InstantQuotationFileServiceUploadedFile(
    Guid FileId,
    string FileName,
    string ContentType,
    long SizeBytes,
    string Sha256,
    string Status);

internal sealed record InstantQuotationFileServiceOperationResult(
    InstantQuotationServiceStatus ServiceStatus,
    InstantQuotationAuthorizationStatus AuthorizationStatus,
    InstantQuotationOperationStatus Status,
    InstantQuotationProblemCategory ProblemCategory,
    string? InternalProblemCode,
    InstantQuotationFileServiceRetryDisposition RetryDisposition);

internal sealed record InstantQuotationFileServiceFinalizationResult(
    InstantQuotationServiceStatus ServiceStatus,
    InstantQuotationAuthorizationStatus AuthorizationStatus,
    InstantQuotationOperationStatus Status,
    InstantQuotationProblemCategory ProblemCategory,
    int? QuotationRequestId,
    IReadOnlyList<InstantQuotationFileServiceFinalizedFile> Files,
    string? InternalProblemCode = null,
    InstantQuotationFileServiceRetryDisposition RetryDisposition = InstantQuotationFileServiceRetryDisposition.None);

internal sealed record InstantQuotationFileServiceFinalizedFile(
    Guid FileId,
    string Bucket,
    string ObjectName,
    string FileName,
    string ContentType,
    long SizeBytes,
    string Sha256,
    string Status);

internal sealed class InstantQuotationFileServiceTransport(
    IHttpClientFactory clientFactory,
    IServiceAccessTokenProvider tokenProvider)
{
    private const long MaximumUploadBytes = 200L * 1024 * 1024;
    private const int MaximumFilesPerSession = 100;
    private static readonly string[] SupportedExtensions =
        [".stl", ".obj", ".3mf", ".step", ".stp", ".iges", ".igs", ".glb", ".gltf"];
    private static readonly JsonSerializerOptions StrictJson = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public async Task<InstantQuotationFileServiceSessionResult> CreateSessionAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var token = await GetTokenAsync(cancellationToken);
        if (token is null)
        {
            return FailedSession(MissingTokenFailure());
        }

        try
        {
            using var request = Authenticated(HttpMethod.Post, "file/v1/instant-quotation/sessions", token);
            using var response = await SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return FailedSession(await ReadFailureAsync(
                    response,
                    token,
                    FileServiceOperation.CreateSession,
                    cancellationToken));
            }

            if (response.StatusCode != HttpStatusCode.Created || !IsJson(response))
            {
                return FailedSession(UnexpectedFailure());
            }

            var payload = await ReadStrictAsync<CreateSessionResponse>(response, cancellationToken);
            if (!IsValid(payload)
                || !HasExactLocation(response, $"/file/v1/instant-quotation/sessions/{payload!.SessionId:D}"))
            {
                return FailedSession(UnexpectedFailure());
            }

            return new(
                InstantQuotationServiceStatus.Available,
                InstantQuotationAuthorizationStatus.Authorized,
                InstantQuotationOperationStatus.Succeeded,
                InstantQuotationProblemCategory.None,
                new InstantQuotationFileCapability(
                    payload.SessionId,
                    payload.SessionToken,
                    payload.ExpiresAt,
                    payload.MaxUploadBytes,
                    payload.MaxFilesPerSession,
                    payload.SupportedExtensions),
                null,
                InstantQuotationFileServiceRetryDisposition.None);
        }
        catch (Exception exception) when (IsTransient(exception, cancellationToken))
        {
            return FailedSession(TransientFailure());
        }
    }

    public async Task<InstantQuotationFileServiceUploadResult> UploadAsync(
        InstantQuotationFileCapability capability,
        InstantQuotationFileServiceUpload upload,
        string operationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(capability);
        ArgumentNullException.ThrowIfNull(upload);
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsValid(capability)
            || !IsValid(upload, capability)
            || !IsValidOperationId(operationId))
        {
            return FailedUpload(ValidationFailure());
        }

        PreparedUpload? prepared;
        try
        {
            prepared = await PrepareUploadAsync(upload, cancellationToken);
        }
        catch (Exception exception) when (IsTransient(exception, cancellationToken))
        {
            return FailedUpload(TransientFailure());
        }

        if (prepared is null)
        {
            return FailedUpload(ValidationFailure());
        }

        await using (prepared.Stream)
        {
            var token = await GetTokenAsync(cancellationToken);
            if (token is null)
            {
                return FailedUpload(MissingTokenFailure());
            }

            try
            {
                using var request = Authenticated(
                    HttpMethod.Post,
                    $"file/v1/instant-quotation/sessions/{capability.SessionId:D}/files",
                    token);
                request.Headers.Add("X-Quote-Session-Token", capability.SessionToken);
                request.Headers.Add("Idempotency-Key", operationId);
                request.Headers.Add("X-Content-SHA256", prepared.Sha256);
                using var multipart = new MultipartFormDataContent();
                var fileContent = new StreamContent(prepared.Stream);
                fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(upload.ContentType);
                fileContent.Headers.ContentLength = upload.ContentLength;
                multipart.Add(fileContent, "files", upload.FileName);
                request.Content = multipart;

                using var response = await SendAsync(request, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    return FailedUpload(await ReadFailureAsync(
                        response,
                        token,
                        FileServiceOperation.Upload,
                        cancellationToken));
                }

                if (response.StatusCode != HttpStatusCode.Created || !IsJson(response))
                {
                    return FailedUpload(UnexpectedFailure());
                }

                var payload = await ReadStrictAsync<UploadResponse>(response, cancellationToken);
                if (!IsValid(payload, upload, prepared.Sha256)
                    || !HasExactLocation(
                        response,
                        $"/file/v1/instant-quotation/sessions/{capability.SessionId:D}/files/{payload!.FileId:D}"))
                {
                    return FailedUpload(UnexpectedFailure());
                }

                return new(
                    InstantQuotationServiceStatus.Available,
                    InstantQuotationAuthorizationStatus.Authorized,
                    InstantQuotationOperationStatus.Succeeded,
                    InstantQuotationProblemCategory.None,
                    new InstantQuotationFileServiceUploadedFile(
                        payload.FileId,
                        payload.FileName,
                        payload.ContentType,
                        payload.SizeBytes,
                        payload.Sha256,
                        payload.Status),
                    null,
                    InstantQuotationFileServiceRetryDisposition.None);
            }
            catch (Exception exception) when (IsTransient(exception, cancellationToken))
            {
                return FailedUpload(TransientFailure());
            }
        }
    }

    public async Task<InstantQuotationFileServiceOperationResult> RemoveAsync(
        InstantQuotationFileCapability capability,
        Guid fileId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(capability);
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsValid(capability) || fileId == Guid.Empty)
        {
            return FailedOperation(ValidationFailure());
        }

        var token = await GetTokenAsync(cancellationToken);
        if (token is null)
        {
            return FailedOperation(MissingTokenFailure());
        }

        try
        {
            using var request = Authenticated(
                HttpMethod.Delete,
                $"file/v1/instant-quotation/sessions/{capability.SessionId:D}/files/{fileId:D}",
                token);
            request.Headers.Add("X-Quote-Session-Token", capability.SessionToken);
            using var response = await SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return FailedOperation(await ReadFailureAsync(
                    response,
                    token,
                    FileServiceOperation.Remove,
                    cancellationToken));
            }

            if (response.StatusCode != HttpStatusCode.NoContent
                || response.Content.Headers.ContentType is not null
                || response.Content.Headers.ContentLength is > 0)
            {
                return FailedOperation(UnexpectedFailure());
            }

            return new(
                InstantQuotationServiceStatus.Available,
                InstantQuotationAuthorizationStatus.Authorized,
                InstantQuotationOperationStatus.Succeeded,
                InstantQuotationProblemCategory.None,
                null,
                InstantQuotationFileServiceRetryDisposition.None);
        }
        catch (Exception exception) when (IsTransient(exception, cancellationToken))
        {
            return FailedOperation(TransientFailure());
        }
    }

    public async Task<InstantQuotationFileServiceFinalizationResult> FinalizeAsync(
        InstantQuotationFileCapability capability,
        int quotationRequestId,
        IReadOnlyList<Guid> fileIds,
        string operationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(capability);
        ArgumentNullException.ThrowIfNull(fileIds);
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsValid(capability)
            || quotationRequestId <= 0
            || fileIds.Count is <= 0 or > MaximumFilesPerSession
            || fileIds.Any(fileId => fileId == Guid.Empty)
            || fileIds.Distinct().Count() != fileIds.Count
            || !IsValidOperationId(operationId))
        {
            return FailedFinalization(ValidationFailure());
        }

        var token = await GetTokenAsync(cancellationToken);
        if (token is null)
        {
            return FailedFinalization(MissingTokenFailure());
        }

        try
        {
            using var request = Authenticated(
                HttpMethod.Post,
                $"file/v1/instant-quotation/sessions/{capability.SessionId:D}/finalizations",
                token);
            request.Content = JsonContent.Create(new FinalizationRequest(quotationRequestId, fileIds));
            request.Headers.Add("X-Quote-Session-Token", capability.SessionToken);
            request.Headers.Add("Idempotency-Key", operationId);

            using var response = await SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return FailedFinalization(await ReadFailureAsync(
                    response,
                    token,
                    FileServiceOperation.Finalize,
                    cancellationToken));
            }

            if (response.StatusCode != HttpStatusCode.OK || !IsJson(response))
            {
                return FailedFinalization(UnexpectedFailure());
            }

            var payload = await ReadStrictAsync<FinalizationResponse>(response, cancellationToken);
            if (!IsValid(payload, quotationRequestId, fileIds))
            {
                return FailedFinalization(UnexpectedFailure());
            }

            return new(
                InstantQuotationServiceStatus.Available,
                InstantQuotationAuthorizationStatus.Authorized,
                InstantQuotationOperationStatus.Succeeded,
                InstantQuotationProblemCategory.None,
                payload!.QuotationRequestId,
                payload.Files.Select(file => ToResult(file!)).ToArray());
        }
        catch (Exception exception) when (IsTransient(exception, cancellationToken))
        {
            return FailedFinalization(TransientFailure());
        }
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken) =>
        await clientFactory.CreateClient("files").SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

    private async ValueTask<string?> GetTokenAsync(CancellationToken cancellationToken)
    {
        var token = await tokenProvider.GetAccessTokenAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(token) ? null : token;
    }

    private async Task<Failure> ReadFailureAsync(
        HttpResponseMessage response,
        string token,
        FileServiceOperation operation,
        CancellationToken cancellationToken)
    {
        ProblemResponse? problem = null;
        if (string.Equals(
            response.Content.Headers.ContentType?.MediaType,
            "application/problem+json",
            StringComparison.OrdinalIgnoreCase))
        {
            problem = await ReadStrictAsync<ProblemResponse>(response, cancellationToken);
        }

        var valid = IsValid(problem, response) && IsAllowed(operation, response.StatusCode, problem!.Code);
        var code = valid ? problem!.Code : null;
        var category = code is not null ? MapProblem(code) : InstantQuotationProblemCategory.Unexpected;
        var authorization = response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
            ? InstantQuotationAuthorizationStatus.Denied
            : InstantQuotationAuthorizationStatus.Authorized;
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            tokenProvider.Invalidate(token);
        }

        return new Failure(
            InstantQuotationServiceStatus.Available,
            authorization,
            category,
            code,
            code is not null ? MapRetry(code) : InstantQuotationFileServiceRetryDisposition.None);
    }

    private static async Task<T?> ReadStrictAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(StrictJson, cancellationToken);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            return default;
        }
    }

    private static async Task<PreparedUpload?> PrepareUploadAsync(
        InstantQuotationFileServiceUpload upload,
        CancellationToken cancellationToken)
    {
        var firstHash = await HashExactAsync(
            await upload.OpenReadAsync(cancellationToken),
            upload.ContentLength,
            cancellationToken);
        if (firstHash is null)
        {
            return null;
        }

        var stream = await upload.OpenReadAsync(cancellationToken);
        if (!stream.CanRead || !stream.CanSeek)
        {
            await stream.DisposeAsync();
            return null;
        }

        var initialPosition = stream.Position;
        var secondHash = await HashExactAsync(stream, upload.ContentLength, cancellationToken, dispose: false);
        if (!string.Equals(firstHash, secondHash, StringComparison.Ordinal))
        {
            await stream.DisposeAsync();
            return null;
        }

        stream.Position = initialPosition;
        return new PreparedUpload(stream, firstHash);
    }

    private static async Task<string?> HashExactAsync(
        Stream stream,
        long expectedLength,
        CancellationToken cancellationToken,
        bool dispose = true)
    {
        try
        {
            if (!stream.CanRead)
            {
                return null;
            }

            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var remaining = expectedLength;
            var buffer = new byte[64 * 1024];
            while (remaining > 0)
            {
                var read = await stream.ReadAsync(
                    buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)),
                    cancellationToken);
                if (read == 0)
                {
                    return null;
                }

                hash.AppendData(buffer, 0, read);
                remaining -= read;
            }

            if (await stream.ReadAsync(buffer.AsMemory(0, 1), cancellationToken) != 0)
            {
                return null;
            }

            return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        }
        finally
        {
            if (dispose)
            {
                await stream.DisposeAsync();
            }
        }
    }

    private static HttpRequestMessage Authenticated(HttpMethod method, string route, string token)
    {
        var request = new HttpRequestMessage(method, route);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static bool IsValid(InstantQuotationFileCapability capability) =>
        capability.SessionId != Guid.Empty
        && IsValidSecret(capability.SessionToken)
        && capability.ExpiresAt != default
        && capability.MaxUploadBytes == MaximumUploadBytes
        && capability.MaxFilesPerSession == MaximumFilesPerSession
        && capability.SupportedExtensions.SequenceEqual(SupportedExtensions, StringComparer.Ordinal);

    private static bool IsValid(CreateSessionResponse? response) =>
        response is not null
        && response.SessionId != Guid.Empty
        && IsValidSecret(response.SessionToken)
        && response.ExpiresAt != default
        && response.MaxUploadBytes == MaximumUploadBytes
        && response.MaxFilesPerSession == MaximumFilesPerSession
        && response.SupportedExtensions is not null
        && response.SupportedExtensions.SequenceEqual(SupportedExtensions, StringComparer.Ordinal);

    private static bool IsValid(
        InstantQuotationFileServiceUpload upload,
        InstantQuotationFileCapability capability)
    {
        if (string.IsNullOrWhiteSpace(upload.FileName)
            || upload.FileName.Length > 255
            || !string.Equals(Path.GetFileName(upload.FileName), upload.FileName, StringComparison.Ordinal)
            || upload.FileName.Any(character => character is < ' ' or '\u007f')
            || upload.ContentLength <= 0
            || upload.ContentLength > Math.Min(MaximumUploadBytes, capability.MaxUploadBytes)
            || upload.OpenReadAsync is null)
        {
            return false;
        }

        var extension = Path.GetExtension(upload.FileName);
        if (!capability.SupportedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        return MediaTypeHeaderValue.TryParse(upload.ContentType, out var mediaType)
            && !string.IsNullOrWhiteSpace(mediaType.MediaType)
            && !upload.ContentType.Contains('\r')
            && !upload.ContentType.Contains('\n');
    }

    private static bool IsValid(UploadResponse? response, InstantQuotationFileServiceUpload upload, string sha256) =>
        response is not null
        && response.FileId != Guid.Empty
        && string.Equals(response.FileName, upload.FileName, StringComparison.Ordinal)
        && string.Equals(
            response.ContentType,
            MediaTypeHeaderValue.Parse(upload.ContentType).MediaType,
            StringComparison.OrdinalIgnoreCase)
        && response.SizeBytes == upload.ContentLength
        && string.Equals(response.Sha256, sha256, StringComparison.Ordinal)
        && string.Equals(response.Status, "clean", StringComparison.Ordinal);

    private static bool IsValid(
        FinalizationResponse? response,
        int quotationRequestId,
        IReadOnlyList<Guid> fileIds) =>
        response is not null
        && response.QuotationRequestId == quotationRequestId
        && response.Files is not null
        && response.Files.Count == fileIds.Count
        && response.Files.All(file => file is not null)
        && response.Files.Select(file => file!.FileId).Order().SequenceEqual(fileIds.Order())
        && response.Files.All(file =>
            file is not null
            && file.FileId != Guid.Empty
            && !string.IsNullOrWhiteSpace(file.Bucket)
            && !string.IsNullOrWhiteSpace(file.ObjectName)
            && !string.IsNullOrWhiteSpace(file.FileName)
            && !string.IsNullOrWhiteSpace(file.ContentType)
            && file.SizeBytes > 0
            && IsLowerSha256(file.Sha256)
            && string.Equals(file.Status, "finalized", StringComparison.Ordinal));

    private static bool IsValid(ProblemResponse? problem, HttpResponseMessage response)
    {
        var responseStatus = response.StatusCode;
        if (problem is null
            || problem.Status != (int)responseStatus
            || !string.Equals(problem.Type, $"https://docs.maliev.com/problems/{problem.Code}", StringComparison.Ordinal))
        {
            return false;
        }

        var expected = (responseStatus, problem.Code) switch
        {
            (HttpStatusCode.BadRequest, "validation_error") =>
                ("Instant quotation request is invalid", "One or more request values are invalid."),
            (HttpStatusCode.Unauthorized, "platform_authentication_required") =>
                ("Platform authentication is required", "The caller must provide an accepted platform identity."),
            (HttpStatusCode.Forbidden, "permission_forbidden") =>
                ("File operation is not permitted", "The caller does not have permission to perform this file operation."),
            (HttpStatusCode.Forbidden, "session_forbidden") =>
                ("Upload session is not accessible", "The upload session could not be authorized."),
            (HttpStatusCode.Conflict, "idempotency_conflict") =>
                ("Idempotency replay conflict", "The idempotency key is already associated with a different request."),
            (HttpStatusCode.Conflict, "upload_in_progress") =>
                ("Instant quotation operation is in progress", "Retry the identical request with the same idempotency key."),
            (HttpStatusCode.RequestEntityTooLarge, "payload_too_large") =>
                ("Upload is too large", "The uploaded file exceeds 209715200 bytes."),
            (HttpStatusCode.UnsupportedMediaType, "unsupported_media_type") =>
                ("Upload media type is unsupported", "The declared media type or file extension is not supported."),
            (HttpStatusCode.UnprocessableEntity, "unsafe_content") =>
                ("Uploaded content is unsafe", "The uploaded content could not be accepted."),
            (HttpStatusCode.ServiceUnavailable, "dependency_unavailable") =>
                ("Instant quotation upload is unavailable", "A required upload dependency is temporarily unavailable."),
            (HttpStatusCode.ServiceUnavailable, "outcome_unknown") =>
                ("Instant quotation outcome is unknown", "Retry the identical request with the same idempotency key."),
            _ => ((string Title, string Detail)?)null,
        };
        if (expected is null
            || !string.Equals(problem.Title, expected.Value.Title, StringComparison.Ordinal)
            || !string.Equals(problem.Detail, expected.Value.Detail, StringComparison.Ordinal))
        {
            return false;
        }

        var challenges = response.Headers.WwwAuthenticate.ToArray();
        return responseStatus == HttpStatusCode.Unauthorized
            ? challenges is [{ Scheme: "Bearer", Parameter: null }]
            : challenges.Length == 0;
    }

    private static bool IsAllowed(
        FileServiceOperation operation,
        HttpStatusCode status,
        string code) => (operation, status, code) switch
        {
            (FileServiceOperation.CreateSession, HttpStatusCode.Unauthorized, "platform_authentication_required") => true,
            (FileServiceOperation.CreateSession, HttpStatusCode.Forbidden, "permission_forbidden") => true,
            (FileServiceOperation.CreateSession, HttpStatusCode.ServiceUnavailable, "dependency_unavailable") => true,

            (FileServiceOperation.Upload, HttpStatusCode.BadRequest, "validation_error") => true,
            (FileServiceOperation.Upload, HttpStatusCode.Unauthorized, "platform_authentication_required") => true,
            (FileServiceOperation.Upload, HttpStatusCode.Forbidden, "permission_forbidden" or "session_forbidden") => true,
            (FileServiceOperation.Upload, HttpStatusCode.Conflict, "idempotency_conflict" or "upload_in_progress") => true,
            (FileServiceOperation.Upload, HttpStatusCode.RequestEntityTooLarge, "payload_too_large") => true,
            (FileServiceOperation.Upload, HttpStatusCode.UnsupportedMediaType, "unsupported_media_type") => true,
            (FileServiceOperation.Upload, HttpStatusCode.UnprocessableEntity, "unsafe_content") => true,
            (FileServiceOperation.Upload, HttpStatusCode.ServiceUnavailable, "dependency_unavailable" or "outcome_unknown") => true,

            (FileServiceOperation.Finalize, HttpStatusCode.BadRequest, "validation_error") => true,
            (FileServiceOperation.Finalize, HttpStatusCode.Unauthorized, "platform_authentication_required") => true,
            (FileServiceOperation.Finalize, HttpStatusCode.Forbidden, "permission_forbidden" or "session_forbidden") => true,
            (FileServiceOperation.Finalize, HttpStatusCode.Conflict, "idempotency_conflict" or "upload_in_progress") => true,
            (FileServiceOperation.Finalize, HttpStatusCode.ServiceUnavailable, "dependency_unavailable" or "outcome_unknown") => true,

            (FileServiceOperation.Remove, HttpStatusCode.BadRequest, "validation_error") => true,
            (FileServiceOperation.Remove, HttpStatusCode.Unauthorized, "platform_authentication_required") => true,
            (FileServiceOperation.Remove, HttpStatusCode.Forbidden, "permission_forbidden" or "session_forbidden") => true,
            (FileServiceOperation.Remove, HttpStatusCode.Conflict, "upload_in_progress") => true,
            (FileServiceOperation.Remove, HttpStatusCode.ServiceUnavailable, "dependency_unavailable" or "outcome_unknown") => true,
            _ => false,
        };

    private static bool HasExactLocation(HttpResponseMessage response, string expectedPath) =>
        response.Headers.Location is { } location
        && !location.IsAbsoluteUri
        && string.Equals(location.OriginalString, expectedPath, StringComparison.Ordinal);

    private static bool IsJson(HttpResponseMessage response) => string.Equals(
        response.Content.Headers.ContentType?.MediaType,
        "application/json",
        StringComparison.OrdinalIgnoreCase);

    private static bool IsValidSecret(string value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length is >= 32 and <= 512
        && value.All(IsPrintableAscii);

    private static bool IsValidOperationId(string value) =>
        !string.IsNullOrEmpty(value)
        && value.Length is >= 16 and <= 128
        && value.All(IsPrintableAscii);

    private static bool IsLowerSha256(string value) =>
        !string.IsNullOrEmpty(value)
        && value.Length == 64
        && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static InstantQuotationProblemCategory MapProblem(string code) => code switch
    {
        "validation_error" or "payload_too_large" or "unsupported_media_type" or "unsafe_content" =>
            InstantQuotationProblemCategory.Validation,
        "platform_authentication_required" or "permission_forbidden" or "session_forbidden" =>
            InstantQuotationProblemCategory.Authorization,
        "idempotency_conflict" or "upload_in_progress" => InstantQuotationProblemCategory.Conflict,
        "dependency_unavailable" or "outcome_unknown" => InstantQuotationProblemCategory.DependencyUnavailable,
        _ => InstantQuotationProblemCategory.Unexpected,
    };

    private static InstantQuotationFileServiceRetryDisposition MapRetry(string code) => code switch
    {
        "upload_in_progress" or "outcome_unknown" => InstantQuotationFileServiceRetryDisposition.RetryIdentical,
        "dependency_unavailable" => InstantQuotationFileServiceRetryDisposition.RetryWithBackoff,
        _ => InstantQuotationFileServiceRetryDisposition.None,
    };

    private static InstantQuotationFileServiceFinalizedFile ToResult(FinalizedFileResponse file) => new(
        file.FileId,
        file.Bucket,
        file.ObjectName,
        file.FileName,
        file.ContentType,
        file.SizeBytes,
        file.Sha256,
        file.Status);

    private static InstantQuotationFileServiceSessionResult FailedSession(Failure failure) => new(
        failure.ServiceStatus,
        failure.AuthorizationStatus,
        InstantQuotationOperationStatus.Failed,
        failure.Category,
        null,
        failure.Code,
        failure.RetryDisposition);

    private static InstantQuotationFileServiceUploadResult FailedUpload(Failure failure) => new(
        failure.ServiceStatus,
        failure.AuthorizationStatus,
        InstantQuotationOperationStatus.Failed,
        failure.Category,
        null,
        failure.Code,
        failure.RetryDisposition);

    private static InstantQuotationFileServiceOperationResult FailedOperation(Failure failure) => new(
        failure.ServiceStatus,
        failure.AuthorizationStatus,
        InstantQuotationOperationStatus.Failed,
        failure.Category,
        failure.Code,
        failure.RetryDisposition);

    private static InstantQuotationFileServiceFinalizationResult FailedFinalization(Failure failure) => new(
        failure.ServiceStatus,
        failure.AuthorizationStatus,
        InstantQuotationOperationStatus.Failed,
        failure.Category,
        null,
        [],
        failure.Code,
        failure.RetryDisposition);

    private static Failure ValidationFailure() => new(
        InstantQuotationServiceStatus.Available,
        InstantQuotationAuthorizationStatus.NotEvaluated,
        InstantQuotationProblemCategory.Validation,
        null,
        InstantQuotationFileServiceRetryDisposition.None);

    private static Failure UnexpectedFailure() => new(
        InstantQuotationServiceStatus.Available,
        InstantQuotationAuthorizationStatus.Authorized,
        InstantQuotationProblemCategory.Unexpected,
        null,
        InstantQuotationFileServiceRetryDisposition.None);

    private static Failure MissingTokenFailure() => new(
        InstantQuotationServiceStatus.Unavailable,
        InstantQuotationAuthorizationStatus.NotEvaluated,
        InstantQuotationProblemCategory.DependencyUnavailable,
        null,
        InstantQuotationFileServiceRetryDisposition.RetryWithBackoff);

    private static Failure TransientFailure() => new(
        InstantQuotationServiceStatus.Unavailable,
        InstantQuotationAuthorizationStatus.NotEvaluated,
        InstantQuotationProblemCategory.DependencyUnavailable,
        null,
        InstantQuotationFileServiceRetryDisposition.RetryWithBackoff);

    private static bool IsPrintableAscii(char value) => value is >= '!' and <= '~';

    private static bool IsTransient(Exception exception, CancellationToken cancellationToken) =>
        exception is HttpRequestException
        || exception is IOException
        || exception is ExecutionRejectedException
        || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested);

    private sealed record PreparedUpload(Stream Stream, string Sha256);

    private enum FileServiceOperation
    {
        CreateSession,
        Upload,
        Finalize,
        Remove,
    }

    private sealed record Failure(
        InstantQuotationServiceStatus ServiceStatus,
        InstantQuotationAuthorizationStatus AuthorizationStatus,
        InstantQuotationProblemCategory Category,
        string? Code,
        InstantQuotationFileServiceRetryDisposition RetryDisposition);

    private sealed record CreateSessionResponse(
        [property: JsonPropertyName("sessionId")] Guid SessionId,
        [property: JsonPropertyName("sessionToken")] string SessionToken,
        [property: JsonPropertyName("expiresAt")] DateTimeOffset ExpiresAt,
        [property: JsonPropertyName("maxUploadBytes")] long MaxUploadBytes,
        [property: JsonPropertyName("maxFilesPerSession")] int MaxFilesPerSession,
        [property: JsonPropertyName("supportedExtensions")] IReadOnlyList<string> SupportedExtensions);

    private sealed record UploadResponse(
        [property: JsonPropertyName("fileId")] Guid FileId,
        [property: JsonPropertyName("fileName")] string FileName,
        [property: JsonPropertyName("contentType")] string ContentType,
        [property: JsonPropertyName("sizeBytes")] long SizeBytes,
        [property: JsonPropertyName("sha256")] string Sha256,
        [property: JsonPropertyName("status")] string Status);

    private sealed record FinalizationRequest(
        [property: JsonPropertyName("quotationRequestId")] int QuotationRequestId,
        [property: JsonPropertyName("fileIds")] IReadOnlyList<Guid> FileIds);

    private sealed record FinalizationResponse(
        [property: JsonPropertyName("quotationRequestId")] int QuotationRequestId,
        [property: JsonPropertyName("files")] IReadOnlyList<FinalizedFileResponse?> Files);

    private sealed record FinalizedFileResponse(
        [property: JsonPropertyName("fileId")] Guid FileId,
        [property: JsonPropertyName("bucket")] string Bucket,
        [property: JsonPropertyName("objectName")] string ObjectName,
        [property: JsonPropertyName("fileName")] string FileName,
        [property: JsonPropertyName("contentType")] string ContentType,
        [property: JsonPropertyName("sizeBytes")] long SizeBytes,
        [property: JsonPropertyName("sha256")] string Sha256,
        [property: JsonPropertyName("status")] string Status);

    private sealed record ProblemResponse(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("status")] int Status,
        [property: JsonPropertyName("detail")] string Detail,
        [property: JsonPropertyName("code")] string Code);
}
