namespace Legacy.Maliev.Web.Application;

public enum NotificationChannel
{
    Info,
    Manufacturing,
    NoReply,
    Support
}

public sealed record EmailNotification(
    string To,
    string Subject,
    string Body,
    string? ReplyTo,
    IReadOnlyList<string>? Cc,
    IReadOnlyList<string>? Bcc);

public sealed record NotificationResult(
    bool Sent,
    bool ServiceAvailable,
    bool Authorized)
{
    /// <summary>Gets source-owned in-process failure metadata; excluded from JSON contracts.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? FailureExceptionType { get; init; }
}

public interface INotificationClient
{
    Task<NotificationResult> SendAsync(
        NotificationChannel channel,
        EmailNotification notification,
        CancellationToken cancellationToken);

    Task<NotificationResult> SendIdempotentAsync(
        NotificationChannel channel,
        EmailNotification notification,
        Guid operationId,
        CancellationToken cancellationToken) => SendAsync(channel, notification, cancellationToken);
}
