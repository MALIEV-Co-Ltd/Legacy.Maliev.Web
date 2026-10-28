using System.Net;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Application.Pricing;
using Legacy.Maliev.Web.Components.Pages.InstantQuotation;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace Legacy.Maliev.Web.Tests;

public sealed class InstantQuotationWorkflowUploadTests
{
    [Fact]
    public void WorkflowSource_UsesExactUploadContractAndStableMarkers()
    {
        var source = System.IO.File.ReadAllText(Path.Combine(WorkflowDirectory(), "InstantQuotationWorkflow.razor"));

        Assert.Contains("<InputFile", source, StringComparison.Ordinal);
        Assert.Contains("multiple", source, StringComparison.Ordinal);
        Assert.Contains(
            "accept=\".stl,.obj,.3mf,.glb,.gltf,.stp,.step,.igs,.iges,model/stl,application/sla,application/vnd.ms-pki.stl,model/x.stl-binary,model/x.stl-ascii,model/obj,model/3mf,model/gltf-binary,model/gltf+json,model/step,model/iges\"",
            source,
            StringComparison.Ordinal);
        Assert.Contains("100 * 1024 * 1024", ReadWorkflowSources(), StringComparison.Ordinal);
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
    public void WorkflowSource_FiltersOversizedFilesBeforeStartingPreviewAnalysis()
    {
        string source = System.IO.File.ReadAllText(Path.Combine(WorkflowDirectory(), "InstantQuotationWorkflow.razor.cs"));
        int sizeCheck = source.IndexOf("file.Size <= InstantQuotationWorkflowCoordinator.MaximumFileSize", StringComparison.Ordinal);
        int previewStart = source.IndexOf("var keys = await BeginPreviewSelectionAsync();", StringComparison.Ordinal);

        Assert.True(sizeCheck >= 0 && previewStart > sizeCheck);
        Assert.Contains("uploadSelectionError = Localizer[", source, StringComparison.Ordinal);
        Assert.Contains("InstantQuotationWorkflowCoordinator.MaximumFileSize);", source, StringComparison.Ordinal);
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

    [Theory]
    [InlineData(BuildPreference.Quality)]
    [InlineData(BuildPreference.Strength)]
    public async Task Initialize_ResinProtectedSessionRepairsStalePreferenceBeforeRepricing(BuildPreference preference)
    {
        var part = PersistedPart("resin.stl", "opaque-restored") with { Configuration = new("M68", "Gray", 1, preference) };
        var store = new RecordingSessionStore
        {
            ExistingOwnerIdentity = "member-42",
            ExistingSession = Session("protected-resume", part),
        };
        await using var workflow = CreateWorkflow(store: store, ownerIdentity: "member-42");

        await workflow.InitializeAsync("protected-resume", default);

        Assert.Equal(0, store.CreateCalls);
        Assert.Equal(BuildPreference.Standard, Assert.Single(workflow.Parts).Configuration.BuildPreference);
        Assert.Equal(BuildPreference.Standard, Assert.Single(store.LastSavedState!.Parts).Configuration.BuildPreference);
        Assert.Equal(BuildPreference.Standard, Assert.Single(workflow.OrderQuote!.Parts).BuildPreference);
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
        await using var workflow = CreateWorkflow(client: client, ownerIdentity: "member-42");
        await workflow.InitializeAsync(default);

        var uploading = workflow.UploadAsync(
            [UploadFile("first.stl"), UploadFile("second.obj")],
            default);
        await client.WaitForUploadsAsync(2);
        Assert.Equal(InstantQuotationWorkflowState.Uploading, workflow.State);
        Assert.Empty(workflow.Parts);
        Assert.All(workflow.Uploads, upload => Assert.Equal(InstantQuotationWorkflowUploadStatus.Uploading, upload.Status));
        client.CompleteSuccess("second.obj", "opaque-second", Geometry(volume: 20_000));
        client.CompleteSuccess("first.stl", "opaque-first", Geometry(volume: 10_000));
        await uploading;

        Assert.Equal(["first.stl", "second.obj"], workflow.Parts.Select(part => part.DisplayFileName));
        Assert.All(workflow.Parts, part => Assert.NotNull(part.Quote));
        Assert.Equal(InstantQuotationWorkflowState.MultiPart, workflow.State);
        Assert.NotNull(workflow.OrderQuote);
        Assert.Equal(["member-42", "member-42"], client.UploadOwners);
    }

    [Fact]
    public async Task SuccessfulSelection_ProgressesThroughConfigurationReviewAndCustomerDetails()
    {
        var client = new ControlledUploadClient();
        await using var workflow = CreateWorkflow(client: client);
        await workflow.InitializeAsync(default);

        var uploading = workflow.UploadAsync([UploadFile("part.stl")], default);
        await client.WaitForUploadsAsync(1);
        Assert.Equal(InstantQuotationWorkflowState.Uploading, workflow.State);
        client.CompleteSuccess("part.stl", "opaque-part", Geometry());
        await uploading;

        var part = Assert.Single(workflow.Parts);
        Assert.Equal(InstantQuotationWorkflowState.Uploaded, workflow.State);
        await workflow.UpdateConfigurationAsync(
            part.PartId,
            part.Configuration.MaterialKey,
            part.Configuration.Color,
            part.Configuration.Quantity,
            default);
        Assert.Equal(InstantQuotationWorkflowState.Configured, workflow.State);

        workflow.EnterReview();
        Assert.Equal(InstantQuotationWorkflowState.Review, workflow.State);
        workflow.EnterCustomerDetails();
        Assert.Equal(InstantQuotationWorkflowState.CustomerDetails, workflow.State);
        workflow.ReturnToReview();
        Assert.Equal(InstantQuotationWorkflowState.Review, workflow.State);
        workflow.ReturnToConfiguration();
        Assert.Equal(InstantQuotationWorkflowState.Configured, workflow.State);
    }

    [Fact]
    public async Task Upload_PassesExactValidatedClaimAndPromotesItOnlyAfterMatchingPersistedDigest()
    {
        var client = new ControlledUploadClient();
        var areas = Enumerable.Range(0, 64).Select(index => 80.0 - index).ToArray();
        var perimeters = Enumerable.Range(0, 64).Select(index => 36.0 - (index * 0.25)).ToArray();
        var claim = Geometry() with
        {
            DimensionXmm = 8,
            DimensionYmm = 12.5,
            DimensionZmm = 10,
            SurfaceAreaMm2 = 550,
            AreaProfileMm2 = areas,
            PerimeterProfileMm = perimeters,
            MinThicknessMm = 0.6,
        };
        var file = UploadFile("bound.stl") with { GeometryClaim = claim };
        await using var workflow = CreateWorkflow(client: client);
        await workflow.InitializeAsync(default);

        var uploading = workflow.UploadAsync([file], default);
        await client.WaitForUploadsAsync(1);

        Assert.Empty(workflow.Parts);
        areas[0] = -1;
        perimeters[0] = -1;
        var passedClaim = Assert.Single(client.UploadClaims);
        Assert.NotSame(claim, passedClaim);
        Assert.Equal(claim.Sha256, passedClaim.Sha256);
        Assert.Equal(64, passedClaim.AreaProfileMm2!.Count);
        Assert.Equal(80.0, passedClaim.AreaProfileMm2[0]);
        Assert.Equal(79.0, passedClaim.AreaProfileMm2[1]);
        Assert.Equal(64, passedClaim.PerimeterProfileMm!.Count);
        Assert.Equal(36.0, passedClaim.PerimeterProfileMm[0]);
        Assert.Equal(35.75, passedClaim.PerimeterProfileMm[1]);
        client.CompleteSuccess("bound.stl", "opaque-bound", claim);
        await uploading;

        var geometry = Assert.Single(workflow.Parts).Geometry;
        Assert.Equal(8, geometry.DimensionXmm);
        Assert.Equal(12.5, geometry.DimensionYmm);
        Assert.Equal(10, geometry.HeightMm);
        Assert.Equal(100, geometry.FootprintMm2);
        Assert.Equal(550, geometry.SurfaceAreaMm2);
        Assert.Equal(0.6, geometry.MinThicknessMm);
    }

    [Fact]
    public async Task InvalidClaim_IsRejectedBeforeUploadBoundary()
    {
        var client = new ControlledUploadClient();
        var analytics = new RecordingAnalyticsTracker();
        await using var workflow = CreateWorkflow(client: client, analytics: analytics);
        await workflow.InitializeAsync(default);

        await workflow.UploadAsync(
            [UploadFile("invalid.stl") with { GeometryClaim = Geometry() with { Sha256 = new string('A', 64) } }],
            default);

        Assert.Empty(client.UploadOperations);
        Assert.Empty(workflow.Parts);
        Assert.Equal(InstantQuotationProblemCategory.Validation, Assert.Single(workflow.Uploads).ProblemCategory);
        Assert.Contains(analytics.StageResults, item =>
            item.Stage is InstantQuotationStage.FileValidation
            && item.Result is InstantQuotationStageResult.Success
            && item.Failure is null
            && item.FileType == "stl");
        Assert.Contains(analytics.StageResults, item =>
            item.Stage is InstantQuotationStage.Parse
            && item.Result is InstantQuotationStageResult.Failure
            && item.Failure is InstantQuotationStageFailure.UnreadableModel
            && item.FileType == "stl");
    }

    [Fact]
    public async Task AuthoritativeUpload_RecordsValidatedParseUploadPricingAndOrderTotalStages()
    {
        var client = new ControlledUploadClient();
        var analytics = new RecordingAnalyticsTracker();
        await using var workflow = CreateWorkflow(client: client, analytics: analytics);
        await workflow.InitializeAsync(default);

        var uploading = workflow.UploadAsync([UploadFile("part.stl")], default);
        await client.WaitForUploadsAsync(1);
        client.CompleteSuccess("part.stl", "opaque-part", Geometry());
        await uploading;

        Assert.Collection(
            analytics.StageResults,
            item => Assert.Equal((InstantQuotationStage.FileValidation, InstantQuotationStageResult.Success, null, "stl"), item),
            item => Assert.Equal((InstantQuotationStage.Parse, InstantQuotationStageResult.Success, null, "stl"), item),
            item => Assert.Equal((InstantQuotationStage.Upload, InstantQuotationStageResult.Success, null, "stl"), item),
            item => Assert.Equal((InstantQuotationStage.Pricing, InstantQuotationStageResult.Success, null, "stl"), item),
            item => Assert.Equal((InstantQuotationStage.OrderTotal, InstantQuotationStageResult.Success, null, (string?)null), item));
    }

    [Fact]
    public async Task PersistedDigestMismatch_IsQuarantinedAndCannotPromoteClaim()
    {
        var client = new ControlledUploadClient();
        await using var workflow = CreateWorkflow(client: client);
        await workflow.InitializeAsync(default);

        var uploading = workflow.UploadAsync([UploadFile("mismatch.stl")], default);
        await client.WaitForUploadsAsync(1);
        client.CompleteSuccess(
            "mismatch.stl",
            "opaque-mismatch",
            Geometry() with { Sha256 = new string('b', 64) });
        await uploading;

        Assert.Empty(workflow.Parts);
        Assert.Equal("opaque-mismatch", Assert.Single(client.RemovedReferences));
        Assert.Equal(InstantQuotationProblemCategory.Validation, Assert.Single(workflow.Uploads).ProblemCategory);
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

    [Theory]
    [InlineData(InstantQuotationWorkflowUploadStatus.Pending)]
    [InlineData(InstantQuotationWorkflowUploadStatus.Uploading)]
    [InlineData(InstantQuotationWorkflowUploadStatus.Uploaded)]
    public async Task PendingRemoveCaller_CleansRemovedSlotButKeepsStartedOrAuthoritativeSlot(
        InstantQuotationWorkflowUploadStatus stage)
    {
        var client = new ControlledUploadClient();
        var store = new RecordingSessionStore();
        var pricing = new RecordingPricingService();
        var tickets = new CallerTickets();
        var activator = new CallerComponentActivator();
        var javascript = new CallerJavaScript();
        using var services = new ServiceCollection().AddLogging()
            .AddLocalization(options => options.ResourcesPath = "Resources")
            .AddSingleton<IComponentActivator>(activator)
            .AddSingleton<NavigationManager>(new InstantQuotationTestNavigationManager())
            .AddSingleton<IJSRuntime>(javascript)
            .AddSingleton<IInstantQuotationSessionStore>(store)
            .AddSingleton<IInstantQuotationUploadClient>(client)
            .AddSingleton<IInstantQuotationPricingService>(pricing)
            .AddSingleton<IInstantQuotationAuthoritativePricingService>(pricing)
            .AddSingleton<IInstantQuotationQuoteTicketService>(tickets)
            .AddSingleton(FdmRuntimeProfileCatalog.LoadEmbedded())
            .BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var root = await renderer.Dispatcher.InvokeAsync(() =>
            renderer.RenderComponentAsync<InstantQuotationWorkflow>(ParameterView.Empty));
        var component = Assert.IsType<InstantQuotationWorkflow>(activator.Workflow);
        // Observe the naturally initialized coordinator; all state below uses its public API.
        var workflowField = typeof(InstantQuotationWorkflow).GetField(
            "workflow", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var workflow = Assert.IsType<InstantQuotationWorkflowCoordinator>(workflowField.GetValue(component));
        Guid localId = default;
        await renderer.Dispatcher.InvokeAsync(() =>
        {
            localId = Assert.Single(workflow.ReserveUploads([UploadFile("kept.stl")]));
        });
        await renderer.Dispatcher.InvokeAsync(() => component.SetParametersAsync(ParameterView.Empty));
        var pendingHtml = await renderer.Dispatcher.InvokeAsync(root.ToHtmlString);
        var row = Regex.Match(pendingHtml,
            $"<article\\b[^>]*aria-labelledby=\"upload-name-{localId}\"[^>]*>.*?</article>", RegexOptions.Singleline);
        Assert.True(row.Success);
        Assert.Single(Regex.Matches(row.Value, "<button\\b").Cast<Match>());
        Assert.Contains("type=\"button\"", row.Value, StringComparison.Ordinal);
        Assert.Contains("kept.stl", WebUtility.HtmlDecode(row.Value), StringComparison.Ordinal);

        var uploading = Task.CompletedTask;
        try
        {
            if (stage is not InstantQuotationWorkflowUploadStatus.Pending)
            {
                await renderer.Dispatcher.InvokeAsync(() =>
                {
                    uploading = workflow.UploadReservedAsync([localId], default);
                });
                await client.WaitForUploadsAsync(1);
                if (stage is InstantQuotationWorkflowUploadStatus.Uploaded)
                {
                    client.CompleteSuccess("kept.stl", "opaque-kept", Geometry());
                    await uploading;
                    Assert.NotNull(workflow.OrderQuote);
                }
            }
            var quote = workflow.OrderQuote;
            var saved = store.LastSavedState;
            var savedSession = await store.GetAsync("protected-session", null, default);
            var authorization = savedSession!.QuoteAuthorization;
            var revision = workflow.AuthoritativeQuoteRevision;
            var pricingCalls = pricing.QuoteCalls;
            var ticketCalls = tickets.IssueCalls;
            var estimateComplete = workflow.HasCompleteAuthoritativeEstimate;
            var quoteComplete = workflow.HasCompleteAuthoritativeQuote;

            // One fixed existing private handler invocation models the already-rendered Remove click.
            var discard = typeof(InstantQuotationWorkflow).GetMethod(
                "DiscardUploadAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            await renderer.Dispatcher.InvokeAsync(async () =>
                await Assert.IsAssignableFrom<Task>(discard.Invoke(component, [localId])));

            Assert.Empty(client.RemovedReferences);
            Assert.Same(quote, workflow.OrderQuote);
            Assert.Same(saved, store.LastSavedState);
            Assert.Same(authorization, (await store.GetAsync("protected-session", null, default))!.QuoteAuthorization);
            Assert.Equal(revision, workflow.AuthoritativeQuoteRevision);
            Assert.Equal(pricingCalls, pricing.QuoteCalls);
            Assert.Equal(ticketCalls, tickets.IssueCalls);
            Assert.Equal(estimateComplete, workflow.HasCompleteAuthoritativeEstimate);
            Assert.Equal(quoteComplete, workflow.HasCompleteAuthoritativeQuote);
            Assert.Equal(0, javascript.Calls);
            if (stage is InstantQuotationWorkflowUploadStatus.Pending)
            {
                Assert.Empty(workflow.Uploads);
                Assert.Empty(workflow.Parts);
                Assert.Empty(client.UploadOperations);
                Assert.Empty(client.UploadCancellationTokens);
                Assert.Equal(InstantQuotationWorkflowState.Empty, workflow.State);
                await renderer.Dispatcher.InvokeAsync(() => component.SetParametersAsync(ParameterView.Empty));
                Assert.DoesNotContain("upload-name-" + localId,
                    await renderer.Dispatcher.InvokeAsync(root.ToHtmlString), StringComparison.Ordinal);
            }
            else
            {
                var kept = Assert.Single(workflow.Uploads);
                Assert.Equal(localId, kept.LocalId);
                Assert.Equal(stage, kept.Status);
                Assert.False(Assert.Single(client.UploadCancellationTokens).IsCancellationRequested);
                if (stage is InstantQuotationWorkflowUploadStatus.Uploaded)
                {
                    Assert.Equal(localId, Assert.Single(workflow.Parts).PreviewCorrelationId);
                    Assert.NotNull(authorization);
                }
                else
                {
                    Assert.Empty(workflow.Parts);
                }
            }
        }
        finally
        {
            client.CancelPendingUploads();
            await uploading;
        }
    }

    [Fact]
    public async Task DiscardPendingUpload_RemovesLocalSlotAndMakesAbsentReservationsNoOp()
    {
        var client = new ControlledUploadClient();
        var store = new RecordingSessionStore();
        var pricing = new RecordingPricingService();
        var analytics = new RecordingAnalyticsTracker();
        await using var workflow = CreateWorkflow(client: client, store: store, pricing: pricing, analytics: analytics);
        await workflow.InitializeAsync(default);
        var saved = store.LastSavedState;
        var opened = 0;
        var reserved = workflow.ReserveUploads([UploadFile("pending.stl") with
        {
            OpenReadStreamAsync = _ =>
            {
                opened++;
                return Task.FromResult<Stream>(Stream.Null);
            },
        }]);
        var localId = Assert.Single(reserved);

        await workflow.DiscardUploadAsync(localId, default);
        await workflow.DiscardUploadAsync(localId, default);
        await workflow.UploadReservedAsync(reserved, default);
        await workflow.UploadReservedAsync([Guid.NewGuid()], default);

        Assert.Empty(workflow.Uploads);
        Assert.Empty(workflow.Parts);
        Assert.Equal(0, opened);
        Assert.Empty(client.UploadOperations);
        Assert.Empty(client.UploadCancellationTokens);
        Assert.Empty(client.RemovedReferences);
        Assert.Empty(analytics.StageResults);
        Assert.Empty(analytics.UploadFailures);
        Assert.Equal(0, pricing.QuoteCalls);
        Assert.Same(saved, store.LastSavedState);
        Assert.Null(workflow.OrderQuote);
        Assert.Equal(InstantQuotationWorkflowState.Empty, workflow.State);
        Assert.False(workflow.HasCompleteAuthoritativeEstimate);
        Assert.False(workflow.HasCompleteAuthoritativeQuote);
        Assert.Throws<InvalidOperationException>(workflow.EnterReview);
        Assert.Throws<InvalidOperationException>(workflow.EnterCustomerDetails);
    }

    [Fact]
    public async Task ReservedBatch_RemovalBeforeSnapshotStartsOnlyRemainingRowsInOrder()
    {
        var client = new ControlledUploadClient();
        await using var workflow = CreateWorkflow(client: client);
        await workflow.InitializeAsync(default);
        var removedOpened = 0;
        var reserved = workflow.ReserveUploads([
            UploadFile("first.stl"),
            UploadFile("removed.stl") with
            {
                OpenReadStreamAsync = _ =>
                {
                    removedOpened++;
                    return Task.FromResult<Stream>(Stream.Null);
                },
            },
            UploadFile("last.stl"),
        ]);
        await workflow.DiscardUploadAsync(reserved[1], default);

        var uploading = workflow.UploadReservedAsync(reserved, default);
        try
        {
            await client.WaitForUploadsAsync(2);
            Assert.Equal(new[] { reserved[0], reserved[2] }, workflow.Uploads.Select(upload => upload.LocalId));
            Assert.Equal(0, removedOpened);
            Assert.Equal(2, client.UploadOperations.Count);
            client.CompleteSuccess("first.stl", "opaque-first", Geometry());
            client.CompleteSuccess("last.stl", "opaque-last", Geometry());
            await uploading;
            Assert.Equal(new[] { reserved[0], reserved[2] }, workflow.Parts.Select(part => part.PreviewCorrelationId));
            Assert.Empty(client.RemovedReferences);
        }
        finally
        {
            client.CancelPendingUploads();
            await uploading;
        }
    }

    [Fact]
    public async Task ReservedBatch_RemovalAfterSnapshotPreventsCapturedEntryFromStarting()
    {
        var client = new ControlledUploadClient();
        var analytics = new RecordingAnalyticsTracker();
        await using var workflow = CreateWorkflow(client: client, analytics: analytics);
        await workflow.InitializeAsync(default);
        var removedOpened = 0;
        var reserved = workflow.ReserveUploads([
            UploadFile("kept.stl"),
            UploadFile("removed.stl") with
            {
                OpenReadStreamAsync = _ =>
                {
                    removedOpened++;
                    return Task.FromResult<Stream>(Stream.Null);
                },
            },
        ]);
        var callbackEntered = false;
        using var removalCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        analytics.BeforeNextStageResult = () =>
        {
            callbackEntered = true;
            Assert.Equal(InstantQuotationWorkflowUploadStatus.Pending,
                Assert.Single(workflow.Uploads, upload => upload.LocalId == reserved[1]).Status);
            return new ValueTask(workflow.DiscardUploadAsync(reserved[1], removalCancellation.Token));
        };

        var uploading = workflow.UploadReservedAsync(reserved, default);
        try
        {
            await client.WaitForUploadsAsync(1);
            Assert.True(callbackEntered);
            Assert.Equal(reserved[0], Assert.Single(workflow.Uploads).LocalId);
            Assert.Equal(0, removedOpened);
            Assert.Single(client.UploadOperations);
            Assert.Single(client.UploadCancellationTokens);
            client.CompleteSuccess("kept.stl", "opaque-kept", Geometry());
            await uploading;
            Assert.Equal(reserved[0], Assert.Single(workflow.Parts).PreviewCorrelationId);
            Assert.Empty(client.RemovedReferences);
        }
        finally
        {
            analytics.BeforeNextStageResult = null;
            client.CancelPendingUploads();
            await uploading;
        }
    }

    [Fact]
    public async Task ReservedBatch_RejectsDuplicatesNonPendingAndCancelledAdmission()
    {
        var client = new ControlledUploadClient();
        await using var workflow = CreateWorkflow(client: client);
        await workflow.InitializeAsync(default);
        var localId = Assert.Single(workflow.ReserveUploads([UploadFile("kept.stl")]));
        await Assert.ThrowsAsync<ArgumentException>(() => workflow.UploadReservedAsync([localId, localId], default));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => workflow.UploadReservedAsync([localId], cancellation.Token));
        Assert.Equal(InstantQuotationWorkflowUploadStatus.Pending, Assert.Single(workflow.Uploads).Status);
        Assert.Empty(client.UploadOperations);
        Assert.Empty(client.UploadCancellationTokens);

        var uploading = workflow.UploadReservedAsync([localId], default);
        try
        {
            await client.WaitForUploadsAsync(1);
            await Assert.ThrowsAsync<InvalidOperationException>(() => workflow.UploadReservedAsync([localId], default));
            await Assert.ThrowsAsync<InstantQuotationWorkflowCoordinator.DiscardRefusedException>(() => workflow.DiscardUploadAsync(localId, default));
            Assert.False(Assert.Single(client.UploadCancellationTokens).IsCancellationRequested);
            client.CompleteSuccess("kept.stl", "opaque-kept", Geometry());
            await uploading;
            await Assert.ThrowsAsync<InvalidOperationException>(() => workflow.UploadReservedAsync([localId], default));
            Assert.Equal(localId, Assert.Single(workflow.Parts).PreviewCorrelationId);
        }
        finally
        {
            client.CancelPendingUploads();
            await uploading;
        }
    }

    [Fact]
    public async Task DiscardPendingUpload_PreservesRemainingAuthoritativeQuoteAndSavedState()
    {
        var client = new ControlledUploadClient();
        var store = new RecordingSessionStore();
        var pricing = new RecordingPricingService();
        await using var workflow = CreateWorkflow(client: client, store: store, pricing: pricing);
        await workflow.InitializeAsync(default);
        var uploading = workflow.UploadAsync([UploadFile("kept.stl")], default);
        try
        {
            await client.WaitForUploadsAsync(1);
            client.CompleteSuccess("kept.stl", "opaque-kept", Geometry());
            await uploading;
        }
        finally
        {
            client.CancelPendingUploads();
            await uploading;
        }
        var part = Assert.Single(workflow.Parts);
        var quote = workflow.OrderQuote;
        Assert.NotNull(quote);
        var saved = store.LastSavedState;
        var pricingCalls = pricing.QuoteCalls;
        var revision = workflow.AuthoritativeQuoteRevision;
        var estimateComplete = workflow.HasCompleteAuthoritativeEstimate;
        var quoteComplete = workflow.HasCompleteAuthoritativeQuote;
        var pending = Assert.Single(workflow.ReserveUploads([UploadFile("pending.stl")]));

        await workflow.DiscardUploadAsync(pending, default);

        Assert.Equal(part.PartId, Assert.Single(workflow.Parts).PartId);
        Assert.Equal(part.PreviewCorrelationId, Assert.Single(workflow.Uploads).LocalId);
        Assert.Same(quote, workflow.OrderQuote);
        Assert.Same(saved, store.LastSavedState);
        Assert.Equal(pricingCalls, pricing.QuoteCalls);
        Assert.Equal(revision, workflow.AuthoritativeQuoteRevision);
        Assert.Equal(estimateComplete, workflow.HasCompleteAuthoritativeEstimate);
        Assert.Equal(quoteComplete, workflow.HasCompleteAuthoritativeQuote);
        Assert.Empty(client.RemovedReferences);
    }

    [Fact]
    public async Task DiscardLastPendingUpload_PricesTheRemainingAcceptedPart()
    {
        var client = new ControlledUploadClient();
        var store = new RecordingSessionStore();
        var pricing = new RecordingPricingService();
        await using var workflow = CreateWorkflow(client: client, store: store, pricing: pricing);
        await workflow.InitializeAsync(default);
        var reserved = workflow.ReserveUploads([UploadFile("kept.stl"), UploadFile("pending.stl")]);
        var uploading = workflow.UploadReservedAsync([reserved[0]], default);
        try
        {
            await client.WaitForUploadsAsync(1);
            client.CompleteSuccess("kept.stl", "opaque-kept", Geometry());
            await uploading;
        }
        finally
        {
            client.CancelPendingUploads();
            await uploading;
        }
        var part = Assert.Single(workflow.Parts);
        Assert.Null(workflow.OrderQuote);
        Assert.False(workflow.HasCompleteAuthoritativeEstimate);
        var calls = pricing.QuoteCalls;

        await workflow.DiscardUploadAsync(reserved[1], default);

        Assert.Equal(part.PartId, Assert.Single(workflow.Parts).PartId);
        Assert.Equal(reserved[0], Assert.Single(workflow.Uploads).LocalId);
        var quote = Assert.IsType<InstantQuotationOrderQuote>(workflow.OrderQuote);
        Assert.Equal(part.PartId, Assert.Single(quote.Parts).PartId);
        Assert.Equal(part.PartId, Assert.Single(store.LastSavedState!.Parts).PartId);
        Assert.True(workflow.HasCompleteAuthoritativeEstimate);
        Assert.False(workflow.HasCompleteAuthoritativeQuote);
        Assert.Throws<InvalidOperationException>(workflow.EnterReview);
        Assert.Equal(calls + 1, pricing.QuoteCalls);
        Assert.Empty(client.RemovedReferences);
        Assert.Single(client.UploadOperations);
    }

    [Fact]
    public async Task DiscardFailedUpload_RemovesOnlyLocalSlotWithoutPersistingOrGrantingReview()
    {
        var client = new ControlledUploadClient();
        var store = new RecordingSessionStore();
        await using var workflow = CreateWorkflow(client: client, store: store);
        await workflow.InitializeAsync(default);
        var saved = store.LastSavedState;
        var uploading = workflow.UploadAsync([UploadFile("broken.stl")], default);
        await client.WaitForUploadsAsync(1);
        client.CompleteUnavailable("broken.stl");
        await uploading;
        var localId = Assert.Single(workflow.Uploads).LocalId;

        await workflow.DiscardUploadAsync(localId, default);
        await workflow.DiscardUploadAsync(localId, default);

        Assert.Empty(workflow.Uploads);
        Assert.Empty(workflow.Parts);
        Assert.Null(workflow.OrderQuote);
        Assert.Equal(InstantQuotationWorkflowState.Empty, workflow.State);
        Assert.False(workflow.HasCompleteAuthoritativeEstimate);
        Assert.False(workflow.HasCompleteAuthoritativeQuote);
        Assert.Throws<InvalidOperationException>(workflow.EnterReview);
        Assert.Throws<InvalidOperationException>(workflow.EnterCustomerDetails);
        Assert.Same(saved, store.LastSavedState);
        Assert.Empty(client.RemovedReferences);
        Assert.Single(client.UploadOperations);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DiscardCancelledUpload_LateCompletionCannotRestoreSlot(bool cancelledCompletion)
    {
        var client = new ControlledUploadClient(ignoreCancellation: true);
        var store = new RecordingSessionStore();
        var analytics = new RecordingAnalyticsTracker();
        await using var workflow = CreateWorkflow(client: client, store: store, analytics: analytics);
        await workflow.InitializeAsync(default);
        var saved = store.LastSavedState;
        var uploading = workflow.UploadAsync([UploadFile("late.stl")], default);
        await client.WaitForUploadsAsync(1);
        var localId = Assert.Single(workflow.Uploads).LocalId;
        try
        {
            workflow.Cancel(localId);
            await workflow.DiscardUploadAsync(localId, default);
            Assert.True(Assert.Single(client.UploadCancellationTokens).IsCancellationRequested);
            Assert.Empty(workflow.Uploads);
            Assert.False(uploading.IsCompleted);
            if (cancelledCompletion)
                client.CompleteCancelled("late.stl");
            else
                client.CompleteSuccess("late.stl", "opaque-discarded", Geometry());
            await uploading;

            Assert.Empty(workflow.Uploads);
            Assert.Empty(workflow.Parts);
            Assert.Null(workflow.OrderQuote);
            Assert.Equal(InstantQuotationWorkflowState.Empty, workflow.State);
            Assert.False(workflow.HasCompleteAuthoritativeEstimate);
            Assert.False(workflow.HasCompleteAuthoritativeQuote);
            Assert.Same(saved, store.LastSavedState);
            Assert.Empty(analytics.UploadFailures);
            if (cancelledCompletion)
                Assert.Empty(client.RemovedReferences);
            else
                Assert.Equal("opaque-discarded", Assert.Single(client.RemovedReferences));
        }
        finally
        {
            if (!uploading.IsCompleted)
                client.CompleteCancelled("late.stl");
            await uploading;
        }
    }

    [Fact]
    public async Task DiscardUpload_RejectsActiveAndAuthoritativeSlots()
    {
        var client = new ControlledUploadClient();
        await using var workflow = CreateWorkflow(client: client);
        await workflow.InitializeAsync(default);
        var localId = Assert.Single(workflow.ReserveUploads([UploadFile("kept.stl")]));
        Assert.Equal(InstantQuotationWorkflowUploadStatus.Pending, Assert.Single(workflow.Uploads).Status);

        var uploading = workflow.UploadReservedAsync([localId], default);
        await client.WaitForUploadsAsync(1);
        try
        {
            await Assert.ThrowsAsync<InstantQuotationWorkflowCoordinator.DiscardRefusedException>(() => workflow.DiscardUploadAsync(localId, default));
            Assert.Equal(InstantQuotationWorkflowUploadStatus.Uploading, Assert.Single(workflow.Uploads).Status);
            Assert.False(Assert.Single(client.UploadCancellationTokens).IsCancellationRequested);
        }
        finally
        {
            client.CompleteSuccess("kept.stl", "opaque-kept", Geometry());
            await uploading;
        }

        var part = Assert.Single(workflow.Parts);
        var quote = workflow.OrderQuote;
        await Assert.ThrowsAsync<InstantQuotationWorkflowCoordinator.DiscardRefusedException>(() => workflow.DiscardUploadAsync(localId, default));
        Assert.Equal(localId, Assert.Single(workflow.Uploads).LocalId);
        Assert.Equal(part.PartId, Assert.Single(workflow.Parts).PartId);
        Assert.Same(quote, workflow.OrderQuote);
        Assert.Equal(InstantQuotationWorkflowState.Uploaded, workflow.State);
        Assert.Empty(client.RemovedReferences);
    }

    [Fact]
    public async Task DiscardFailedUpload_PreservesRemainingAuthoritativeQuoteAndSavedState()
    {
        var client = new ControlledUploadClient();
        var store = new RecordingSessionStore();
        var pricing = new RecordingPricingService();
        await using var workflow = CreateWorkflow(client: client, store: store, pricing: pricing);
        await workflow.InitializeAsync(default);
        var uploading = workflow.UploadAsync([UploadFile("good.stl"), UploadFile("bad.stl")], default);
        await client.WaitForUploadsAsync(2);
        client.CompleteSuccess("good.stl", "opaque-good", Geometry());
        client.CompleteUnavailable("bad.stl");
        await uploading;
        var part = Assert.Single(workflow.Parts);
        var quote = workflow.OrderQuote;
        Assert.NotNull(quote);
        var saved = store.LastSavedState;
        var pricingCalls = pricing.QuoteCalls;
        var revision = workflow.AuthoritativeQuoteRevision;
        var failed = Assert.Single(workflow.Uploads, upload => upload.Status is InstantQuotationWorkflowUploadStatus.Error);

        await workflow.DiscardUploadAsync(failed.LocalId, default);

        Assert.Equal(part.PartId, Assert.Single(workflow.Parts).PartId);
        Assert.Equal(part.PreviewCorrelationId, Assert.Single(workflow.Uploads).LocalId);
        Assert.Same(quote, workflow.OrderQuote);
        Assert.Same(saved, store.LastSavedState);
        Assert.Equal(pricingCalls, pricing.QuoteCalls);
        Assert.Equal(revision, workflow.AuthoritativeQuoteRevision);
        Assert.Equal(InstantQuotationWorkflowState.Uploaded, workflow.State);
        Assert.Empty(client.RemovedReferences);
    }

    [Fact]
    public async Task DiscardFailedUpload_AlreadyDisposedOwnedSourceDoesNotBlockLocalRemoval()
    {
        var client = new ControlledUploadClient();
        var store = new RecordingSessionStore();
        await using var workflow = CreateWorkflow(client: client, store: store);
        await workflow.InitializeAsync(default);
        var saved = store.LastSavedState;
        var uploading = workflow.UploadAsync([UploadFile("broken.stl")], default);
        await client.WaitForUploadsAsync(1);
        client.CompleteUnavailable("broken.stl");
        await uploading;
        var localId = Assert.Single(workflow.Uploads).LocalId;

        // Cut only the competing retry's disposal race; slot state comes from the public upload flow.
        var entriesField = typeof(InstantQuotationWorkflowCoordinator).GetField(
            "entries", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var entry = Assert.Single(Assert.IsAssignableFrom<System.Collections.IEnumerable>(
            entriesField.GetValue(workflow)).Cast<object>());
        var sourceProperty = entry.GetType().GetProperty("OperationCancellation")!;
        var ownedSource = Assert.IsAssignableFrom<CancellationTokenSource>(sourceProperty.GetValue(entry));
        ownedSource.Dispose();

        await workflow.DiscardUploadAsync(localId, default);

        Assert.Empty(workflow.Uploads);
        Assert.Empty(workflow.Parts);
        Assert.Null(workflow.OrderQuote);
        Assert.Equal(InstantQuotationWorkflowState.Empty, workflow.State);
        Assert.False(workflow.HasCompleteAuthoritativeEstimate);
        Assert.False(workflow.HasCompleteAuthoritativeQuote);
        Assert.Throws<InvalidOperationException>(workflow.EnterReview);
        Assert.Same(saved, store.LastSavedState);
        Assert.Empty(client.RemovedReferences);
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
    public async Task AboveHundredMegabytes_IsRejectedBeforeUploadBoundary()
    {
        var client = new ControlledUploadClient();
        await using var workflow = CreateWorkflow(client: client);
        await workflow.InitializeAsync(default);

        await workflow.UploadAsync(
            [UploadFile("oversized.stl") with { Length = InstantQuotationWorkflowCoordinator.MaximumFileSize + 1 }],
            default);

        Assert.Equal(100L * 1024 * 1024, InstantQuotationWorkflowCoordinator.MaximumFileSize);
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
        var analytics = new RecordingAnalyticsTracker();
        await using var workflow = CreateWorkflow(client: client, analytics: analytics);
        await workflow.InitializeAsync(default);

        var uploading = workflow.UploadAsync([UploadFile("part.stl")], default);
        await client.WaitForUploadsAsync(1);
        client.CompleteSuccess("part.stl", "opaque-mismatch", Geometry(), operationId: "wrong-operation");
        await uploading;

        Assert.Equal("opaque-mismatch", Assert.Single(client.RemovedReferences));
        Assert.Empty(workflow.Parts);
        Assert.Equal(InstantQuotationWorkflowUploadStatus.Error, Assert.Single(workflow.Uploads).Status);
        Assert.Empty(analytics.UploadFailures);
    }

    [Fact]
    public async Task CancelThenLateSuccess_DoesNotMutateState_AndRetryUsesFreshOperationId()
    {
        var client = new ControlledUploadClient(ignoreCancellation: true);
        var analytics = new RecordingAnalyticsTracker();
        await using var workflow = CreateWorkflow(client: client, analytics: analytics);
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
        Assert.Empty(analytics.UploadFailures);
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
        var analytics = new RecordingAnalyticsTracker();
        client.RemoveResults.Enqueue(InstantQuotationProblemCategory.Conflict);
        client.RemoveResults.Enqueue(InstantQuotationProblemCategory.None);
        await using var workflow = CreateWorkflow(client: client, analytics: analytics);
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
        Assert.Empty(analytics.UploadFailures);
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
    public async Task RejectedQuantityRepriceNeverShowsUnverifiedBulkSavings()
    {
        var persisted = PersistedPart("part.stl", "opaque") with
        {
            Configuration = new InstantQuotationPartConfiguration("PLA", "Black", 10),
        };
        var store = new RecordingSessionStore
        {
            ExistingSession = Session("protected-resume", persisted),
        };
        await using var workflow = CreateWorkflow(store: store);
        await workflow.InitializeAsync("protected-resume", default);
        var part = Assert.Single(workflow.Parts);
        var edits = new QuantityEditState();
        Assert.Null(InstantQuotationBulkSavings.Calculate(part, false));

        edits.Begin(part.PartId);
        store.RejectNextPut = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => edits.RepriceAsync(part.PartId,
            () => workflow.UpdateConfigurationAsync(part.PartId, "PLA", "Black", 10, default)));

        var afterFailure = Assert.Single(workflow.Parts);
        Assert.Equal(10, afterFailure.Configuration.Quantity);
        Assert.Null(afterFailure.Quote);
        Assert.True(edits.IsPending(part.PartId));
        Assert.Null(InstantQuotationBulkSavings.Calculate(afterFailure, edits.IsPending(part.PartId)));

        await edits.RepriceAsync(part.PartId,
            () => workflow.UpdateConfigurationAsync(part.PartId, "PLA", "Black", 10, default));

        Assert.False(edits.IsPending(part.PartId));
        Assert.Equal(10, Assert.Single(workflow.Parts).Quote!.Quantity);
        Assert.Null(InstantQuotationBulkSavings.Calculate(Assert.Single(workflow.Parts), false));
    }

    [Fact]
    public async Task ConfigurationChange_AcceptsTenThousandAndRejectsQuantityAboveMaximum()
    {
        var client = new ControlledUploadClient();
        await using var workflow = CreateWorkflow(client: client);
        await workflow.InitializeAsync(default);
        var uploading = workflow.UploadAsync([UploadFile("part.stl")], default);
        await client.WaitForUploadsAsync(1);
        client.CompleteSuccess("part.stl", "opaque", Geometry());
        await uploading;
        var part = Assert.Single(workflow.Parts);

        await workflow.UpdateConfigurationAsync(part.PartId, "PLA", "Black", 10_000, default);

        Assert.Equal(10_000, Assert.Single(workflow.Parts).Configuration.Quantity);
        Assert.Equal(10_000, Assert.Single(workflow.OrderQuote!.Parts).Quantity);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            workflow.UpdateConfigurationAsync(part.PartId, "PLA", "Black", 10_001, default));
        Assert.Equal(10_000, Assert.Single(workflow.Parts).Configuration.Quantity);
    }

    [Fact]
    public async Task BuildPreferenceChange_PersistsOnlySelectedPartAndRecomputesAuthoritativeQuote()
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

        await workflow.UpdateConfigurationAsync(
            part.PartId,
            part.Configuration.MaterialKey,
            part.Configuration.Color,
            part.Configuration.Quantity,
            BuildPreference.Quality,
            default);

        var updated = Assert.Single(workflow.Parts);
        Assert.Equal(BuildPreference.Quality, updated.Configuration.BuildPreference);
        Assert.Equal("PLA", updated.Configuration.MaterialKey);
        Assert.Equal("Black", updated.Configuration.Color);
        Assert.Equal(1, updated.Configuration.Quantity);
        Assert.Equal(2, pricing.QuoteCalls);
        Assert.Equal(BuildPreference.Quality, Assert.Single(store.LastSavedState!.Parts).Configuration.BuildPreference);
    }

    [Theory]
    [InlineData(BuildPreference.Quality)]
    [InlineData(BuildPreference.Strength)]
    public async Task ResinConfiguration_CanonicalizesPersistedPreferenceBeforePricingAndMaterialTransitions(BuildPreference preference)
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
        var id = Assert.Single(workflow.Parts).PartId;

        await workflow.UpdateConfigurationAsync(id, "PLA", "Black", 1, preference, default);
        Assert.Equal(preference, Assert.Single(workflow.Parts).Configuration.BuildPreference);
        await workflow.UpdateConfigurationAsync(id, "M68", "Gray", 1, default);
        Assert.Equal(BuildPreference.Standard, Assert.Single(workflow.Parts).Configuration.BuildPreference);
        Assert.Equal(BuildPreference.Standard, Assert.Single(store.LastSavedState!.Parts).Configuration.BuildPreference);
        await workflow.UpdateConfigurationAsync(id, "M68", "Gray", 1, preference, default);
        Assert.Equal(BuildPreference.Standard, Assert.Single(workflow.Parts).Configuration.BuildPreference);
        await workflow.UpdateConfigurationAsync(id, "PLA", "Black", 1, default);
        Assert.Equal(BuildPreference.Standard, Assert.Single(workflow.Parts).Configuration.BuildPreference);
        await workflow.UpdateConfigurationAsync(id, "PLA", "Black", 1, preference, default);
        Assert.Equal(preference, Assert.Single(store.LastSavedState!.Parts).Configuration.BuildPreference);
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

    [Fact]
    public async Task TerminalUploadFailures_RecordExactLogicalOperationAndRetrySeparately()
    {
        var client = new ControlledUploadClient();
        var analytics = new RecordingAnalyticsTracker();
        await using var workflow = CreateWorkflow(client: client, analytics: analytics);
        await workflow.InitializeAsync(default);

        var firstAttempt = workflow.UploadAsync([UploadFile("part.stl")], default);
        await client.WaitForUploadsAsync(1);
        client.CompleteUnavailable("part.stl");
        await firstAttempt;
        var upload = Assert.Single(workflow.Uploads);

        var retry = workflow.RetryAsync(upload.LocalId, default);
        await client.WaitForUploadsAsync(2);
        client.CompleteUnavailable("part.stl");
        await retry;

        Assert.Equal(2, analytics.UploadFailures.Count);
        Assert.NotEqual(client.UploadOperations[0], client.UploadOperations[1]);
        Assert.Equal(2, analytics.UploadFailures.Select(item => item.OperationId).Distinct().Count());
        Assert.DoesNotContain(
            analytics.UploadFailures,
            item => client.UploadOperations.Contains(item.OperationId, StringComparer.Ordinal));
        Assert.All(analytics.UploadFailures, item =>
        {
            Assert.Equal(InstantQuotationProblemCategory.DependencyUnavailable, item.Category);
            Assert.Equal(1, item.FileCount);
        });
    }

    [Fact]
    public async Task RetryIdenticalTerminalFailures_RecordEachLogicalAttemptOnce()
    {
        var client = new ControlledUploadClient();
        var analytics = new RecordingAnalyticsTracker();
        await using var workflow = CreateWorkflow(client: client, analytics: analytics);
        await workflow.InitializeAsync(default);

        var firstAttempt = workflow.UploadAsync([UploadFile("part.stl")], default);
        await client.WaitForUploadsAsync(1);
        client.CompleteUnavailable(
            "part.stl",
            InstantQuotationUploadRetryDisposition.RetryIdentical);
        await firstAttempt;
        var upload = Assert.Single(workflow.Uploads);

        var retry = workflow.RetryAsync(upload.LocalId, default);
        await client.WaitForUploadsAsync(2);
        client.CompleteUnavailable(
            "part.stl",
            InstantQuotationUploadRetryDisposition.RetryIdentical);
        await retry;

        Assert.Equal(client.UploadOperations[0], client.UploadOperations[1]);
        Assert.Equal(2, analytics.UploadFailures.Count);
        Assert.Equal(2, analytics.UploadFailures.Select(item => item.OperationId).Distinct().Count());
        Assert.All(
            analytics.UploadFailures,
            item => Assert.Equal(InstantQuotationProblemCategory.DependencyUnavailable, item.Category));
    }

    [Fact]
    public async Task RetryIdenticalFailure_ReusesOriginalUploadOperationIdAndContent()
    {
        var client = new ControlledUploadClient();
        await using var workflow = CreateWorkflow(client: client);
        await workflow.InitializeAsync(default);

        var firstAttempt = workflow.UploadAsync([UploadFile("part.stl")], default);
        await client.WaitForUploadsAsync(1);
        client.CompleteUnavailable(
            "part.stl",
            InstantQuotationUploadRetryDisposition.RetryIdentical);
        await firstAttempt;
        var upload = Assert.Single(workflow.Uploads);

        var retry = workflow.RetryAsync(upload.LocalId, default);
        await client.WaitForUploadsAsync(2);
        client.CompleteSuccess("part.stl", "opaque-part", Geometry());
        await retry;

        Assert.Equal(client.UploadOperations[0], client.UploadOperations[1]);
        Assert.Single(workflow.Parts);
    }

    [Fact]
    public async Task CallerCancellation_RetryReusesOriginalUploadOperationId()
    {
        var client = new ControlledUploadClient();
        await using var workflow = CreateWorkflow(client: client);
        await workflow.InitializeAsync(default);
        using var cancellation = new CancellationTokenSource();

        var uploading = workflow.UploadAsync([UploadFile("part.stl")], cancellation.Token);
        await client.WaitForUploadsAsync(1);
        cancellation.Cancel();
        await uploading;
        var upload = Assert.Single(workflow.Uploads);
        Assert.Equal(InstantQuotationWorkflowUploadStatus.Cancelled, upload.Status);

        var retry = workflow.RetryAsync(upload.LocalId, default);
        await client.WaitForUploadsAsync(2);
        client.CompleteSuccess("part.stl", "opaque-part", Geometry());
        await retry;

        Assert.Equal(client.UploadOperations[0], client.UploadOperations[1]);
        Assert.Single(workflow.Parts);
    }

    [Fact]
    public async Task CancelAndOverlappingIdenticalRetry_DoesNotDeletePromotedFileReference()
    {
        var client = new ControlledUploadClient(ignoreCancellation: true);
        await using var workflow = CreateWorkflow(client: client);
        await workflow.InitializeAsync(default);

        var reserved = Assert.Single(workflow.ReserveUploads([UploadFile("part.stl")]));
        var original = workflow.UploadReservedAsync([reserved], default);
        await client.WaitForUploadsAsync(1);
        workflow.Cancel(reserved);
        var retry = workflow.RetryAsync(reserved, default);
        await client.WaitForUploadsAsync(2);

        client.CompleteSuccess("part.stl", "opaque-shared", Geometry());
        await original;
        client.CompleteSuccess("part.stl", "opaque-shared", Geometry());
        await retry;

        Assert.Equal(client.UploadOperations[0], client.UploadOperations[1]);
        Assert.Single(workflow.Parts);
        Assert.DoesNotContain("opaque-shared", client.RemovedReferences);
    }

    [Theory]
    [InlineData((int)InstantQuotationUploadRetryDisposition.RetryIdentical)]
    [InlineData((int)InstantQuotationUploadRetryDisposition.RetryWithBackoff)]
    public async Task OverlappingRetryFailureThenOriginalSuccess_PromotesReconciledFile(
        int retryFailureDisposition)
    {
        var client = new ControlledUploadClient(ignoreCancellation: true);
        await using var workflow = CreateWorkflow(client: client);
        await workflow.InitializeAsync(default);

        var reserved = Assert.Single(workflow.ReserveUploads([UploadFile("part.stl")]));
        var original = workflow.UploadReservedAsync([reserved], default);
        await client.WaitForUploadsAsync(1);
        workflow.Cancel(reserved);
        var retry = workflow.RetryAsync(reserved, default);
        await client.WaitForUploadsAsync(2);

        client.CompleteLatestUnavailable(
            "part.stl",
            (InstantQuotationUploadRetryDisposition)retryFailureDisposition);
        await retry;
        client.CompleteSuccess("part.stl", "opaque-reconciled", Geometry());
        await original;

        Assert.Equal(client.UploadOperations[0], client.UploadOperations[1]);
        Assert.Single(workflow.Parts);
        Assert.DoesNotContain("opaque-reconciled", client.RemovedReferences);
    }

    [Fact]
    public async Task ValidationFailureRecordsOnceButCancellationRecordsNothing()
    {
        var analytics = new RecordingAnalyticsTracker();
        var client = new ControlledUploadClient(ignoreCancellation: true);
        await using var workflow = CreateWorkflow(client: client, analytics: analytics);
        await workflow.InitializeAsync(default);

        await workflow.UploadAsync(
            [new InstantQuotationWorkflowUploadFile("empty.stl", "application/octet-stream", 0, Geometry(), _ => Task.FromResult<Stream>(Stream.Null))],
            default);
        var reserved = workflow.ReserveUploads([UploadFile("cancelled.stl")]);
        var uploading = workflow.UploadReservedAsync(reserved, default);
        await client.WaitForUploadsAsync(1);
        workflow.Cancel(Assert.Single(reserved));
        client.CompleteSuccess("cancelled.stl", "opaque-cancelled", Geometry());
        await uploading;

        var failure = Assert.Single(analytics.UploadFailures);
        Assert.Equal(InstantQuotationProblemCategory.Validation, failure.Category);
        Assert.Equal(1, failure.FileCount);
    }

    [Fact]
    public async Task AuthoritativeQuoteRevisionAdvancesOnlyWhenCurrentServerQuoteIsStored()
    {
        var client = new ControlledUploadClient();
        await using var workflow = CreateWorkflow(client: client);
        await workflow.InitializeAsync(default);

        Assert.Equal(0, workflow.AuthoritativeQuoteRevision);
        Assert.False(workflow.HasCompleteAuthoritativeEstimate);
        Assert.False(workflow.HasCompleteAuthoritativeQuote);

        var uploading = workflow.UploadAsync([UploadFile("part.stl")], default);
        await client.WaitForUploadsAsync(1);
        client.CompleteSuccess("part.stl", "opaque", Geometry());
        await uploading;

        Assert.Equal(1, workflow.AuthoritativeQuoteRevision);
        Assert.True(workflow.HasCompleteAuthoritativeEstimate);
        Assert.False(workflow.HasCompleteAuthoritativeQuote);

        var part = Assert.Single(workflow.Parts);
        await workflow.UpdateConfigurationAsync(part.PartId, "PLA", "Black", 2, default);

        Assert.Equal(2, workflow.AuthoritativeQuoteRevision);
        Assert.True(workflow.HasCompleteAuthoritativeEstimate);
        Assert.True(workflow.HasCompleteAuthoritativeQuote);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UploadStart_HeldSinkCannotResumeAnObsoleteCanceledAttempt(bool retryIdentical)
    {
        var sink = new HeldUploadStartSink();
        var client = new ControlledUploadClient(ignoreCancellation: true);
        await using var workflow = CreateWorkflow(client: client, analytics: new InstantQuotationAnalyticsTracker(sink));
        await workflow.InitializeAsync(default);
        using var lease = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var original = workflow.UploadAsync([UploadFile("held-start.stl")], default);
        Task? retry = null;
        Guid? localId = null;
        Exception? primary = null;
        try
        {
            await sink.Entered.Task.WaitAsync(lease.Token);
            localId = Assert.Single(workflow.Uploads).LocalId;
            Assert.Empty(client.UploadOperations);
            workflow.Cancel(localId.Value);
            if (retryIdentical)
            {
                retry = workflow.RetryAsync(localId.Value, default);
                await client.WaitForUploadsAsync(1).WaitAsync(lease.Token);
                Assert.Equal(InstantQuotationWorkflowUploadStatus.Uploading, Assert.Single(workflow.Uploads).Status);
                Assert.False(Assert.Single(client.UploadCancellationTokens).IsCancellationRequested);
            }
            sink.Release.TrySetResult();
            await original.WaitAsync(lease.Token);
            if (retryIdentical)
            {
                Assert.Single(client.UploadOperations);
                Assert.Equal(InstantQuotationWorkflowUploadStatus.Uploading, Assert.Single(workflow.Uploads).Status);
                Assert.False(Assert.Single(client.UploadCancellationTokens).IsCancellationRequested);
                Assert.Empty(workflow.Parts);
                client.CompleteSuccess("held-start.stl", "opaque-current", Geometry());
                await retry!.WaitAsync(lease.Token);
                Assert.Single(workflow.Parts);
            }
            else
            {
                Assert.Empty(client.UploadOperations);
                Assert.Equal(InstantQuotationWorkflowUploadStatus.Cancelled, Assert.Single(workflow.Uploads).Status);
                Assert.Empty(workflow.Parts);
            }
            Assert.Equal(1, sink.Starts);
        }
        catch (Exception exception) { primary = exception; throw; }
        finally
        {
            await MaterialPricingTestCleanup.ReleaseAllAsync(primary,
                () =>
                {
                    sink.Release.TrySetResult();
                    if (localId is { } id) workflow.Cancel(id);
                    if (retry is { IsCompleted: false } && client.UploadOperations.Count > 0)
                        client.CompleteLatestUnavailable("held-start.stl", InstantQuotationUploadRetryDisposition.None);
                    return ValueTask.CompletedTask;
                },
                async () => await MaterialPricingTestCleanup.DrainQuoteAsync(
                    original, () => lease.Cancel(), () => sink.Release.TrySetResult(), lease.Token, null,
                    receipt => Assert.True(receipt.ActualTaskSettled), TimeSpan.FromSeconds(5),
                    expectedTerminalFailure: false, taskKind: "obsolete-upload-start"),
                async () =>
                {
                    if (retry is null) return;
                    await MaterialPricingTestCleanup.DrainQuoteAsync(
                        retry, () => lease.Cancel(), () => sink.Release.TrySetResult(), lease.Token, null,
                        receipt => Assert.True(receipt.ActualTaskSettled), TimeSpan.FromSeconds(5),
                        expectedTerminalFailure: false, taskKind: "current-upload-start");
                });
        }
    }

    private sealed class HeldUploadStartSink : IInstantQuotationAnalyticsSink
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Starts { get; private set; }
        public async ValueTask EmitAsync(InstantQuotationAnalyticsPayload payload, CancellationToken cancellationToken = default)
        {
            if (payload is not InstantQuotationUploadStartPayload) return;
            Starts++;
            Entered.TrySetResult();
            await Release.Task;
        }
    }


    [Theory]
    [InlineData(9)]
    [InlineData(10)]
    public async Task ReservedBatch_AdmitsEveryVerifiedPartBeforeHeldFirstComparisonQuote(int count)
    {
        var client = new ControlledUploadClient();
        var pricing = new HeldAdmissionPricingService();
        var store = new RecordingSessionStore();
        var persisted = Enumerable.Range(0, count).Select(_ => new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
        store.OnPersist = state => { if (state.Parts.Count > 0) persisted[state.Parts.Count - 1].TrySetResult(); };
        await using var workflow = CreateWorkflow(client, store, pricing);
        await workflow.InitializeAsync(default);
        using var lease = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var files = Enumerable.Range(1, count).Select(index => UploadFile($"part-{index}.stl")).ToArray();
        var uploading = workflow.UploadAsync(files, lease.Token);
        try
        {
            await client.WaitForUploadsAsync(count);
            for (var index = 0; index < count; index++)
            {
                client.CompleteSuccess(files[index].FileName, $"opaque-{index}", Geometry());
                await persisted[index].Task.WaitAsync(lease.Token);
                Assert.Equal(index + 1, workflow.Parts.Count);
                Assert.Null(workflow.OrderQuote);
                if (index + 1 < count) Assert.Empty(pricing.Requests);
            }
            await pricing.Entered.Task.WaitAsync(lease.Token);
            Assert.Equal(count, Assert.Single(pricing.Requests).Parts.Count);
            Assert.False(uploading.IsCompleted);
            Assert.All(workflow.Uploads, upload => Assert.Equal(InstantQuotationWorkflowUploadStatus.Uploaded, upload.Status));
            pricing.Release.TrySetResult();
            await uploading.WaitAsync(lease.Token);
            Assert.True(workflow.HasCompleteAuthoritativeEstimate);
            Assert.Equal(1, workflow.AuthoritativeQuoteRevision);
        }
        finally
        {
            pricing.Release.TrySetResult();
            await workflow.DisposeAsync();
            await uploading.WaitAsync(TimeSpan.FromSeconds(5));
            if (pricing.Entered.Task.IsCompletedSuccessfully)
                await pricing.HeldCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReservedBatch_LastFailedOrCancelledAttemptFlushesAcceptedPart(bool cancelLast)
    {
        var client = new ControlledUploadClient();
        var pricing = new HeldAdmissionPricingService();
        var accepted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new RecordingSessionStore { OnPersist = state => { if (state.Parts.Count == 1) accepted.TrySetResult(); } };
        await using var workflow = CreateWorkflow(client, store, pricing);
        await workflow.InitializeAsync(default);
        using var lease = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var uploading = workflow.UploadAsync([UploadFile("accepted.stl"), UploadFile("terminal.stl")], lease.Token);
        try
        {
            await client.WaitForUploadsAsync(2);
            client.CompleteSuccess("accepted.stl", "opaque-accepted", Geometry());
            await accepted.Task.WaitAsync(lease.Token);
            Assert.Empty(pricing.Requests);
            if (cancelLast) workflow.Cancel(workflow.Uploads[1].LocalId);
            else client.CompleteUnavailable("terminal.stl");
            await pricing.Entered.Task.WaitAsync(lease.Token);
            Assert.Equal("accepted.stl", Assert.Single(Assert.Single(pricing.Requests).Parts).DisplayFileName);
            pricing.Release.TrySetResult();
            await uploading.WaitAsync(lease.Token);
            Assert.Single(workflow.Parts);
            Assert.True(workflow.HasCompleteAuthoritativeEstimate);
            Assert.Equal(cancelLast ? InstantQuotationWorkflowUploadStatus.Cancelled : InstantQuotationWorkflowUploadStatus.Error,
                workflow.Uploads[1].Status);
        }
        finally
        {
            pricing.Release.TrySetResult();
            await workflow.DisposeAsync();
            await uploading.WaitAsync(TimeSpan.FromSeconds(5));
            if (pricing.Entered.Task.IsCompletedSuccessfully)
                await pricing.HeldCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task FutureUnreservedClaims_DoNotDelayFirstReadyEstimate()
    {
        var client = new ControlledUploadClient();
        var pricing = new RecordingPricingService();
        await using var workflow = CreateWorkflow(client: client, pricing: pricing);
        await workflow.InitializeAsync(default);
        var first = workflow.UploadAsync([UploadFile("ready.stl")], default);
        await client.WaitForUploadsAsync(1);
        client.CompleteSuccess("ready.stl", "opaque-ready", Geometry());
        await first.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(workflow.HasCompleteAuthoritativeEstimate);
        Assert.Single(workflow.Uploads);
        Assert.Equal(1, pricing.QuoteCalls);
        // The five parser callbacks have not reserved upload entries yet.
    }


    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReservedBatch_ConfigurationAndRemovalCannotBlockRemainingAdmission(bool removeAccepted)
    {
        var client = new ControlledUploadClient();
        var pricing = new HeldAdmissionPricingService();
        var admitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new RecordingSessionStore { OnPersist = state => { if (state.Parts.Count == 1) admitted.TrySetResult(); } };
        var analytics = new RecordingAnalyticsTracker();
        await using var workflow = CreateWorkflow(client, store, pricing, analytics: analytics);
        await workflow.InitializeAsync(default);
        using var lease = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var uploading = workflow.UploadAsync([UploadFile("first.stl"), UploadFile("second.stl")], lease.Token);
        try
        {
            await client.WaitForUploadsAsync(2);
            client.CompleteSuccess("first.stl", "opaque-first", Geometry());
            await admitted.Task.WaitAsync(lease.Token);
            var part = Assert.Single(workflow.Parts);
            if (removeAccepted) await workflow.RemoveAsync(part.PartId, lease.Token);
            else await workflow.UpdateConfigurationAsync(part.PartId, "PLA", "Black", 7, lease.Token);
            Assert.Empty(pricing.Requests);
            client.CompleteSuccess("second.stl", "opaque-second", Geometry());
            await pricing.Entered.Task.WaitAsync(lease.Token);
            var requested = Assert.Single(pricing.Requests);
            Assert.Equal(removeAccepted ? 1 : 2, requested.Parts.Count);
            if (!removeAccepted) Assert.Equal(7, requested.Parts.Single(item => item.PartId == part.PartId).Configuration.Quantity);
            else Assert.DoesNotContain(requested.Parts, item => item.PartId == part.PartId);
            pricing.Release.TrySetResult();
            await uploading.WaitAsync(lease.Token);
            Assert.True(workflow.HasCompleteAuthoritativeEstimate);
            Assert.Equal(removeAccepted ? 1 : 2, analytics.StageResults.Count(stage => stage.Stage is InstantQuotationStage.Pricing));
            Assert.Equal(removeAccepted ? 1 : 2, analytics.StageResults.Count(stage => stage.Stage is InstantQuotationStage.OrderTotal));
        }
        finally
        {
            pricing.Release.TrySetResult();
            await workflow.DisposeAsync();
            await uploading.WaitAsync(TimeSpan.FromSeconds(5));
            if (pricing.Entered.Task.IsCompletedSuccessfully)
                await pricing.HeldCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NewReservation_RetiresHeldPriorUploadQuoteWithoutWaitingForItsDependency(bool failRetiredQuote)
    {
        var client = new ControlledUploadClient();
        var pricing = new HeldAdmissionPricingService(failHeldQuote: failRetiredQuote);
        var analytics = new RecordingAnalyticsTracker();
        await using var workflow = CreateWorkflow(client: client, pricing: pricing, analytics: analytics);
        await workflow.InitializeAsync(default);
        using var lease = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var first = workflow.UploadAsync([UploadFile("first.stl")], lease.Token);
        Task? second = null;
        try
        {
            await client.WaitForUploadsAsync(1);
            client.CompleteSuccess("first.stl", "opaque-first", Geometry());
            await pricing.Entered.Task.WaitAsync(lease.Token);
            second = workflow.UploadAsync([UploadFile("second.stl")], lease.Token);
            await client.WaitForUploadsAsync(2);
            client.CompleteSuccess("second.stl", "opaque-second", Geometry());
            await second.WaitAsync(lease.Token);
            Assert.Equal(2, workflow.Parts.Count);
            Assert.True(workflow.HasCompleteAuthoritativeEstimate);
            Assert.Equal(2, workflow.OrderQuote!.Parts.Count);
            Assert.Equal(1, workflow.AuthoritativeQuoteRevision);
            pricing.Release.TrySetResult();
            await first.WaitAsync(lease.Token);
            Assert.Equal(2, workflow.OrderQuote.Parts.Count);
            Assert.Equal(1, workflow.AuthoritativeQuoteRevision);
            Assert.All(workflow.Uploads, upload => Assert.Equal(InstantQuotationWorkflowUploadStatus.Uploaded, upload.Status));
            Assert.Empty(analytics.UploadFailures);
        }
        finally
        {
            pricing.Release.TrySetResult();
            await workflow.DisposeAsync();
            await first.WaitAsync(TimeSpan.FromSeconds(5));
            if (second is not null) await second.WaitAsync(TimeSpan.FromSeconds(5));
            if (pricing.Entered.Task.IsCompletedSuccessfully)
                await pricing.HeldCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task Dispose_BatchWithPersistedAdmissionDoesNotFlushOrPublishAnEstimate()
    {
        var client = new ControlledUploadClient();
        var pricing = new HeldAdmissionPricingService();
        var admitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new RecordingSessionStore { OnPersist = state => { if (state.Parts.Count == 1) admitted.TrySetResult(); } };
        await using var workflow = CreateWorkflow(client, store, pricing);
        await workflow.InitializeAsync(default);
        using var lease = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var uploading = workflow.UploadAsync([UploadFile("first.stl"), UploadFile("second.stl")], lease.Token);
        try
        {
            await client.WaitForUploadsAsync(2);
            client.CompleteSuccess("first.stl", "opaque-first", Geometry());
            await admitted.Task.WaitAsync(lease.Token);
            await workflow.DisposeAsync();
            await uploading.WaitAsync(lease.Token);
            Assert.Empty(pricing.Requests);
            Assert.Null(workflow.OrderQuote);
            Assert.Equal(0, workflow.AuthoritativeQuoteRevision);
        }
        finally
        {
            pricing.Release.TrySetResult();
            await workflow.DisposeAsync();
            await uploading.WaitAsync(TimeSpan.FromSeconds(5));
            if (pricing.Entered.Task.IsCompletedSuccessfully)
                await pricing.HeldCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }


    [Fact]
    public async Task NewReservation_RetiresHeldConfigurationQuoteAndKeepsItsPersistedIntent()
    {
        var client = new ControlledUploadClient();
        var pricing = new HeldAdmissionPricingService(heldCall: 2);
        await using var workflow = CreateWorkflow(client: client, pricing: pricing);
        await workflow.InitializeAsync(default);
        using var lease = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var first = workflow.UploadAsync([UploadFile("first.stl")], lease.Token);
        await client.WaitForUploadsAsync(1);
        client.CompleteSuccess("first.stl", "opaque-first", Geometry());
        await first.WaitAsync(lease.Token);
        var firstPart = Assert.Single(workflow.Parts);
        var configuring = workflow.UpdateConfigurationAsync(firstPart.PartId, "PLA", "Black", 7, lease.Token);
        Task? second = null;
        try
        {
            await pricing.Entered.Task.WaitAsync(lease.Token);
            second = workflow.UploadAsync([UploadFile("second.stl")], lease.Token);
            await client.WaitForUploadsAsync(2);
            client.CompleteSuccess("second.stl", "opaque-second", Geometry());
            await second.WaitAsync(lease.Token);
            await configuring.WaitAsync(lease.Token);
            Assert.Equal(2, workflow.OrderQuote!.Parts.Count);
            Assert.Equal(7, workflow.Parts.Single(part => part.PartId == firstPart.PartId).Configuration.Quantity);
            Assert.Equal(2, workflow.AuthoritativeQuoteRevision);
            pricing.Release.TrySetResult();
            await pricing.HeldCompleted.Task.WaitAsync(lease.Token);
            Assert.Equal(2, workflow.AuthoritativeQuoteRevision);
        }
        finally
        {
            pricing.Release.TrySetResult();
            await workflow.DisposeAsync();
            await configuring.WaitAsync(TimeSpan.FromSeconds(5));
            if (second is not null) await second.WaitAsync(TimeSpan.FromSeconds(5));
            if (pricing.Entered.Task.IsCompletedSuccessfully)
                await pricing.HeldCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task FinalBatchQuoteFailure_IsRecoverableAndNeverRecordsPricingSuccess(bool rejectFinalPut, bool cancelHeldQuote)
    {
        var client = new ControlledUploadClient();
        var pricing = new HeldAdmissionPricingService(failHeldQuote: !rejectFinalPut, cancelHeldQuote: cancelHeldQuote);
        var store = new RecordingSessionStore();
        var analytics = new RecordingAnalyticsTracker();
        await using var workflow = CreateWorkflow(client, store, pricing, analytics: analytics);
        await workflow.InitializeAsync(default);
        using var lease = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var admitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        store.OnPersist = state =>
        {
            if (state.Parts.Count != 2) return;
            if (rejectFinalPut) store.RejectNextPut = true;
            admitted.TrySetResult();
        };
        var uploading = workflow.UploadAsync([UploadFile("first.stl"), UploadFile("second.stl")], lease.Token);
        try
        {
            await client.WaitForUploadsAsync(2);
            client.CompleteSuccess("first.stl", "opaque-first", Geometry());
            client.CompleteSuccess("second.stl", "opaque-second", Geometry());
            await admitted.Task.WaitAsync(lease.Token);
            if (!rejectFinalPut) await pricing.Entered.Task.WaitAsync(lease.Token);
            pricing.Release.TrySetResult();
            await uploading.WaitAsync(lease.Token);
            Assert.Equal(2, workflow.Parts.Count);
            Assert.Null(workflow.OrderQuote);
            Assert.Equal(InstantQuotationWorkflowState.Error, workflow.State);
            Assert.Equal(InstantQuotationProblemCategory.Unexpected, Assert.Single(analytics.UploadFailures).Category);
            Assert.DoesNotContain(analytics.StageResults, stage => stage.Stage is InstantQuotationStage.Pricing or InstantQuotationStage.OrderTotal);
        }
        finally
        {
            pricing.Release.TrySetResult();
            await workflow.DisposeAsync();
            await uploading.WaitAsync(TimeSpan.FromSeconds(5));
            if (pricing.Entered.Task.IsCompletedSuccessfully)
                await pricing.HeldCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    private sealed class HeldAdmissionPricingService(int heldCall = 1, bool failHeldQuote = false, bool cancelHeldQuote = false) : IInstantQuotationPricingService, IInstantQuotationAuthoritativePricingService
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource HeldCompleted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal List<InstantQuotationOrderState> Requests { get; } = [];
        public InstantQuotationOrderQuote Quote(InstantQuotationOrderState state) => SyntheticPhysicalPricingTestData.Quote(state);
        public async Task<InstantQuotationOrderQuote?> QuoteAsync(InstantQuotationSessionState session,
            string? ownerIdentity, bool includeComparisons, CancellationToken cancellationToken)
        {
            Requests.Add(session.RequestState);
            Assert.True(includeComparisons);
            if (Requests.Count != heldCall) return Quote(session.RequestState);
            Entered.TrySetResult();
            try
            {
                await Release.Task;
                if (cancelHeldQuote) throw new OperationCanceledException("Dependency-owned timeout.");
                if (failHeldQuote) throw new InvalidOperationException("Pricing unavailable.");
                return Quote(session.RequestState);
            }
            finally { HeldCompleted.TrySetResult(); }
        }
    }

    [Fact]
    public async Task Upload_SingleAndMultipleBodiesPersistSourceDefaultAny()
    {
        var client = new ControlledUploadClient();
        var store = new RecordingSessionStore();
        await using var workflow = CreateWorkflow(client: client, store: store);
        await workflow.InitializeAsync(default);
        var multi = UploadFile("multi.stl") with { GeometryClaim = Geometry() with { BodyCount = 2 } };

        var uploading = workflow.UploadAsync([multi, UploadFile("single.stl")], default);
        await client.WaitForUploadsAsync(2);
        client.CompleteSuccess("multi.stl", "opaque-multi", multi.GeometryClaim);
        client.CompleteSuccess("single.stl", "opaque-single", Geometry());
        await uploading;

        Assert.Equal("Any", Assert.Single(workflow.Parts, part => part.DisplayFileName == "multi.stl").Configuration.Color);
        Assert.Equal("Any", Assert.Single(workflow.Parts, part => part.DisplayFileName == "single.stl").Configuration.Color);
        Assert.Equal("Any", Assert.Single(store.LastSavedState!.Parts, part => part.DisplayFileName == "multi.stl").Configuration.Color);
        Assert.Equal("Any", Assert.Single(store.LastSavedState.Parts, part => part.DisplayFileName == "single.stl").Configuration.Color);
        Assert.NotNull(workflow.OrderQuote);
        Assert.Equal("Any", Assert.Single(workflow.Parts, part => part.DisplayFileName == "multi.stl").Quote!.Color);
        Assert.Equal("Any", Assert.Single(workflow.Parts, part => part.DisplayFileName == "single.stl").Quote!.Color);
    }

    [Theory]
    [InlineData("PLA", "White")]
    [InlineData("PETG", "Red")]
    [InlineData("M68", "Gray")]
    [InlineData("G217", "Clear")]
    public async Task Configure_MultipleBodiesKeepAnyWithoutChangingAnotherPart(string material, string color)
    {
        var multi = PersistedPart("multi.stl", "opaque-multi") with
        {
            Geometry = AuthoritativeInstantQuotationGeometry.RestoreFromProtectedSession(10, 1_000, 100, [10], [10], 12, 2, true),
        };
        var single = PersistedPart("single.stl", "opaque-single");
        var store = new RecordingSessionStore { ExistingSession = Session("color-session", multi, single) };
        await using var workflow = CreateWorkflow(store: store);
        await workflow.InitializeAsync("color-session", default);

        await workflow.UpdateConfigurationAsync(multi.PartId, material, color, 3, default);

        var actual = Assert.Single(workflow.Parts, part => part.PartId == multi.PartId);
        Assert.Equal(material, actual.Configuration.MaterialKey);
        Assert.Equal("Any", actual.Configuration.Color);
        Assert.Equal(3, actual.Configuration.Quantity);
        Assert.Equal("Any", actual.Quote!.Color);
        Assert.Equal("Any", Assert.Single(store.LastSavedState!.Parts, part => part.PartId == multi.PartId).Configuration.Color);
        var untouched = Assert.Single(workflow.Parts, part => part.PartId == single.PartId);
        Assert.Equal(single.Configuration, untouched.Configuration);
        Assert.Equal(single.Geometry, untouched.Geometry);
        Assert.Equal("Black", untouched.Quote!.Color);
    }

    [Theory]
    [InlineData("#12345")]
    [InlineData("#GGGGGG")]
    [InlineData("__custom__")]
    [InlineData("")]
    public async Task Configure_MultipleBodiesRejectMalformedColorBeforeCoercion(string color)
    {
        var multi = PersistedPart("multi.stl", "opaque-multi") with
        {
            Geometry = AuthoritativeInstantQuotationGeometry.RestoreFromProtectedSession(10, 1_000, 100, [10], [10], 12, 2, true),
        };
        var store = new RecordingSessionStore { ExistingSession = Session("color-session", multi) };
        await using var workflow = CreateWorkflow(store: store);
        await workflow.InitializeAsync("color-session", default);
        var saved = store.LastSavedState;
        var configuration = Assert.Single(workflow.Parts).Configuration;
        var quote = workflow.OrderQuote;

        await Assert.ThrowsAsync<ArgumentException>(() =>
            workflow.UpdateConfigurationAsync(multi.PartId, "PLA", color, 1, default));

        Assert.Same(saved, store.LastSavedState);
        Assert.Equal(configuration, Assert.Single(workflow.Parts).Configuration);
        Assert.Same(quote, workflow.OrderQuote);
    }

    [Theory]
    [InlineData("PLA", "White", 2, "Any")]
    [InlineData("PLA", "#123ABC", 2, "Any")]
    [InlineData("M68", "Gray", 2, "Any")]
    [InlineData("PLA", "#123ABC", 1, "#123ABC")]
    [InlineData("PETG", "#00ff7F", 1, "#00ff7F")]
    public async Task Initialize_RestoredColorUsesActualBodyCountWithoutNarrowingSingleBodyHex(
        string material, string color, int bodies, string expected)
    {
        var part = PersistedPart("restored.stl", "opaque-restored") with
        {
            Geometry = AuthoritativeInstantQuotationGeometry.RestoreFromProtectedSession(10, 1_000, 100, [10], [10], 12, bodies, true),
            Configuration = new(material, color, 2),
        };
        var store = new RecordingSessionStore { ExistingSession = Session("color-session", part) };
        await using var workflow = CreateWorkflow(store: store);

        await workflow.InitializeAsync("color-session", default);

        var restored = Assert.Single(workflow.Parts);
        Assert.Equal(expected, restored.Configuration.Color);
        Assert.Equal(expected, Assert.Single(store.LastSavedState!.Parts).Configuration.Color);
        Assert.Equal(expected, restored.Quote!.Color);
        Assert.Equal(material, restored.Configuration.MaterialKey);
        Assert.Equal(2, restored.Configuration.Quantity);
        Assert.Equal(bodies, restored.Geometry.BodyCount);
        Assert.Equal(0, store.CreateCalls);
    }

    [Fact]
    public async Task Initialize_SavedSingleBodyBlackDefaultRemainsConfiguredWithoutBecomingAny()
    {
        var part = PersistedPart("saved-black.stl", "opaque-saved-black") with
        {
            Configuration = new("PLA", "Black", 1),
        };
        var store = new RecordingSessionStore { ExistingSession = Session("saved-black-session", part) };
        await using var workflow = CreateWorkflow(store: store);

        await workflow.InitializeAsync("saved-black-session", default);

        var restored = Assert.Single(workflow.Parts);
        Assert.Equal(part.PartId, restored.PartId);
        Assert.Equal(1, restored.Geometry.BodyCount);
        Assert.Equal(part.Configuration, restored.Configuration);
        Assert.Equal(part.Configuration, Assert.Single(store.LastSavedState!.Parts).Configuration);
        Assert.Equal("Black", restored.Quote!.Color);
        Assert.Equal(InstantQuotationWorkflowState.Configured, workflow.State);
        Assert.Equal(0, store.CreateCalls);
        workflow.EnterReview();
        Assert.Equal(InstantQuotationWorkflowState.Review, workflow.State);
    }

    private static InstantQuotationWorkflowCoordinator CreateWorkflow(
        ControlledUploadClient? client = null,
        RecordingSessionStore? store = null,
        IInstantQuotationPricingService? pricing = null,
        string? ownerIdentity = null,
        IInstantQuotationAnalyticsTracker? analytics = null) => new(
            store ?? new RecordingSessionStore(),
            client ?? new ControlledUploadClient(),
            pricing ?? new InstantQuotationPricingService(),
            ownerIdentity,
            analytics ?? NoOpInstantQuotationAnalyticsTracker.Instance,
            authoritativePricingService: pricing as IInstantQuotationAuthoritativePricingService
                ?? SyntheticAuthoritativePricingTestService.Instance);

    private static InstantQuotationWorkflowUploadFile UploadFile(string name) => new(
        name,
        "application/octet-stream",
        4,
        Geometry(),
        _ => Task.FromResult<Stream>(new MemoryStream([1, 2, 3, 4], writable: false)));

    private static InstantQuotationGeometryClaim Geometry(double volume = 1_000) => new(
        1,
        new string('a', 64),
        10,
        10,
        10,
        volume,
        600,
        Enumerable.Repeat(100.0, 64).ToArray(),
        Enumerable.Repeat(40.0, 64).ToArray(),
        100,
        1,
        true,
        false,
        false,
        0.8);

    private static InstantQuotationPart PersistedPart(string fileName, string reference)
    {
        var upload = InstantQuotationUploadResult.Succeeded(
            "persisted-operation",
            new InstantQuotationUploadReference(reference),
            Geometry().Sha256);
        return new InstantQuotationPart(
            Guid.NewGuid(),
            fileName,
            upload.UploadReference!,
            AuthoritativeInstantQuotationGeometry.FromCompletedLegacyUpload(upload, Geometry())!,
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
        private string? createdOwnerIdentity;

        public int CreateCalls { get; private set; }

        public string? LastOwnerIdentity { get; private set; }

        public InstantQuotationOrderState? LastSavedState { get; private set; }

        public string? LastRequestedSessionId { get; private set; }

        public InstantQuotationSessionState? ExistingSession { get; init; }

        public string? ExistingOwnerIdentity { get; init; }

        public Action<InstantQuotationOrderState>? OnPersist { get; set; }

        public bool RejectNextPut { get; set; }

        public Task<InstantQuotationSessionState> CreateAsync(string? ownerIdentity, InstantQuotationOrderState requestState, CancellationToken cancellationToken)
        {
            CreateCalls++;
            LastOwnerIdentity = ownerIdentity;
            createdOwnerIdentity = ownerIdentity;
            LastSavedState = requestState;
            createdSession = new InstantQuotationSessionState("protected-session", "submission", requestState, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
            return Task.FromResult(createdSession);
        }

        public Task<InstantQuotationSessionState?> GetAsync(string sessionId, string? ownerIdentity, CancellationToken cancellationToken)
        {
            LastRequestedSessionId = sessionId;
            LastOwnerIdentity = ownerIdentity;
            return Task.FromResult(
                createdSession?.SessionId == sessionId && createdOwnerIdentity == ownerIdentity
                        ? createdSession
                        : ExistingSession?.SessionId == sessionId && ExistingOwnerIdentity == ownerIdentity
                            ? ExistingSession
                            : null);
        }

        public Task<bool> PutAsync(InstantQuotationSessionState session, string? ownerIdentity, CancellationToken cancellationToken)
        {
            if (RejectNextPut)
            {
                RejectNextPut = false;
                return Task.FromResult(false);
            }

            LastOwnerIdentity = ownerIdentity;
            LastSavedState = session.RequestState;
            createdSession = session;
            OnPersist?.Invoke(session.RequestState);
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

    private sealed class RecordingPricingService : IInstantQuotationPricingService,
        IInstantQuotationAuthoritativePricingService
    {
        public int QuoteCalls { get; private set; }

        public InstantQuotationOrderQuote Quote(InstantQuotationOrderState state)
        {
            QuoteCalls++;
            return SyntheticPhysicalPricingTestData.Quote(state);
        }

        public Task<InstantQuotationOrderQuote?> QuoteAsync(
            InstantQuotationSessionState session,
            string? ownerIdentity,
            bool includeComparisons,
            CancellationToken cancellationToken) =>
            Task.FromResult<InstantQuotationOrderQuote?>(Quote(session.RequestState));
    }

    private sealed class CallerComponentActivator : IComponentActivator
    {
        public InstantQuotationWorkflow? Workflow { get; private set; }

        public IComponent CreateInstance(Type componentType)
        {
            var component = (IComponent)Activator.CreateInstance(componentType)!;
            if (component is InstantQuotationWorkflow workflow) Workflow = workflow;
            return component;
        }
    }

    private sealed class CallerJavaScript : IJSRuntime
    {
        public int Calls { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, default, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            Calls++;
            return ValueTask.FromException<TValue>(new InvalidOperationException("Static caller fixture must not execute browser JavaScript."));
        }
    }

    // Same synthetic-only ticket boundary as ProfileProducerBoundaryTests, not ticket-security evidence.
    private sealed class CallerTickets : IInstantQuotationQuoteTicketService
    {
        public int IssueCalls { get; private set; }

        public InstantQuotationQuoteAuthorization Issue(
            InstantQuotationSessionState session, InstantQuotationOrderQuote quote, DateTimeOffset now)
        {
            IssueCalls++;
            return new(["synthetic"], "synthetic");
        }

        public bool Validate(InstantQuotationSessionState session, InstantQuotationOrderQuote quote,
            InstantQuotationQuoteAuthorization authorization, DateTimeOffset now) => true;
    }

    private sealed class RecordingAnalyticsTracker : IInstantQuotationAnalyticsTracker
    {
        public Func<ValueTask>? BeforeNextStageResult { get; set; }

        public List<(InstantQuotationStage Stage, InstantQuotationStageResult Result, InstantQuotationStageFailure? Failure, string? FileType)> StageResults { get; } = [];

        public ValueTask RecordStageResultAsync(
            InstantQuotationStage stage,
            InstantQuotationStageResult result,
            InstantQuotationStageFailure? failure,
            string? fileType,
            CancellationToken cancellationToken = default)
        {
            StageResults.Add((stage, result, failure, fileType));
            var before = BeforeNextStageResult;
            BeforeNextStageResult = null;
            return before?.Invoke() ?? ValueTask.CompletedTask;
        }

        public ValueTask RecordUploadStartAsync(
            string batchId,
            int fileCount,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public List<(string OperationId, InstantQuotationProblemCategory Category, int FileCount)> UploadFailures { get; } = [];

        public ValueTask RecordUploadFailureAsync(
            string operationId,
            InstantQuotationProblemCategory category,
            int fileCount,
            CancellationToken cancellationToken = default)
        {
            UploadFailures.Add((operationId, category, fileCount));
            return ValueTask.CompletedTask;
        }

        public ValueTask RecordEstimateShownAsync(long revision, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask RecordReviewReachedAsync(long revision, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class ControlledUploadClient(bool ignoreCancellation = false) : IInstantQuotationUploadClient
    {
        private readonly Dictionary<string, Queue<PendingUpload>> completions = new(StringComparer.Ordinal);

        public List<string> UploadOperations { get; } = [];

        public List<CancellationToken> UploadCancellationTokens { get; } = [];

        public List<InstantQuotationGeometryClaim> UploadClaims { get; } = [];

        public List<string?> UploadOwners { get; } = [];

        public List<string> RemovedReferences { get; } = [];

        public List<string> RemoveOperations { get; } = [];

        public Queue<InstantQuotationProblemCategory> RemoveResults { get; } = [];

        public Task<InstantQuotationUploadResult> UploadAsync(string sessionId, string? ownerIdentity, Stream content, string fileName, string contentType, long contentLength, InstantQuotationGeometryClaim geometryClaim, string operationId, CancellationToken cancellationToken)
        {
            UploadOperations.Add(operationId);
            UploadCancellationTokens.Add(cancellationToken);
            UploadClaims.Add(geometryClaim);
            UploadOwners.Add(ownerIdentity);
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

        public Task<InstantQuotationRemoveResult> RemoveAsync(string sessionId, string? ownerIdentity, InstantQuotationUploadReference uploadReference, string operationId, CancellationToken cancellationToken)
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

        public Task<InstantQuotationFinalizationResult> FinalizeAsync(string sessionId, string? ownerIdentity, int quotationRequestId, IReadOnlyList<InstantQuotationUploadReference> uploadReferences, string operationId, CancellationToken cancellationToken) =>
            Task.FromResult(InstantQuotationFinalizationResult.Unavailable(operationId));

        public void CompleteSuccess(
            string fileName,
            string reference,
            InstantQuotationGeometryClaim geometry,
            string? operationId = null)
        {
            var pending = NextPending(fileName);
            pending.Completion.SetResult(InstantQuotationUploadResult.Succeeded(
                operationId ?? pending.OperationId,
                new InstantQuotationUploadReference(reference),
                geometry.Sha256));
        }

        public void CompleteUnavailable(
            string fileName,
            InstantQuotationUploadRetryDisposition retryDisposition = InstantQuotationUploadRetryDisposition.None)
        {
            var pending = NextPending(fileName);
            pending.Completion.SetResult(InstantQuotationUploadResult.Failed(
                pending.OperationId,
                InstantQuotationServiceStatus.Unavailable,
                InstantQuotationAuthorizationStatus.NotEvaluated,
                InstantQuotationProblemCategory.DependencyUnavailable,
                retryDisposition));
        }

        public void CompleteCancelled(string fileName)
        {
            NextPending(fileName).Completion.SetCanceled(UploadCancellationTokens.Last());
        }

        public void CancelPendingUploads()
        {
            foreach (var pending in completions.Values.SelectMany(queue => queue))
                pending.Completion.TrySetCanceled();
        }

        public void CompleteLatestUnavailable(
            string fileName,
            InstantQuotationUploadRetryDisposition retryDisposition)
        {
            var pending = completions[fileName]
                .Last(item => !item.Completion.Task.IsCompleted);
            pending.Completion.SetResult(InstantQuotationUploadResult.Failed(
                pending.OperationId,
                InstantQuotationServiceStatus.Unavailable,
                InstantQuotationAuthorizationStatus.NotEvaluated,
                InstantQuotationProblemCategory.DependencyUnavailable,
                retryDisposition));
        }

        private PendingUpload NextPending(string fileName)
        {
            var queue = completions[fileName];
            while (queue.Count > 0)
            {
                var pending = queue.Dequeue();
                if (!pending.Completion.Task.IsCompleted)
                {
                    return pending;
                }
            }

            throw new InvalidOperationException($"No pending upload exists for {fileName}.");
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
