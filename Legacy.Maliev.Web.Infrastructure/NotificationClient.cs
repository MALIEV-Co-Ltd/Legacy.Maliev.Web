using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Legacy.Maliev.Web.Application;
using Microsoft.Extensions.Logging;

namespace Legacy.Maliev.Web.Infrastructure;

internal sealed class NotificationClient(
    IHttpClientFactory clientFactory,
    IServiceAccessTokenProvider tokenProvider,
    ILogger<NotificationClient> logger) : INotificationClient
{
    public async Task<NotificationResult> SendAsync(
        NotificationChannel channel,
        EmailNotification notification,
        CancellationToken cancellationToken)
    {
        var token = await tokenProvider.GetAccessTokenAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(token))
        {
            logger.LogWarning("Email notification was rejected because service authentication was unavailable.");
            return new NotificationResult(false, false, false);
        }

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"notifications/v1/email/{channel}")
            {
                Content = JsonContent.Create(notification)
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await clientFactory.CreateClient("notifications")
                .SendAsync(request, cancellationToken);

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                tokenProvider.Invalidate(token);
                logger.LogWarning(
                    "Notification service rejected the Web service identity with status {StatusCode}.",
                    response.StatusCode);
                return new NotificationResult(false, true, false);
            }

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Notification service rejected an email with status {StatusCode}.",
                    response.StatusCode);
                return new NotificationResult(
                    false,
                    (int)response.StatusCode < (int)HttpStatusCode.InternalServerError,
                    true);
            }

            return new NotificationResult(true, true, true);
        }
        catch (Exception exception) when (IsTransient(exception, cancellationToken))
        {
            logger.LogWarning(exception, "Notification service was unavailable while sending email.");
            return new NotificationResult(false, false, true);
        }
    }

    private static bool IsTransient(Exception exception, CancellationToken cancellationToken) =>
        exception is HttpRequestException
        || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested);
}
