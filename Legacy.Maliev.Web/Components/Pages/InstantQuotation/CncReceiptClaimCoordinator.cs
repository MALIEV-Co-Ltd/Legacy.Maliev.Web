using static Legacy.Maliev.Web.Components.Pages.InstantQuotation.CncSubmissionAdmission;

namespace Legacy.Maliev.Web.Components.Pages.InstantQuotation;

/// <summary>Owns an atomically claimed receipt set and prevents restoration after persistence starts.</summary>
internal sealed class CncReceiptClaimLease(
    ICncUploadReceiptStore store,
    CncUploadReceiptClaimSet claimSet,
    TimeProvider clock)
{
    private int state;

    internal IReadOnlyList<CncUploadReceiptState> Receipts => claimSet.Receipts;

    internal bool TryMarkPersistenceStarted() => Interlocked.CompareExchange(ref state, 1, 0) == 0;

    internal bool TryRestoreBeforePersistence()
    {
        if (Interlocked.CompareExchange(ref state, 2, 0) != 0)
        {
            return false;
        }

        store.Restore(claimSet, clock.GetUtcNow());
        return true;
    }
}

/// <summary>Atomically claims only the server-validated CNC model and drawing receipts.</summary>
internal sealed class CncReceiptClaimCoordinator(ICncUploadReceiptStore store, TimeProvider clock)
{
    internal bool TryClaim(CncSubmission submission, out CncReceiptClaimLease? lease)
    {
        lease = null;
        if (submission?.OrderItems is not { Count: > 0 })
        {
            return false;
        }

        var claims = new List<CncUploadReceiptClaim>();
        foreach (ItemDetail item in submission.OrderItems)
        {
            if (item is null || item.ValidatedCncModelReceipt is null
                || !TryAdd(claims, item.ValidatedCncModelReceipt, item.CncModelUploadReceipt))
            {
                return false;
            }

            bool drawingExpected = !string.IsNullOrWhiteSpace(item.CncDrawingStoragePath)
                || !string.IsNullOrWhiteSpace(item.CncDrawingUploadReceipt);
            if (drawingExpected
                && (item.ValidatedCncDrawingReceipt is null
                    || !TryAdd(claims, item.ValidatedCncDrawingReceipt, item.CncDrawingUploadReceipt)))
            {
                return false;
            }

            if (!drawingExpected && item.ValidatedCncDrawingReceipt is not null)
            {
                return false;
            }
        }

        if (!store.TryClaimAll(claims, clock.GetUtcNow(), out CncUploadReceiptClaimSet? claimSet)
            || claimSet is null
            || claimSet.Receipts.Count != claims.Count)
        {
            return false;
        }

        lease = new CncReceiptClaimLease(store, claimSet, clock);
        return true;
    }

    private static bool TryAdd(
        ICollection<CncUploadReceiptClaim> claims,
        CncProtectedReceipt receipt,
        string protectedReceipt)
    {
        if (string.IsNullOrWhiteSpace(protectedReceipt)
            || string.IsNullOrWhiteSpace(receipt.FormId)
            || string.IsNullOrWhiteSpace(receipt.SessionId)
            || string.IsNullOrWhiteSpace(receipt.ItemId)
            || string.IsNullOrWhiteSpace(receipt.Role))
        {
            return false;
        }

        claims.Add(new CncUploadReceiptClaim(
            receipt.FormId,
            receipt.SessionId,
            receipt.ItemId,
            receipt.Role,
            protectedReceipt));
        return true;
    }
}
