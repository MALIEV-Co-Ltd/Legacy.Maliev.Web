using System.Text.Json.Nodes;
using Legacy.Maliev.Web.Application.Pricing;

namespace Legacy.Maliev.Web.Tests;

public sealed class FdmRuntimeProfileCatalogTests
{
    [Fact]
    public void EmbeddedManifestCoversExactlyTheFdmCatalogAndApprovedBuilds()
    {
        var catalog = FdmRuntimeProfileCatalog.LoadEmbedded();
        var manifest = JsonNode.Parse(catalog.BrowserManifestJson)!.AsObject();

        Assert.Equal("1.0", manifest["schemaVersion"]!.GetValue<string>());
        Assert.Equal("bambu-x1c-2026-09-22.4", catalog.ProfileVersion);
        Assert.Equal(
            PricingCatalog.Materials.Values.Where(static material => material.Process == PrintProcess.Fdm)
                .Select(static material => material.Key).OrderBy(static key => key, StringComparer.Ordinal),
            manifest["materials"]!.AsObject().Select(static property => property.Key)
                .OrderBy(static key => key, StringComparer.Ordinal));
        Assert.True(catalog.ResolvePolicy("PLA", BuildPreference.Standard).AutomaticPricingEligible);
        Assert.True(catalog.ResolvePolicy("PETG", BuildPreference.Quality).AutomaticPricingEligible);
        Assert.False(catalog.ResolvePolicy("M68", BuildPreference.Standard).AutomaticPricingEligible);
        Assert.False(catalog.ResolvePolicy("UNKNOWN", BuildPreference.Standard).AutomaticPricingEligible);
    }

    [Theory]
    [InlineData("schemaVersion", "2.0")]
    [InlineData("profileVersion", "")]
    public void InvalidRootMetadataFailsClosed(string property, string value)
    {
        var manifest = EmbeddedManifest();
        manifest[property] = value;

        Assert.Throws<InvalidDataException>(() => FdmRuntimeProfileCatalog.Parse(manifest.ToJsonString()));
    }

    [Fact]
    public void MissingCatalogMaterialFailsClosed()
    {
        var manifest = EmbeddedManifest();
        manifest["materials"]!.AsObject().Remove("PLA");

        Assert.Throws<InvalidDataException>(() => FdmRuntimeProfileCatalog.Parse(manifest.ToJsonString()));
    }

    [Fact]
    public void UnqualifiedAutomaticPricingFailsClosed()
    {
        var manifest = EmbeddedManifest();
        manifest["materials"]!["PLA"]!["reasonCodes"] = new JsonArray("profile_unqualified");

        Assert.Throws<InvalidDataException>(() => FdmRuntimeProfileCatalog.Parse(manifest.ToJsonString()));
    }

    [Theory]
    [InlineData("sourceArtifactSha256", "wrong")]
    [InlineData("maximumVolumetricFlowMm3PerSecond", "Infinity")]
    [InlineData("minimumCoolingSpeedMmPerSecond", "-1")]
    public void InvalidMaterialEvidenceFailsClosed(string property, string value)
    {
        var manifest = EmbeddedManifest();
        manifest["materials"]!["PLA"]![property] = value;

        Assert.Throws<InvalidDataException>(() => FdmRuntimeProfileCatalog.Parse(manifest.ToJsonString()));
    }

    [Fact]
    public void MissingBuildAndInvalidMachineEvidenceFailClosed()
    {
        var manifest = EmbeddedManifest();
        manifest["builds"]!.AsObject().Remove("Strength");
        Assert.Throws<InvalidDataException>(() => FdmRuntimeProfileCatalog.Parse(manifest.ToJsonString()));

        manifest = EmbeddedManifest();
        manifest["machine"]!["sourceArtifactSha256"] = "wrong";
        Assert.Throws<InvalidDataException>(() => FdmRuntimeProfileCatalog.Parse(manifest.ToJsonString()));
    }

    [Theory]
    [InlineData("layer_height", "NaN")]
    [InlineData("sparse_infill_density", "200%")]
    [InlineData("wall_loops", "-1")]
    public void InvalidBuildSettingFailsClosed(string setting, string value)
    {
        var manifest = EmbeddedManifest();
        manifest["builds"]!["Standard"]!["settings"]![setting] = value;

        Assert.Throws<InvalidDataException>(() => FdmRuntimeProfileCatalog.Parse(manifest.ToJsonString()));
    }

    private static JsonObject EmbeddedManifest() =>
        JsonNode.Parse(FdmRuntimeProfileCatalog.LoadEmbedded().BrowserManifestJson)!.AsObject();
}
