using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Application.Pricing;
using Legacy.Maliev.Web.Components.Pages.InstantQuotation;

namespace Legacy.Maliev.Web.Tests;

public sealed class Source21PaletteRestoreTests
{
    // Source21 narrows offered names. Durable restore is a Legacy-only boundary:
    // keep its strict refusal rather than silently rewriting a saved configuration.
    public static TheoryData<string, string, string> RejectedColors
    {
        get
        {
            var rows = new TheoryData<string, string, string>();
            foreach (var (material, valid, retired) in new[]
            {
                ("PETG", "Clear", "Silver,Purple,Pink"),
                ("ABS", "Red", "Silver,Orange,Purple,Pink"),
                ("ASA", "Natural", "Silver,Red,Orange,Yellow,Green,Blue,Purple,Pink"),
                ("HIPS", "White", "Black"),
                ("TPU", "Black", "Any,Clear,Red,Blue"),
                ("PC", "Translucent", "Any,Natural,White,Gray"),
                ("PC-FR", "Black", "Any,Natural,White,Gray"),
                ("PA6", "Natural", "Any,White,Gray"),
                ("PA12", "Natural", "Any,White,Gray"),
                ("ABS-FR", "Black", "Any,Natural,White,Gray"),
                ("PLA-CF", "Black", "Natural"),
                ("PETG-CF", "Black", "Natural"),
                ("PET-CF", "Black", "Natural"),
                ("PA-CF", "Black", "Natural"),
                ("ASA-CF", "Black", "Natural"),
                ("PETG-ESD", "Black", "Natural"),
            })
            {
                foreach (var color in retired.Split(',')) rows.Add(material, color, valid);
            }

            foreach (var color in new[] { "#12345", "#GGGGGG", "__custom__", "", "black" })
                rows.Add("PLA", color, "Black");
            rows.Add("PLA-CF", "#123ABC", "Black");
            rows.Add("M68", "#123ABC", "Gray");
            rows.Add("M68", "Any", "Gray");
            return rows;
        }
    }

    public static TheoryData<string, string> SavedColors
    {
        get
        {
            var rows = new TheoryData<string, string>();
            foreach (var row in InstantQuotationWorkflowPricingTests.ExactMaterialColors)
            {
                var material = (string)row[0];
                foreach (var color in (string[])row[1]) rows.Add(material, color);
            }
            foreach (var material in new[] { "PLA", "PETG", "ABS", "ASA", "TPU", "PC", "PC-FR", "PA6", "PA12", "ABS-FR" })
                rows.Add(material, "#00ff7F");
            return rows;
        }
    }

    [Theory]
    [InlineData("HIPS", "White")]
    [InlineData("TPU", "Black")]
    [InlineData("PC", "Black")]
    [InlineData("PC-FR", "Black")]
    [InlineData("PA6", "Natural")]
    [InlineData("PA12", "Natural")]
    [InlineData("ABS-FR", "Black")]
    public void Catalog_FirstNamedColorMatchesSourceSelectionDefault(string material, string expected)
    {
        Assert.Equal(expected, PricingCatalog.MaterialColors[material][0]);
    }

    [Fact]
    public void Catalog_TpuLabelIncludesSourceHardness()
    {
        Assert.Equal("TPU 95A — Flexible Thermoplastic Polyurethane", PricingCatalog.ResolveMaterial("TPU")!.DisplayName);
    }

    [Theory]
    [MemberData(nameof(RejectedColors))]
    public async Task InvalidSavedColor_IsRefusedWithoutPricingOrDeletingOriginalAndValidControlRestores(
        string material, string invalidColor, string validColor)
    {
        Assert.False(PricingCatalog.IsColorSupported(material, invalidColor));
        var rejected = Part(material, invalidColor);
        Assert.Throws<ArgumentException>(() => SyntheticPhysicalPricingTestData.Quote(new([rejected])));

        var original = Session(rejected);
        var store = new Store(original);
        var pricing = new SyntheticPricing();
        var uploads = new UnusedUploads();
        await using (var workflow = Workflow(store, pricing, uploads))
        {
            await workflow.InitializeAsync(original.SessionId, default);
            Assert.Equal(InstantQuotationWorkflowState.Empty, workflow.State);
            Assert.Empty(workflow.Parts);
            Assert.Null(workflow.OrderQuote);
            Assert.NotEqual(original.SessionId, workflow.ProtectedSessionIdentity);
            Assert.Equal(1, store.CreateCalls);
            Assert.Equal(0, store.PutCalls);
            Assert.Equal(0, pricing.Calls);
        }
        Assert.Same(original, store.Original);
        Assert.Same(rejected, Assert.Single(store.Original.RequestState.Parts));
        Assert.Equal(invalidColor, rejected.Configuration.Color);
        Assert.Equal(0, store.RemoveCalls);
        Assert.Equal(0, uploads.Calls);

        // Same part, geometry and store/price fixture; only the rejected color differs.
        await AssertRestoresAsync(rejected with { Configuration = rejected.Configuration with { Color = validColor } });
    }

    [Theory]
    [MemberData(nameof(SavedColors))]
    public Task SupportedNamedAndApprovedSavedHex_RestorePersistQuoteAndReviewWithoutChangingColor(
        string material, string color) => AssertRestoresAsync(Part(material, color));

    private static async Task AssertRestoresAsync(InstantQuotationPart part)
    {
        var original = Session(part);
        var store = new Store(original);
        var pricing = new SyntheticPricing();
        var uploads = new UnusedUploads();
        await using (var workflow = Workflow(store, pricing, uploads))
        {
            await workflow.InitializeAsync(original.SessionId, default);
            var restored = Assert.Single(workflow.Parts);
            Assert.Equal(part.PartId, restored.PartId);
            Assert.Equal(part.Configuration, restored.Configuration);
            Assert.Equal(part.Geometry, restored.Geometry);
            Assert.Equal(part.Configuration.Color, restored.Quote!.Color);
            Assert.Equal(original.SessionId, workflow.ProtectedSessionIdentity);
            Assert.Equal(InstantQuotationWorkflowState.Configured, workflow.State);
            Assert.Equal(part.Configuration, Assert.Single(store.Saved!.RequestState.Parts).Configuration);
            Assert.Equal(0, store.CreateCalls);
            Assert.True(store.PutCalls > 0);
            Assert.True(pricing.Calls > 0);
            workflow.EnterReview();
            Assert.Equal(InstantQuotationWorkflowState.Review, workflow.State);
        }
        Assert.Same(part, Assert.Single(original.RequestState.Parts));
        Assert.Equal(0, store.RemoveCalls);
        Assert.Equal(0, uploads.Calls);
    }

    private static InstantQuotationWorkflowCoordinator Workflow(Store store, SyntheticPricing pricing, UnusedUploads uploads) =>
        new(store, uploads, new InstantQuotationPricingService(), null, authoritativePricingService: pricing);

    private static InstantQuotationPart Part(string material, string color) => new(
        Guid.NewGuid(), "saved.stl", new("original-file-reference"),
        AuthoritativeInstantQuotationGeometry.RestoreFromProtectedSession(10, 1_000, 100, [100], [40], 12, 1, true),
        new(material, color, 2));

    private static InstantQuotationSessionState Session(InstantQuotationPart part) =>
        new("saved-session", "submission", new([part]), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private sealed class Store(InstantQuotationSessionState original) : IInstantQuotationSessionStore
    {
        public InstantQuotationSessionState Original { get; } = original;
        public InstantQuotationSessionState? Saved { get; private set; }
        public int CreateCalls { get; private set; }
        public int PutCalls { get; private set; }
        public int RemoveCalls { get; private set; }

        public Task<InstantQuotationSessionState?> GetAsync(string sessionId, string? ownerIdentity, CancellationToken cancellationToken) =>
            Task.FromResult<InstantQuotationSessionState?>(sessionId == Original.SessionId ? Saved ?? Original : null);

        public Task<InstantQuotationSessionState> CreateAsync(string? ownerIdentity, InstantQuotationOrderState requestState, CancellationToken cancellationToken)
        {
            CreateCalls++;
            return Task.FromResult(new InstantQuotationSessionState("new-session", "new-submission", requestState, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        }

        public Task<bool> PutAsync(InstantQuotationSessionState session, string? ownerIdentity, CancellationToken cancellationToken)
        {
            PutCalls++;
            Saved = session;
            return Task.FromResult(true);
        }

        public Task<bool> RemoveAsync(string sessionId, string? ownerIdentity, CancellationToken cancellationToken)
        {
            RemoveCalls++;
            throw new InvalidOperationException("Palette refusal must retain the original session.");
        }
    }

    // Synthetic pricing exercises color admission and workflow state, not manufacturing or signature authority.
    private sealed class SyntheticPricing : IInstantQuotationAuthoritativePricingService
    {
        public int Calls { get; private set; }

        public Task<InstantQuotationOrderQuote?> QuoteAsync(InstantQuotationSessionState session, string? ownerIdentity,
            bool includeComparisons, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult<InstantQuotationOrderQuote?>(SyntheticPhysicalPricingTestData.Quote(session.RequestState));
        }
    }

    private sealed class UnusedUploads : IInstantQuotationUploadClient
    {
        public int Calls { get; private set; }

        public Task<InstantQuotationUploadResult> UploadAsync(string sessionId, string? ownerIdentity, Stream content,
            string fileName, string contentType, long contentLength, InstantQuotationGeometryClaim geometryClaim,
            string operationId, CancellationToken cancellationToken) => Unexpected<InstantQuotationUploadResult>();

        public Task<InstantQuotationRemoveResult> RemoveAsync(string sessionId, string? ownerIdentity,
            InstantQuotationUploadReference uploadReference, string operationId, CancellationToken cancellationToken) =>
            Unexpected<InstantQuotationRemoveResult>();

        public Task<InstantQuotationFinalizationResult> FinalizeAsync(string sessionId, string? ownerIdentity,
            int quotationRequestId, IReadOnlyList<InstantQuotationUploadReference> uploadReferences,
            string operationId, CancellationToken cancellationToken) => Unexpected<InstantQuotationFinalizationResult>();

        private Task<T> Unexpected<T>()
        {
            Calls++;
            throw new InvalidOperationException("Restoring color must not upload, delete or finalize files.");
        }
    }
}
