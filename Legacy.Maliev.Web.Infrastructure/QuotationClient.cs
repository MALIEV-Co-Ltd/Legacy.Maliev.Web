using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Legacy.Maliev.Web.Application;
using Microsoft.Extensions.Logging;

namespace Legacy.Maliev.Web.Infrastructure;

internal sealed class QuotationClient(
    IHttpClientFactory clientFactory,
    IServiceAccessTokenProvider tokenProvider,
    ILogger<QuotationClient> logger) : IQuotationClient
{
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
            using var request = new HttpRequestMessage(HttpMethod.Post, "QuotationRequests")
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
                        null))
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

            response.EnsureSuccessStatusCode();
            var created = await response.Content.ReadFromJsonAsync<CreatedResource>(cancellationToken);
            return new QuotationRequestResult(created?.Id, true, true);
        }
        catch (Exception exception) when (IsTransient(exception, cancellationToken))
        {
            logger.LogWarning(exception, "Quotation service was unavailable while creating a public request.");
            return new QuotationRequestResult(null, false, true);
        }
    }

    private static bool IsTransient(Exception exception, CancellationToken cancellationToken) =>
        exception is HttpRequestException
        || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested);

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
        bool? Done);

    private sealed record CreatedResource(int Id);
}
