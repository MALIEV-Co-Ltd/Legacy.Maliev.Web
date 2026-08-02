using Legacy.Maliev.Web.Application;

namespace Legacy.Maliev.Web.Tests;

public sealed class CustomerOrderSubmissionServiceTests
{
    [Fact]
    public async Task Submit_ValidAdditiveOrder_UsesTrustedCustomerAndCanonicalReplayKeys()
    {
        var transport = new RecordingSubmissionTransport();
        var notifications = new RecordingNotificationClient();
        var service = new CustomerOrderSubmissionService(transport, notifications);
        var operationId = Guid.Parse("b15e0682-6f53-4203-a0d4-cb6e8d96bcc8");
        var draft = Draft(CustomerOrderKind.Additive, [new TestUpload("part.stl", "model/stl", [1, 2, 3])]);

        var result = await service.SubmitAsync(
            trustedCustomerId: 42,
            trustedCustomerEmail: "customer@example.com",
            draft,
            operationId,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.True(result.Persisted);
        Assert.Equal(731, result.OrderId);
        Assert.True(result.FilesUploaded);
        Assert.True(result.NotificationSent);
        Assert.Equal(42, transport.CustomerId);
        Assert.Equal("legacy-web-member-order-b15e0682-6f53-4203-a0d4-cb6e8d96bcc8", transport.CreateKey);
        Assert.Equal("legacy-web-member-order-b15e0682-6f53-4203-a0d4-cb6e8d96bcc8-status-new", transport.StatusKey);
        Assert.Equal("legacy-web-member-order-b15e0682-6f53-4203-a0d4-cb6e8d96bcc8-upload", transport.UploadKey);
        Assert.Equal("customer@example.com", Assert.Single(notifications.Notifications).To);
        Assert.Equal(NotificationChannel.NoReply, notifications.Channel);
    }

    [Fact]
    public async Task Submit_NoFiles_SkipsUploadAndStillCompletes()
    {
        var transport = new RecordingSubmissionTransport();
        var notifications = new RecordingNotificationClient();
        var service = new CustomerOrderSubmissionService(transport, notifications);

        var result = await service.SubmitAsync(
            42,
            "customer@example.com",
            Draft(CustomerOrderKind.Scanning, []),
            Guid.Parse("c3b4a208-ec16-4d28-b680-ee571977ec1c"),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.False(result.FilesUploaded);
        Assert.Equal(0, transport.UploadCalls);
        Assert.Empty(transport.LinkedObjects);
    }

    [Fact]
    public async Task Submit_UploadFails_ReturnsPersistedPartialAndDoesNotNotify()
    {
        var transport = new RecordingSubmissionTransport
        {
            UploadResult = new(null, false, true, false),
        };
        var notifications = new RecordingNotificationClient();
        var service = new CustomerOrderSubmissionService(transport, notifications);

        var result = await service.SubmitAsync(
            42,
            "customer@example.com",
            Draft(CustomerOrderKind.Machining, [new TestUpload("part.step", string.Empty, [1])]),
            Guid.Parse("145e7de5-c7a5-447e-ab0e-d21b9fc3d143"),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.True(result.Persisted);
        Assert.False(result.ServiceAvailable);
        Assert.Empty(notifications.Notifications);
    }

    [Fact]
    public async Task Submit_InvalidTrustedIdentity_FailsClosedWithoutServiceCalls()
    {
        var transport = new RecordingSubmissionTransport();
        var notifications = new RecordingNotificationClient();
        var service = new CustomerOrderSubmissionService(transport, notifications);

        var result = await service.SubmitAsync(
            0,
            "not-an-email",
            Draft(CustomerOrderKind.Additive, []),
            Guid.Empty,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.False(result.Persisted);
        Assert.False(result.Authorized);
        Assert.Equal(0, transport.CreateCalls);
        Assert.Empty(notifications.Notifications);
    }

    private static CustomerOrderDraft Draft(
        CustomerOrderKind kind,
        IReadOnlyList<ICustomerOrderUploadFile> files) => new(
            kind,
            "Prototype bracket",
            "Please review tolerances.",
            ProcessId: 3,
            MaterialId: 5,
            SurfaceFinishId: 7,
            ColorId: kind == CustomerOrderKind.Additive ? 9 : null,
            Quantity: 2,
            AllowSocialMedia: false,
            Files: files);

    private sealed class RecordingSubmissionTransport : ICustomerOrderSubmissionTransport
    {
        public int CreateCalls { get; private set; }
        public int UploadCalls { get; private set; }
        public int CustomerId { get; private set; }
        public string? CreateKey { get; private set; }
        public string? StatusKey { get; private set; }
        public string? UploadKey { get; private set; }
        public List<CustomerOrderUploadedObject> LinkedObjects { get; } = [];
        public CustomerOrderUploadResult UploadResult { get; set; } = new(
            [new CustomerOrderUploadedObject("maliev.com", "uploads/42/2026-8-2/part.stl")],
            true,
            true,
            false);

        public Task<CustomerOrderCreateResult> CreateAsync(
            int trustedCustomerId,
            CustomerOrderDraft draft,
            string idempotencyKey,
            CancellationToken cancellationToken)
        {
            CreateCalls++;
            CustomerId = trustedCustomerId;
            CreateKey = idempotencyKey;
            return Task.FromResult(new CustomerOrderCreateResult(731, true, true, false));
        }

        public Task<CustomerOrderOperationResult> AddNewStatusAsync(
            int orderId,
            string idempotencyKey,
            CancellationToken cancellationToken)
        {
            StatusKey = idempotencyKey;
            return Task.FromResult(new CustomerOrderOperationResult(true, true, true, false));
        }

        public Task<CustomerOrderUploadResult> UploadAsync(
            int trustedCustomerId,
            IReadOnlyList<ICustomerOrderUploadFile> files,
            string idempotencyKey,
            CancellationToken cancellationToken)
        {
            UploadCalls++;
            UploadKey = idempotencyKey;
            return Task.FromResult(UploadResult);
        }

        public Task<CustomerOrderOperationResult> LinkAsync(
            int trustedCustomerId,
            int orderId,
            CustomerOrderUploadedObject uploadedObject,
            CancellationToken cancellationToken)
        {
            LinkedObjects.Add(uploadedObject);
            return Task.FromResult(new CustomerOrderOperationResult(true, true, true, false));
        }
    }

    private sealed class RecordingNotificationClient : INotificationClient
    {
        public NotificationChannel Channel { get; private set; }
        public List<EmailNotification> Notifications { get; } = [];

        public Task<NotificationResult> SendAsync(
            NotificationChannel channel,
            EmailNotification notification,
            CancellationToken cancellationToken)
        {
            Channel = channel;
            Notifications.Add(notification);
            return Task.FromResult(new NotificationResult(true, true, true));
        }
    }

    private sealed class TestUpload(string fileName, string contentType, byte[] content) : ICustomerOrderUploadFile
    {
        public string FileName { get; } = fileName;
        public string ContentType { get; } = contentType;
        public long Length => content.LongLength;
        public Stream OpenReadStream() => new MemoryStream(content, writable: false);
    }
}
