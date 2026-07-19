using System.Security.Claims;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Components.Pages.InstantQuotation;

namespace Legacy.Maliev.Web.Tests;

public sealed class InstantQuotationWorkflowUploadTests
{
    [Fact]
    public void WorkflowSource_UsesExactUploadContractAndStableMarkers()
    {
        var source = System.IO.File.ReadAllText(Path.Combine(WorkflowDirectory(), "InstantQuotationWorkflow.razor"));

        Assert.Contains("<InputFile", source, StringComparison.Ordinal);
        Assert.Contains("multiple", source, StringComparison.Ordinal);
        Assert.Contains("accept=\".stl,.obj,.3mf,.glb,.gltf,.stp,.step,.igs,.iges\"", source, StringComparison.Ordinal);
        Assert.Contains("200 * 1024 * 1024", ReadWorkflowSources(), StringComparison.Ordinal);
        foreach (var marker in new[]
        {
            "data-workflow-upload",
            "data-workflow-viewer",
            "data-workflow-part-list",
            "data-workflow-material-picker",
            "data-workflow-part-pricing",
            "data-workflow-order-total",
            "data-workflow-lead-time",
            "data-workflow-review",
            "data-workflow-customer-details",
            "data-workflow-submitted",
        })
        {
            Assert.Contains(marker, source, StringComparison.Ordinal);
        }

        foreach (var forbidden in new[]
        {
            "session-id", "sessionId=", "upload-reference", "storagePath", "access_token", "credential",
            "Height (mm)", "Solid volume", "Footprint", "areaProfile", "perimeterProfile",
        })
        {
            Assert.DoesNotContain(forbidden, source, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Initialize_CreatesProtectedEmptySessionForStableMemberIdentity()
    {
        var store = new RecordingSessionStore();
        await using var workflow = CreateWorkflow(store: store, ownerIdentity: "member-42");

        await workflow.InitializeAsync(default);

        Assert.Equal("member-42", store.LastOwnerIdentity);
        Assert.Empty(store.LastSavedState!.Parts);
        Assert.Equal(InstantQuotationWorkflowState.Empty, workflow.State);
        Assert.DoesNotContain("member-42", ReadWorkflowSources(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Initialize_ExistingProtectedSession_RestoresPartsAndAuthoritativeQuote()
    {
        var store = new RecordingSessionStore
        {
            ExistingOwnerIdentity = "member-42",
            ExistingSession = Session("protected-resume", PersistedPart("restored.stl", "opaque-restored")),
        };
        await using var workflow = CreateWorkflow(store: store, ownerIdentity: "member-42");

        await workflow.InitializeAsync("protected-resume", default);

        Assert.Equal("protected-resume", store.LastRequestedSessionId);
        Assert.Equal("member-42", store.LastOwnerIdentity);
        Assert.Equal(0, store.CreateCalls);
        var restored = Assert.Single(workflow.Parts);
        var restoredUpload = Assert.Single(workflow.Uploads);
        Assert.Equal("restored.stl", restored.DisplayFileName);
        Assert.Equal("restored.stl", restoredUpload.DisplayFileName);
        Assert.Equal(InstantQuotationWorkflowUploadStatus.Uploaded, restoredUpload.Status);
        Assert.NotNull(restored.Quote);
        Assert.NotNull(workflow.OrderQuote);
    }

    [Fact]
    public async Task Initialize_OwnerMismatch_DoesNotRestoreProtectedPartsAndCreatesFreshSession()
    {
        var store = new RecordingSessionStore
        {
            ExistingOwnerIdentity = "member-42",
            ExistingSession = Session("protected-resume", PersistedPart("private.stl", "opaque-private")),
        };
        await using var workflow = CreateWorkflow(store: store, ownerIdentity: "member-99");

        await workflow.InitializeAsync("protected-resume", default);

        Assert.Equal(1, store.CreateCalls);
        Assert.Empty(workflow.Parts);
        Assert.Null(workflow.OrderQuote);
        Assert.Equal(InstantQuotationWorkflowState.Empty, workflow.State);
    }

    [Fact]
    public async Task Initialize_InvalidProtectedState_CreatesFreshSession()
    {
        var invalidPart = PersistedPart("invalid.stl", "opaque-invalid") with
        {
            Configuration = new InstantQuotationPartConfiguration("UNSUPPORTED", "Black", 1),
        };
        var store = new RecordingSessionStore
        {
            ExistingSession = Session("protected-invalid", invalidPart),
        };
        await using var workflow = CreateWorkflow(store: store);

        await workflow.InitializeAsync("protected-invalid", default);

        Assert.Equal(1, store.CreateCalls);
        Assert.Empty(workflow.Parts);
        Assert.Null(workflow.OrderQuote);
    }

    [Fact]
    public async Task Initialize_MalformedProtectedState_CreatesFreshSession()
    {
        var store = new RecordingSessionStore
        {
            ExistingSession = new InstantQuotationSessionState(
                "protected-malformed",
                "submission",
                null!,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow),
        };
        await using var workflow = CreateWorkflow(store: store);

        await workflow.InitializeAsync("protected-malformed", default);

        Assert.Equal(1, store.CreateCalls);
        Assert.Empty(workflow.Parts);
    }

    [Fact]
    public async Task CreatedSessionIdentity_IsPersistedServerSideAndRestoredByNextCoordinator()
    {
        var store = new RecordingSessionStore();
        var accessor = new RecordingSessionIdentityAccessor();
        await using (var first = CreateWorkflow(store: store))
        {
            await first.InitializeAsync(await accessor.GetProtectedSessionIdentityAsync(default), default);
            await accessor.SetProtectedSessionIdentityAsync(first.ProtectedSessionIdentity, default);
        }

        await using var resumed = CreateWorkflow(store: store);
        await resumed.InitializeAsync(await accessor.GetProtectedSessionIdentityAsync(default), default);

        Assert.Equal(1, store.CreateCalls);
        Assert.Equal("protected-session", accessor.Value);
        Assert.Equal("protected-session", store.LastRequestedSessionId);
        Assert.Equal(InstantQuotationWorkflowState.Empty, resumed.State);
    }

    [Fact]
    public void ResolveOwnerIdentity_UsesOnlyAuthenticatedNameIdentifier()
    {
        var member = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "member-42"), new Claim(ClaimTypes.Name, "Customer")],
            "cookie"));
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "forged")]));

        Assert.Equal("member-42", InstantQuotationWorkflow.ResolveOwnerIdentity(member));
        Assert.Null(InstantQuotationWorkflow.ResolveOwnerIdentity(anonymous));
        Assert.Null(InstantQuotationWorkflow.ResolveOwnerIdentity(null));
    }

    [Fact]
    public async Task SuccessfulUploads_UseOnlyAuthoritativeResultsAndPreserveSelectionOrder()
    {
        var client = new ControlledUploadClient();
        await using var workflow = CreateWorkflow(client: client);
        await workflow.InitializeAsync(default);

        var uploading = workflow.UploadAsync(
            [UploadFile("first.stl"), UploadFile("second.obj")],
            default);
        await client.WaitForUploadsAsync(2);
        client.CompleteSuccess("second.obj", "opaque-second", Geometry(volume: 20_000));
        client.CompleteSuccess("first.stl", "opaque-first", Geometry(volume: 10_000));
        await uploading;

        Assert.Equal(["first.stl", "second.obj"], workflow.Parts.Select(part => part.DisplayFileName));
        Assert.All(workflow.Parts, part => Assert.NotNull(part.Quote));
        Assert.Equal(InstantQuotationWorkflowState.MultiPart, workflow.State);
        Assert.NotNull(workflow.OrderQuote);
    }

    [Fact]
    public async Task ReservedUploadBatch_AllocatesStableOrderedLocalIdsBeforeTransportStarts()
    {
        var client = new ControlledUploadClient();
        await using var workflow = CreateWorkflow(client: client);
        await workflow.InitializeAsync(default);

        var reserved = workflow.ReserveUploads([UploadFile("same.stl"), UploadFile("same.stl")]);

        Assert.Equal(2, reserved.Count);
        Assert.NotEqual(reserved[0], reserved[1]);
        Assert.Equal(reserved, workflow.Uploads.Select(static upload => upload.LocalId));
        Assert.Empty(client.UploadOperations);

        var uploading = workflow.UploadReservedAsync(reserved, default);
        await client.WaitForUploadsAsync(2);
        client.CompleteSuccess("same.stl", "opaque-first", Geometry(volume: 10_000));
        client.CompleteSuccess("same.stl", "opaque-second", Geometry(volume: 20_000));
        await uploading;

        Assert.Equal(reserved, workflow.Parts.Select(static part => part.PreviewCorrelationId));
    }

    [Fact]
    public async Task UnavailableUpload_ProducesRecoverableErrorWithoutCreatingPart()
    {
        var client = new ControlledUploadClient();
        await using var workflow = CreateWorkflow(client: client);
        await workflow.InitializeAsync(default);

        var uploading = workflow.UploadAsync([UploadFile("broken.stl")], default);
        await client.WaitForUploadsAsync(1);
        client.CompleteUnavailable("broken.stl");
        await uploading;

        var item = Assert.Single(workflow.Uploads);
        Assert.Equal(InstantQuotationWorkflowUploadStatus.Error, item.Status);
        Assert.Equal(InstantQuotationProblemCategory.DependencyUnavailable, item.ProblemCategory);
        Assert.Empty(workflow.Parts);
        Assert.Equal(InstantQuotationWorkflowState.Error, workflow.State);
    }

    [Theory]
    [InlineData("part.exe")]
    [InlineData("part.stl.exe")]
    [InlineData("part")]
    public async Task UnsupportedExtension_IsRejectedBeforeUploadBoundary(string fileName)
    {
        var client = new ControlledUploadClient();
        await using var workflow = CreateWorkflow(client: client);
        await workflow.InitializeAsync(default);

        await workflow.UploadAsync([UploadFile(fileName)], default);

        Assert.Empty(client.UploadOperations);
        var upload = Assert.Single(workflow.Uploads);
        Assert.Equal(InstantQuotationWorkflowUploadStatus.Error, upload.Status);
        Assert.Equal(InstantQuotationProblemCategory.Validation, upload.ProblemCategory);
    }

    [Fact]
    public async Task SupportedExtension_IsMatchedCaseInsensitively()
    {
        var client = new ControlledUploadClient();
        await using var workflow = CreateWorkflow(client: client);
        await workflow.InitializeAsync(default);

        var uploading = workflow.UploadAsync([UploadFile("PART.STL")], default);
        await client.WaitForUploadsAsync(1);
        client.CompleteSuccess("PART.STL", "opaque-uppercase", Geometry());
        await uploading;

        Assert.Single(workflow.Parts);
    }

    [Fact]
    public async Task MismatchedOperationAuthoritativeSuccess_IsQuarantinedAndRejected()
    {
        var client = new ControlledUploadClient();
        await using var workflow = CreateWorkflow(client: client);
        await workflow.InitializeAsync(default);

        var uploading = workflow.UploadAsync([UploadFile("part.stl")], default);
        await client.WaitForUploadsAsync(1);
        client.CompleteSuccess("part.stl", "opaque-mismatch", Geometry(), operationId: "wrong-operation");
        await uploading;

        Assert.Equal("opaque-mismatch", Assert.Single(client.RemovedReferences));
        Assert.Empty(workflow.Parts);
        Assert.Equal(InstantQuotationWorkflowUploadStatus.Error, Assert.Single(workflow.Uploads).Status);
    }

    [Fact]
    public async Task CancelThenLateSuccess_DoesNotMutateState_AndRetryUsesFreshOperationId()
    {
        var client = new ControlledUploadClient(ignoreCancellation: true);
        await using var workflow = CreateWorkflow(client: client);
        await workflow.InitializeAsync(default);

        var uploading = workflow.UploadAsync([UploadFile("part.stl")], default);
        await client.WaitForUploadsAsync(1);
        var firstOperation = Assert.Single(client.UploadOperations);
        var localId = Assert.Single(workflow.Uploads).LocalId;
        workflow.Cancel(localId);
        client.CompleteSuccess("part.stl", "stale", Geometry());
        await uploading;

        Assert.Empty(workflow.Parts);
        Assert.Equal("stale", Assert.Single(client.RemovedReferences));
        var retrying = workflow.RetryAsync(localId, default);
        await client.WaitForUploadsAsync(2);
        var secondOperation = client.UploadOperations[1];
        Assert.NotEqual(firstOperation, secondOperation);
        client.CompleteSuccess("part.stl", "current", Geometry());
        await retrying;

        Assert.Single(workflow.Parts);
        Assert.Equal(InstantQuotationWorkflowUploadStatus.Uploaded, Assert.Single(workflow.Uploads).Status);
    }

    [Fact]
    public async Task MultipartFailure_PreservesSuccessfulPartAndRecoverableUiSections()
    {
        var client = new ControlledUploadClient();
        await using var workflow = CreateWorkflow(client: client);
        await workflow.InitializeAsync(default);

        var uploading = workflow.UploadAsync(
            [UploadFile("good.stl"), UploadFile("bad.obj")],
            default);
        await client.WaitForUploadsAsync(2);
        client.CompleteUnavailable("bad.obj");
        client.CompleteSuccess("good.stl", "opaque-good", Geometry());
        await uploading;

        Assert.Equal(InstantQuotationWorkflowState.Error, workflow.State);
        Assert.Equal("good.stl", Assert.Single(workflow.Parts).DisplayFileName);
        var source = ReadWorkflowSources();
        Assert.Contains("State is InstantQuotationWorkflowState.Error && Parts.Count > 0", source, StringComparison.Ordinal);
        Assert.Contains("Viewer: true, Parts: true, Configuration: true", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Component_ResolvesPricingContractFromDependencyInjection()
    {
        var source = System.IO.File.ReadAllText(Path.Combine(WorkflowDirectory(), "InstantQuotationWorkflow.razor.cs"));

        Assert.Contains("GetService<IInstantQuotationPricingService>()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new InstantQuotationPricingService", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Component_UsesServerSessionIdentityAccessorWithoutMarkupParameter()
    {
        var codeBehind = System.IO.File.ReadAllText(Path.Combine(WorkflowDirectory(), "InstantQuotationWorkflow.razor.cs"));
        var markup = System.IO.File.ReadAllText(Path.Combine(WorkflowDirectory(), "InstantQuotationWorkflow.razor"));

        Assert.Contains("GetService<IInstantQuotationWorkflowSessionIdentityAccessor>()", codeBehind, StringComparison.Ordinal);
        Assert.Contains("GetProtectedSessionIdentityAsync", codeBehind, StringComparison.Ordinal);
        Assert.Contains("SetProtectedSessionIdentityAsync", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("SessionIdentity", markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SessionId", markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Remove_CallsBoundaryOnlyForOpaqueReferenceAndPersistsRemainingParts()
    {
        var client = new ControlledUploadClient();
        var store = new RecordingSessionStore();
        await using var workflow = CreateWorkflow(client: client, store: store);
        await workflow.InitializeAsync(default);
        var uploading = workflow.UploadAsync([UploadFile("part.stl")], default);
        await client.WaitForUploadsAsync(1);
        client.CompleteSuccess("part.stl", "opaque-part", Geometry());
        await uploading;

        await workflow.RemoveAsync(Assert.Single(workflow.Parts).PartId, default);

        Assert.Equal("opaque-part", Assert.Single(client.RemovedReferences));
        Assert.Empty(workflow.Parts);
        Assert.Empty(store.LastSavedState!.Parts);
        Assert.Equal(InstantQuotationWorkflowState.Empty, workflow.State);
    }

    [Fact]
    public async Task FailedRemove_RetryUsesFreshOperationAndDoesNotReuploadPart()
    {
        var client = new ControlledUploadClient();
        client.RemoveResults.Enqueue(InstantQuotationProblemCategory.Conflict);
        client.RemoveResults.Enqueue(InstantQuotationProblemCategory.None);
        await using var workflow = CreateWorkflow(client: client);
        await workflow.InitializeAsync(default);
        var uploading = workflow.UploadAsync([UploadFile("part.stl")], default);
        await client.WaitForUploadsAsync(1);
        client.CompleteSuccess("part.stl", "opaque-part", Geometry());
        await uploading;
        var partId = Assert.Single(workflow.Parts).PartId;

        await workflow.RemoveAsync(partId, default);
        var firstRemoveOperation = Assert.Single(client.RemoveOperations);
        var failedItem = Assert.Single(workflow.Uploads);
        Assert.Equal(InstantQuotationWorkflowUploadStatus.Error, failedItem.Status);
        Assert.Single(workflow.Parts);

        await workflow.RetryAsync(failedItem.LocalId, default);

        Assert.Equal(2, client.RemoveOperations.Count);
        Assert.NotEqual(firstRemoveOperation, client.RemoveOperations[1]);
        Assert.Single(client.UploadOperations);
        Assert.Empty(workflow.Parts);
    }

    [Fact]
    public async Task ConfigurationChange_UsesCatalogChoicesPersistsAndRecomputesServerQuote()
    {
        var client = new ControlledUploadClient();
        var store = new RecordingSessionStore();
        var pricing = new RecordingPricingService();
        await using var workflow = CreateWorkflow(client: client, store: store, pricing: pricing);
        await workflow.InitializeAsync(default);
        var uploading = workflow.UploadAsync([UploadFile("part.stl")], default);
        await client.WaitForUploadsAsync(1);
        client.CompleteSuccess("part.stl", "opaque", Geometry());
        await uploading;
        var part = Assert.Single(workflow.Parts);

        await workflow.UpdateConfigurationAsync(part.PartId, "PETG", "Blue", 10, default);

        var updated = Assert.Single(workflow.Parts);
        Assert.Equal(new InstantQuotationPartConfiguration("PETG", "Blue", 10), updated.Configuration);
        Assert.Equal(["Any", "Black", "White", "Gray", "Silver", "Red", "Orange", "Yellow", "Green", "Blue", "Purple", "Pink"], workflow.GetColors("PETG"));
        Assert.Equal(2, pricing.QuoteCalls);
        Assert.Equal(updated.PartId, Assert.Single(store.LastSavedState!.Parts).PartId);
        Assert.Equal("PETG", Assert.Single(store.LastSavedState.Parts).Configuration.MaterialKey);
    }

    [Fact]
    public async Task InvalidConfiguration_IsRejectedWithoutChangingProtectedState()
    {
        var client = new ControlledUploadClient();
        var store = new RecordingSessionStore();
        await using var workflow = CreateWorkflow(client: client, store: store);
        await workflow.InitializeAsync(default);
        var uploading = workflow.UploadAsync([UploadFile("part.stl")], default);
        await client.WaitForUploadsAsync(1);
        client.CompleteSuccess("part.stl", "opaque", Geometry());
        await uploading;
        var part = Assert.Single(workflow.Parts);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            workflow.UpdateConfigurationAsync(part.PartId, "PETG", "NotAColor", 1, default));

        Assert.Equal("PLA", Assert.Single(workflow.Parts).Configuration.MaterialKey);
        Assert.Equal("PLA", Assert.Single(store.LastSavedState!.Parts).Configuration.MaterialKey);
    }

    [Fact]
    public async Task Dispose_CancelsOutstandingUpload()
    {
        var client = new ControlledUploadClient(ignoreCancellation: true);
        var workflow = CreateWorkflow(client: client);
        await workflow.InitializeAsync(default);
        var uploading = workflow.UploadAsync([UploadFile("part.stl")], default);
        await client.WaitForUploadsAsync(1);

        await workflow.DisposeAsync();
        client.CompleteSuccess("part.stl", "opaque-after-dispose", Geometry());
        await uploading;

        Assert.True(client.UploadCancellationTokens[0].IsCancellationRequested);
        Assert.Equal(InstantQuotationWorkflowUploadStatus.Cancelled, Assert.Single(workflow.Uploads).Status);
        Assert.Equal("opaque-after-dispose", Assert.Single(client.RemovedReferences));
    }

    private static InstantQuotationWorkflowCoordinator CreateWorkflow(
        ControlledUploadClient? client = null,
        RecordingSessionStore? store = null,
        IInstantQuotationPricingService? pricing = null,
        string? ownerIdentity = null) => new(
            store ?? new RecordingSessionStore(),
            client ?? new ControlledUploadClient(),
            pricing ?? new InstantQuotationPricingService(),
            ownerIdentity);

    private static InstantQuotationWorkflowUploadFile UploadFile(string name) => new(
        name,
        "application/octet-stream",
        4,
        _ => Task.FromResult<Stream>(new MemoryStream([1, 2, 3, 4], writable: false)));

    private static InstantQuotationGeometry Geometry(double volume = 1_000) =>
        new(10, volume, 100, [100, 90], [40, 38], 100, 1, true);

    private static InstantQuotationPart PersistedPart(string fileName, string reference)
    {
        var upload = InstantQuotationUploadResult.Succeeded(
            "persisted-operation",
            new InstantQuotationUploadReference(reference),
            Geometry());
        return new InstantQuotationPart(
            Guid.NewGuid(),
            fileName,
            upload.UploadReference!,
            upload.AuthoritativeGeometry!,
            new InstantQuotationPartConfiguration("PLA", "Black", 1));
    }

    private static InstantQuotationSessionState Session(string sessionId, params InstantQuotationPart[] parts) => new(
        sessionId,
        "submission",
        new InstantQuotationOrderState(parts),
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow);

    private static string WorkflowDirectory() => Path.Combine(
        FindRepositoryRoot(), "Legacy.Maliev.Web", "Components", "Pages", "InstantQuotation");

    private static string ReadWorkflowSources() => string.Join(
        Environment.NewLine,
        Directory.EnumerateFiles(WorkflowDirectory(), "InstantQuotationWorkflow*", SearchOption.TopDirectoryOnly)
            .Select(System.IO.File.ReadAllText));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Web.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }

    private sealed class RecordingSessionStore : IInstantQuotationSessionStore
    {
        private InstantQuotationSessionState? createdSession;

        public int CreateCalls { get; private set; }

        public string? LastOwnerIdentity { get; private set; }

        public InstantQuotationOrderState? LastSavedState { get; private set; }

        public string? LastRequestedSessionId { get; private set; }

        public InstantQuotationSessionState? ExistingSession { get; init; }

        public string? ExistingOwnerIdentity { get; init; }

        public Task<InstantQuotationSessionState> CreateAsync(string? ownerIdentity, InstantQuotationOrderState requestState, CancellationToken cancellationToken)
        {
            CreateCalls++;
            LastOwnerIdentity = ownerIdentity;
            LastSavedState = requestState;
            createdSession = new InstantQuotationSessionState("protected-session", "submission", requestState, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
            return Task.FromResult(createdSession);
        }

        public Task<InstantQuotationSessionState?> GetAsync(string sessionId, string? ownerIdentity, CancellationToken cancellationToken)
        {
            LastRequestedSessionId = sessionId;
            LastOwnerIdentity = ownerIdentity;
            return Task.FromResult(
                ExistingSession?.SessionId == sessionId && ExistingOwnerIdentity == ownerIdentity
                    ? ExistingSession
                    : createdSession?.SessionId == sessionId && ExistingOwnerIdentity == ownerIdentity
                        ? createdSession
                        : null);
        }

        public Task<bool> PutAsync(InstantQuotationSessionState session, string? ownerIdentity, CancellationToken cancellationToken)
        {
            LastOwnerIdentity = ownerIdentity;
            LastSavedState = session.RequestState;
            createdSession = session;
            return Task.FromResult(true);
        }

        public Task<bool> RemoveAsync(string sessionId, string? ownerIdentity, CancellationToken cancellationToken) => Task.FromResult(true);
    }

    private sealed class RecordingSessionIdentityAccessor : IInstantQuotationWorkflowSessionIdentityAccessor
    {
        public string? Value { get; private set; }

        public ValueTask<string?> GetProtectedSessionIdentityAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(Value);

        public ValueTask SetProtectedSessionIdentityAsync(
            string protectedSessionIdentity,
            CancellationToken cancellationToken)
        {
            Value = protectedSessionIdentity;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingPricingService : IInstantQuotationPricingService
    {
        private readonly InstantQuotationPricingService inner = new();

        public int QuoteCalls { get; private set; }

        public InstantQuotationOrderQuote Quote(InstantQuotationOrderState state)
        {
            QuoteCalls++;
            return inner.Quote(state);
        }
    }

    private sealed class ControlledUploadClient(bool ignoreCancellation = false) : IInstantQuotationUploadClient
    {
        private readonly Dictionary<string, Queue<PendingUpload>> completions = new(StringComparer.Ordinal);

        public List<string> UploadOperations { get; } = [];

        public List<CancellationToken> UploadCancellationTokens { get; } = [];

        public List<string> RemovedReferences { get; } = [];

        public List<string> RemoveOperations { get; } = [];

        public Queue<InstantQuotationProblemCategory> RemoveResults { get; } = [];

        public Task<InstantQuotationUploadResult> UploadAsync(string sessionId, Stream content, string fileName, string contentType, long contentLength, string operationId, CancellationToken cancellationToken)
        {
            UploadOperations.Add(operationId);
            UploadCancellationTokens.Add(cancellationToken);
            var completion = new TaskCompletionSource<InstantQuotationUploadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!completions.TryGetValue(fileName, out var queue))
            {
                queue = new Queue<PendingUpload>();
                completions[fileName] = queue;
            }

            queue.Enqueue(new PendingUpload(operationId, completion));
            if (!ignoreCancellation)
            {
                cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
            }

            return completion.Task;
        }

        public Task<InstantQuotationRemoveResult> RemoveAsync(string sessionId, InstantQuotationUploadReference uploadReference, string operationId, CancellationToken cancellationToken)
        {
            RemovedReferences.Add(uploadReference.Value);
            RemoveOperations.Add(operationId);
            var problem = RemoveResults.TryDequeue(out var queued) ? queued : InstantQuotationProblemCategory.None;
            return Task.FromResult(new InstantQuotationRemoveResult(
                operationId,
                InstantQuotationServiceStatus.Available,
                InstantQuotationAuthorizationStatus.Authorized,
                problem is InstantQuotationProblemCategory.None ? InstantQuotationOperationStatus.Succeeded : InstantQuotationOperationStatus.Failed,
                problem));
        }

        public Task<InstantQuotationFinalizationResult> FinalizeAsync(string sessionId, int quotationRequestId, IReadOnlyList<InstantQuotationUploadReference> uploadReferences, string operationId, CancellationToken cancellationToken) =>
            Task.FromResult(InstantQuotationFinalizationResult.Unavailable(operationId));

        public void CompleteSuccess(
            string fileName,
            string reference,
            InstantQuotationGeometry geometry,
            string? operationId = null)
        {
            var pending = completions[fileName].Dequeue();
            pending.Completion.SetResult(InstantQuotationUploadResult.Succeeded(
                operationId ?? pending.OperationId,
                new InstantQuotationUploadReference(reference),
                geometry));
        }

        public void CompleteUnavailable(string fileName)
        {
            var pending = completions[fileName].Dequeue();
            pending.Completion.SetResult(InstantQuotationUploadResult.Unavailable(pending.OperationId));
        }

        public async Task WaitForUploadsAsync(int count)
        {
            for (var attempt = 0; attempt < 100 && UploadOperations.Count < count; attempt++)
            {
                await Task.Delay(10);
            }

            Assert.True(UploadOperations.Count >= count, $"Expected {count} upload calls, observed {UploadOperations.Count}.");
        }

        private sealed record PendingUpload(
            string OperationId,
            TaskCompletionSource<InstantQuotationUploadResult> Completion);
    }
}
