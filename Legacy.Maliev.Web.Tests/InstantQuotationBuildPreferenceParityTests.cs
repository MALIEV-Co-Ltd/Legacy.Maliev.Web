using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Application.Pricing;

namespace Legacy.Maliev.Web.Tests;

public sealed class InstantQuotationBuildPreferenceParityTests
{
    [Theory]
    [InlineData(null, BuildPreference.Standard)]
    [InlineData("", BuildPreference.Standard)]
    [InlineData("unknown", BuildPreference.Standard)]
    [InlineData(" QUALITY ", BuildPreference.Quality)]
    [InlineData("standard", BuildPreference.Standard)]
    [InlineData("Strength", BuildPreference.Strength)]
    public void Catalog_ResolvesCanonicalBuildPreferenceWithStandardFallback(string? value, BuildPreference expected)
    {
        Assert.Equal(expected, PricingCatalog.ResolveBuildPreference(value));
    }

    [Fact]
    public void Pricing_AppliesPerPartBuildPreferenceAndReturnsAutomaticMaterialComparison()
    {
        var standard = Quote(BuildPreference.Standard);
        var strength = Quote(BuildPreference.Strength);
        var quality = Quote(BuildPreference.Quality);

        Assert.Equal(BuildPreference.Standard, standard.BuildPreference);
        Assert.Equal(BuildPreference.Strength, strength.BuildPreference);
        Assert.Equal(BuildPreference.Quality, quality.BuildPreference);
        Assert.True(quality.UnitPrice > strength.UnitPrice);
        Assert.True(strength.UnitPrice > standard.UnitPrice);
        Assert.Equal(PricingCatalog.Materials.Count, quality.MaterialPrices.Count);
        Assert.Equal(PricingCatalog.Materials.Keys, quality.MaterialPrices.Select(static price => price.MaterialKey));
        Assert.Equal(quality.UnitPrice, quality.MaterialPrices.Single(static price => price.MaterialKey == "PLA").UnitPrice, 2);
    }

    [Fact]
    public void InteractiveSurface_ExposesLocalizedPerPartBuildChoicesAndMaterialPrices()
    {
        var root = FindRepositoryRoot();
        var workflow = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Web", "Components", "Pages", "InstantQuotation", "InstantQuotationWorkflow.razor"));
        var review = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Web", "Components", "Pages", "InstantQuotation", "InstantQuotationReview.razor"));

        Assert.Contains("data-workflow-build-preference", workflow, StringComparison.Ordinal);
        Assert.Contains("BuildPreference.Quality", workflow, StringComparison.Ordinal);
        Assert.Contains("BuildPreference.Standard", workflow, StringComparison.Ordinal);
        Assert.Contains("BuildPreference.Strength", workflow, StringComparison.Ordinal);
        Assert.Contains("data-workflow-material-price", workflow, StringComparison.Ordinal);
        Assert.Contains("data-workflow-material-comparison", workflow, StringComparison.Ordinal);
        Assert.Contains("data-workflow-part-number", workflow, StringComparison.Ordinal);
        Assert.Contains("ChangeBuildPreferenceAsync", workflow, StringComparison.Ordinal);
        Assert.Contains("Build preference", review, StringComparison.Ordinal);
        Assert.Contains("data-workflow-part-number", review, StringComparison.Ordinal);
    }

    private static InstantQuotationPartQuote Quote(BuildPreference preference)
    {
        var claim = new InstantQuotationGeometryClaim(1, new string('a', 64), 40, 40, 40, 48_000, 9_600,
            Enumerable.Repeat(1_200.0, 64).ToArray(), Enumerable.Repeat(140.0, 64).ToArray(),
            1_024, 1, true, false, false, 2);
        var upload = InstantQuotationUploadResult.Succeeded("operation", new InstantQuotationUploadReference("opaque-upload"), claim.Sha256);
        var part = new InstantQuotationPart(Guid.NewGuid(), "part.stl", upload.UploadReference!,
            AuthoritativeInstantQuotationGeometry.FromCompletedLegacyUpload(upload, claim)!,
            new InstantQuotationPartConfiguration("PLA", "Black", 1, preference));

        return new InstantQuotationPricingService().Quote(new InstantQuotationOrderState([part])).Parts.Single();
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Web.slnx"))) return directory.FullName;
        }

        throw new DirectoryNotFoundException("Unable to locate the Legacy.Maliev.Web repository root.");
    }
}
