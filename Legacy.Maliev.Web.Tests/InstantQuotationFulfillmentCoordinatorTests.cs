using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Application.Pricing;

namespace Legacy.Maliev.Web.Tests;

public sealed class InstantQuotationFulfillmentCoordinatorTests
{
    private const string SubmissionId = "ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789";

    [Fact]
    public void QuotationPayloadSnapshotCopiesAndFreezesBothRecipientCollections()
    {
        var cc = new[] { "office@example.test" };
        var bcc = new[] { "mail-tracking@maliev.com" };
        var message = new EmailNotification("customer@example.test", "Quotation", "frozen", null, cc, bcc);
        var original = new InstantQuotationNotificationPayload(message, message, DateTimeOffset.UtcNow.AddMinutes(30));
        var frozen = original.Snapshot();
        cc[0] = "changed@example.test";
        bcc[0] = "changed@example.test";
        foreach (var copy in new[] { frozen.Customer, frozen.Manufacturing })
        {
            Assert.Equal("office@example.test", Assert.Single(copy.Cc!));
            Assert.Equal("mail-tracking@maliev.com", Assert.Single(copy.Bcc!));
            Assert.Throws<NotSupportedException>(() => ((IList<string>)copy.Cc!)[0] = "mutated@example.test");
            Assert.Throws<NotSupportedException>(() => ((IList<string>)copy.Bcc!)[0] = "mutated@example.test");
        }
        Assert.Equal(original.ExpiresAt, frozen.ExpiresAt);
    }

    [Fact]
    public async Task QuotationNotificationPreparationFailureDoesNotSendEitherMessage()
    {
        var lease = new RecordingLease(FilesLinkedCheckpoint());
        var client = new RecordingClient { QuotationPreparationAvailable = false };
        var result = await new InstantQuotationFulfillmentCoordinator(client).FulfillAsync(
            Session(Part(1)), Quote(Part(1)), null, Customer(), lease, lease.Checkpoint, CancellationToken.None);
        Assert.Equal(InstantQuotationSubmissionOutcome.Partial, result.Outcome);
        Assert.Empty(client.QuotationNotifications);
        Assert.NotEqual(InstantQuotationSubmissionCheckpointStatus.Completed, lease.Checkpoint.Status);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OneFailedQuotationMessageStillAttemptsOtherAndRetryUsesFrozenPayload(bool customerFails)
    {
        var lease = new RecordingLease(FilesLinkedCheckpoint());
        var client = new RecordingClient { FailedQuotationRecipient = customerFails ? "customer@example.test" : "manufacturing@maliev.com" };
        var coordinator = new InstantQuotationFulfillmentCoordinator(client);
        var result = await coordinator.FulfillAsync(Session(Part(1)), Quote(Part(1)), null, Customer(), lease, lease.Checkpoint, CancellationToken.None);
        Assert.Equal(InstantQuotationSubmissionOutcome.Partial, result.Outcome);
        Assert.Equal(2, client.QuotationNotifications.Count);
        Assert.Equal(1, client.QuotationPreparations);
        Assert.NotEqual(client.QuotationNotifications[0].OperationId, client.QuotationNotifications[1].OperationId);
        var failed = client.QuotationNotifications.Single(item => item.Message.To == client.FailedQuotationRecipient);
        client.FailedQuotationRecipient = null;
        result = await coordinator.FulfillAsync(Session(Part(1)), Quote(Part(1)), null, Customer(), lease, lease.Checkpoint, CancellationToken.None);
        Assert.Equal(InstantQuotationSubmissionOutcome.Completed, result.Outcome);
        Assert.Equal(3, client.QuotationNotifications.Count);
        Assert.Equal(1, client.QuotationPreparations);
        Assert.Equal(failed, client.QuotationNotifications[^1]);
        Assert.True(lease.Checkpoint.CustomerQuotationNotificationSent);
        Assert.True(lease.Checkpoint.ManufacturingQuotationNotificationSent);
    }

    [Fact]
    public async Task FailedFreezeCheckpointDoesNotSendQuotationMessages()
    {
        var lease = new RecordingLease(FilesLinkedCheckpoint()) { FailNotificationFreeze = true };
        var client = new RecordingClient();
        var result = await new InstantQuotationFulfillmentCoordinator(client).FulfillAsync(
            Session(Part(1)), Quote(Part(1)), null, Customer(), lease, lease.Checkpoint, CancellationToken.None);
        Assert.Equal(InstantQuotationSubmissionOutcome.Partial, result.Outcome);
        Assert.Empty(client.QuotationNotifications);
        Assert.Null(lease.Checkpoint.QuotationNotifications);
    }

    [Fact]
    public async Task SuccessfulSendWithoutCheckpointReplaysExactOperationAndBody()
    {
        var lease = new RecordingLease(FilesLinkedCheckpoint()) { FailNotificationSendCheckpoint = true };
        var client = new RecordingClient();
        var coordinator = new InstantQuotationFulfillmentCoordinator(client);
        var result = await coordinator.FulfillAsync(Session(Part(1)), Quote(Part(1)), null, Customer(), lease, lease.Checkpoint, CancellationToken.None);
        Assert.Equal(InstantQuotationSubmissionOutcome.Partial, result.Outcome);
        var first = Assert.Single(client.QuotationNotifications);
        lease.FailNotificationSendCheckpoint = false;
        result = await coordinator.FulfillAsync(Session(Part(1)), Quote(Part(1)), null, Customer(), lease, lease.Checkpoint, CancellationToken.None);
        Assert.Equal(InstantQuotationSubmissionOutcome.Completed, result.Outcome);
        Assert.Equal(first, client.QuotationNotifications[1]);
        Assert.Equal(1, client.QuotationPreparations);
        Assert.Equal(2, client.QuotationNotifications.Select(item => item.OperationId).Distinct().Count());
    }

    [Fact]
    public async Task ExpiredFrozenLinksFailClosedWithoutRegeneratingOrSending()
    {
        var lease = new RecordingLease(FilesLinkedCheckpoint());
        var client = new RecordingClient { ExpiredQuotationLinks = true };
        var coordinator = new InstantQuotationFulfillmentCoordinator(client);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var result = await coordinator.FulfillAsync(Session(Part(1)), Quote(Part(1)), null, Customer(), lease, lease.Checkpoint, CancellationToken.None);
            Assert.Equal(InstantQuotationSubmissionOutcome.Partial, result.Outcome);
            Assert.Equal(lease.Checkpoint.RequestReference, result.RequestReference);
        }
        Assert.Equal(1, client.QuotationPreparations);
        Assert.Empty(client.QuotationNotifications);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CancellationOrLostLeaseAfterFirstSendPreventsSecondSend(bool cancel)
    {
        using var cancellation = new CancellationTokenSource();
        var lease = new RecordingLease(FilesLinkedCheckpoint());
        var client = new RecordingClient
        {
            AfterQuotationSend = () =>
            {
                if (cancel) cancellation.Cancel();
                else lease.FailNextRenewal = true;
            },
        };
        var coordinator = new InstantQuotationFulfillmentCoordinator(client);
        if (cancel)
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => coordinator.FulfillAsync(
                Session(Part(1)), Quote(Part(1)), null, Customer(), lease, lease.Checkpoint, cancellation.Token));
        else
        {
            var result = await coordinator.FulfillAsync(
                Session(Part(1)), Quote(Part(1)), null, Customer(), lease, lease.Checkpoint, cancellation.Token);
            Assert.Equal(InstantQuotationSubmissionOutcome.Partial, result.Outcome);
        }
        Assert.Single(client.QuotationNotifications);
        Assert.False(lease.Checkpoint.ManufacturingQuotationNotificationSent);
        Assert.NotEqual(InstantQuotationSubmissionCheckpointStatus.Completed, lease.Checkpoint.Status);
    }

    [Fact]
    public async Task Fulfill_GuestSubmission_CheckpointsEveryBoundaryAndCreatesOneOrderPerPart()
    {
        var checkpoint = FilesLinkedCheckpoint();
        var lease = new RecordingLease(checkpoint);
        var client = new RecordingClient();
        var coordinator = new InstantQuotationFulfillmentCoordinator(client);

        var result = await coordinator.FulfillAsync(
            Session(Part(1), Part(2, "bbbbbbbb-cccc-dddd-eeee-ffffffffffff")),
            Quote(Part(1), Part(2, "bbbbbbbb-cccc-dddd-eeee-ffffffffffff")),
            null,
            Customer(),
            lease,
            checkpoint,
            CancellationToken.None);

        Assert.Equal(InstantQuotationSubmissionOutcome.Completed, result.Outcome);
        Assert.Equal([0, 1], client.OrderPartIndexes);
        Assert.Single(client.IdentityPasswords);
        Assert.Single(client.WelcomeOperationIds);
        Assert.Equal(
            [
                InstantQuotationSubmissionCheckpointStatus.CustomerProvisioned,
                InstantQuotationSubmissionCheckpointStatus.OrdersProvisioning,
                InstantQuotationSubmissionCheckpointStatus.OrdersProvisioning,
                InstantQuotationSubmissionCheckpointStatus.OrdersProvisioning,
                InstantQuotationSubmissionCheckpointStatus.OrdersProvisioned,
                InstantQuotationSubmissionCheckpointStatus.IdentityPrepared,
                InstantQuotationSubmissionCheckpointStatus.IdentityProvisioned,
                InstantQuotationSubmissionCheckpointStatus.WelcomePrepared,
                InstantQuotationSubmissionCheckpointStatus.WelcomeNotificationSent,
                InstantQuotationSubmissionCheckpointStatus.WelcomeNotificationSent,
                InstantQuotationSubmissionCheckpointStatus.WelcomeNotificationSent,
                InstantQuotationSubmissionCheckpointStatus.WelcomeNotificationSent,
                InstantQuotationSubmissionCheckpointStatus.Completed,
            ],
            lease.Writes.Select(item => item.Status));
        Assert.Null(lease.Checkpoint.TemporaryPassword);
        Assert.Equal([901, 902], lease.Checkpoint.OrderIds);
    }

    [Fact]
    public async Task Fulfill_OrderDependencyLostResponse_RetryResumesAtFirstUncheckpointedPart()
    {
        var checkpoint = FilesLinkedCheckpoint();
        var lease = new RecordingLease(checkpoint);
        var client = new RecordingClient { UnavailableOrderPartIndex = 1 };
        var coordinator = new InstantQuotationFulfillmentCoordinator(client);
        var parts = new[] { Part(1), Part(2, "bbbbbbbb-cccc-dddd-eeee-ffffffffffff") };

        var first = await coordinator.FulfillAsync(
            Session(parts), Quote(parts), null, Customer(), lease, checkpoint, CancellationToken.None);
        client.UnavailableOrderPartIndex = null;
        var retry = await coordinator.FulfillAsync(
            Session(parts), Quote(parts), null, Customer(), lease, lease.Checkpoint, CancellationToken.None);

        Assert.Equal(InstantQuotationSubmissionOutcome.Partial, first.Outcome);
        Assert.Equal(InstantQuotationProblemCategory.DependencyUnavailable, first.ProblemCategory);
        Assert.Equal(InstantQuotationSubmissionOutcome.Completed, retry.Outcome);
        Assert.Equal([0, 1, 1], client.OrderPartIndexes);
        Assert.Single(client.CustomerCalls);
        Assert.Single(client.IdentityPasswords);
        Assert.Equal([901, 902], lease.Checkpoint.OrderIds);
    }

    [Fact]
    public async Task Fulfill_IdentityDependencyUnavailable_StillCreatesOrdersAndSkipsWelcome()
    {
        var checkpoint = FilesLinkedCheckpoint();
        var lease = new RecordingLease(checkpoint);
        var client = new RecordingClient { IdentityServiceAvailable = false };
        var coordinator = new InstantQuotationFulfillmentCoordinator(client);

        var result = await coordinator.FulfillAsync(
            Session(Part(1)), Quote(Part(1)), null, Customer(), lease, checkpoint, CancellationToken.None);

        Assert.Equal(InstantQuotationSubmissionOutcome.Completed, result.Outcome);
        Assert.Equal([0], client.OrderPartIndexes);
        Assert.Empty(client.WelcomeOperationIds);
        Assert.False(lease.Checkpoint.IdentityCreated);
    }

    [Fact]
    public async Task Fulfill_WelcomeLostResponse_RetryUsesSameOperationWithoutRecreatingOrders()
    {
        var checkpoint = FilesLinkedCheckpoint();
        var lease = new RecordingLease(checkpoint);
        var client = new RecordingClient { WelcomeServiceAvailable = false };
        var coordinator = new InstantQuotationFulfillmentCoordinator(client);

        var first = await coordinator.FulfillAsync(
            Session(Part(1)), Quote(Part(1)), null, Customer(), lease, checkpoint, CancellationToken.None);
        client.WelcomeServiceAvailable = true;
        var retry = await coordinator.FulfillAsync(
            Session(Part(1)), Quote(Part(1)), null, Customer(), lease, lease.Checkpoint, CancellationToken.None);

        Assert.Equal(InstantQuotationSubmissionOutcome.Partial, first.Outcome);
        Assert.Equal(InstantQuotationSubmissionOutcome.Completed, retry.Outcome);
        Assert.Equal([0], client.OrderPartIndexes);
        Assert.Single(client.WelcomePreparationTokens);
        Assert.Equal(2, client.WelcomeOperationIds.Count);
        Assert.Equal(client.WelcomeOperationIds[0], client.WelcomeOperationIds[1]);
        Assert.Equal(["confirmation-token", "confirmation-token"], client.SentConfirmationTokens);
    }

    [Fact]
    public async Task Fulfill_LostLeaseBeforeRemoteWrite_PerformsNoRemoteMutation()
    {
        var checkpoint = FilesLinkedCheckpoint();
        var lease = new RecordingLease(checkpoint) { FailNextRenewal = true };
        var client = new RecordingClient();
        var coordinator = new InstantQuotationFulfillmentCoordinator(client);

        var result = await coordinator.FulfillAsync(
            Session(Part(1)), Quote(Part(1)), null, Customer(), lease, checkpoint, CancellationToken.None);

        Assert.Equal(InstantQuotationSubmissionOutcome.Partial, result.Outcome);
        Assert.Equal(InstantQuotationProblemCategory.Conflict, result.ProblemCategory);
        Assert.Empty(client.CustomerCalls);
        Assert.Empty(client.IdentityPasswords);
        Assert.Empty(client.OrderPartIndexes);
        Assert.Empty(client.WelcomePreparationTokens);
        Assert.Empty(client.WelcomeOperationIds);
    }

    [Fact]
    public async Task Fulfill_DefiniteOrderFailure_CompensatesCreatedGraphAndRollsBackCheckpoint()
    {
        var checkpoint = FilesLinkedCheckpoint();
        var lease = new RecordingLease(checkpoint);
        var client = new RecordingClient { UnexpectedOrderPartIndex = 1 };
        var coordinator = new InstantQuotationFulfillmentCoordinator(client);
        var parts = new[] { Part(1), Part(2, "bbbbbbbb-cccc-dddd-eeee-ffffffffffff") };

        var result = await coordinator.FulfillAsync(
            Session(parts), Quote(parts), null, Customer(), lease, checkpoint, CancellationToken.None);

        Assert.Equal(InstantQuotationSubmissionOutcome.Partial, result.Outcome);
        Assert.Equal(InstantQuotationProblemCategory.Unexpected, result.ProblemCategory);
        Assert.Equal([902, 901], client.CompensatedOrderIds);
        Assert.Equal([71], client.CompensatedCustomerIds);
        Assert.Equal(InstantQuotationSubmissionCheckpointStatus.FilesLinked, lease.Checkpoint.Status);
        Assert.Null(lease.Checkpoint.CustomerId);
        Assert.Null(lease.Checkpoint.OrderIds);
        Assert.False(lease.Checkpoint.CompensationRequired);
        Assert.Empty(client.IdentityPasswords);
    }

    [Fact]
    public async Task Fulfill_InterruptedCompensation_RetryResumesBeforeAnyProvisioningWrite()
    {
        var checkpoint = FilesLinkedCheckpoint() with
        {
            Status = InstantQuotationSubmissionCheckpointStatus.OrdersProvisioning,
            CustomerId = 71,
            CustomerCreated = true,
            OrderIds = [901],
            CompensationRequired = true,
        };
        var lease = new RecordingLease(checkpoint);
        var client = new RecordingClient { CompensationSucceeds = false };
        var coordinator = new InstantQuotationFulfillmentCoordinator(client);

        var first = await coordinator.FulfillAsync(
            Session(Part(1)), Quote(Part(1)), null, Customer(), lease, checkpoint, CancellationToken.None);
        client.CompensationSucceeds = true;
        var retry = await coordinator.FulfillAsync(
            Session(Part(1)), Quote(Part(1)), null, Customer(), lease, lease.Checkpoint, CancellationToken.None);

        Assert.Equal(InstantQuotationSubmissionOutcome.Partial, first.Outcome);
        Assert.Equal(InstantQuotationSubmissionOutcome.Partial, retry.Outcome);
        Assert.Equal([901, 901], client.CompensatedOrderIds);
        Assert.Single(client.CompensatedCustomerIds);
        Assert.Equal(InstantQuotationSubmissionCheckpointStatus.FilesLinked, lease.Checkpoint.Status);
        Assert.Empty(client.CustomerCalls);
        Assert.Empty(client.OrderPartIndexes);
        Assert.Empty(client.IdentityPasswords);
    }

    private static InstantQuotationSubmissionCheckpoint FilesLinkedCheckpoint()
    {
        var parts = new[] { Part(1), Part(2, "bbbbbbbb-cccc-dddd-eeee-ffffffffffff") };
        return new(
            SubmissionId,
            417,
            InstantQuotationSubmissionCheckpointStatus.FilesLinked,
            new string('0', 64),
            parts.Select(part => File(part)).ToArray());
    }

    private static InstantQuotationSessionState Session(params InstantQuotationPart[] parts) => new(
        "session",
        SubmissionId,
        new InstantQuotationOrderState(parts),
        DateTimeOffset.Parse("2026-08-30T00:00:00+07:00"),
        DateTimeOffset.Parse("2026-08-30T00:00:00+07:00"));

    private static InstantQuotationOrderQuote Quote(params InstantQuotationPart[] parts) =>
        SyntheticPhysicalPricingTestData.Quote(new InstantQuotationOrderState(parts));

    private static InstantQuotationPart Part(int quantity, string uploadId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee") => new(
        Guid.Parse(uploadId),
        $"part-{quantity}.stl",
        new InstantQuotationUploadReference(uploadId),
        AuthoritativeInstantQuotationGeometry.RestoreFromProtectedSession(
            1,
            new string(quantity == 1 ? 'a' : 'b', 64),
            10,
            20,
            10,
            1_000,
            700,
            Enumerable.Repeat(100.0, 64).ToArray(),
            Enumerable.Repeat(60.0, 64).ToArray(),
            12,
            1,
            true,
            false,
            false,
            0.8),
        new InstantQuotationPartConfiguration("ABS", "Black", quantity, BuildPreference.Strength));

    private static InstantQuotationFinalizedFile File(InstantQuotationPart part) => new(
        Guid.Parse(part.UploadReference.Value),
        "private-quotation-files",
        $"instant-quotation/417/{part.PartId:N}.stl",
        part.DisplayFileName,
        "model/stl",
        1_000,
        part.Geometry.Sha256);

    private static InstantQuotationCustomerSubmission Customer() => new(
        "Mali",
        "Ev",
        "mali@example.com",
        "02-000-0000",
        "Thailand",
        "MALIEV",
        "0100000000000 (สำนักงานใหญ่)",
        "Manufacturing request",
        "089-000-0000",
        null,
        "1 Billing Road",
        null,
        "Bangkok",
        "Bangkok",
        "10110");

    private sealed class RecordingLease(InstantQuotationSubmissionCheckpoint checkpoint)
        : IInstantQuotationSubmissionLease
    {
        public InstantQuotationSubmissionCheckpoint Checkpoint { get; private set; } = checkpoint;
        public List<InstantQuotationSubmissionCheckpoint> Writes { get; } = [];
        public bool FailNextRenewal { get; set; }
        public bool FailNotificationFreeze { get; set; }
        public bool FailNotificationSendCheckpoint { get; set; }

        public Task<bool> RenewAsync(CancellationToken cancellationToken)
        {
            var result = !FailNextRenewal;
            FailNextRenewal = false;
            return Task.FromResult(result);
        }

        public Task<InstantQuotationSubmissionCheckpointRead> ReadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new InstantQuotationSubmissionCheckpointRead(true, Checkpoint));

        public Task<bool> TryPutAsync(
            InstantQuotationSubmissionCheckpoint value,
            InstantQuotationSubmissionCheckpointStatus? expectedPriorStatus,
            CancellationToken cancellationToken)
        {
            if (Checkpoint.Status != expectedPriorStatus
                || (FailNotificationFreeze && value.QuotationNotifications is not null && Checkpoint.QuotationNotifications is null))
            {
                return Task.FromResult(false);
            }
            if (FailNotificationSendCheckpoint && value.CustomerQuotationNotificationSent && !Checkpoint.CustomerQuotationNotificationSent)
                return Task.FromResult(false);

            Checkpoint = value;
            Writes.Add(value);
            return Task.FromResult(true);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingClient : IInstantQuotationFulfillmentClient
    {
        public List<string?> CustomerCalls { get; } = [];
        public List<string> IdentityPasswords { get; } = [];
        public List<int> OrderPartIndexes { get; } = [];
        public List<string> WelcomePreparationTokens { get; } = [];
        public List<string> SentConfirmationTokens { get; } = [];
        public List<Guid> WelcomeOperationIds { get; } = [];
        public bool IdentityServiceAvailable { get; set; } = true;
        public bool WelcomeServiceAvailable { get; set; } = true;
        public int? UnavailableOrderPartIndex { get; set; }
        public int? UnexpectedOrderPartIndex { get; set; }
        public bool CompensationSucceeds { get; set; } = true;
        public List<int> CompensatedOrderIds { get; } = [];
        public List<int> CompensatedCustomerIds { get; } = [];
        public bool QuotationPreparationAvailable { get; set; } = true;
        public bool ExpiredQuotationLinks { get; set; }
        public int QuotationPreparations { get; private set; }
        public string? FailedQuotationRecipient { get; set; }
        public List<(EmailNotification Message, Guid OperationId)> QuotationNotifications { get; } = [];
        public Action? AfterQuotationSend { get; init; }

        public Task<InstantQuotationNotificationPreparationResult> PrepareQuotationNotificationsAsync(
            InstantQuotationSessionState session, InstantQuotationOrderQuote quote, InstantQuotationCustomerSubmission customer,
            int requestReference, IReadOnlyList<InstantQuotationFinalizedFile> files, CancellationToken cancellationToken)
        {
            QuotationPreparations++;
            return Task.FromResult(new InstantQuotationNotificationPreparationResult(QuotationPreparationAvailable
                ? new(new("customer@example.test", "Quotation", "frozen customer body", null, null, null),
                    new("manufacturing@maliev.com", "Quotation", "frozen manufacturing body", null, null, null),
                    DateTimeOffset.UtcNow.AddMinutes(ExpiredQuotationLinks ? -1 : 30)) : null, QuotationPreparationAvailable, true));
        }

        public Task<NotificationResult> SendQuotationNotificationAsync(EmailNotification message, Guid operationId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            QuotationNotifications.Add((message, operationId));
            AfterQuotationSend?.Invoke();
            return Task.FromResult(new NotificationResult(message.To != FailedQuotationRecipient, true, true));
        }

        public Task<InstantQuotationCustomerProvisionResult> ProvisionCustomerAsync(
            string? ownerIdentity,
            InstantQuotationCustomerSubmission customer,
            CancellationToken cancellationToken)
        {
            CustomerCalls.Add(ownerIdentity);
            return Task.FromResult(new InstantQuotationCustomerProvisionResult(71, true, true, true));
        }

        public Task<InstantQuotationIdentityProvisionResult> ProvisionIdentityAsync(
            int customerId,
            InstantQuotationCustomerSubmission customer,
            string temporaryPassword,
            CancellationToken cancellationToken)
        {
            IdentityPasswords.Add(temporaryPassword);
            return Task.FromResult(new InstantQuotationIdentityProvisionResult(
                IdentityServiceAvailable,
                IdentityServiceAvailable,
                true));
        }

        public Task<InstantQuotationOrderProvisionResult> ProvisionOrderAsync(
            string submissionId,
            int partIndex,
            int customerId,
            string? customerDescription,
            InstantQuotationPart part,
            InstantQuotationPartQuote quote,
            int leadTimeDays,
            InstantQuotationFinalizedFile file,
            CancellationToken cancellationToken)
        {
            OrderPartIndexes.Add(partIndex);
            return Task.FromResult(UnavailableOrderPartIndex == partIndex
                ? new InstantQuotationOrderProvisionResult(null, false, false, true, false)
                : UnexpectedOrderPartIndex == partIndex
                    ? new InstantQuotationOrderProvisionResult(901 + partIndex, false, true, true, false)
                : new InstantQuotationOrderProvisionResult(901 + partIndex, true, true, true, false));
        }

        public Task<bool> CompensateOrderAsync(int orderId, CancellationToken cancellationToken)
        {
            CompensatedOrderIds.Add(orderId);
            return Task.FromResult(CompensationSucceeds);
        }

        public Task<bool> CompensateCustomerAsync(int customerId, CancellationToken cancellationToken)
        {
            CompensatedCustomerIds.Add(customerId);
            return Task.FromResult(CompensationSucceeds);
        }

        public Task<NotificationResult> SendWelcomeAsync(
            InstantQuotationCustomerSubmission customer,
            string temporaryPassword,
            string confirmationToken,
            Guid operationId,
            CancellationToken cancellationToken)
        {
            WelcomeOperationIds.Add(operationId);
            SentConfirmationTokens.Add(confirmationToken);
            return Task.FromResult(new NotificationResult(
                WelcomeServiceAvailable,
                WelcomeServiceAvailable,
                true));
        }

        public Task<InstantQuotationWelcomePreparationResult> PrepareWelcomeAsync(
            InstantQuotationCustomerSubmission customer,
            CancellationToken cancellationToken)
        {
            WelcomePreparationTokens.Add("confirmation-token");
            return Task.FromResult(new InstantQuotationWelcomePreparationResult(
                "confirmation-token",
                true,
                true));
        }
    }
}
