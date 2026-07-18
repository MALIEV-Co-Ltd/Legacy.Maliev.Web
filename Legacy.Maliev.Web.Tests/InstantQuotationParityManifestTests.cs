using System.Text.Json;

namespace Legacy.Maliev.Web.Tests;

public sealed class InstantQuotationParityManifestTests
{
    private static readonly string[] RequiredFormats =
    [
        ".stl",
        ".obj",
        ".3mf",
        ".glb",
        ".gltf",
        ".stp",
        ".step",
        ".igs",
        ".iges",
    ];

    private static readonly string[] CriticalControls =
    [
        "upload",
        "viewer",
        "part-list",
        "material-picker",
        "part-pricing",
        "order-total",
        "lead-time",
        "review",
        "customer-form",
        "submit-confirmation",
    ];

    private static readonly string[] RequiredStates =
    [
        "empty",
        "uploading",
        "uploaded",
        "error",
        "multi-part",
        "configured",
        "review",
        "submitted",
    ];

    [Fact]
    public void ProductionManifest_CapturesCriticalJourneyAndWireBoundaries()
    {
        using var manifest = LoadManifest();
        var root = manifest.RootElement;

        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("production-observation-and-current-source", root.GetProperty("baselineKind").GetString());
        Assert.False(root.GetProperty("allowsProductionWrites").GetBoolean());
        Assert.Equal(
            "https://www.maliev.com/instantquotation/3d-printing",
            root.GetProperty("productionRoute").GetString());

        AssertExactSet(RequiredFormats, root.GetProperty("supportedFileExtensions"));
        AssertExactSet(CriticalControls, root.GetProperty("criticalControls"));
        AssertExactSet(RequiredStates, root.GetProperty("visibleStates"));

        var locales = root.GetProperty("browserMatrix").GetProperty("locales");
        AssertExactSet(["en", "th"], locales);

        var viewports = root.GetProperty("browserMatrix").GetProperty("viewports");
        Assert.Contains(viewports.EnumerateArray(), viewport => viewport.GetProperty("name").GetString() == "desktop");
        Assert.Contains(viewports.EnumerateArray(), viewport => viewport.GetProperty("name").GetString() == "mobile");

        var boundaries = root.GetProperty("wireBoundaries");
        AssertWireFields(boundaries, "estimate", [
            "success", "process", "unitPrice", "subtotal", "subtotalThb", "weightGrams",
            "boundingCm3", "printTimeMinutes", "materialPerUnit", "tiers", "currency",
        ]);
        AssertWireFields(boundaries, "orderTotal", [
            "success", "printing", "shipping", "vat", "finalOrderPrice", "currency",
        ]);
        AssertWireFields(boundaries, "upload", ["success", "path"]);
        AssertWireRequestFields(boundaries, "submission", [
            "OrderItems.Index",
            "OrderItems[i].FileName",
            "OrderItems[i].Material",
            "OrderItems[i].Color",
            "OrderItems[i].Dimension",
            "OrderItems[i].Quantity",
            "OrderItems[i].EstimatedUnitCost",
            "OrderItems[i].EstimatedUnitPrintTime",
            "OrderItems[i].EstimatedTotalCost",
            "OrderItems[i].EstimatedTotalPrintTime",
            "OrderItems[i].StoragePath",
            "OrderItems[i].GeometryWarning",
            "FirstName",
            "LastName",
            "Email",
            "Telephone",
            "Company",
            "TaxNumber",
            "Country",
            "Description",
            "EstimatedLeadTime",
            "EstimatedTotalPrice",
            "__RequestVerificationToken",
        ]);
        var submission = boundaries.EnumerateArray().Single(
            value => value.GetProperty("name").GetString() == "submission");
        Assert.Equal("post-redirect-get-html", submission.GetProperty("responseKind").GetString());
    }

    [Fact]
    public void ProductionManifest_RequiresConsentSafeAnalyticsAndDeferredAspireIdentity()
    {
        using var manifest = LoadManifest();
        var root = manifest.RootElement;

        var analytics = root.GetProperty("analytics");
        AssertExactSet(
            ["file_upload_start", "file_upload_complete", "request_quote"],
            analytics.GetProperty("observedEvents"));
        AssertExactSet(
            ["upload_failure", "estimate_shown", "review_reached"],
            analytics.GetProperty("targetEventsOwnedByIssue152"));
        Assert.True(analytics.GetProperty("requiresConsentBeforeEmission").GetBoolean());
        Assert.True(analytics.GetProperty("forbidsPii").GetBoolean());
        Assert.Equal("request_quote", analytics.GetProperty("primaryAdsConversion").GetString());

        var releaseGate = root.GetProperty("releaseGate");
        Assert.True(releaseGate.GetProperty("requiresExactBuildSha").GetBoolean());
        Assert.True(releaseGate.GetProperty("requiresHealthyAspireResources").GetBoolean());
        Assert.True(releaseGate.GetProperty("blocksProductionDeploymentWithoutOwnerApproval").GetBoolean());
        Assert.Equal(154, releaseGate.GetProperty("aspireDependencyIssue").GetInt32());
    }

    private static JsonDocument LoadManifest()
    {
        var manifestPath = Path.Combine(
            FindRepositoryRoot(),
            "tests",
            "instant-quotation",
            "production-parity-manifest.json");
        Assert.True(File.Exists(manifestPath), $"Production parity manifest was not found: {manifestPath}");
        return JsonDocument.Parse(File.ReadAllText(manifestPath));
    }

    private static void AssertExactSet(IEnumerable<string> expected, JsonElement actual)
    {
        Assert.Equal(
            expected.Order(StringComparer.OrdinalIgnoreCase),
            actual.EnumerateArray().Select(value => value.GetString()!).Order(StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);
    }

    private static void AssertWireFields(JsonElement boundaries, string boundaryName, string[] expectedFields)
    {
        var boundary = boundaries.EnumerateArray().Single(
            value => value.GetProperty("name").GetString() == boundaryName);
        AssertExactSet(expectedFields, boundary.GetProperty("requiredResponseFields"));
    }

    private static void AssertWireRequestFields(JsonElement boundaries, string boundaryName, string[] expectedFields)
    {
        var boundary = boundaries.EnumerateArray().Single(
            value => value.GetProperty("name").GetString() == boundaryName);
        AssertExactSet(expectedFields, boundary.GetProperty("requiredRequestFields"));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Web.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
