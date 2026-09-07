using Legacy.Maliev.Web.Application;

namespace Legacy.Maliev.Web.Components.Pages.InstantQuotation;

internal enum CncSubmissionPersistenceOutcome
{
    Retry,
    Unconfirmed,
    Partial,
    Completed,
}

internal sealed record CncSubmissionPersistenceResult(
    CncSubmissionPersistenceOutcome Outcome,
    int? RequestId = null);

/// <summary>Coordinates the non-replayable persistence steps after CNC admission and receipt claiming.</summary>
internal sealed class CncSubmissionPersistenceCoordinator(
    ICncRequestClient requests,
    ICncProfilePersistenceClient profiles,
    ICncFileFinalizationClient files,
    CncNotificationCoordinator notifications)
{
    internal async Task<CncSubmissionPersistenceResult> ExecuteAsync(
        CncSubmission submission,
        CncReceiptClaimLease lease,
        CncRequestSubmission request,
        CncProfilePersistenceRequest? profile,
        string sessionId,
        DateTimeOffset submissionStartedAtUtc,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        CncRequestResult created = await requests.CreateAsync(request, cancellationToken);
        if (created.Outcome == CncRequestOutcome.NotSent)
        {
            lease.TryRestoreBeforePersistence();
            return new(CncSubmissionPersistenceOutcome.Retry);
        }

        lease.TryMarkPersistenceStarted();
        if (created.Outcome != CncRequestOutcome.Created || created.RequestId is not > 0)
        {
            return new(CncSubmissionPersistenceOutcome.Unconfirmed, created.RequestId);
        }

        if (submission.OrderItems is not { Count: > 0 })
        {
            return new(CncSubmissionPersistenceOutcome.Partial, created.RequestId);
        }

        int requestId = created.RequestId.Value;
        bool partial = false;
        if (profile is not null)
        {
            CncProfilePersistenceResult profileResult = await profiles.CompleteAsync(profile, cancellationToken);
            partial = profileResult.Outcome != CncProfilePersistenceOutcome.Completed;
        }

        var finalizedItems = new List<CncNotificationFinalizedItem>(submission.OrderItems.Count);
        foreach (CncSubmissionAdmission.ItemDetail item in submission.OrderItems)
        {
            CncFileFinalizationResult model = await files.FinalizeAsync(
                new CncFileFinalizationRequest(
                    requestId,
                    sessionId,
                    submissionStartedAtUtc,
                    new CncClaimedFileCoordinates(sessionId, item.StoragePath, "model")),
                cancellationToken);
            if (model.Outcome != CncFileFinalizationOutcome.Linked || string.IsNullOrWhiteSpace(model.DestinationObjectName))
            {
                return new(CncSubmissionPersistenceOutcome.Partial, requestId);
            }

            string? drawingObjectName = null;
            if (!string.IsNullOrWhiteSpace(item.CncDrawingStoragePath))
            {
                CncFileFinalizationResult drawing = await files.FinalizeAsync(
                    new CncFileFinalizationRequest(
                        requestId,
                        sessionId,
                        submissionStartedAtUtc,
                        new CncClaimedFileCoordinates(sessionId, item.CncDrawingStoragePath, "drawing")),
                    cancellationToken);
                if (drawing.Outcome != CncFileFinalizationOutcome.Linked || string.IsNullOrWhiteSpace(drawing.DestinationObjectName))
                {
                    return new(CncSubmissionPersistenceOutcome.Partial, requestId);
                }

                drawingObjectName = drawing.DestinationObjectName;
            }

            finalizedItems.Add(new CncNotificationFinalizedItem(model.DestinationObjectName, drawingObjectName));
        }

        CncNotificationDeliveryResult delivery = await notifications.DeliverAsync(
            submission,
            requestId,
            finalizedItems,
            operationId,
            cancellationToken);
        return new(
            !partial && delivery.Complete
                ? CncSubmissionPersistenceOutcome.Completed
                : CncSubmissionPersistenceOutcome.Partial,
            requestId);
    }
}
