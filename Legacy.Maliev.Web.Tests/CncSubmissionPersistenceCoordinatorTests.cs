using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Components.Pages.InstantQuotation;
using Newtonsoft.Json;
using static Legacy.Maliev.Web.Components.Pages.InstantQuotation.CncSubmissionAdmission;

namespace Legacy.Maliev.Web.Tests;

public sealed class CncSubmissionPersistenceCoordinatorTests
{
    [Fact]
    public async Task CreatedRequest_ProjectsTheConfirmedRequestIdIntoProfilePersistence()
    {
        var store = new RecordingReceiptStore();
        var lease = new CncReceiptClaimLease(
            store,
            new CncUploadReceiptClaimSet(
            [new CncUploadReceiptState("form", "session", "item", "model", "receipt", DateTimeOffset.MaxValue)]),
            TimeProvider.System);
        var profiles = new CapturingProfileClient();
        var events = new List<string>();
        var coordinator = new CncSubmissionPersistenceCoordinator(
            new FixedRequestClient(new(CncRequestOutcome.Created, 42)),
            profiles,
            new LinkedFileClient(events),
            new CncNotificationCoordinator(new SuccessfulSignedLinkClient(events), new SuccessfulNotificationClient(events)));
        var customer = new CustomerAccountDetails(
            7, "Nat", "V", "Nat V", null, null, null, "n@example.test", null,
            null, null, null, null, null, null, null, null);
        var completion = new CncProfileCompletion(
            "Nat", "V", "n@example.test", "0800000000", null, string.Empty, string.Empty,
            new(null, "Road", null, "Bangkok", "Bangkok", "10110"), "Thailand", true, null, null);

        var result = await coordinator.ExecuteAsync(
            ValidSubmission(), lease, Request(), new(0, 7, customer, completion), "session", StartedAt, OperationId, default);

        Assert.Equal(CncSubmissionPersistenceOutcome.Completed, result.Outcome);
        Assert.Equal(42, profiles.Request?.QuotationRequestId);
    }

    private static readonly Guid JourneyId = Guid.Parse("d00f2ab4-0d1f-4f86-a1aa-c1b8c9486454");
    private static readonly Guid OperationId = Guid.Parse("895b50df-7b2d-47aa-aac0-9a28481da568");
    private static readonly DateTimeOffset StartedAt = new(2026, 9, 7, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RequestDefinitelyNotSent_RestoresClaimAndReturnsRetry()
    {
        var store = new RecordingReceiptStore();
        var lease = new CncReceiptClaimLease(
            store,
            new CncUploadReceiptClaimSet(
            [new CncUploadReceiptState("form", "session", "item", "model", "receipt", DateTimeOffset.MaxValue)]),
            TimeProvider.System);
        var coordinator = new CncSubmissionPersistenceCoordinator(
            new FixedRequestClient(new(CncRequestOutcome.NotSent)),
            new UnexpectedProfileClient(),
            new UnexpectedFileClient(),
            new CncNotificationCoordinator(new UnexpectedSignedLinkClient(), new UnexpectedNotificationClient()));

        CncSubmissionPersistenceResult result = await coordinator.ExecuteAsync(
            new CncSubmission(),
            lease,
            Request(),
            null,
            "session",
            StartedAt,
            OperationId,
            default);

        Assert.Equal(CncSubmissionPersistenceOutcome.Retry, result.Outcome);
        Assert.Null(result.RequestId);
        Assert.Equal(1, store.RestoreCalls);
        Assert.True(lease.TryRestoreBeforePersistence() is false);
    }

    [Fact]
    public async Task CreatedRequestWithNoFinalizableItems_IsTerminalPartialAndNeverRestoresClaim()
    {
        var store = new RecordingReceiptStore();
        var lease = new CncReceiptClaimLease(
            store,
            new CncUploadReceiptClaimSet(
            [new CncUploadReceiptState("form", "session", "item", "model", "receipt", DateTimeOffset.MaxValue)]),
            TimeProvider.System);
        var coordinator = new CncSubmissionPersistenceCoordinator(
            new FixedRequestClient(new(CncRequestOutcome.Created, 42)),
            new UnexpectedProfileClient(),
            new UnexpectedFileClient(),
            new CncNotificationCoordinator(new UnexpectedSignedLinkClient(), new UnexpectedNotificationClient()));

        CncSubmissionPersistenceResult result = await coordinator.ExecuteAsync(
            new CncSubmission(), lease, Request(), null, "session", StartedAt, OperationId, default);

        Assert.Equal(CncSubmissionPersistenceOutcome.Partial, result.Outcome);
        Assert.Equal(42, result.RequestId);
        Assert.Equal(0, store.RestoreCalls);
        Assert.False(lease.TryRestoreBeforePersistence());
    }

    [Fact]
    public async Task CreatedRequest_FinalizesClaimedFilesThenDeliversBothNotifications()
    {
        var store = new RecordingReceiptStore();
        var lease = new CncReceiptClaimLease(
            store,
            new CncUploadReceiptClaimSet(
            [new CncUploadReceiptState("form", "session", "item", "model", "receipt", DateTimeOffset.MaxValue)]),
            TimeProvider.System);
        var events = new List<string>();
        var coordinator = new CncSubmissionPersistenceCoordinator(
            new FixedRequestClient(new(CncRequestOutcome.Created, 42)),
            new UnexpectedProfileClient(),
            new LinkedFileClient(events),
            new CncNotificationCoordinator(new SuccessfulSignedLinkClient(events), new SuccessfulNotificationClient(events)));

        CncSubmissionPersistenceResult result = await coordinator.ExecuteAsync(
            ValidSubmission(), lease, Request(), null, "session", StartedAt, OperationId, default);

        Assert.Equal(CncSubmissionPersistenceOutcome.Completed, result.Outcome);
        Assert.Equal(42, result.RequestId);
        Assert.Equal(["file:owned/part.step", "link:final/model.step", "send:customer@example.com", "send:manufacturing@maliev.com"], events);
        Assert.Equal(0, store.RestoreCalls);
    }

    private static CncSubmission ValidSubmission()
    {
        var snapshot = JsonConvert.DeserializeObject<CncEstimateSnapshot>(CncSubmissionSnapshotTests.SnapshotJson())!;
        return new CncSubmission
        {
            FirstName = "Somsak",
            LastName = "Jaidee",
            Email = "customer@example.com",
            Mobile = "+66810000000",
            Country = "Thailand",
            BillingStreet1 = "36/1 Moo 3",
            BillingCity = "Pak Kret",
            BillingProvince = "Nonthaburi",
            BillingPostalCode = "11120",
            ShipToBillingAddress = true,
            OrderItems =
            [
                new ItemDetail
                {
                    FileName = "part.step",
                    StoragePath = "owned/part.step",
                    Process = "CncMilling",
                    Material = "Aluminum 6061",
                    Quantity = "1",
                    CncTolerance = "ISO 2768-m",
                    CncThreads = "none",
                    CncRoughness = "Ra 3.2",
                    CncInspection = "standard",
                    CncCertificate = "none",
                    ValidatedCncEstimate = snapshot,
                    CanonicalCncEstimateJson = CncSubmissionSnapshotTests.SnapshotJson(),
                },
            ],
        };
    }

    private static CncRequestSubmission Request() => new(
        new QuotationRequestSubmission("Nat", "V", "n@example.test", "0800000000", "Thailand", null, null, "review"),
        JourneyId);

    private sealed class FixedRequestClient(CncRequestResult result) : ICncRequestClient
    {
        public Task<CncRequestResult> CreateAsync(CncRequestSubmission submission, CancellationToken cancellationToken) =>
            Task.FromResult(result);
    }

    private sealed class UnexpectedProfileClient : ICncProfilePersistenceClient
    {
        public Task<CncProfilePersistenceResult> CompleteAsync(CncProfilePersistenceRequest request, CancellationToken cancellationToken) =>
            throw new Xunit.Sdk.XunitException("Profile persistence must not run before request creation.");
    }

    private sealed class CapturingProfileClient : ICncProfilePersistenceClient
    {
        internal CncProfilePersistenceRequest? Request { get; private set; }

        public Task<CncProfilePersistenceResult> CompleteAsync(CncProfilePersistenceRequest request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new CncProfilePersistenceResult(
                request.QuotationRequestId,
                CncProfilePersistenceOutcome.Completed,
                CncProfilePersistenceStage.Complete));
        }
    }

    private sealed class UnexpectedFileClient : ICncFileFinalizationClient
    {
        public Task<CncFileFinalizationResult> FinalizeAsync(CncFileFinalizationRequest request, CancellationToken cancellationToken) =>
            throw new Xunit.Sdk.XunitException("File finalization must not run before request creation.");
    }

    private sealed class LinkedFileClient(List<string> events) : ICncFileFinalizationClient
    {
        public Task<CncFileFinalizationResult> FinalizeAsync(CncFileFinalizationRequest request, CancellationToken cancellationToken)
        {
            events.Add($"file:{request.File.StoragePath}");
            return Task.FromResult(new CncFileFinalizationResult(CncFileFinalizationOutcome.Linked, "final/model.step", 91));
        }
    }

    private sealed class SuccessfulSignedLinkClient(List<string> events) : ICncSignedLinkClient
    {
        public Task<Uri?> GetAsync(string objectName, CancellationToken cancellationToken)
        {
            events.Add($"link:{objectName}");
            return Task.FromResult<Uri?>(new($"https://files.example/{objectName}"));
        }
    }

    private sealed class SuccessfulNotificationClient(List<string> events) : INotificationClient
    {
        public Task<NotificationResult> SendAsync(NotificationChannel channel, EmailNotification notification, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<NotificationResult> SendIdempotentAsync(NotificationChannel channel, EmailNotification notification, Guid operationId, CancellationToken cancellationToken)
        {
            events.Add($"send:{notification.To}");
            return Task.FromResult(new NotificationResult(true, true, true));
        }
    }

    private sealed class UnexpectedSignedLinkClient : ICncSignedLinkClient
    {
        public Task<Uri?> GetAsync(string objectName, CancellationToken cancellationToken) =>
            throw new Xunit.Sdk.XunitException("Signed-link resolution must not run before request creation.");
    }

    private sealed class UnexpectedNotificationClient : INotificationClient
    {
        public Task<NotificationResult> SendAsync(
            NotificationChannel channel,
            EmailNotification notification,
            CancellationToken cancellationToken) =>
            throw new Xunit.Sdk.XunitException("Notification must not run before request creation.");

        public Task<NotificationResult> SendIdempotentAsync(
            NotificationChannel channel,
            EmailNotification message,
            Guid operationId,
            CancellationToken cancellationToken) =>
            throw new Xunit.Sdk.XunitException("Notification must not run before request creation.");
    }

    private sealed class RecordingReceiptStore : ICncUploadReceiptStore
    {
        public bool IsSharedDistributedAtomic => true;
        internal int RestoreCalls { get; private set; }
        public void Restore(CncUploadReceiptClaimSet claimSet, DateTimeOffset now) => RestoreCalls++;
        public bool TryClaimAll(IReadOnlyCollection<CncUploadReceiptClaim> claims, DateTimeOffset now, out CncUploadReceiptClaimSet? claimSet) => throw new NotSupportedException();
        public bool TryReserve(CncUploadReceiptState receipt, DateTimeOffset now, int maximumOutstandingPerForm, out CncUploadReceiptReservation? reservation) => throw new NotSupportedException();
        public void Finalize(CncUploadReceiptReservation reservation, DateTimeOffset now) => throw new NotSupportedException();
        public void Rollback(CncUploadReceiptReservation reservation, DateTimeOffset now) => throw new NotSupportedException();
    }
}
