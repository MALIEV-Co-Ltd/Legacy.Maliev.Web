using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Legacy.Maliev.Web.Application;
using Microsoft.Extensions.Logging;

namespace Legacy.Maliev.Web.Infrastructure;

internal sealed class ContactClient(
    IHttpClientFactory clientFactory,
    IServiceAccessTokenProvider tokenProvider,
    ILogger<ContactClient> logger) : IContactClient
{
    public async Task<ContactSubmissionResult> SubmitAsync(
        ContactSubmission submission,
        CancellationToken cancellationToken)
    {
        var token = await tokenProvider.GetAccessTokenAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(token))
        {
            logger.LogWarning("Contact submission was rejected because service authentication was unavailable.");
            return new ContactSubmissionResult(null, false, false);
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "Messages")
            {
                Content = JsonContent.Create(submission)
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await clientFactory.CreateClient("contacts")
                .SendAsync(request, cancellationToken);

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                tokenProvider.Invalidate(token);
                logger.LogWarning(
                    "Contact service rejected the Web service identity with status {StatusCode}.",
                    response.StatusCode);
                return new ContactSubmissionResult(null, true, false);
            }

            if (response.StatusCode != HttpStatusCode.Created)
            {
                return new ContactSubmissionResult(
                    null,
                    (int)response.StatusCode < 500 && !response.IsSuccessStatusCode,
                    true);
            }

            if (!string.Equals(
                    response.Content.Headers.ContentType?.MediaType,
                    "application/json",
                    StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning("Contact service returned an unexpected success content type.");
                return new ContactSubmissionResult(null, false, true);
            }

            var created = await response.Content.ReadFromJsonAsync<ContactRequestResponse>(cancellationToken);
            return created is { Id: > 0 }
                ? new ContactSubmissionResult(created.Id, true, true)
                : new ContactSubmissionResult(null, false, true);
        }
        catch (Exception exception) when (IsTransient(exception, cancellationToken))
        {
            logger.LogWarning(exception, "Contact service was unavailable while submitting the public contact form.");
            return new ContactSubmissionResult(null, false, true);
        }
    }

    private static bool IsTransient(Exception exception, CancellationToken cancellationToken) =>
        exception is HttpRequestException or System.Text.Json.JsonException or NotSupportedException
        || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested);

    private sealed record ContactRequestResponse(int Id);
}
