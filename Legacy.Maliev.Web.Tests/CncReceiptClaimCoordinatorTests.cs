using Legacy.Maliev.Web.Components.Pages.InstantQuotation;
using static Legacy.Maliev.Web.Components.Pages.InstantQuotation.CncSubmissionAdmission;

namespace Legacy.Maliev.Web.Tests;

public sealed class CncReceiptClaimCoordinatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ValidatedModelAndDrawing_AreClaimedAsOneExactSet()
    {
        var store = new RecordingStore();
        var coordinator = new CncReceiptClaimCoordinator(store, new FixedClock(Now));
        CncSubmission submission = Submission(withDrawing: true);

        bool claimed = coordinator.TryClaim(submission, out CncReceiptClaimLease? lease);

        Assert.True(claimed);
        Assert.NotNull(lease);
        Assert.Equal(2, store.Claims!.Count);
        Assert.Equal(["model", "drawing"], store.Claims.Select(claim => claim.Role));
        Assert.Equal(["protected-model", "protected-drawing"], store.Claims.Select(claim => claim.ProtectedReceipt));
        Assert.Equal(Now, store.ClaimedAt);
    }

    [Fact]
    public void PrePersistenceFailure_RestoresExactlyOnce()
    {
        var store = new RecordingStore();
        var coordinator = new CncReceiptClaimCoordinator(store, new FixedClock(Now));
        Assert.True(coordinator.TryClaim(Submission(), out CncReceiptClaimLease? lease));

        Assert.True(lease!.TryRestoreBeforePersistence());
        Assert.False(lease.TryRestoreBeforePersistence());
        Assert.False(lease.TryMarkPersistenceStarted());
        Assert.Equal(1, store.RestoreCalls);
        Assert.Same(store.ClaimSet, store.Restored);
    }

    [Fact]
    public void PersistenceStart_PermanentlyForbidsReceiptRestoration()
    {
        var store = new RecordingStore();
        var coordinator = new CncReceiptClaimCoordinator(store, new FixedClock(Now));
        Assert.True(coordinator.TryClaim(Submission(), out CncReceiptClaimLease? lease));

        Assert.True(lease!.TryMarkPersistenceStarted());
        Assert.False(lease.TryMarkPersistenceStarted());
        Assert.False(lease.TryRestoreBeforePersistence());
        Assert.Equal(0, store.RestoreCalls);
    }

    [Fact]
    public void MissingValidatedModelOrDrawing_FailsBeforeStoreClaim()
    {
        var store = new RecordingStore();
        var coordinator = new CncReceiptClaimCoordinator(store, new FixedClock(Now));
        CncSubmission missingModel = Submission();
        missingModel.OrderItems[0].ValidatedCncModelReceipt = null!;
        CncSubmission missingDrawing = Submission(withDrawing: true);
        missingDrawing.OrderItems[0].ValidatedCncDrawingReceipt = null!;

        Assert.False(coordinator.TryClaim(missingModel, out _));
        Assert.False(coordinator.TryClaim(missingDrawing, out _));
        Assert.Equal(0, store.ClaimCalls);
    }

    [Fact]
    public void UnexpectedDrawingProjectionOrStoreMismatch_FailsClosed()
    {
        var store = new RecordingStore();
        var coordinator = new CncReceiptClaimCoordinator(store, new FixedClock(Now));
        CncSubmission unexpected = Submission();
        unexpected.OrderItems[0].ValidatedCncDrawingReceipt = Receipt("drawing");

        Assert.False(coordinator.TryClaim(unexpected, out _));
        Assert.Equal(0, store.ClaimCalls);

        store.ReturnCount = 0;
        Assert.False(coordinator.TryClaim(Submission(), out _));
        Assert.Equal(1, store.ClaimCalls);
    }

    private static CncSubmission Submission(bool withDrawing = false)
    {
        var item = new ItemDetail
        {
            CncModelUploadReceipt = "protected-model",
            ValidatedCncModelReceipt = Receipt("model"),
        };
        if (withDrawing)
        {
            item.CncDrawingStoragePath = "owned/drawing.pdf";
            item.CncDrawingUploadReceipt = "protected-drawing";
            item.ValidatedCncDrawingReceipt = Receipt("drawing");
        }

        return new CncSubmission { OrderItems = [item] };
    }

    private static CncProtectedReceipt Receipt(string role) => new(
        "session", "form", "item", role, role + ".step", "owned/" + role + ".step",
        Now, Now.AddHours(1), "nonce-" + role);

    private sealed class RecordingStore : ICncUploadReceiptStore
    {
        internal int ClaimCalls { get; private set; }
        internal int RestoreCalls { get; private set; }
        internal int? ReturnCount { get; set; }
        internal IReadOnlyCollection<CncUploadReceiptClaim>? Claims { get; private set; }
        internal DateTimeOffset ClaimedAt { get; private set; }
        internal CncUploadReceiptClaimSet? ClaimSet { get; private set; }
        internal CncUploadReceiptClaimSet? Restored { get; private set; }
        public bool IsSharedDistributedAtomic => true;
        public bool TryClaimAll(IReadOnlyCollection<CncUploadReceiptClaim> claims, DateTimeOffset now, out CncUploadReceiptClaimSet? claimSet)
        {
            ClaimCalls++;
            Claims = claims;
            ClaimedAt = now;
            int count = ReturnCount ?? claims.Count;
            ClaimSet = claimSet = new(Enumerable.Range(0, count).Select(index =>
                new CncUploadReceiptState("form", "session", "item-" + index, "model", "protected", Now.AddHours(1))).ToArray());
            return true;
        }
        public void Restore(CncUploadReceiptClaimSet claimSet, DateTimeOffset now) { RestoreCalls++; Restored = claimSet; }
        public bool TryReserve(CncUploadReceiptState receipt, DateTimeOffset now, int maximumOutstandingPerForm, out CncUploadReceiptReservation? reservation) => throw new NotSupportedException();
        public void Finalize(CncUploadReceiptReservation reservation, DateTimeOffset now) => throw new NotSupportedException();
        public void Rollback(CncUploadReceiptReservation reservation, DateTimeOffset now) => throw new NotSupportedException();
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
