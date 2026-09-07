using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Components.Pages.InstantQuotation;
using Newtonsoft.Json;
using static Legacy.Maliev.Web.Components.Pages.InstantQuotation.CncSubmissionAdmission;

namespace Legacy.Maliev.Web.Tests;

public sealed class CncNotificationCoordinatorTests
{
    [Fact]
    public async Task ValidFinalizedObjects_ResolveBeforeSendingAndDeliverCustomerThenManufacturing()
    {
        var events = new List<string>();
        var links = new StubLinks(events, new Dictionary<string, Uri>
        {
            ["model"] = new("https://files.example/model"),
            ["drawing"] = new("https://files.example/drawing")
        });
        var notifications = new StubNotifications(events);
        var coordinator = new CncNotificationCoordinator(links, notifications);

        CncNotificationDeliveryResult result = await coordinator.DeliverAsync(
            Submission(withDrawing: true),
            42,
            [new("model", "drawing")],
            Guid.Parse("6f9674d0-b9c0-465d-843e-3435946239df"),
            CancellationToken.None);

        Assert.True(result.Complete);
        Assert.Equal(["link:model", "link:drawing", "send:customer@example.com", "send:manufacturing@maliev.com"], events);
        Assert.Equal(2, notifications.OperationIds.Count);
        Assert.NotEqual(notifications.OperationIds[0], notifications.OperationIds[1]);
        Assert.DoesNotContain(Guid.Empty, notifications.OperationIds);
    }

    [Fact]
    public async Task SameRootOperation_ProducesStablePurposeSpecificIds()
    {
        Guid root = Guid.Parse("6f9674d0-b9c0-465d-843e-3435946239df");
        var first = new StubNotifications([]);
        var second = new StubNotifications([]);

        await new CncNotificationCoordinator(SingleModelLinks(), first).DeliverAsync(
            Submission(), 42, [new("model")], root, CancellationToken.None);
        await new CncNotificationCoordinator(SingleModelLinks(), second).DeliverAsync(
            Submission(), 42, [new("model")], root, CancellationToken.None);

        Assert.Equal(first.OperationIds, second.OperationIds);
        Assert.NotEqual(first.OperationIds[0], first.OperationIds[1]);
    }

    [Fact]
    public async Task CustomerRejection_DoesNotSuppressManufacturingAttempt()
    {
        var notifications = new StubNotifications([], [new(false, true, true), new(true, true, true)]);
        var coordinator = new CncNotificationCoordinator(SingleModelLinks(), notifications);

        CncNotificationDeliveryResult result = await coordinator.DeliverAsync(
            Submission(), 42, [new("model")], Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.Complete);
        Assert.False(result.Customer!.Sent);
        Assert.True(result.Manufacturing!.Sent);
        Assert.Equal(2, notifications.OperationIds.Count);
    }

    [Fact]
    public async Task AnyMissingLink_FailsClosedBeforeNotificationDelivery()
    {
        var notifications = new StubNotifications([]);
        var coordinator = new CncNotificationCoordinator(
            new StubLinks([], new Dictionary<string, Uri> { ["model"] = new("https://files.example/model") }),
            notifications);

        CncNotificationDeliveryResult result = await coordinator.DeliverAsync(
            Submission(withDrawing: true), 42, [new("model", "missing")], Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.LinksResolved);
        Assert.False(result.Composed);
        Assert.Empty(notifications.OperationIds);
    }

    [Fact]
    public async Task InvalidInputsOrComposerMismatch_FailClosedBeforeNotificationDelivery()
    {
        var notifications = new StubNotifications([]);
        var coordinator = new CncNotificationCoordinator(SingleModelLinks(), notifications);

        CncNotificationDeliveryResult invalidOperation = await coordinator.DeliverAsync(
            Submission(), 42, [new("model")], Guid.Empty, CancellationToken.None);
        CncNotificationDeliveryResult mismatchedItems = await coordinator.DeliverAsync(
            Submission(), 42, [], Guid.NewGuid(), CancellationToken.None);
        CncNotificationDeliveryResult invalidRequest = await coordinator.DeliverAsync(
            Submission(), 0, [new("model")], Guid.NewGuid(), CancellationToken.None);

        Assert.False(invalidOperation.LinksResolved);
        Assert.False(mismatchedItems.LinksResolved);
        Assert.True(invalidRequest.LinksResolved);
        Assert.False(invalidRequest.Composed);
        Assert.Empty(notifications.OperationIds);
    }

    private static StubLinks SingleModelLinks() => new([], new Dictionary<string, Uri>
    {
        ["model"] = new("https://files.example/model")
    });

    private static CncSubmission Submission(bool withDrawing = false)
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
                    CncDrawingFileName = withDrawing ? "drawing.pdf" : string.Empty,
                    CncDrawingStoragePath = withDrawing ? "owned/drawing.pdf" : string.Empty,
                    Process = "CncMilling",
                    Material = "Aluminum 6061",
                    Quantity = "1",
                    CncTolerance = "ISO 2768-m",
                    CncThreads = "none",
                    CncRoughness = "Ra 3.2",
                    CncInspection = "standard",
                    CncCertificate = "none",
                    ValidatedCncEstimate = snapshot,
                    CanonicalCncEstimateJson = CncSubmissionSnapshotTests.SnapshotJson()
                }
            ]
        };
    }

    private sealed class StubLinks(List<string> events, IReadOnlyDictionary<string, Uri> links) : ICncSignedLinkClient
    {
        public Task<Uri?> GetAsync(string objectName, CancellationToken cancellationToken)
        {
            events.Add($"link:{objectName}");
            return Task.FromResult(links.TryGetValue(objectName, out Uri? link) ? link : null);
        }
    }

    private sealed class StubNotifications(List<string> events, IReadOnlyList<NotificationResult>? results = null) : INotificationClient
    {
        private int index;
        internal List<Guid> OperationIds { get; } = [];

        public Task<NotificationResult> SendAsync(NotificationChannel channel, EmailNotification notification, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<NotificationResult> SendIdempotentAsync(
            NotificationChannel channel,
            EmailNotification notification,
            Guid operationId,
            CancellationToken cancellationToken)
        {
            events.Add($"send:{notification.To}");
            OperationIds.Add(operationId);
            NotificationResult result = results is not null && index < results.Count
                ? results[index++]
                : new(true, true, true);
            return Task.FromResult(result);
        }
    }
}
