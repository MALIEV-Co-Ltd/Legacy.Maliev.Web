using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Legacy.Maliev.Web.Application;
using Polly;

namespace Legacy.Maliev.Web.Infrastructure;

internal sealed record InstantQuotationFileServiceCapability(
    Guid SessionId,
    string SessionToken);

internal sealed record InstantQuotationFileServiceFinalizationResult(
    InstantQuotationServiceStatus ServiceStatus,
    InstantQuotationAuthorizationStatus AuthorizationStatus,
    InstantQuotationOperationStatus Status,
    InstantQuotationProblemCategory ProblemCategory,
    int? QuotationRequestId,
    IReadOnlyList<InstantQuotationFileServiceFinalizedFile> Files);

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
    private static readonly JsonSerializerOptions StrictJson = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public async Task<InstantQuotationFileServiceFinalizationResult> FinalizeAsync(
        InstantQuotationFileServiceCapability capability,
        int quotationRequestId,
        IReadOnlyList<Guid> fileIds,
        string operationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(capability);
        ArgumentNullException.ThrowIfNull(fileIds);
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsValid(capability, quotationRequestId, fileIds, operationId))
        {
            return Failed(
                InstantQuotationServiceStatus.Available,
                InstantQuotationAuthorizationStatus.NotEvaluated,
                InstantQuotationProblemCategory.Validation);
        }

        var token = await tokenProvider.GetAccessTokenAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(token))
        {
            return Failed(
                InstantQuotationServiceStatus.Unavailable,
                InstantQuotationAuthorizationStatus.NotEvaluated,
                InstantQuotationProblemCategory.DependencyUnavailable);
        }

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"file/v1/instant-quotation/sessions/{capability.SessionId:D}/finalizations")
            {
                Content = JsonContent.Create(new FinalizationRequest(quotationRequestId, fileIds)),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("X-Quote-Session-Token", capability.SessionToken);
            request.Headers.Add("Idempotency-Key", operationId);

            using var response = await clientFactory.CreateClient("files")
                .SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return await ReadFailureAsync(response, token, cancellationToken);
            }

            if (response.StatusCode != HttpStatusCode.OK)
            {
                return Failed(
                    InstantQuotationServiceStatus.Available,
                    InstantQuotationAuthorizationStatus.Authorized,
                    InstantQuotationProblemCategory.Unexpected);
            }

            if (!string.Equals(
                response.Content.Headers.ContentType?.MediaType,
                "application/json",
                StringComparison.OrdinalIgnoreCase))
            {
                return Failed(
                    InstantQuotationServiceStatus.Available,
                    InstantQuotationAuthorizationStatus.Authorized,
                    InstantQuotationProblemCategory.Unexpected);
            }

            FinalizationResponse? payload;
            try
            {
                payload = await response.Content.ReadFromJsonAsync<FinalizationResponse>(
                    StrictJson,
                    cancellationToken);
            }
            catch (Exception exception) when (exception is JsonException or NotSupportedException)
            {
                return Failed(
                    InstantQuotationServiceStatus.Available,
                    InstantQuotationAuthorizationStatus.Authorized,
                    InstantQuotationProblemCategory.Unexpected);
            }
            if (!IsValid(payload, quotationRequestId, fileIds))
            {
                return Failed(
                    InstantQuotationServiceStatus.Available,
                    InstantQuotationAuthorizationStatus.Authorized,
                    InstantQuotationProblemCategory.Unexpected);
            }

            return new InstantQuotationFileServiceFinalizationResult(
                InstantQuotationServiceStatus.Available,
                InstantQuotationAuthorizationStatus.Authorized,
                InstantQuotationOperationStatus.Succeeded,
                InstantQuotationProblemCategory.None,
                payload!.QuotationRequestId,
                payload.Files.Select(file => ToResult(file!)).ToArray());
        }
        catch (Exception exception) when (IsTransient(exception, cancellationToken))
        {
            return Failed(
                InstantQuotationServiceStatus.Unavailable,
                InstantQuotationAuthorizationStatus.NotEvaluated,
                InstantQuotationProblemCategory.DependencyUnavailable);
        }
    }

    private async Task<InstantQuotationFileServiceFinalizationResult> ReadFailureAsync(
        HttpResponseMessage response,
        string token,
        CancellationToken cancellationToken)
    {
        ProblemResponse? problem = null;
        if (string.Equals(
            response.Content.Headers.ContentType?.MediaType,
            "application/problem+json",
            StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                problem = await response.Content.ReadFromJsonAsync<ProblemResponse>(
                    StrictJson,
                    cancellationToken);
            }
            catch (System.Text.Json.JsonException)
            {
            }
        }

        var category = IsValid(problem, response)
            ? MapProblem(problem!.Code)
            : InstantQuotationProblemCategory.Unexpected;
        var authorization = response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
            ? InstantQuotationAuthorizationStatus.Denied
            : InstantQuotationAuthorizationStatus.Authorized;
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            tokenProvider.Invalidate(token);
        }

        return Failed(InstantQuotationServiceStatus.Available, authorization, category);
    }

    private static bool IsValid(
        InstantQuotationFileServiceCapability capability,
        int quotationRequestId,
        IReadOnlyList<Guid> fileIds,
        string operationId) =>
        capability.SessionId != Guid.Empty
        && !string.IsNullOrWhiteSpace(capability.SessionToken)
        && capability.SessionToken.Length is >= 32 and <= 512
        && capability.SessionToken.All(IsPrintableAscii)
        && quotationRequestId > 0
        && fileIds.Count > 0
        && fileIds.All(fileId => fileId != Guid.Empty)
        && fileIds.Distinct().Count() == fileIds.Count
        && !string.IsNullOrEmpty(operationId)
        && operationId.Length is >= 16 and <= 128
        && operationId.All(IsPrintableAscii);

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
            && !string.IsNullOrEmpty(file.Sha256)
            && file.Sha256.Length == 64
            && file.Sha256.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f')
            && string.Equals(file.Status, "finalized", StringComparison.Ordinal));

    private static InstantQuotationFileServiceFinalizedFile ToResult(FinalizedFileResponse file) => new(
        file.FileId,
        file.Bucket,
        file.ObjectName,
        file.FileName,
        file.ContentType,
        file.SizeBytes,
        file.Sha256,
        file.Status);

    private static InstantQuotationProblemCategory MapProblem(string? code) => code switch
    {
        "validation_error" or "payload_too_large" or "unsupported_media_type" or "unsafe_content" =>
            InstantQuotationProblemCategory.Validation,
        "platform_authentication_required" or "permission_forbidden" or "session_forbidden" =>
            InstantQuotationProblemCategory.Authorization,
        "idempotency_conflict" or "upload_in_progress" => InstantQuotationProblemCategory.Conflict,
        "dependency_unavailable" or "outcome_unknown" => InstantQuotationProblemCategory.DependencyUnavailable,
        _ => InstantQuotationProblemCategory.Unexpected,
    };

    private static bool IsValid(ProblemResponse? problem, HttpResponseMessage response)
    {
        var responseStatus = response.StatusCode;
        if (problem is null
            || problem.Status != (int)responseStatus
            || !string.Equals(
                problem.Type,
                $"https://docs.maliev.com/problems/{problem.Code}",
                StringComparison.Ordinal))
        {
            return false;
        }

        var expected = (responseStatus, problem.Code) switch
        {
            (HttpStatusCode.BadRequest, "validation_error") => (
                "Instant quotation request is invalid",
                "One or more request values are invalid."),
            (HttpStatusCode.Unauthorized, "platform_authentication_required") => (
                "Platform authentication is required",
                "The caller must provide an accepted platform identity."),
            (HttpStatusCode.Forbidden, "permission_forbidden") => (
                "File operation is not permitted",
                "The caller does not have permission to perform this file operation."),
            (HttpStatusCode.Forbidden, "session_forbidden") => (
                "Upload session is not accessible",
                "The upload session could not be authorized."),
            (HttpStatusCode.Conflict, "idempotency_conflict") => (
                "Idempotency replay conflict",
                "The idempotency key is already associated with a different request."),
            (HttpStatusCode.Conflict, "upload_in_progress") => (
                "Instant quotation operation is in progress",
                "Retry the identical request with the same idempotency key."),
            (HttpStatusCode.ServiceUnavailable, "dependency_unavailable") => (
                "Instant quotation upload is unavailable",
                "A required upload dependency is temporarily unavailable."),
            (HttpStatusCode.ServiceUnavailable, "outcome_unknown") => (
                "Instant quotation outcome is unknown",
                "Retry the identical request with the same idempotency key."),
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

    private static InstantQuotationFileServiceFinalizationResult Failed(
        InstantQuotationServiceStatus serviceStatus,
        InstantQuotationAuthorizationStatus authorizationStatus,
        InstantQuotationProblemCategory category) => new(
        serviceStatus,
        authorizationStatus,
        InstantQuotationOperationStatus.Failed,
        category,
        null,
        []);

    private static bool IsPrintableAscii(char value) => value is >= '!' and <= '~';

    private static bool IsTransient(Exception exception, CancellationToken cancellationToken) =>
        exception is HttpRequestException
        || exception is ExecutionRejectedException
        || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested);

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
