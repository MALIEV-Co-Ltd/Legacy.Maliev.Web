using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Legacy.Maliev.Web.Application;
using Polly;

namespace Legacy.Maliev.Web.Infrastructure;

internal sealed class InstantQuotationRequestFileClient(
    IHttpClientFactory clientFactory,
    IServiceAccessTokenProvider tokenProvider) : IInstantQuotationRequestFileClient
{
    private static readonly JsonSerializerOptions StrictJson = new()
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public async Task<InstantQuotationRequestFileLinkResult> LinkAsync(
        int quotationRequestId,
        InstantQuotationFinalizedFile file,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);
        cancellationToken.ThrowIfCancellationRequested();
        if (quotationRequestId <= 0 || !IsValid(file) || !IsValidIdempotencyKey(idempotencyKey))
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
            var route = $"quotationrequests/{quotationRequestId}/files"
                + $"?bucket={Uri.EscapeDataString(file.Bucket)}"
                + $"&objectName={Uri.EscapeDataString(file.ObjectName)}";
            using var request = new HttpRequestMessage(HttpMethod.Post, route);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("Idempotency-Key", idempotencyKey);
            using var response = await clientFactory.CreateClient("quotations").SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (response.StatusCode == HttpStatusCode.Created)
            {
                if (!IsJson(response))
                {
                    return Unexpected();
                }

                var payload = await ReadStrictAsync<QuotationRequestFileResponse>(response, cancellationToken);
                if (!IsValid(payload, quotationRequestId, file)
                    || response.Headers.Location is null
                    || !string.Equals(
                        response.Headers.Location.OriginalString,
                        $"/quotationrequests/files/{payload!.Id}",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return Unexpected();
                }

                return new InstantQuotationRequestFileLinkResult(
                    InstantQuotationServiceStatus.Available,
                    InstantQuotationAuthorizationStatus.Authorized,
                    InstantQuotationOperationStatus.Succeeded,
                    InstantQuotationProblemCategory.None);
            }

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    tokenProvider.Invalidate(token);
                }

                return Failed(
                    InstantQuotationServiceStatus.Available,
                    InstantQuotationAuthorizationStatus.Denied,
                    InstantQuotationProblemCategory.Authorization);
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return Failed(
                    InstantQuotationServiceStatus.Available,
                    InstantQuotationAuthorizationStatus.Authorized,
                    InstantQuotationProblemCategory.Conflict);
            }

            var problem = IsProblem(response)
                ? await ReadStrictAsync<ProblemResponse>(response, cancellationToken)
                : null;
            return TryMapProblem(response.StatusCode, problem, out var category)
                ? Failed(
                    InstantQuotationServiceStatus.Available,
                    InstantQuotationAuthorizationStatus.Authorized,
                    category)
                : Unexpected();
        }
        catch (Exception exception) when (IsTransient(exception, cancellationToken))
        {
            return Failed(
                InstantQuotationServiceStatus.Unavailable,
                InstantQuotationAuthorizationStatus.NotEvaluated,
                InstantQuotationProblemCategory.DependencyUnavailable);
        }
    }

    private static async Task<T?> ReadStrictAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await JsonSerializer.DeserializeAsync<T>(stream, StrictJson, cancellationToken);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            return default;
        }
    }

    private static bool IsValid(InstantQuotationFinalizedFile file) =>
        file.FileId != Guid.Empty
        && IsBounded(file.Bucket, 50)
        && IsBounded(file.ObjectName, 2_048)
        && IsBounded(file.FileName, 512)
        && IsBounded(file.ContentType, 256)
        && file.SizeBytes > 0
        && file.Sha256 is { Length: 64 }
        && file.Sha256.All(static character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool IsValid(
        QuotationRequestFileResponse? payload,
        int quotationRequestId,
        InstantQuotationFinalizedFile file) =>
        payload is not null
        && payload.Id > 0
        && payload.RequestId == quotationRequestId
        && string.Equals(payload.Bucket, file.Bucket, StringComparison.Ordinal)
        && string.Equals(payload.ObjectName, file.ObjectName, StringComparison.Ordinal)
        && payload.CreatedDate != default;

    private static bool TryMapProblem(
        HttpStatusCode status,
        ProblemResponse? problem,
        out InstantQuotationProblemCategory category)
    {
        category = InstantQuotationProblemCategory.Unexpected;
        if (problem is null
            || problem.Status != (int)status
            || string.IsNullOrWhiteSpace(problem.Type)
            || string.IsNullOrWhiteSpace(problem.Title))
        {
            return false;
        }

        category = (status, problem.Code) switch
        {
            (HttpStatusCode.BadRequest, "storage_coordinate_required" or "idempotency_key_too_long") =>
                InstantQuotationProblemCategory.Validation,
            (HttpStatusCode.Conflict, "idempotency_key_conflict") => InstantQuotationProblemCategory.Conflict,
            (HttpStatusCode.ServiceUnavailable, "idempotency_store_unavailable") =>
                InstantQuotationProblemCategory.DependencyUnavailable,
            _ => InstantQuotationProblemCategory.Unexpected,
        };
        return category != InstantQuotationProblemCategory.Unexpected;
    }

    private static bool IsValidIdempotencyKey(string? value) => value is { Length: >= 16 and <= 128 }
        && value.All(static character => character is >= '!' and <= '~');

    private static bool IsBounded(string? value, int maximum) =>
        value is not null && value.Length is > 0 && value.Length <= maximum && !string.IsNullOrWhiteSpace(value);

    private static bool IsJson(HttpResponseMessage response) => string.Equals(
        response.Content.Headers.ContentType?.MediaType,
        "application/json",
        StringComparison.OrdinalIgnoreCase);

    private static bool IsProblem(HttpResponseMessage response) => string.Equals(
        response.Content.Headers.ContentType?.MediaType,
        "application/problem+json",
        StringComparison.OrdinalIgnoreCase);

    private static bool IsTransient(Exception exception, CancellationToken cancellationToken) =>
        exception is HttpRequestException
        || exception is ExecutionRejectedException
        || exception is TimeoutException
        || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested);

    private static InstantQuotationRequestFileLinkResult Unexpected() => Failed(
        InstantQuotationServiceStatus.Available,
        InstantQuotationAuthorizationStatus.Authorized,
        InstantQuotationProblemCategory.Unexpected);

    private static InstantQuotationRequestFileLinkResult Failed(
        InstantQuotationServiceStatus serviceStatus,
        InstantQuotationAuthorizationStatus authorizationStatus,
        InstantQuotationProblemCategory problemCategory) => new(
            serviceStatus,
            authorizationStatus,
            InstantQuotationOperationStatus.Failed,
            problemCategory);

    private sealed record QuotationRequestFileResponse(
        [property: JsonPropertyName("Id")] int Id,
        [property: JsonPropertyName("RequestId")] int RequestId,
        [property: JsonPropertyName("Bucket")] string? Bucket,
        [property: JsonPropertyName("ObjectName")] string? ObjectName,
        [property: JsonPropertyName("CreatedDate")] DateTimeOffset CreatedDate,
        [property: JsonPropertyName("ModifiedDate")] DateTimeOffset? ModifiedDate = null);

    private sealed record ProblemResponse(
        [property: JsonPropertyName("type")] string? Type,
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("status")] int Status,
        [property: JsonPropertyName("detail")] string? Detail,
        [property: JsonPropertyName("code")] string? Code);
}
