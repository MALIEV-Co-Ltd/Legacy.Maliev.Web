using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Legacy.Maliev.Web.Application;
using Microsoft.Extensions.Logging;
using Polly;

namespace Legacy.Maliev.Web.Infrastructure;

internal sealed class QuotationClient(
    IHttpClientFactory clientFactory,
    IServiceAccessTokenProvider tokenProvider,
    ILogger<QuotationClient> logger) : IQuotationClient
{
    private static readonly JsonSerializerOptions ExactLegacyJson = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = false,
        PropertyNamingPolicy = null,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public async Task<QuotationRequestResult> CreateRequestAsync(
        QuotationRequestSubmission submission,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return new QuotationRequestResult(null, true, false);
        }

        var token = await tokenProvider.GetAccessTokenAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(token))
        {
            logger.LogWarning("Quotation submission was rejected because service authentication was unavailable.");
            return new QuotationRequestResult(null, false, false);
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "quotationrequests/")
            {
                Content = JsonContent.Create(
                    new QuotationRequestPayload(
                        submission.FirstName,
                        submission.LastName,
                        submission.Email,
                        submission.TelephoneNumber,
                        submission.Country,
                        submission.CompanyName,
                        submission.TaxIdentification,
                        submission.Message,
                        null,
                        false),
                    options: ExactLegacyJson)
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("Idempotency-Key", idempotencyKey);
            using var response = await clientFactory.CreateClient("quotations")
                .SendAsync(request, cancellationToken);

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                tokenProvider.Invalidate(token);
                logger.LogWarning(
                    "Quotation service rejected the Web service identity with status {StatusCode}.",
                    response.StatusCode);
                return new QuotationRequestResult(null, true, false);
            }

            if (response.StatusCode != HttpStatusCode.Created
                || !string.Equals(
                    response.Content.Headers.ContentType?.MediaType,
                    "application/json",
                    StringComparison.OrdinalIgnoreCase))
            {
                return new QuotationRequestResult(null, true, true);
            }

            CreatedResource? created;
            try
            {
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                created = await JsonSerializer.DeserializeAsync<CreatedResource>(
                    stream,
                    ExactLegacyJson,
                    cancellationToken);
            }
            catch (JsonException)
            {
                return new QuotationRequestResult(null, true, true);
            }

            if (!IsValid(created, submission)
                || response.Headers.Location is null
                || !string.Equals(
                    response.Headers.Location.OriginalString,
                    $"/quotationrequests/{created!.Id}",
                    StringComparison.OrdinalIgnoreCase))
            {
                return new QuotationRequestResult(null, true, true);
            }

            return new QuotationRequestResult(created!.Id, true, true);
        }
        catch (Exception exception) when (IsTransient(exception, cancellationToken))
        {
            logger.LogWarning(exception, "Quotation service was unavailable while creating a public request.");
            return new QuotationRequestResult(null, false, true);
        }
    }

    private static bool IsTransient(Exception exception, CancellationToken cancellationToken) =>
        exception is HttpRequestException
        || exception is ExecutionRejectedException
        || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested);

    private static bool IsValid(CreatedResource? created, QuotationRequestSubmission submission) =>
        created is not null
        && created.Id > 0
        && string.Equals(created.FirstName, submission.FirstName, StringComparison.Ordinal)
        && string.Equals(created.LastName, submission.LastName, StringComparison.Ordinal)
        && string.Equals(created.Email, submission.Email, StringComparison.Ordinal)
        && string.Equals(created.TelephoneNumber, submission.TelephoneNumber, StringComparison.Ordinal)
        && string.Equals(created.Country, submission.Country, StringComparison.Ordinal)
        && string.Equals(created.CompanyName, submission.CompanyName, StringComparison.Ordinal)
        && string.Equals(created.TaxIdentification, submission.TaxIdentification, StringComparison.Ordinal)
        && string.Equals(created.Message, submission.Message, StringComparison.Ordinal)
        && string.IsNullOrEmpty(created.InternalComment)
        && !created.Done
        && created.CreatedDate != default;

    private sealed record QuotationRequestPayload(
        string FirstName,
        string LastName,
        string Email,
        string? TelephoneNumber,
        string Country,
        string? CompanyName,
        string? TaxIdentification,
        string Message,
        string? InternalComment,
        bool Done);

    private sealed record CreatedResource(
        [property: JsonPropertyName("Id")] int Id,
        [property: JsonPropertyName("FirstName")] string? FirstName,
        [property: JsonPropertyName("LastName")] string? LastName,
        [property: JsonPropertyName("Email")] string? Email,
        [property: JsonPropertyName("TelephoneNumber")] string? TelephoneNumber,
        [property: JsonPropertyName("Country")] string? Country,
        [property: JsonPropertyName("CompanyName")] string? CompanyName,
        [property: JsonPropertyName("TaxIdentification")] string? TaxIdentification,
        [property: JsonPropertyName("Message")] string? Message,
        [property: JsonPropertyName("Done")] bool Done,
        [property: JsonPropertyName("CreatedDate")] DateTimeOffset CreatedDate,
        [property: JsonPropertyName("InternalComment")] string? InternalComment = null,
        [property: JsonPropertyName("ModifiedDate")] DateTimeOffset? ModifiedDate = null);
}
