using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Components.Pages.InstantQuotation;
using Microsoft.Extensions.DependencyInjection;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Coordinator lifetime proof with a registered protected store and real resin kernel; not upload admission proof.</summary>
public sealed class MaterialPricingLifetimeTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Fact]
    public async Task Disposal_CancelsReachedPricingToken()
    {
        await using var fixture = await Fixture.CreateAsync();
        var pending = fixture.Pricing.Arm();
        var update = fixture.Workflow.UpdateConfigurationAsync(fixture.PartId, "M68", "White", 2, default);
        try
        {
            await pending.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await fixture.Workflow.DisposeAsync();
            Assert.True(pending.Token.IsCancellationRequested, "Coordinator disposal did not cancel reached pricing.");
        }
        finally { pending.Release.TrySetResult(); await IgnoreCancellationAsync(update); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NoncooperativeLatePrice_AfterDisposalCannotPublishQuoteOrTicket(bool failLate)
    {
        await using var fixture = await Fixture.CreateAsync();
        var revision = fixture.Workflow.AuthoritativeQuoteRevision;
        var pending = fixture.Pricing.Arm(ignoreCancellation: true, failLate: failLate);
        var update = fixture.Workflow.UpdateConfigurationAsync(fixture.PartId, "M68", "White", 2, default);
        try
        {
            await pending.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await fixture.Workflow.DisposeAsync();
            pending.Release.TrySetResult();
            await IgnoreCancellationAsync(update);
            await pending.Exited.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Null(fixture.Workflow.OrderQuote);
            Assert.Equal(revision, fixture.Workflow.AuthoritativeQuoteRevision);
            var stored = (await fixture.Store.GetAsync(fixture.SessionId, Owner, default))!;
            Assert.Null(stored.QuoteAuthorization);
            Assert.Null(stored.PhysicalReceipts);
        }
        finally { pending.Release.TrySetResult(); await IgnoreCancellationAsync(update); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NoncooperativePrice_StopsProducerWaitBeforeDependencyRelease(bool callerAbort)
    {
        await using var fixture = await Fixture.CreateAsync();
        using var caller = new CancellationTokenSource();
        var pending = fixture.Pricing.Arm(ignoreCancellation: true);
        var update = fixture.Workflow.UpdateConfigurationAsync(fixture.PartId, "M68", "White", 2, caller.Token);
        var stage = "waiting-pricing-entry";
        try
        {
            await pending.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            stage = "stopping-producer-wait";
            if (callerAbort) caller.Cancel(); else await fixture.Workflow.DisposeAsync();
            stage = "awaiting-producer-completion";
            var winner = await Task.WhenAny(update, Task.Delay(TimeSpan.FromSeconds(2)));
            Assert.Same(update, winner);
            var canceled = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => update);
            if (callerAbort) Assert.Equal(caller.Token, canceled.CancellationToken);
            Assert.False(pending.Exited.Task.IsCompleted);
            Assert.Null(fixture.Workflow.OrderQuote);
            Assert.Null((await fixture.Store.GetAsync(fixture.SessionId, Owner, default))!.QuoteAuthorization);
        }
        catch (Exception exception)
        {
            output.WriteLine("pricing-lifetime stage={0}; exception={1}; entered={2}; exited={3}; update={4}; workingSetBytes={5}; managedBytes={6}; memoryLoadBytes={7}; gcAvailableBytes={8}; threadPoolThreads={9}; pendingWorkItems={10}",
                stage, exception.GetType().FullName, pending.Entered.Task.IsCompleted, pending.Exited.Task.IsCompleted,
                update.Status, Environment.WorkingSet, GC.GetTotalMemory(false), GC.GetGCMemoryInfo().MemoryLoadBytes,
                GC.GetGCMemoryInfo().TotalAvailableMemoryBytes, ThreadPool.ThreadCount, ThreadPool.PendingWorkItemCount);
            throw;
        }
        finally
        {
            try
            {
                pending.Release.TrySetResult(); await IgnoreCancellationAsync(update);
                await pending.Exited.Task.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (Exception exception)
            {
                stage = "cleanup-after-" + stage;
                output.WriteLine("pricing-lifetime stage={0}; exception={1}; entered={2}; exited={3}; update={4}; workingSetBytes={5}; managedBytes={6}; memoryLoadBytes={7}; gcAvailableBytes={8}; threadPoolThreads={9}; pendingWorkItems={10}",
                    stage, exception.GetType().FullName, pending.Entered.Task.IsCompleted, pending.Exited.Task.IsCompleted,
                    update.Status, Environment.WorkingSet, GC.GetTotalMemory(false), GC.GetGCMemoryInfo().MemoryLoadBytes,
                    GC.GetGCMemoryInfo().TotalAvailableMemoryBytes, ThreadPool.ThreadCount, ThreadPool.PendingWorkItemCount);
                throw;
            }
        }
    }

    [Fact]
    public async Task QueuedConfiguration_DisposalDoesNotMutateNewerRequestOrStartAnotherPrice()
    {
        await using var fixture = await Fixture.CreateAsync(partCount: 2);
        var pending = fixture.Pricing.Arm();
        var first = fixture.Workflow.UpdateConfigurationAsync(fixture.PartId, "M68", "White", 2, default);
        await pending.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var queued = fixture.Workflow.UpdateConfigurationAsync(fixture.OtherPartId, "M68", "White", 3, default);
        try
        {
            await fixture.Workflow.DisposeAsync();
            pending.Release.TrySetResult();
            await IgnoreCancellationAsync(first);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);
            Assert.Equal(2, fixture.Workflow.Parts.Single(part => part.PartId == fixture.PartId).Configuration.Quantity);
            Assert.Equal(2, (await fixture.Store.GetAsync(fixture.SessionId, Owner, default))!.Parts.Single(part => part.PartId == fixture.PartId).Configuration.Quantity);
            Assert.Equal(1, fixture.Workflow.Parts.Single(part => part.PartId == fixture.OtherPartId).Configuration.Quantity);
            Assert.Equal(1, (await fixture.Store.GetAsync(fixture.SessionId, Owner, default))!.Parts.Single(part => part.PartId == fixture.OtherPartId).Configuration.Quantity);
            Assert.Null(fixture.Workflow.OrderQuote);
        }
        finally { pending.Release.TrySetResult(); await IgnoreCancellationAsync(first); await IgnoreCancellationAsync(queued); }
    }

    [Fact]
    public async Task CallerCancellation_PreservesOriginalTokenAndNoFinalAuthority()
    {
        await using var fixture = await Fixture.CreateAsync();
        using var caller = new CancellationTokenSource();
        var pending = fixture.Pricing.Arm();
        var update = fixture.Workflow.UpdateConfigurationAsync(fixture.PartId, "M68", "White", 2, caller.Token);
        await pending.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        caller.Cancel();
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => update);
        Assert.Equal(caller.Token, error.CancellationToken);
        Assert.Null(fixture.Workflow.OrderQuote);
        Assert.Null((await fixture.Store.GetAsync(fixture.SessionId, Owner, default))!.QuoteAuthorization);
    }

    [Fact]
    public async Task RestoredSession_DisposalCancelsReachedPricingAndCannotIssueTicket()
    {
        await using var fixture = await Fixture.CreateAsync(initialize: false);
        var pending = fixture.Pricing.Arm();
        var restore = fixture.Workflow.InitializeAsync(fixture.SessionId, default);
        try
        {
            await pending.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await fixture.Workflow.DisposeAsync();
            Assert.True(pending.Token.IsCancellationRequested);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => restore);
            Assert.Null(fixture.Workflow.OrderQuote);
            Assert.Null((await fixture.Store.GetAsync(fixture.SessionId, Owner, default))!.QuoteAuthorization);
        }
        finally { pending.Release.TrySetResult(); await IgnoreCancellationAsync(restore); }
    }

    [Fact]
    public async Task InvalidConfiguration_LeavesExistingValidQuoteAndAuthorizationUntouched()
    {
        await using var fixture = await Fixture.CreateAsync();
        var quote = fixture.Workflow.OrderQuote;
        var before = (await fixture.Store.GetAsync(fixture.SessionId, Owner, default))!;
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            fixture.Workflow.UpdateConfigurationAsync(fixture.PartId, "M68", "White", 0, default));
        Assert.Same(quote, fixture.Workflow.OrderQuote);
        var after = (await fixture.Store.GetAsync(fixture.SessionId, Owner, default))!;
        Assert.Equal(before.UpdatedAt, after.UpdatedAt);
        Assert.True(before.QuoteAuthorization!.OrderTicket == after.QuoteAuthorization!.OrderTicket,
            "Invalid configuration changed protected authorization.");
    }


    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task SupersededActualPhysicalQuote_CannotReplaceCurrentReceiptOrProtectedAuthorization(
        bool failLate, bool captureBeforeHold)
    {
        await using var fixture = await Fixture.CreateAsync(physical: true);
        var older = fixture.Pricing.Arm(ignoreCancellation: true, failLate: failLate, captureBeforeHold: captureBeforeHold);
        var first = fixture.Workflow.UpdateConfigurationAsync(fixture.PartId, "PLA", "Black", 2, default);
        await older.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var newer = fixture.Workflow.UpdateConfigurationAsync(fixture.PartId, "PLA", "Black", 3, default);
        Exception? primary = null;
        try
        {
            await newer.WaitAsync(TimeSpan.FromSeconds(5));
            await first.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(older.Token.IsCancellationRequested);
            Assert.False(older.ActualTask!.IsCompleted);
            var quote = Assert.IsType<InstantQuotationOrderQuote>(fixture.Workflow.OrderQuote);
            var stored = (await fixture.Store.GetAsync(fixture.SessionId, Owner, default))!;
            var receipt = Assert.Single(stored.PhysicalReceipts!,
                row => row.MaterialKey == "PLA");
            Assert.Equal(3, Assert.Single(stored.Parts).Configuration.Quantity);
            Assert.Equal(3, receipt.Quantity);
            Assert.Equal(Owner, receipt.OwnerIdentity);
            Assert.Equal(fixture.SessionId, receipt.SessionId);
            Assert.Equal(fixture.PartId, receipt.PartId);
            Assert.Equal(fixture.PhysicalDigest, receipt.UploadSha256);
            Assert.Equal(Guid.Parse("66666666-6666-6666-6666-666666666666"), receipt.FileId);
            Assert.Equal(3.2, receipt.BoundingCm3PerUnit, 3);
            Assert.True(receipt.DepositedMm3 > 0);
            Assert.True(receipt.MotionSeconds > 0);
            Assert.True(fixture.Tickets.Validate(stored, quote, stored.QuoteAuthorization!, DateTimeOffset.UtcNow));
            var revision = fixture.Workflow.AuthoritativeQuoteRevision;
            var protectedBefore = System.Text.Json.JsonSerializer.Serialize(stored);
            older.Release.TrySetResult();
            if (failLate) await Assert.ThrowsAsync<ControlledLatePriceException>(() => older.ActualTask!);
            else await older.ActualTask!.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Same(quote, fixture.Workflow.OrderQuote);
            Assert.Equal(revision, fixture.Workflow.AuthoritativeQuoteRevision);
            var after = (await fixture.Store.GetAsync(fixture.SessionId, Owner, default))!;
            Assert.True(protectedBefore == System.Text.Json.JsonSerializer.Serialize(after));
        }
        catch (Exception exception) { primary = exception; throw; }
        finally
        {
            older.Release.TrySetResult();
            await MaterialPricingTestCleanup.ReleaseAllAsync(primary,
                async () => await DrainSupersessionTaskAsync(first),
                async () => await DrainSupersessionTaskAsync(newer),
                async () => await DrainSupersessionTaskAsync(older.ActualTask!));
        }
    }

    [Fact]
    public async Task SupersessionCallbackFault_BelongsToOldRequestAndDoesNotBlockCurrentAdmission()
    {
        await using var fixture = await Fixture.CreateAsync();
        var older = fixture.Pricing.Arm(ignoreCancellation: true);
        var first = fixture.Workflow.UpdateConfigurationAsync(fixture.PartId, "M68", "White", 2, default);
        await older.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var registration = older.Token.Register(static () => throw new ControlledCancellationCallbackException());
        var newer = fixture.Workflow.UpdateConfigurationAsync(fixture.PartId, "M68", "White", 3, default);
        Exception? primary = null;
        try
        {
            await newer.WaitAsync(TimeSpan.FromSeconds(5));
            var error = await Assert.ThrowsAsync<InstantQuotationWorkflowCoordinator.ObsoleteQuoteCancellationException>(() => first);
            var callbacks = Assert.IsType<AggregateException>(error.InnerException);
            Assert.IsType<ControlledCancellationCallbackException>(Assert.Single(callbacks.InnerExceptions));
            Assert.False(older.ActualTask!.IsCompleted);
            Assert.Equal(3, Assert.Single(fixture.Workflow.Parts).Configuration.Quantity);
            Assert.NotNull(fixture.Workflow.OrderQuote);
            var stored = (await fixture.Store.GetAsync(fixture.SessionId, Owner, default))!;
            Assert.Equal(3, Assert.Single(stored.Parts).Configuration.Quantity);
            Assert.NotNull(stored.QuoteAuthorization);
        }
        catch (Exception exception) { primary = exception; throw; }
        finally
        {
            older.Release.TrySetResult();
            await MaterialPricingTestCleanup.ReleaseAllAsync(primary,
                async () => await DrainSupersessionTaskAsync(first, callbackFault: true),
                async () => await DrainSupersessionTaskAsync(newer),
                async () => await DrainSupersessionTaskAsync(older.ActualTask!));
        }
    }

    private sealed class ControlledCancellationCallbackException : Exception { }

    [Theory]
    [InlineData("M68", "White", 0)]
    [InlineData("UNKNOWN", "White", 3)]
    [InlineData("M68", "INVALID", 3)]
    public async Task InvalidNewRequest_DoesNotRetireReachedSamePartOwnership(string material, string color, int quantity)
    {
        await using var fixture = await Fixture.CreateAsync();
        var older = fixture.Pricing.Arm(ignoreCancellation: true);
        var first = fixture.Workflow.UpdateConfigurationAsync(fixture.PartId, "M68", "White", 2, default);
        Exception? primary = null;
        try
        {
            await older.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.ThrowsAnyAsync<ArgumentException>(() =>
                fixture.Workflow.UpdateConfigurationAsync(fixture.PartId, material, color, quantity, default));
            Assert.False(older.Token.IsCancellationRequested);
            Assert.False(first.IsCompleted);
            older.Release.TrySetResult();
            await first.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(2, Assert.Single(fixture.Workflow.Parts).Configuration.Quantity);
            Assert.NotNull((await fixture.Store.GetAsync(fixture.SessionId, Owner, default))!.QuoteAuthorization);
        }
        catch (Exception exception) { primary = exception; throw; }
        finally
        {
            older.Release.TrySetResult();
            await MaterialPricingTestCleanup.ReleaseAllAsync(primary,
                async () => await DrainSupersessionTaskAsync(first),
                async () => await DrainSupersessionTaskAsync(older.ActualTask!));
        }
    }

    [Fact]
    public async Task UnrelatedPartRequest_DoesNotRetireTheCurrentPartsPricingConsumer()
    {
        await using var fixture = await Fixture.CreateAsync(partCount: 2);
        var older = fixture.Pricing.Arm(ignoreCancellation: true);
        var first = fixture.Workflow.UpdateConfigurationAsync(fixture.PartId, "M68", "White", 2, default);
        await older.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var other = fixture.Workflow.UpdateConfigurationAsync(fixture.OtherPartId, "M68", "White", 3, default);
        Exception? primary = null;
        try
        {
            Assert.False(older.Token.IsCancellationRequested);
            Assert.False(other.IsCompleted);
            older.Release.TrySetResult();
            await first.WaitAsync(TimeSpan.FromSeconds(5));
            await other.WaitAsync(TimeSpan.FromSeconds(5));
            var parts = fixture.Workflow.Parts;
            Assert.Equal(2, parts.Single(part => part.PartId == fixture.PartId).Configuration.Quantity);
            Assert.Equal(3, parts.Single(part => part.PartId == fixture.OtherPartId).Configuration.Quantity);
        }
        catch (Exception exception) { primary = exception; throw; }
        finally
        {
            older.Release.TrySetResult();
            await MaterialPricingTestCleanup.ReleaseAllAsync(primary,
                async () => await DrainSupersessionTaskAsync(first),
                async () => await DrainSupersessionTaskAsync(other),
                async () => await DrainSupersessionTaskAsync(older.ActualTask!));
        }
    }

    [Theory]
    [InlineData("success")]
    [InlineData("refused")]
    [InlineData("unknown")]
    [InlineData("mismatch")]
    [InlineData("denied")]
    [InlineData("caller-canceled")]
    public async Task AdmittedRemoval_RetiresOnlyAfterExactAuthorizedAcknowledgement(string outcome)
    {
        var removal = new ControlledRemovalClient(outcome);
        await using var fixture = await Fixture.CreateAsync(physical: true, partCount: 2, removal: removal);
        using var caller = new CancellationTokenSource();
        var older = fixture.Pricing.Arm(ignoreCancellation: true, captureBeforeHold: true);
        var first = fixture.Workflow.UpdateConfigurationAsync(fixture.PartId, "PLA", "Black", 2, default);
        Task removing = Task.CompletedTask;
        Exception? primary = null;
        var acknowledged = 0;
        Task? rejectedAfterAcknowledgement = null;
        try
        {
            await older.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var before = (await fixture.Store.GetAsync(fixture.SessionId, Owner, default))!;
            var protectedBefore = System.Text.Json.JsonSerializer.Serialize(before);
            removing = fixture.Workflow.RemoveAsync(fixture.PartId, () =>
            {
                acknowledged++;
                rejectedAfterAcknowledgement = fixture.Workflow.UpdateConfigurationAsync(
                    fixture.PartId, "PLA", "Black", 3, default);
            }, caller.Token);
            await removal.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(older.Token.IsCancellationRequested);
            Assert.Equal(0, acknowledged);
            if (outcome == "caller-canceled") caller.Cancel();
            else removal.Release.TrySetResult();
            if (outcome == "caller-canceled")
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => removing);
            else await removing.WaitAsync(TimeSpan.FromSeconds(5));

            if (outcome == "success")
            {
                await first.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.True(older.Token.IsCancellationRequested);
                Assert.Equal(1, acknowledged);
                await Assert.ThrowsAsync<ArgumentException>(() => rejectedAfterAcknowledgement!);
                Assert.False(older.ActualTask!.IsCompleted);
                Assert.Equal(fixture.OtherPartId, Assert.Single(fixture.Workflow.Parts).PartId);
                var quote = Assert.IsType<InstantQuotationOrderQuote>(fixture.Workflow.OrderQuote);
                var current = (await fixture.Store.GetAsync(fixture.SessionId, Owner, default))!;
                var receipt = Assert.Single(current.PhysicalReceipts!, row => row.MaterialKey == "PLA");
                Assert.Equal(fixture.OtherPartId, receipt.PartId);
                Assert.Equal(Guid.Parse("88888888-8888-8888-8888-888888888888"), receipt.FileId);
                Assert.Equal(fixture.PhysicalDigest, receipt.UploadSha256);
                Assert.Equal(Owner, receipt.OwnerIdentity);
                Assert.Equal(fixture.SessionId, receipt.SessionId);
                Assert.Equal(1, receipt.Quantity);
                Assert.True(receipt.DepositedMm3 > 0);
                Assert.True(fixture.Tickets.Validate(current, quote, current.QuoteAuthorization!, DateTimeOffset.UtcNow));
                var afterRemoval = System.Text.Json.JsonSerializer.Serialize(current);
                older.Release.TrySetResult();
                await older.ActualTask.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Same(quote, fixture.Workflow.OrderQuote);
                Assert.True(afterRemoval == System.Text.Json.JsonSerializer.Serialize(
                    (await fixture.Store.GetAsync(fixture.SessionId, Owner, default))!));
            }
            else
            {
                Assert.False(older.Token.IsCancellationRequested);
                Assert.False(first.IsCompleted);
                Assert.Equal(0, acknowledged);
                Assert.Null(rejectedAfterAcknowledgement);
                Assert.Equal(2, fixture.Workflow.Parts.Count);
                Assert.True(protectedBefore == System.Text.Json.JsonSerializer.Serialize(
                    (await fixture.Store.GetAsync(fixture.SessionId, Owner, default))!));
                older.Release.TrySetResult();
                await first.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Equal(2, fixture.Workflow.Parts.Count);
            }
        }
        catch (Exception exception) { primary = exception; throw; }
        finally
        {
            removal.Release.TrySetResult();
            older.Release.TrySetResult();
            await MaterialPricingTestCleanup.ReleaseAllAsync(primary,
                async () => await DrainSupersessionTaskAsync(first),
                async () => await DrainSupersessionTaskAsync(removing),
                async () => { if (older.ActualTask is { } actual) await DrainSupersessionTaskAsync(actual); });
        }
    }

    [Fact]
    public async Task AcknowledgedRemoval_RejectsCachedAdmissionAndQueuedMutationUnderExistingGates()
    {
        var removal = new ControlledRemovalClient("success");
        await using var fixture = await Fixture.CreateAsync(partCount: 2, removal: removal);
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var gate = (SemaphoreSlim)typeof(InstantQuotationWorkflowCoordinator)
            .GetField("stateGate", flags)!.GetValue(fixture.Workflow)!;
        var entries = (System.Collections.IEnumerable)typeof(InstantQuotationWorkflowCoordinator)
            .GetField("entries", flags)!.GetValue(fixture.Workflow)!;
        var entry = entries.Cast<object>().Single(candidate =>
            ((InstantQuotationPart?)candidate.GetType().GetProperty("Part")!.GetValue(candidate))?.PartId == fixture.PartId);
        Assert.Equal("M68", fixture.Workflow.ValidateConfiguration(
            fixture.PartId, "M68", "White", 7).Key);
        using var lease = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await gate.WaitAsync(lease.Token);
        var held = true;
        var queued = fixture.Workflow.UpdateConfigurationAsync(fixture.PartId, "M68", "White", 7, default);
        Task removing = Task.CompletedTask;
        var acknowledged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Exception? primary = null;
        try
        {
            Assert.False(queued.IsCompleted);
            removing = fixture.Workflow.RemoveAsync(fixture.PartId, () => acknowledged.TrySetResult(), default);
            await removal.Entered.Task.WaitAsync(lease.Token);
            removal.Release.TrySetResult();
            await acknowledged.Task.WaitAsync(lease.Token);
            // Exercise the actual private admission boundary with the previously validated cached entry.
            var admission = typeof(InstantQuotationWorkflowCoordinator)
                .GetMethod("BeginConfigurationRequest", flags)!;
            var refused = Assert.Throws<System.Reflection.TargetInvocationException>(() =>
                admission.Invoke(fixture.Workflow, [fixture.PartId, entry]));
            Assert.IsType<ArgumentException>(refused.InnerException);
            Assert.False(queued.IsCompleted);
            gate.Release();
            held = false;
            await queued.WaitAsync(lease.Token);
            await removing.WaitAsync(lease.Token);
            var removedPart = (InstantQuotationPart)entry.GetType().GetProperty("Part")!.GetValue(entry)!;
            Assert.Equal(1, removedPart.Configuration.Quantity);
            Assert.Equal(fixture.OtherPartId, Assert.Single(fixture.Workflow.Parts).PartId);
            var stored = (await fixture.Store.GetAsync(fixture.SessionId, Owner, default))!;
            Assert.Equal(fixture.OtherPartId, Assert.Single(stored.Parts).PartId);
            Assert.Equal(1, Assert.Single(stored.Parts).Configuration.Quantity);
            Assert.NotNull(stored.QuoteAuthorization);
        }
        catch (Exception exception) { primary = exception; throw; }
        finally
        {
            if (held) gate.Release();
            removal.Release.TrySetResult();
            await MaterialPricingTestCleanup.ReleaseAllAsync(primary,
                async () => await DrainSupersessionTaskAsync(queued),
                async () => await DrainSupersessionTaskAsync(removing));
        }
    }

    private sealed class ControlledRemovalClient(string outcome) : IInstantQuotationUploadClient
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<InstantQuotationRemoveResult> RemoveAsync(string sessionId, string? owner,
            InstantQuotationUploadReference reference, string operationId, CancellationToken token)
        {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(token);
            return new(outcome == "mismatch" ? "different-operation" : operationId,
                outcome == "unknown" ? InstantQuotationServiceStatus.Unavailable : InstantQuotationServiceStatus.Available,
                outcome == "denied" ? InstantQuotationAuthorizationStatus.Denied : InstantQuotationAuthorizationStatus.Authorized,
                outcome == "refused" ? InstantQuotationOperationStatus.Failed : InstantQuotationOperationStatus.Succeeded,
                outcome == "refused" ? InstantQuotationProblemCategory.Validation : InstantQuotationProblemCategory.None);
        }
        public Task<InstantQuotationUploadResult> UploadAsync(string sessionId, string? owner, Stream content,
            string fileName, string contentType, long contentLength, InstantQuotationGeometryClaim claim,
            string operationId, CancellationToken token) => throw new InvalidOperationException("No upload expected.");
        public Task<InstantQuotationFinalizationResult> FinalizeAsync(string sessionId, string? owner, int requestId,
            IReadOnlyList<InstantQuotationUploadReference> references, string operationId, CancellationToken token) =>
            throw new InvalidOperationException("No finalization expected.");
    }

    private static async Task DrainSupersessionTaskAsync(Task task, bool callbackFault = false)
    {
        try { await task.WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (OperationCanceledException) when (task.IsCanceled) { }
        catch (ControlledLatePriceException) { }
        catch (InstantQuotationWorkflowCoordinator.ObsoleteQuoteCancellationException exception) when (
            callbackFault && exception.InnerException is AggregateException callbacks
            && callbacks.InnerExceptions.All(error => error is ControlledCancellationCallbackException))
        { }
    }

    private static async Task IgnoreCancellationAsync(Task task)
    {
        try { await task; } catch (OperationCanceledException) { } catch (ControlledLatePriceException) { }
    }

    private const string Owner = "pricing-lifetime-fixture-owner";
    private sealed class ControlledLatePriceException : Exception { }
    private sealed class Pending(bool ignoreCancellation, bool failLate, bool captureBeforeHold)
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Exited { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken Token { get; set; }
        public bool IgnoreCancellation { get; } = ignoreCancellation;
        public bool FailLate { get; } = failLate;
        public bool CaptureBeforeHold { get; } = captureBeforeHold;
        public Task<InstantQuotationOrderQuote?>? ActualTask { get; set; }
    }

    private sealed class ControlledObserverBoundary(IInstantQuotationAuthoritativePricingService inner)
        : IInstantQuotationAuthoritativePricingService
    {
        private readonly Queue<Pending> pending = new();
        private readonly List<Pending> all = [];
        private readonly List<(Task Task, Pending? Control)> actual = [];
        public Pending Arm(bool ignoreCancellation = false, bool failLate = false, bool captureBeforeHold = false)
        {
            var next = new Pending(ignoreCancellation, failLate, captureBeforeHold); pending.Enqueue(next); all.Add(next); return next;
        }
        public void ReleaseAll() { foreach (var entry in all) entry.Release.TrySetResult(); }
        public async Task DrainAsync()
        {
            foreach (var item in actual)
            {
                try { await item.Task.WaitAsync(TimeSpan.FromSeconds(5)); }
                catch (OperationCanceledException) when (item.Control?.Token.IsCancellationRequested == true) { }
                catch (ControlledLatePriceException) when (item.Control?.FailLate == true) { }
            }
        }
        public Task<InstantQuotationOrderQuote?> QuoteAsync(InstantQuotationSessionState session,
            string? owner, bool includeComparisons, CancellationToken token) =>
            Start(session, owner, includeComparisons, null, token);

        public Task<InstantQuotationOrderQuote?> QuoteAsync(InstantQuotationSessionState session,
            string? owner, bool includeComparisons,
            Func<InstantQuotationMaterialPricingProgress, CancellationToken, ValueTask> observer,
            CancellationToken token) => Start(session, owner, includeComparisons, observer, token);

        private Task<InstantQuotationOrderQuote?> Start(InstantQuotationSessionState session,
            string? owner, bool includeComparisons,
            Func<InstantQuotationMaterialPricingProgress, CancellationToken, ValueTask>? observer, CancellationToken token)
        {
            pending.TryDequeue(out var next);
            var task = RunAsync();
            if (next is not null) next.ActualTask = task;
            actual.Add((task, next));
            return task;

            Task<InstantQuotationOrderQuote?> Inner(CancellationToken current) => observer is null
                ? inner.QuoteAsync(session, owner, includeComparisons, current)
                : inner.QuoteAsync(session, owner, includeComparisons, observer, current);

            async Task<InstantQuotationOrderQuote?> RunAsync()
            {
                if (next is null) return await Inner(token);
                try
                {
                    next.Token = token;
                    var current = next.IgnoreCancellation ? CancellationToken.None : token;
                    var captured = next.CaptureBeforeHold ? await Inner(current) : null;
                    next.Entered.TrySetResult();
                    await next.Release.Task.WaitAsync(current);
                    if (next.FailLate) throw new ControlledLatePriceException();
                    return next.CaptureBeforeHold ? captured : await Inner(current);
                }
                finally { next.Exited.TrySetResult(); }
            }
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly TestingWebApplicationFactory factory = new();
        private readonly AsyncServiceScope scope;
        public IInstantQuotationSessionStore Store { get; }
        public ControlledObserverBoundary Pricing { get; }
        public InstantQuotationWorkflowCoordinator Workflow { get; }
        public Guid PartId { get; } = Guid.Parse("55555555-5555-5555-5555-555555555555");
        public Guid OtherPartId { get; } = Guid.Parse("77777777-7777-7777-7777-777777777777");
        public IInstantQuotationQuoteTicketService Tickets { get; }
        private static readonly byte[] PhysicalBytes = WallThicknessParityBrowserFixture.Box(8);
        public string PhysicalDigest => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(PhysicalBytes));
        public string SessionId { get; private set; } = "";
        private Fixture(bool physical, ControlledRemovalClient? removal)
        {
            scope = factory.Services.CreateAsyncScope();
            Store = scope.ServiceProvider.GetRequiredService<IInstantQuotationSessionStore>();
            var profiles = Legacy.Maliev.Web.Application.Pricing.FdmRuntimeProfileCatalog.LoadEmbedded();
            Pricing = new(physical
                ? new InstantQuotationAuthoritativePricingService(
                    new InstantQuotationBoundPhysicalAnalysisService(Store,
                        new InstantQuotationAdmittedMeshService(new PhysicalInputReader()), profiles),
                    profiles, new InstantQuotationPricingService())
                : scope.ServiceProvider.GetRequiredService<IInstantQuotationAuthoritativePricingService>());
            Tickets = scope.ServiceProvider.GetRequiredService<IInstantQuotationQuoteTicketService>();
            Workflow = new(Store,
                removal ?? scope.ServiceProvider.GetRequiredService<IInstantQuotationUploadClient>(),
                scope.ServiceProvider.GetRequiredService<IInstantQuotationPricingService>(), Owner,
                quoteTicketService: Tickets,
                authoritativePricingService: Pricing);
        }
        public static async Task<Fixture> CreateAsync(bool initialize = true, bool physical = false, int partCount = 1, ControlledRemovalClient? removal = null)
        {
            var fixture = new Fixture(physical, removal);
            try
            {
                var reference = new InstantQuotationUploadReference("66666666-6666-6666-6666-666666666666");
                var digest = physical ? fixture.PhysicalDigest : new string('a', 64);
                var geometry = AuthoritativeInstantQuotationGeometry.FromCompletedLegacyUpload(
                    InstantQuotationUploadResult.Succeeded("controlled-historical-session", reference, digest),
                    new InstantQuotationGeometryClaim(1, digest, physical ? 20 : 8, physical ? 20 : 8, 8,
                        physical ? 3200 : 512, physical ? 1440 : 384,
                        Enumerable.Repeat(1d, 64).ToArray(), Enumerable.Repeat(1d, 64).ToArray(),
                        12, 1, true, false, false, 1))!;
                var part = new InstantQuotationPart(fixture.PartId, "controlled.stl", reference, geometry,
                    new InstantQuotationPartConfiguration(physical ? "PLA" : "M68", physical ? "Black" : "White", 1),
                    physical ? new InstantQuotationPhysicalAnalysisUpload(
                        Guid.Parse(reference.Value), "controlled.stl", "model/stl", PhysicalBytes.Length, digest, "clean") : null);
                var parts = partCount == 1 ? new[] { part } : new[] { part, part with
                {
                    PartId = fixture.OtherPartId,
                    UploadReference = new InstantQuotationUploadReference("88888888-8888-8888-8888-888888888888"),
                    PhysicalAnalysisUpload = part.PhysicalAnalysisUpload is { } admitted
                        ? admitted with { FileId = Guid.Parse("88888888-8888-8888-8888-888888888888") } : null,
                } };
                var state = await fixture.Store.CreateAsync(Owner, new InstantQuotationOrderState(parts), default);
                fixture.SessionId = state.SessionId;
                if (initialize) await fixture.Workflow.InitializeAsync(state.SessionId, default);
                return fixture;
            }
            catch { await fixture.DisposeAsync(); throw; }
        }
        public async ValueTask DisposeAsync()
        {
            Pricing.ReleaseAll();
            try { await Workflow.DisposeAsync(); await Pricing.DrainAsync(); }
            finally
            {
                try { await scope.DisposeAsync(); }
                finally { await factory.DisposeAsync(); }
            }
        }

        private sealed class PhysicalInputReader : IInstantQuotationPhysicalAnalysisInputReader
        {
            public Task<InstantQuotationPhysicalAnalysisInputResult> ReadAsync(
                string sessionId, string? owner, Guid partId, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                return Task.FromResult(new InstantQuotationPhysicalAnalysisInputResult(
                    PhysicalBytes, InstantQuotationPhysicalAnalysisInputFailure.None,
                    partId == Guid.Parse("77777777-7777-7777-7777-777777777777")
                        ? Guid.Parse("88888888-8888-8888-8888-888888888888")
                        : Guid.Parse("66666666-6666-6666-6666-666666666666"), "controlled.stl",
                    Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(PhysicalBytes))));
            }
        }
    }
}
