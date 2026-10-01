using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Components.Pages.InstantQuotation;
using Microsoft.Extensions.DependencyInjection;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Coordinator lifetime proof with a registered protected store and real resin kernel; not upload admission proof.</summary>
public sealed class MaterialPricingLifetimeTests
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
        try
        {
            await pending.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (callerAbort) caller.Cancel(); else await fixture.Workflow.DisposeAsync();
            var winner = await Task.WhenAny(update, Task.Delay(TimeSpan.FromSeconds(2)));
            Assert.Same(update, winner);
            var canceled = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => update);
            if (callerAbort) Assert.Equal(caller.Token, canceled.CancellationToken);
            Assert.False(pending.Exited.Task.IsCompleted);
            Assert.Null(fixture.Workflow.OrderQuote);
            Assert.Null((await fixture.Store.GetAsync(fixture.SessionId, Owner, default))!.QuoteAuthorization);
        }
        finally
        {
            pending.Release.TrySetResult(); await IgnoreCancellationAsync(update);
            await pending.Exited.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task QueuedConfiguration_DisposalDoesNotMutateNewerRequestOrStartAnotherPrice()
    {
        await using var fixture = await Fixture.CreateAsync();
        var pending = fixture.Pricing.Arm();
        var first = fixture.Workflow.UpdateConfigurationAsync(fixture.PartId, "M68", "White", 2, default);
        await pending.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var queued = fixture.Workflow.UpdateConfigurationAsync(fixture.PartId, "M68", "White", 3, default);
        try
        {
            await fixture.Workflow.DisposeAsync();
            pending.Release.TrySetResult();
            await IgnoreCancellationAsync(first);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);
            Assert.Equal(2, Assert.Single(fixture.Workflow.Parts).Configuration.Quantity);
            Assert.Equal(2, Assert.Single((await fixture.Store.GetAsync(fixture.SessionId, Owner, default))!.Parts).Configuration.Quantity);
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

    private static async Task IgnoreCancellationAsync(Task task)
    {
        try { await task; } catch (OperationCanceledException) { } catch (ControlledLatePriceException) { }
    }

    private const string Owner = "pricing-lifetime-fixture-owner";
    private sealed class ControlledLatePriceException : Exception { }
    private sealed class Pending(bool ignoreCancellation, bool failLate)
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Exited { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken Token { get; set; }
        public bool IgnoreCancellation { get; } = ignoreCancellation;
        public bool FailLate { get; } = failLate;
    }

    private sealed class ControlledObserverBoundary(IInstantQuotationAuthoritativePricingService inner)
        : IInstantQuotationAuthoritativePricingService
    {
        private readonly Queue<Pending> pending = new();
        private readonly List<Pending> all = [];
        public Pending Arm(bool ignoreCancellation = false, bool failLate = false)
        {
            var next = new Pending(ignoreCancellation, failLate); pending.Enqueue(next); all.Add(next); return next;
        }
        public void ReleaseAll() { foreach (var entry in all) entry.Release.TrySetResult(); }
        public async Task<InstantQuotationOrderQuote?> QuoteAsync(InstantQuotationSessionState session,
            string? owner, bool includeComparisons, CancellationToken token)
        {
            if (!pending.TryDequeue(out var next)) return await inner.QuoteAsync(session, owner, includeComparisons, token);
            try
            {
                next.Token = token; next.Entered.TrySetResult();
                await next.Release.Task.WaitAsync(next.IgnoreCancellation ? default : token);
                if (next.FailLate) throw new ControlledLatePriceException();
                return await inner.QuoteAsync(session, owner, includeComparisons, next.IgnoreCancellation ? default : token);
            }
            finally { next.Exited.TrySetResult(); }
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
        public string SessionId { get; private set; } = "";
        private Fixture()
        {
            scope = factory.Services.CreateAsyncScope();
            Store = scope.ServiceProvider.GetRequiredService<IInstantQuotationSessionStore>();
            Pricing = new(scope.ServiceProvider.GetRequiredService<IInstantQuotationAuthoritativePricingService>());
            Workflow = new(Store,
                scope.ServiceProvider.GetRequiredService<IInstantQuotationUploadClient>(),
                scope.ServiceProvider.GetRequiredService<IInstantQuotationPricingService>(), Owner,
                quoteTicketService: scope.ServiceProvider.GetRequiredService<IInstantQuotationQuoteTicketService>(),
                authoritativePricingService: Pricing);
        }
        public static async Task<Fixture> CreateAsync(bool initialize = true)
        {
            var fixture = new Fixture();
            try
            {
                var reference = new InstantQuotationUploadReference("66666666-6666-6666-6666-666666666666");
                var digest = new string('a', 64);
                var geometry = AuthoritativeInstantQuotationGeometry.FromCompletedLegacyUpload(
                    InstantQuotationUploadResult.Succeeded("controlled-historical-session", reference, digest),
                    new InstantQuotationGeometryClaim(1, digest, 8, 8, 8, 512, 384,
                        Enumerable.Repeat(1d, 64).ToArray(), Enumerable.Repeat(1d, 64).ToArray(),
                        12, 1, true, false, false, 1))!;
                var part = new InstantQuotationPart(fixture.PartId, "controlled.stl", reference, geometry,
                    new InstantQuotationPartConfiguration("M68", "White", 1));
                var state = await fixture.Store.CreateAsync(Owner, new InstantQuotationOrderState([part]), default);
                fixture.SessionId = state.SessionId;
                if (initialize) await fixture.Workflow.InitializeAsync(state.SessionId, default);
                return fixture;
            }
            catch { await fixture.DisposeAsync(); throw; }
        }
        public async ValueTask DisposeAsync()
        {
            Pricing.ReleaseAll();
            await Workflow.DisposeAsync();
            await scope.DisposeAsync();
            await factory.DisposeAsync();
        }
    }
}
