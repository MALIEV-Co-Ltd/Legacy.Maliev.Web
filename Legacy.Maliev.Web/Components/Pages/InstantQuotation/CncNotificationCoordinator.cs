using System.Security.Cryptography;
using System.Text;
using Legacy.Maliev.Web.Application;
using static Legacy.Maliev.Web.Components.Pages.InstantQuotation.CncSubmissionAdmission;

namespace Legacy.Maliev.Web.Components.Pages.InstantQuotation;

internal sealed record CncNotificationFinalizedItem(string ModelObjectName, string? DrawingObjectName = null);

internal sealed record CncNotificationDeliveryResult(
    bool LinksResolved,
    bool Composed,
    NotificationResult? Customer,
    NotificationResult? Manufacturing)
{
    internal bool Complete => LinksResolved && Composed
        && Customer?.Sent == true && Manufacturing?.Sent == true;
}

/// <summary>Resolves finalized files and delivers the two source-compatible CNC notifications.</summary>
internal sealed class CncNotificationCoordinator(
    ICncSignedLinkClient signedLinks,
    INotificationClient notifications)
{
    internal async Task<CncNotificationDeliveryResult> DeliverAsync(
        CncSubmission submission,
        int requestId,
        IReadOnlyList<CncNotificationFinalizedItem> finalizedItems,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        if (submission is null || finalizedItems is null || operationId == Guid.Empty
            || submission.OrderItems is null || finalizedItems.Count != submission.OrderItems.Count)
        {
            return new(false, false, null, null);
        }

        var resolved = new List<CncNotificationItemLinks>(finalizedItems.Count);
        foreach (CncNotificationFinalizedItem item in finalizedItems)
        {
            if (item is null || string.IsNullOrWhiteSpace(item.ModelObjectName))
            {
                return new(false, false, null, null);
            }

            Uri? model = await signedLinks.GetAsync(item.ModelObjectName, cancellationToken);
            if (model is null)
            {
                return new(false, false, null, null);
            }

            Uri? drawing = null;
            if (!string.IsNullOrWhiteSpace(item.DrawingObjectName))
            {
                drawing = await signedLinks.GetAsync(item.DrawingObjectName, cancellationToken);
                if (drawing is null)
                {
                    return new(false, false, null, null);
                }
            }

            resolved.Add(new CncNotificationItemLinks(model, drawing));
        }

        if (!CncNotificationComposer.TryCompose(submission, requestId, resolved, out CncNotificationPlan? plan)
            || plan is null)
        {
            return new(true, false, null, null);
        }

        NotificationResult customer = await notifications.SendIdempotentAsync(
            plan.CustomerChannel,
            plan.Customer,
            DeriveOperationId(operationId, "customer"),
            cancellationToken);

        NotificationResult manufacturing = await notifications.SendIdempotentAsync(
            plan.ManufacturingChannel,
            plan.Manufacturing,
            DeriveOperationId(operationId, "manufacturing"),
            cancellationToken);

        return new(true, true, customer, manufacturing);
    }

    private static Guid DeriveOperationId(Guid operationId, string purpose)
    {
        byte[] purposeBytes = Encoding.UTF8.GetBytes(purpose);
        byte[] input = new byte[16 + purposeBytes.Length];
        operationId.TryWriteBytes(input);
        purposeBytes.CopyTo(input.AsSpan(16));
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(input, hash);
        hash[7] = (byte)((hash[7] & 0x0f) | 0x50);
        hash[8] = (byte)((hash[8] & 0x3f) | 0x80);
        return new Guid(hash[..16]);
    }
}
