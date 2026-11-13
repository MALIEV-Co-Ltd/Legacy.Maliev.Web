using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Application.Pricing;
using Legacy.Maliev.Web.Components.Pages.InstantQuotation;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Actual GLB bytes and production physics/tickets, without a browser or external FileService.</summary>
public sealed class InstantQuotationAutomaticDefaultAcceptanceTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task FreshDefaultUploads_EnterReviewOnlyAfterCurrentProtectedPhysicalPricesExist(int bodies)
    {
        var fixture = await Fixture.CreateAsync(bodies: bodies);
        await MaterialPricingTestCleanup.RunAsync(async () =>
        {
            await fixture.UploadAsync("first.glb");
            Assert.Equal(InstantQuotationWorkflowState.Configured, fixture.Workflow.State);
            await fixture.AssertCurrentDefaultAuthorityAsync(1);
            await fixture.UploadAsync("second.glb", "third.glb");
            Assert.Equal(InstantQuotationWorkflowState.Configured, fixture.Workflow.State);
            await fixture.AssertCurrentDefaultAuthorityAsync(3);
            fixture.Workflow.EnterReview();
            Assert.Equal(InstantQuotationWorkflowState.Review, fixture.Workflow.State);
            fixture.Workflow.EnterCustomerDetails();
            Assert.Equal(InstantQuotationWorkflowState.CustomerDetails, fixture.Workflow.State);
        }, static () => ValueTask.CompletedTask, fixture.DisposeAsync, _ => { });
    }

    [Fact]
    public async Task SavedBlackConfiguration_RestoresWithCurrentProtectedPhysicalAuthorityWithoutBecomingAny()
    {
        var fixture = await Fixture.CreateAsync();
        await MaterialPricingTestCleanup.RunAsync(async () =>
        {
            await fixture.UploadAsync("saved-black.glb");
            await fixture.AssertProtectedBlackRestoreAsync();
        }, static () => ValueTask.CompletedTask, fixture.DisposeAsync, _ => { });
    }

    [Theory]
    [InlineData("release", 1)]
    [InlineData("release", 2)]
    [InlineData("cancel", 1)]
    [InlineData("cancel", 2)]
    [InlineData("dispose", 1)]
    [InlineData("dispose", 2)]
    [InlineData("stale-store", 1)]
    [InlineData("stale-store", 2)]
    public async Task PendingDefaultQuote_CannotAcceptBeforeItsCurrentProtectedCommit(string transition, int bodies)
    {
        var fixture = await Fixture.CreateAsync(bodies: bodies);
        await MaterialPricingTestCleanup.RunAsync(async () =>
        {
            fixture.Control.HoldQuote = true;
            using var caller = new CancellationTokenSource();
            var uploading = fixture.UploadAsync(caller.Token, "held.glb");
            Exception? primary = null;
            try
            {
                await fixture.Control.Entered.Task.WaitAsync(TimeSpan.FromSeconds(33));
                fixture.AssertEligibleSourceConfiguration();
                Assert.False(fixture.Workflow.HasCompleteAuthoritativeQuote);
                Assert.Throws<InvalidOperationException>(() => fixture.Workflow.EnterReview());
                Assert.Null((await fixture.Store.GetAsync(fixture.SessionId, Owner, default))!.QuoteAuthorization);
                if (transition == "stale-store")
                {
                    var state = (await fixture.Store.GetAsync(fixture.SessionId, Owner, default))!;
                    Assert.True(await fixture.Store.PutAsync(state with
                    {
                        RequestState = new(state.Parts.Select(part => part with
                        { Configuration = part.Configuration with { Quantity = 2 } }).ToArray()),
                    }, Owner, default));
                    fixture.Control.Release.TrySetResult();
                    await uploading.WaitAsync(TimeSpan.FromSeconds(5));
                    Assert.False(fixture.Workflow.HasCompleteAuthoritativeQuote);
                    Assert.NotEqual(InstantQuotationWorkflowState.Configured, fixture.Workflow.State);
                }
                else if (transition == "release")
                {
                    fixture.Control.Release.TrySetResult();
                    await uploading.WaitAsync(TimeSpan.FromSeconds(5));
                    Assert.Equal(InstantQuotationWorkflowState.Configured, fixture.Workflow.State);
                    await fixture.AssertCurrentDefaultAuthorityAsync(1);
                }
                else
                {
                    var admitted = (await fixture.Store.GetAsync(fixture.SessionId, Owner, default))!;
                    var admittedPart = Assert.Single(admitted.Parts);
                    var revision = fixture.Workflow.AuthoritativeQuoteRevision;
                    Assert.Equal(0, revision);
                    if (transition == "cancel") caller.Cancel();
                    else await fixture.Workflow.DisposeAsync();
                    await uploading.WaitAsync(TimeSpan.FromSeconds(5));
                    // Admission completed before pricing was held; retiring pricing retains the uploaded file.
                    Assert.Equal(InstantQuotationWorkflowUploadStatus.Uploaded, Assert.Single(fixture.Workflow.Uploads).Status);
                    Assert.False(fixture.Workflow.HasCompleteAuthoritativeQuote);
                    var cancelled = (await fixture.Store.GetAsync(fixture.SessionId, Owner, default))!;
                    Assert.Null(cancelled.QuoteAuthorization);
                    var retained = Assert.Single(cancelled.Parts);
                    Assert.Equal(admittedPart.PartId, retained.PartId);
                    Assert.Equal(admittedPart.UploadReference, retained.UploadReference);
                    Assert.Equal(admittedPart.PhysicalAnalysisUpload!.FileId, retained.PhysicalAnalysisUpload!.FileId);
                    Assert.Equal(admittedPart.Configuration, retained.Configuration);
                    Assert.Contains(fixture.Control.ActualTasks, task => !task.IsCompleted);
                    fixture.Control.Release.TrySetResult();
                    await fixture.DrainAsync(null);
                    Assert.All(fixture.Control.ActualTasks, task => Assert.True(task.IsCompleted));
                    Assert.Equal(revision, fixture.Workflow.AuthoritativeQuoteRevision);
                    Assert.Null(fixture.Workflow.OrderQuote);
                    Assert.False(fixture.Workflow.HasCompleteAuthoritativeQuote);
                    var settled = (await fixture.Store.GetAsync(fixture.SessionId, Owner, default))!;
                    Assert.Null(settled.QuoteAuthorization);
                    Assert.Null(settled.PhysicalReceipts);
                    Assert.Equal(System.Text.Json.JsonSerializer.Serialize(cancelled.RequestState),
                        System.Text.Json.JsonSerializer.Serialize(settled.RequestState));
                }
            }
            catch (Exception exception) { primary = exception; throw; }
            finally
            {
                await MaterialPricingTestCleanup.ReleaseAllAsync(primary,
                    () => { fixture.Control.Release.TrySetResult(); return ValueTask.CompletedTask; },
                    async () => await fixture.DrainAsync(primary));
            }
        }, static () => ValueTask.CompletedTask, fixture.DisposeAsync, _ => { });
    }

    [Theory]
    [InlineData("no-authority", 1)]
    [InlineData("no-authority", 2)]
    [InlineData("no-ticket-service", 1)]
    [InlineData("no-ticket-service", 2)]
    [InlineData("null-quote", 1)]
    [InlineData("null-quote", 2)]
    [InlineData("missing-receipt", 1)]
    [InlineData("missing-receipt", 2)]
    [InlineData("wrong-quote-color", 1)]
    [InlineData("wrong-quote-color", 2)]
    [InlineData("invalid-ticket", 1)]
    [InlineData("invalid-ticket", 2)]
    [InlineData("expired-ticket", 1)]
    [InlineData("expired-ticket", 2)]
    [InlineData("missing-authorization", 1)]
    [InlineData("missing-authorization", 2)]
    [InlineData("session", 1)]
    [InlineData("session", 2)]
    [InlineData("owner", 1)]
    [InlineData("owner", 2)]
    [InlineData("file", 1)]
    [InlineData("file", 2)]
    [InlineData("sha", 1)]
    [InlineData("sha", 2)]
    [InlineData("quantity", 1)]
    [InlineData("quantity", 2)]
    [InlineData("build", 1)]
    [InlineData("build", 2)]
    [InlineData("profile", 1)]
    [InlineData("profile", 2)]
    [InlineData("missing-protected-receipt", 1)]
    [InlineData("missing-protected-receipt", 2)]
    [InlineData("refused-store", 1)]
    [InlineData("refused-store", 2)]
    [InlineData("failed-upload", 1)]
    [InlineData("failed-upload", 2)]
    [InlineData("unsupported-extension", 1)]
    [InlineData("unsupported-extension", 2)]
    public async Task FreshDefaultAcceptance_RejectsAbsentRefusedOrMismatchedAuthority(string mode, int bodies)
    {
        var fixture = await Fixture.CreateAsync(mode, bodies);
        await MaterialPricingTestCleanup.RunAsync(async () =>
        {
            var actual = await Record.ExceptionAsync(() => fixture.UploadAsync(
                mode == "unsupported-extension" ? "rejected.exe" : "rejected.glb"));
            Assert.Null(actual);
            if (mode == "wrong-quote-color") await fixture.AssertOnlyQuoteColorDiffersAsync();
            Assert.NotEqual(InstantQuotationWorkflowState.Configured, fixture.Workflow.State);
            Assert.False(fixture.Workflow.HasCompleteAuthoritativeQuote);
            Assert.Throws<InvalidOperationException>(() => fixture.Workflow.EnterReview());
            if (mode is "failed-upload" or "unsupported-extension")
                Assert.Empty(fixture.Workflow.Parts);
            else
            {
                Assert.Single(fixture.Workflow.Parts);
                fixture.AssertEligibleSourceConfiguration();
            }
        }, static () => ValueTask.CompletedTask, fixture.DisposeAsync, _ => { });
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task AuthorizationWriteFault_PreservesTerminalFailurePolicyAndCannotAcceptDefaults(int bodies)
    {
        var fixture = await Fixture.CreateAsync("faulted-store", bodies);
        await MaterialPricingTestCleanup.RunAsync(async () =>
        {
            var actual = await Record.ExceptionAsync(() => fixture.UploadAsync("fault.glb"));
            Assert.Null(actual);
            fixture.AssertEligibleSourceConfiguration();
            Assert.Equal(InstantQuotationWorkflowUploadStatus.Error, Assert.Single(fixture.Workflow.Uploads).Status);
            Assert.Equal(InstantQuotationProblemCategory.Unexpected, Assert.Single(fixture.Workflow.Uploads).ProblemCategory);
            Assert.False(fixture.Workflow.HasCompleteAuthoritativeQuote);
            Assert.NotEqual(InstantQuotationWorkflowState.Configured, fixture.Workflow.State);
        }, static () => ValueTask.CompletedTask, fixture.DisposeAsync, _ => { });
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task AcknowledgedRemoval_CannotAcceptFreshDefaultsWhileAnotherUploadPublishes(int bodies)
    {
        var fixture = await Fixture.CreateAsync(bodies: bodies);
        await MaterialPricingTestCleanup.RunAsync(async () =>
        {
            fixture.Control.ReturnNullQuote = true;
            await fixture.UploadAsync("unconfigured-first.glb");
            fixture.AssertEligibleSourceConfiguration();
            Assert.Equal(InstantQuotationWorkflowState.Uploaded, fixture.Workflow.State);
            var first = Assert.Single(fixture.Workflow.Parts);
            var entries = (System.Collections.IEnumerable)typeof(InstantQuotationWorkflowCoordinator)
                .GetField("entries", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .GetValue(fixture.Workflow)!;
            var retired = entries.Cast<object>().Single(entry =>
                entry.GetType().GetProperty("Part")!.GetValue(entry) is InstantQuotationPart part && part.PartId == first.PartId);
            var accepted = retired.GetType().GetProperty("HasConfigured")!;
            Assert.False(Assert.IsType<bool>(accepted.GetValue(retired)));
            fixture.Control.ReturnNullQuote = false;
            fixture.Control.HoldQuote = true;
            var uploading = fixture.UploadAsync("second.glb");
            await fixture.Control.Entered.Task.WaitAsync(TimeSpan.FromSeconds(33));
            fixture.Control.HoldRemovalStore = true;
            var acknowledged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var removing = fixture.RemoveAsync(first.PartId, () => acknowledged.TrySetResult());
            Exception? primary = null;
            try
            {
                await acknowledged.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.False(removing.IsCompleted);
                fixture.Control.Release.TrySetResult();
                await fixture.Control.RemovalStoreEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                await uploading.WaitAsync(TimeSpan.FromSeconds(5));
                // Removal owns the next store write, so this is still the prior quote publication's state.
                Assert.NotEqual(InstantQuotationWorkflowState.Configured, fixture.Workflow.State);
                Assert.False(Assert.IsType<bool>(accepted.GetValue(retired)));
                Assert.Throws<InvalidOperationException>(() => fixture.Workflow.EnterReview());
                fixture.Control.RemovalStoreRelease.TrySetResult();
                await removing.WaitAsync(TimeSpan.FromSeconds(33));
                Assert.DoesNotContain(fixture.Workflow.Parts, part => part.PartId == first.PartId);
                Assert.Equal(InstantQuotationWorkflowState.Configured, fixture.Workflow.State);
                await fixture.AssertCurrentDefaultAuthorityAsync(1);
            }
            catch (Exception exception) { primary = exception; throw; }
            finally
            {
                await MaterialPricingTestCleanup.ReleaseAllAsync(primary,
                    () => { fixture.Control.Release.TrySetResult(); fixture.Control.RemovalStoreRelease.TrySetResult(); return ValueTask.CompletedTask; },
                    async () => await fixture.DrainAsync(primary));
            }
        }, static () => ValueTask.CompletedTask, fixture.DisposeAsync, _ => { });
    }

    private const string Owner = "automatic-default-controlled-owner";
    private static readonly InstantQuotationGltfUnitPolicy UnitPolicy = new(1, InstantQuotationGltfLengthUnit.Millimetres);

    private sealed class Control : IDisposable
    {
        private readonly object gate = new();
        private readonly List<Task<InstantQuotationOrderQuote?>> tasks = [];
        private readonly CancellationTokenSource lifetime = new(TimeSpan.FromMinutes(3));
        private bool stopped;
        internal string Mode { get; }
        internal bool HoldQuote { get; set; }
        internal bool ReturnNullQuote { get; set; }
        internal bool HoldRemovalStore { get; set; }
        internal InstantQuotationOrderQuote? OriginalQuoteBeforeColorMismatch { get; private set; }
        internal CancellationToken LifetimeToken => lifetime.Token;
        internal TaskCompletionSource RemovalStoreEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource RemovalStoreRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal InvalidOperationException Primary { get; } = new("Controlled authorization write failure.");
        internal Control(string mode) { Mode = mode; }
        internal Task[] ActualTasks { get { lock (gate) return tasks.Cast<Task>().ToArray(); } }

        internal Task<InstantQuotationOrderQuote?> Start(Func<CancellationToken, Task<InstantQuotationOrderQuote?>> run,
            CancellationToken caller)
        {
            lock (gate)
            {
                if (stopped)
                    return Task.FromCanceled<InstantQuotationOrderQuote?>(new CancellationToken(true));
                var task = RunAsync();
                tasks.Add(task);
                _ = task.ContinueWith(static completed => { _ = completed.Exception; }, CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
                return task;
            }

            async Task<InstantQuotationOrderQuote?> RunAsync()
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(caller, lifetime.Token);
                var quote = await run(linked.Token);
                if (HoldQuote)
                {
                    Entered.TrySetResult();
                    // Ignore only the retired consumer while retaining an owned finite test lifetime.
                    await Release.Task.WaitAsync(lifetime.Token);
                }
                if (Mode == "null-quote" || ReturnNullQuote) return null;
                if (Mode == "wrong-quote-color" && quote is not null)
                {
                    OriginalQuoteBeforeColorMismatch = quote;
                    return quote with { Parts = quote.Parts.Select(part => part with { Color = "White" }).ToArray() };
                }
                return Mode == "missing-receipt" && quote is not null
                    ? quote with { Parts = quote.Parts.Select(part => part with { PhysicalReceipt = null }).ToArray() }
                    : quote;
            }
        }

        internal void Stop() { lock (gate) stopped = true; Release.TrySetResult(); RemovalStoreRelease.TrySetResult(); }
        internal async Task DrainAsync(Exception? primary)
        {
            var actual = ActualTasks;
            await MaterialPricingTestCleanup.ReleaseAllAsync(primary, actual.Select(task =>
                (Func<ValueTask>)(async () => await MaterialPricingTestCleanup.DrainQuoteAsync(
                    task, static () => { }, () => Release.TrySetResult(), lifetime.Token, null,
                    receipt => Assert.True(receipt.ActualTaskSettled), TimeSpan.FromSeconds(5),
                    expectedTerminalFailure: true, afterWaitFailure: lifetime.Cancel,
                    taskKind: "automatic-default-actual-quote"))).ToArray());
        }
        public void Dispose()
        {
            if (ActualTasks.All(task => task.IsCompleted)) lifetime.Dispose();
        }
    }

    private sealed class Pricing(IInstantQuotationAuthoritativePricingService inner, Control control)
        : IInstantQuotationAuthoritativePricingService
    {
        public Task<InstantQuotationOrderQuote?> QuoteAsync(InstantQuotationSessionState session,
            string? owner, bool comparisons, CancellationToken token) =>
            control.Start(current => inner.QuoteAsync(session, owner, comparisons, current), token);
        public Task<InstantQuotationOrderQuote?> QuoteAsync(InstantQuotationSessionState session,
            string? owner, bool comparisons,
            Func<InstantQuotationMaterialPricingProgress, CancellationToken, ValueTask> observer,
            CancellationToken token) =>
            control.Start(current => inner.QuoteAsync(session, owner, comparisons, observer, current), token);
    }

    private sealed class ExpiredTickets(IInstantQuotationQuoteTicketService inner) : IInstantQuotationQuoteTicketService
    {
        public InstantQuotationQuoteAuthorization Issue(InstantQuotationSessionState session, InstantQuotationOrderQuote quote,
            DateTimeOffset now) => inner.Issue(session, quote, now.AddMinutes(-31));
        public bool Validate(InstantQuotationSessionState session, InstantQuotationOrderQuote quote,
            InstantQuotationQuoteAuthorization authorization, DateTimeOffset now) => inner.Validate(session, quote, authorization, now);
    }

    private sealed class EvidenceStore(IInstantQuotationSessionStore inner, Control control)
        : IInstantQuotationSessionStore
    {
        public Task<InstantQuotationSessionState> CreateAsync(string? owner,
            InstantQuotationOrderState state, CancellationToken token) => inner.CreateAsync(owner, state, token);
        public async Task<InstantQuotationSessionState?> GetAsync(string id, string? owner, CancellationToken token)
        {
            var state = await inner.GetAsync(id, owner, token);
            if (state?.QuoteAuthorization is null) return state;
            var part = state.Parts[0];
            return control.Mode switch
            {
                "missing-authorization" => state with { QuoteAuthorization = null },
                "invalid-ticket" => state with { QuoteAuthorization = state.QuoteAuthorization with { OrderTicket = "invalid" } },
                "session" => state with { SessionId = "different-controlled-session" },
                "owner" => state with { OwnerIdentity = "different-controlled-owner" },
                "file" => state with
                {
                    RequestState = new([part with
                { PhysicalAnalysisUpload = part.PhysicalAnalysisUpload! with { FileId = Guid.NewGuid() } }])
                },
                "sha" => state with
                {
                    RequestState = new([part with
                { PhysicalAnalysisUpload = part.PhysicalAnalysisUpload! with { Sha256 = new string('f', 64) } }])
                },
                "quantity" => state with
                {
                    RequestState = new([part with
                { Configuration = part.Configuration with { Quantity = 2 } }])
                },
                "build" => state with
                {
                    RequestState = new([part with
                { Configuration = part.Configuration with { BuildPreference = BuildPreference.Strength } }])
                },
                "profile" => state with
                {
                    PhysicalReceipts = state.PhysicalReceipts!.Select(receipt =>
                    receipt with { ProfileVersion = "different-profile" }).ToArray()
                },
                "missing-protected-receipt" => state with { PhysicalReceipts = [] },
                _ => state,
            };
        }
        public async Task<bool> PutAsync(InstantQuotationSessionState state, string? owner, CancellationToken token)
        {
            if (control.HoldRemovalStore && state.QuoteAuthorization is null && state.Parts.Count == 1)
            {
                control.RemovalStoreEntered.TrySetResult();
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, control.LifetimeToken);
                await control.RemovalStoreRelease.Task.WaitAsync(linked.Token);
            }
            if (state.QuoteAuthorization is not null)
            {
                if (control.Mode == "refused-store") return false;
                if (control.Mode == "faulted-store") throw control.Primary;
            }
            return await inner.PutAsync(state, owner, token);
        }
        public Task<bool> RemoveAsync(string id, string? owner, CancellationToken token) =>
            inner.RemoveAsync(id, owner, token);
    }

    private sealed class Uploads(Control control) : IInstantQuotationUploadClient, IInstantQuotationPhysicalAnalysisInputReader
    {
        private sealed record Stored(string Session, string? Owner, Guid File, string Name, byte[] Bytes, string Sha,
            InstantQuotationGltfUnitPolicy GltfUnitPolicy);
        private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, Stored> files = new();
        internal EvidenceStore? Store { get; set; }
        public async Task<InstantQuotationUploadResult> UploadAsync(string session, string? owner, Stream content,
            string fileName, string contentType, long length, InstantQuotationGeometryClaim claim,
            string operation, CancellationToken token)
        {
            Assert.Equal(UnitPolicy, claim.GltfUnitPolicy);
            if (control.Mode == "failed-upload")
                return InstantQuotationUploadResult.Failed(operation, InstantQuotationServiceStatus.Available,
                    InstantQuotationAuthorizationStatus.Authorized, InstantQuotationProblemCategory.Validation);
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, token);
            var bytes = buffer.ToArray();
            var sha = Convert.ToHexStringLower(SHA256.HashData(bytes));
            Assert.Equal(length, bytes.LongLength);
            Assert.Equal(claim.Sha256, sha);
            var id = Guid.NewGuid();
            files[id] = new(session, owner, id, fileName, bytes, sha, claim.GltfUnitPolicy);
            return InstantQuotationUploadResult.Succeeded(operation, new(id.ToString("D")), sha,
                new(id, fileName, contentType, bytes.Length, sha, "clean") { GltfUnitPolicy = claim.GltfUnitPolicy });
        }
        public async Task<InstantQuotationPhysicalAnalysisInputResult> ReadAsync(string session, string? owner,
            Guid partId, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var state = await Store!.GetAsync(session, owner, token);
            var id = state?.Parts.SingleOrDefault(part => part.PartId == partId)?.PhysicalAnalysisUpload?.FileId;
            return id is { } file && files.TryGetValue(file, out var value)
                && value.Session == session && value.Owner == owner
                ? new(value.Bytes, InstantQuotationPhysicalAnalysisInputFailure.None, value.File, value.Name, value.Sha)
                { GltfUnitPolicy = value.GltfUnitPolicy }
                : InstantQuotationPhysicalAnalysisInputResult.Unavailable(InstantQuotationPhysicalAnalysisInputFailure.UploadUnavailable);
        }
        public Task<InstantQuotationRemoveResult> RemoveAsync(string session, string? owner,
            InstantQuotationUploadReference reference, string operation, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (!Guid.TryParse(reference.Value, out var id) || !files.TryGetValue(id, out var stored)
                || stored.Session != session || stored.Owner != owner)
                return Task.FromResult(InstantQuotationRemoveResult.Unavailable(operation));
            files.TryRemove(id, out _);
            return Task.FromResult(new InstantQuotationRemoveResult(operation, InstantQuotationServiceStatus.Available,
                InstantQuotationAuthorizationStatus.Authorized, InstantQuotationOperationStatus.Succeeded,
                InstantQuotationProblemCategory.None));
        }
        public Task<InstantQuotationFinalizationResult> FinalizeAsync(string session, string? owner, int request,
            IReadOnlyList<InstantQuotationUploadReference> references, string operation, CancellationToken token) =>
            Task.FromResult(InstantQuotationFinalizationResult.Unavailable(operation));
    }

    private sealed class Host(Control control, Uploads uploads)
        : TestingWebApplicationFactory(BrowserHostIdentityVerifier.SourceProjectDirectory())
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                var store = services.Single(item => item.ServiceType == typeof(IInstantQuotationSessionStore));
                services.RemoveAll<IInstantQuotationSessionStore>();
                services.Add(new ServiceDescriptor(typeof(IInstantQuotationSessionStore), provider =>
                {
                    var wrapped = new EvidenceStore((IInstantQuotationSessionStore)(store.ImplementationInstance
                        ?? store.ImplementationFactory?.Invoke(provider)
                        ?? ActivatorUtilities.CreateInstance(provider, store.ImplementationType!)), control);
                    uploads.Store = wrapped; return wrapped;
                }, store.Lifetime));
                services.RemoveAll<IInstantQuotationUploadClient>();
                services.AddSingleton<IInstantQuotationUploadClient>(uploads);
                services.RemoveAll<IInstantQuotationPhysicalAnalysisInputReader>();
                services.AddSingleton<IInstantQuotationPhysicalAnalysisInputReader>(uploads);
                var pricing = services.Single(item => item.ServiceType == typeof(IInstantQuotationAuthoritativePricingService));
                services.RemoveAll<IInstantQuotationAuthoritativePricingService>();
                services.Add(new ServiceDescriptor(typeof(IInstantQuotationAuthoritativePricingService), provider =>
                    new Pricing((IInstantQuotationAuthoritativePricingService)(pricing.ImplementationInstance
                        ?? pricing.ImplementationFactory?.Invoke(provider)
                        ?? ActivatorUtilities.CreateInstance(provider, pricing.ImplementationType!)), control), pricing.Lifetime));
            });
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly Control control;
        private readonly Host factory;
        private AsyncServiceScope scope;
        private bool scopeCreated;
        private readonly List<Task> uploads = [];
        private IInstantQuotationQuoteTicketService? tickets;
        private InstantQuotationWorkflowCoordinator? workflow;
        private EvidenceStore? store;
        internal Control Control => control;
        internal InstantQuotationWorkflowCoordinator Workflow => workflow ?? throw new InvalidOperationException("Fixture is not initialized.");
        internal EvidenceStore Store => store ?? throw new InvalidOperationException("Fixture is not initialized.");
        internal string SessionId => Workflow.ProtectedSessionIdentity;
        private readonly byte[] bytes;
        private readonly string sha;
        private readonly int bodies;
        private Fixture(string mode, int bodies)
        {
            this.bodies = bodies;
            bytes = FixtureBytes(bodies);
            sha = Convert.ToHexStringLower(SHA256.HashData(bytes));
            control = new(mode);
            factory = new(control, new Uploads(control));
        }
        internal static async Task<Fixture> CreateAsync(string mode = "current", int bodies = 1)
        {
            var fixture = new Fixture(mode, bodies);
            try
            {
                fixture.scope = fixture.factory.Services.CreateAsyncScope();
                fixture.scopeCreated = true;
                var provider = fixture.scope.ServiceProvider;
                fixture.store = (EvidenceStore)provider.GetRequiredService<IInstantQuotationSessionStore>();
                var tickets = provider.GetRequiredService<IInstantQuotationQuoteTicketService>();
                fixture.tickets = tickets;
                fixture.workflow = new(fixture.Store, provider.GetRequiredService<IInstantQuotationUploadClient>(),
                    provider.GetRequiredService<IInstantQuotationPricingService>(), Owner,
                    quoteTicketService: mode == "no-ticket-service" ? null
                        : mode == "expired-ticket" ? new ExpiredTickets(tickets) : tickets,
                    authoritativePricingService: mode == "no-authority" ? null
                        : provider.GetRequiredService<IInstantQuotationAuthoritativePricingService>());
                await fixture.Workflow.InitializeAsync(default);
                return fixture;
            }
            catch (Exception primary)
            {
                await MaterialPricingTestCleanup.ReleaseAllAsync(primary, fixture.DisposeAsync);
                throw;
            }
        }
        internal Task UploadAsync(params string[] names) => UploadAsync(default, names);
        internal Task UploadAsync(CancellationToken token, params string[] names)
        {
            var claim = new InstantQuotationGeometryClaim(1, sha, bodies == 1 ? 10 : 30, 10, 10, 1000 * bodies, 600 * bodies,
                Enumerable.Repeat(100d * bodies, 64).ToArray(), Enumerable.Repeat(40d * bodies, 64).ToArray(),
                12 * bodies, bodies, true, false, false, 10)
            { GltfUnitPolicy = UnitPolicy };
            var task = Workflow.UploadAsync(names.Select(name => new InstantQuotationWorkflowUploadFile(
                name, "model/gltf-binary", bytes.Length, claim,
                current => { current.ThrowIfCancellationRequested(); return Task.FromResult<Stream>(new MemoryStream(bytes, false)); })).ToArray(), token);
            uploads.Add(task);
            return task;
        }
        internal Task RemoveAsync(Guid partId, Action acknowledged)
        {
            var task = Workflow.RemoveAsync(partId, acknowledged, default);
            uploads.Add(task);
            return task;
        }
        internal void AssertEligibleSourceConfiguration()
        {
            Assert.NotEmpty(Workflow.Parts);
            Assert.All(Workflow.Parts, part =>
            {
                Assert.Equal(new InstantQuotationPartConfiguration("PLA", "Any", 1), part.Configuration);
                Assert.Equal(bodies, part.Geometry.BodyCount);
            });
        }
        internal async Task AssertCurrentDefaultAuthorityAsync(int count)
        {
            if (bodies == 1)
            {
                Assert.Equal(960, bytes.Length);
                Assert.Equal("2d31d5a61f110106cf30c0c17d713602b8ab5c9d90c6c802e980e4c1c0bee8dc", sha);
            }
            var quote = Assert.IsType<InstantQuotationOrderQuote>(Workflow.OrderQuote);
            var state = (await Store.GetAsync(SessionId, Owner, default))!;
            Assert.Equal(count, state.Parts.Count);
            Assert.Equal(count, state.Parts.Select(part => part.PartId).Distinct().Count());
            Assert.True(Workflow.HasCompleteAuthoritativeQuote);
            Assert.NotNull(state.QuoteAuthorization);
            Assert.True(Assert.IsAssignableFrom<IInstantQuotationQuoteTicketService>(tickets)
                .Validate(state, quote, state.QuoteAuthorization, DateTimeOffset.UtcNow));
            Assert.All(state.Parts, part =>
            {
                Assert.Equal(new InstantQuotationPartConfiguration("PLA", "Any", 1), part.Configuration);
                Assert.Equal(bodies, part.Geometry.BodyCount);
                var line = Assert.Single(quote.Parts, item => item.PartId == part.PartId);
                var receipt = Assert.IsType<InstantQuotationPhysicalAnalysisReceipt>(line.PhysicalReceipt);
                Assert.Equal("Any", line.Color);
                Assert.Equal("PLA", line.MaterialKey);
                Assert.Equal(1, line.Quantity);
                var binding = Assert.IsType<InstantQuotationPhysicalAnalysisBinding>(
                    Workflow.CurrentPhysicalAnalysisBinding(part.PartId, receipt.ProfileVersion));
                Assert.True(receipt.Matches(binding, "PLA"));
                Assert.True(FdmRuntimeProfileCatalog.LoadEmbedded().TryResolveTrustedProfile(
                    "PLA", BuildPreference.Standard, out var profile));
                Assert.Equal(profile!.ResolvedProfileSha256, receipt.ProfileSha256);
                Assert.Equal(FdmRuntimeProfileCatalog.LoadEmbedded().ProfileVersion, receipt.ProfileVersion);
                Assert.Equal(Legacy.Maliev.Web.Application.Pricing.Simulation.FdmSimulationEngine.AnalysisVersion, receipt.AnalysisVersion);
                Assert.Equal(part.PhysicalAnalysisUpload!.FileId, receipt.FileId);
                Assert.Equal(UnitPolicy, part.PhysicalAnalysisUpload.GltfUnitPolicy);
                Assert.Equal(UnitPolicy, receipt.GltfUnitPolicy);
                Assert.Equal(sha, receipt.UploadSha256);
                Assert.Equal(bodies == 1 ? 10 : 30, part.Geometry.DimensionXmm);
                Assert.Equal(10, part.Geometry.DimensionYmm);
                Assert.Equal(10, part.Geometry.DimensionZmm);
                Assert.Equal(bodies == 1 ? 1 : 3, receipt.BoundingCm3PerUnit, 3);
                Assert.True(receipt.DepositedMm3 > 0);
                Assert.True(receipt.MotionSeconds > 0);
                Assert.Contains(receipt, state.PhysicalReceipts!);
            });
        }
        internal async Task AssertOnlyQuoteColorDiffersAsync()
        {
            AssertEligibleSourceConfiguration();
            var state = Assert.IsType<InstantQuotationSessionState>(await Store.GetAsync(SessionId, Owner, default));
            var part = Assert.Single(state.Parts);
            Assert.Equal(new InstantQuotationPartConfiguration("PLA", "Any", 1), part.Configuration);
            var original = Assert.IsType<InstantQuotationOrderQuote>(Control.OriginalQuoteBeforeColorMismatch);
            var quote = Assert.IsType<InstantQuotationOrderQuote>(Workflow.OrderQuote);
            var originalLine = Assert.Single(original.Parts);
            var line = Assert.Single(quote.Parts);
            Assert.Equal("Any", originalLine.Color);
            Assert.Equal("White", line.Color);
            Assert.NotEqual(part.Configuration.Color, line.Color);
            Assert.Equal(originalLine, line with { Color = originalLine.Color });
            Assert.Equal(original, quote with { Parts = original.Parts });
            var authorization = Assert.IsType<InstantQuotationQuoteAuthorization>(state.QuoteAuthorization);
            var service = Assert.IsAssignableFrom<IInstantQuotationQuoteTicketService>(tickets);
            Assert.True(service.Validate(state, original, authorization, DateTimeOffset.UtcNow));
            Assert.True(service.Validate(state, quote, authorization, DateTimeOffset.UtcNow));
            var receipt = Assert.IsType<InstantQuotationPhysicalAnalysisReceipt>(line.PhysicalReceipt);
            Assert.Equal(originalLine.PhysicalReceipt, receipt);
            var binding = Assert.IsType<InstantQuotationPhysicalAnalysisBinding>(
                Workflow.CurrentPhysicalAnalysisBinding(part.PartId, receipt.ProfileVersion));
            Assert.True(receipt.Matches(binding, "PLA"));
            Assert.True(FdmRuntimeProfileCatalog.LoadEmbedded().TryResolveTrustedProfile(
                "PLA", BuildPreference.Standard, out var profile));
            Assert.Equal(profile!.ResolvedProfileSha256, receipt.ProfileSha256);
            Assert.Equal(FdmRuntimeProfileCatalog.LoadEmbedded().ProfileVersion, receipt.ProfileVersion);
            Assert.Equal(Legacy.Maliev.Web.Application.Pricing.Simulation.FdmSimulationEngine.AnalysisVersion, receipt.AnalysisVersion);
            Assert.Equal(part.PhysicalAnalysisUpload!.FileId, receipt.FileId);
            Assert.Equal(sha, receipt.UploadSha256);
            Assert.Equal(UnitPolicy, receipt.GltfUnitPolicy);
            Assert.Equal(bodies == 1 ? 1 : 3, receipt.BoundingCm3PerUnit, 3);
            Assert.True(double.IsFinite(line.UnitPrice) && line.UnitPrice > 0);
            Assert.True(double.IsFinite(receipt.DepositedMm3) && receipt.DepositedMm3 > 0);
            Assert.True(double.IsFinite(receipt.SupportMm3) && receipt.SupportMm3 >= 0);
            Assert.True(double.IsFinite(receipt.MotionSeconds) && receipt.MotionSeconds > 0);
            Assert.Contains(receipt, state.PhysicalReceipts!);
        }

        internal async Task AssertProtectedBlackRestoreAsync()
        {
            Assert.Equal(1, bodies);
            var uploaded = Assert.Single(Workflow.Parts);
            await Workflow.UpdateConfigurationAsync(uploaded.PartId, "PLA", "Black", 1, default);
            var before = Assert.IsType<InstantQuotationSessionState>(await Store.GetAsync(SessionId, Owner, default));
            var beforeQuote = Assert.IsType<InstantQuotationOrderQuote>(Workflow.OrderQuote);
            AssertBlackAuthority(Workflow, before, beforeQuote);
            var beforePart = Assert.Single(before.Parts);
            var beforePayload = System.Text.Json.JsonSerializer.Serialize(before.RequestState);
            var beforeReceipt = Assert.IsType<InstantQuotationPhysicalAnalysisReceipt>(Assert.Single(beforeQuote.Parts).PhysicalReceipt);

            var provider = scope.ServiceProvider;
            await using var restored = new InstantQuotationWorkflowCoordinator(Store,
                provider.GetRequiredService<IInstantQuotationUploadClient>(),
                provider.GetRequiredService<IInstantQuotationPricingService>(), Owner,
                quoteTicketService: tickets,
                authoritativePricingService: provider.GetRequiredService<IInstantQuotationAuthoritativePricingService>());
            await restored.InitializeAsync(SessionId, default);

            var after = Assert.IsType<InstantQuotationSessionState>(await Store.GetAsync(SessionId, Owner, default));
            var afterQuote = Assert.IsType<InstantQuotationOrderQuote>(restored.OrderQuote);
            AssertBlackAuthority(restored, after, afterQuote);
            Assert.Equal(before.SessionId, after.SessionId);
            Assert.Equal(before.OwnerIdentity, after.OwnerIdentity);
            Assert.Equal(SessionId, restored.ProtectedSessionIdentity);
            Assert.Equal(beforePayload, System.Text.Json.JsonSerializer.Serialize(after.RequestState));
            var afterPart = Assert.Single(after.Parts);
            Assert.Equal(beforePart.PartId, afterPart.PartId);
            Assert.Equal(beforePart.UploadReference, afterPart.UploadReference);
            Assert.Equal(beforePart.PhysicalAnalysisUpload, afterPart.PhysicalAnalysisUpload);
            Assert.Equal(beforeReceipt, Assert.Single(afterQuote.Parts).PhysicalReceipt);
            restored.EnterReview();
            Assert.Equal(InstantQuotationWorkflowState.Review, restored.State);
            restored.EnterCustomerDetails();
            Assert.Equal(InstantQuotationWorkflowState.CustomerDetails, restored.State);

            void AssertBlackAuthority(InstantQuotationWorkflowCoordinator coordinator,
                InstantQuotationSessionState state, InstantQuotationOrderQuote quote)
            {
                Assert.Equal(InstantQuotationWorkflowState.Configured, coordinator.State);
                Assert.True(coordinator.HasCompleteAuthoritativeQuote);
                var part = Assert.Single(state.Parts);
                Assert.Equal(new InstantQuotationPartConfiguration("PLA", "Black", 1), part.Configuration);
                Assert.Equal(part.Configuration, Assert.Single(coordinator.Parts).Configuration);
                Assert.Equal(1, part.Geometry.BodyCount);
                var authorization = Assert.IsType<InstantQuotationQuoteAuthorization>(state.QuoteAuthorization);
                Assert.True(Assert.IsAssignableFrom<IInstantQuotationQuoteTicketService>(tickets)
                    .Validate(state, quote, authorization, DateTimeOffset.UtcNow));
                var line = Assert.Single(quote.Parts);
                Assert.Equal(part.PartId, line.PartId);
                Assert.Equal("Black", line.Color);
                Assert.Equal("PLA", line.MaterialKey);
                Assert.Equal(1, line.Quantity);
                Assert.True(double.IsFinite(line.UnitPrice) && line.UnitPrice > 0);
                var receipt = Assert.IsType<InstantQuotationPhysicalAnalysisReceipt>(line.PhysicalReceipt);
                var binding = Assert.IsType<InstantQuotationPhysicalAnalysisBinding>(
                    coordinator.CurrentPhysicalAnalysisBinding(part.PartId, receipt.ProfileVersion));
                Assert.True(receipt.Matches(binding, "PLA"));
                Assert.True(FdmRuntimeProfileCatalog.LoadEmbedded().TryResolveTrustedProfile(
                    "PLA", BuildPreference.Standard, out var profile));
                Assert.Equal(profile!.ResolvedProfileSha256, receipt.ProfileSha256);
                Assert.Equal(FdmRuntimeProfileCatalog.LoadEmbedded().ProfileVersion, receipt.ProfileVersion);
                Assert.Equal(Legacy.Maliev.Web.Application.Pricing.Simulation.FdmSimulationEngine.AnalysisVersion, receipt.AnalysisVersion);
                Assert.Equal(part.PhysicalAnalysisUpload!.FileId, receipt.FileId);
                Assert.Equal(sha, receipt.UploadSha256);
                Assert.Equal(UnitPolicy, receipt.GltfUnitPolicy);
                Assert.Equal(1, receipt.BoundingCm3PerUnit, 3);
                Assert.True(receipt.DepositedMm3 > 0);
                Assert.True(receipt.MotionSeconds > 0);
                Assert.Contains(receipt, state.PhysicalReceipts!);
            }
        }

        private static byte[] FixtureBytes(int bodies)
        {
            var original = Convert.FromBase64String(AdmittedGltfMeshReaderTests.OriginalGlb);
            if (bodies == 1) return original;
            Assert.Equal(2, bodies);
            var jsonLength = BitConverter.ToInt32(original, 12);
            var document = JsonNode.Parse(Encoding.UTF8.GetString(original, 20, jsonLength))!;
            // A translated second mesh instance gives two disconnected closed cubes in the actual upload bytes.
            document["nodes"]!.AsArray().Add(new JsonObject
            {
                ["mesh"] = 0,
                ["translation"] = new JsonArray(20, 0, 0),
            });
            document["nodes"]![0]!["children"]!.AsArray().Add(2);
            var json = Encoding.UTF8.GetBytes(document.ToJsonString());
            var paddedLength = (json.Length + 3) & ~3;
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(0x46546c67);
                writer.Write(2);
                writer.Write(20 + paddedLength + original.Length - 20 - jsonLength);
                writer.Write(paddedLength);
                writer.Write(0x4e4f534a);
                writer.Write(json);
                for (var index = json.Length; index < paddedLength; index++) writer.Write((byte)' ');
                writer.Write(original.AsSpan(20 + jsonLength));
            }
            var result = stream.ToArray();
            var mesh = Legacy.Maliev.Web.Application.Pricing.Simulation.AdmittedGltfMeshReader.Read(result);
            Assert.Equal(24, mesh.Triangles.Count);
            Assert.Equal(1, mesh.UnitsToMillimetres);
            var vertices = mesh.Triangles.SelectMany(triangle => new[] { triangle.A, triangle.B, triangle.C }).ToArray();
            Assert.Equal(30f, vertices.Max(vertex => vertex.X) - vertices.Min(vertex => vertex.X));
            Assert.Equal(12, mesh.Triangles.Count(triangle => triangle.A.X <= 5 && triangle.B.X <= 5 && triangle.C.X <= 5));
            Assert.Equal(12, mesh.Triangles.Count(triangle => triangle.A.X >= 15 && triangle.B.X >= 15 && triangle.C.X >= 15));
            return result;
        }
        internal async Task DrainAsync(Exception? primary)
        {
            control.Stop();
            await MaterialPricingTestCleanup.ReleaseAllAsync(primary,
                async () => await control.DrainAsync(primary),
                async () =>
                {
                    foreach (var task in uploads)
                        await MaterialPricingTestCleanup.DrainQuoteAsync(task, static () => { },
                            () => control.Release.TrySetResult(), CancellationToken.None, null,
                            receipt => Assert.True(receipt.ActualTaskSettled), TimeSpan.FromSeconds(5),
                            expectedTerminalFailure: true, taskKind: "automatic-default-upload");
                });
        }
        public async ValueTask DisposeAsync()
        {
            control.Stop();
            await MaterialPricingTestCleanup.ReleaseAllAsync(null,
                async () => { if (workflow is not null) await workflow.DisposeAsync(); },
                async () => await DrainAsync(null),
                async () => { if (scopeCreated) await scope.DisposeAsync(); },
                async () => await factory.DisposeAsync(),
                () => { control.Dispose(); return ValueTask.CompletedTask; });
        }
    }
}
