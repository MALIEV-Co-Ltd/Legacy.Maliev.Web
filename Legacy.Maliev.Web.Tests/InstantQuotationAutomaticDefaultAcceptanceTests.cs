using System.Security.Cryptography;
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
    [Fact]
    public async Task FreshDefaultUploads_EnterReviewOnlyAfterCurrentProtectedPhysicalPricesExist()
    {
        var fixture = await Fixture.CreateAsync();
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

    [Theory]
    [InlineData("release")]
    [InlineData("cancel")]
    [InlineData("dispose")]
    [InlineData("stale-store")]
    public async Task PendingDefaultQuote_CannotAcceptBeforeItsCurrentProtectedCommit(string transition)
    {
        var fixture = await Fixture.CreateAsync();
        await MaterialPricingTestCleanup.RunAsync(async () =>
        {
            fixture.Control.HoldQuote = true;
            using var caller = new CancellationTokenSource();
            var uploading = fixture.UploadAsync(caller.Token, "held.glb");
            Exception? primary = null;
            try
            {
                await fixture.Control.Entered.Task.WaitAsync(TimeSpan.FromSeconds(33));
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
                    if (transition == "cancel") caller.Cancel();
                    else await fixture.Workflow.DisposeAsync();
                    await uploading.WaitAsync(TimeSpan.FromSeconds(5));
                    Assert.Equal(InstantQuotationWorkflowUploadStatus.Cancelled, Assert.Single(fixture.Workflow.Uploads).Status);
                    Assert.False(fixture.Workflow.HasCompleteAuthoritativeQuote);
                    Assert.Null((await fixture.Store.GetAsync(fixture.SessionId, Owner, default))!.QuoteAuthorization);
                    Assert.Contains(fixture.Control.ActualTasks, task => !task.IsCompleted);
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
    [InlineData("no-authority")]
    [InlineData("no-ticket-service")]
    [InlineData("null-quote")]
    [InlineData("missing-receipt")]
    [InlineData("invalid-ticket")]
    [InlineData("expired-ticket")]
    [InlineData("missing-authorization")]
    [InlineData("session")]
    [InlineData("owner")]
    [InlineData("file")]
    [InlineData("sha")]
    [InlineData("quantity")]
    [InlineData("build")]
    [InlineData("profile")]
    [InlineData("missing-protected-receipt")]
    [InlineData("refused-store")]
    [InlineData("failed-upload")]
    [InlineData("unsupported-extension")]
    public async Task FreshDefaultAcceptance_RejectsAbsentRefusedOrMismatchedAuthority(string mode)
    {
        var fixture = await Fixture.CreateAsync(mode);
        await MaterialPricingTestCleanup.RunAsync(async () =>
        {
            var actual = await Record.ExceptionAsync(() => fixture.UploadAsync(
                mode == "unsupported-extension" ? "rejected.exe" : "rejected.glb"));
            Assert.Null(actual);
            Assert.NotEqual(InstantQuotationWorkflowState.Configured, fixture.Workflow.State);
            Assert.False(fixture.Workflow.HasCompleteAuthoritativeQuote);
            Assert.Throws<InvalidOperationException>(() => fixture.Workflow.EnterReview());
            if (mode is "failed-upload" or "unsupported-extension")
                Assert.Empty(fixture.Workflow.Parts);
            else
                Assert.Single(fixture.Workflow.Parts);
        }, static () => ValueTask.CompletedTask, fixture.DisposeAsync, _ => { });
    }

    [Fact]
    public async Task AuthorizationWriteFault_PreservesTerminalFailurePolicyAndCannotAcceptDefaults()
    {
        var fixture = await Fixture.CreateAsync("faulted-store");
        await MaterialPricingTestCleanup.RunAsync(async () =>
        {
            var actual = await Record.ExceptionAsync(() => fixture.UploadAsync("fault.glb"));
            Assert.Null(actual);
            Assert.Equal(InstantQuotationWorkflowUploadStatus.Error, Assert.Single(fixture.Workflow.Uploads).Status);
            Assert.Equal(InstantQuotationProblemCategory.Unexpected, Assert.Single(fixture.Workflow.Uploads).ProblemCategory);
            Assert.False(fixture.Workflow.HasCompleteAuthoritativeQuote);
            Assert.NotEqual(InstantQuotationWorkflowState.Configured, fixture.Workflow.State);
        }, static () => ValueTask.CompletedTask, fixture.DisposeAsync, _ => { });
    }

    [Fact]
    public async Task AcknowledgedRemoval_CannotAcceptFreshDefaultsWhileAnotherUploadPublishes()
    {
        var fixture = await Fixture.CreateAsync();
        await MaterialPricingTestCleanup.RunAsync(async () =>
        {
            fixture.Control.ReturnNullQuote = true;
            await fixture.UploadAsync("unconfigured-first.glb");
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
        private static readonly byte[] Bytes = Convert.FromBase64String(AdmittedGltfMeshReaderTests.OriginalGlb);
        private static readonly string Sha = Convert.ToHexStringLower(SHA256.HashData(Bytes));
        private Fixture(string mode)
        {
            control = new(mode);
            factory = new(control, new Uploads(control));
        }
        internal static async Task<Fixture> CreateAsync(string mode = "current")
        {
            var fixture = new Fixture(mode);
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
            var claim = new InstantQuotationGeometryClaim(1, Sha, 10, 10, 10, 1000, 600,
                Enumerable.Repeat(100d, 64).ToArray(), Enumerable.Repeat(40d, 64).ToArray(),
                12, 1, true, false, false, 10) { GltfUnitPolicy = UnitPolicy };
            var task = Workflow.UploadAsync(names.Select(name => new InstantQuotationWorkflowUploadFile(
                name, "model/gltf-binary", Bytes.Length, claim,
                current => { current.ThrowIfCancellationRequested(); return Task.FromResult<Stream>(new MemoryStream(Bytes, false)); })).ToArray(), token);
            uploads.Add(task);
            return task;
        }
        internal Task RemoveAsync(Guid partId, Action acknowledged)
        {
            var task = Workflow.RemoveAsync(partId, acknowledged, default);
            uploads.Add(task);
            return task;
        }
        internal async Task AssertCurrentDefaultAuthorityAsync(int count)
        {
            Assert.Equal(960, Bytes.Length);
            Assert.Equal("2d31d5a61f110106cf30c0c17d713602b8ab5c9d90c6c802e980e4c1c0bee8dc", Sha);
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
                Assert.Equal(new InstantQuotationPartConfiguration("PLA", "Black", 1), part.Configuration);
                var line = Assert.Single(quote.Parts, item => item.PartId == part.PartId);
                var receipt = Assert.IsType<InstantQuotationPhysicalAnalysisReceipt>(line.PhysicalReceipt);
                Assert.Equal(part.PhysicalAnalysisUpload!.FileId, receipt.FileId);
                Assert.Equal(UnitPolicy, part.PhysicalAnalysisUpload.GltfUnitPolicy);
                Assert.Equal(UnitPolicy, receipt.GltfUnitPolicy);
                Assert.Equal(Sha, receipt.UploadSha256);
                Assert.Equal(1, receipt.BoundingCm3PerUnit, 3);
                Assert.True(receipt.DepositedMm3 > 0);
                Assert.True(receipt.MotionSeconds > 0);
                Assert.Contains(receipt, state.PhysicalReceipts!);
            });
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
