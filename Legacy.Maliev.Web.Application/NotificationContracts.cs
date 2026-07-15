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
    bool Authorized);

public interface INotificationClient
{
    Task<NotificationResult> SendAsync(
        NotificationChannel channel,
        EmailNotification notification,
        CancellationToken cancellationToken);
}
