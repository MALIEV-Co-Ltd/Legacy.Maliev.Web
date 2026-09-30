using System.Text.Json.Nodes;
using System.Security.Cryptography;
using Legacy.Maliev.Web.Application.Pricing;
using Legacy.Maliev.Web.Application.Pricing.Simulation;

namespace Legacy.Maliev.Web.Tests;

public sealed class EsdMaterialProfileTests
{
    [Theory]
    [InlineData("PA612-ESD", "maliev-fiberon-pa612-esd", "4AB7785FFA566C1C99AA030DB2118557381888EDA1BAEF8188D997B97796D6C4")]
    [InlineData("ABS-ESD", "maliev-esun-abs-esd", "828F68AF2A9238BB3F4D15C88CD5E252A92B7CBFD3E643F10C48C4E861999C92")]
    public void PackagedManufacturerCompositionMatchesRuntimePhysicalEvidence(
        string material, string name, string expectedDigest)
    {
        var assembly = typeof(FdmRuntimeProfileCatalog).Assembly;
        byte[] filament = Read("Resolved/" + name + "-x1c-filament.resolved.json");
        byte[] process = Read("Resolved/" + name + "-x1c-process.resolved.json");
        byte[] combined = [.. filament, .. process];
        Assert.Equal(expectedDigest, Convert.ToHexString(SHA256.HashData(combined)));
        var manifest = JsonNode.Parse(FdmRuntimeProfileCatalog.LoadEmbedded().BrowserManifestJson)!;
        Assert.Equal(expectedDigest, manifest["materials"]![material]!["sourceArtifactSha256"]!.GetValue<string>());
        byte[] Read(string path)
        {
            using var resource = assembly.GetManifestResourceStream(
                "Legacy.Maliev.Web.Application.Pricing.Profiles." + path.Replace('/', '.'));
            Assert.NotNull(resource);
            using var buffer = new MemoryStream();
            resource.CopyTo(buffer);
            return buffer.ToArray();
        }
    }

    [Theory]
    [InlineData("PA612-ESD", 1.10, 12, 4, 3.38)]
    [InlineData("ABS-ESD", 0.97, 22, 3, 1.09)]
    public void ManufacturerMaterialResolvesEveryBuildWithBoundPhysicalLimits(
        string key, double density, double flow, double cooling, double cost)
    {
        var material = PricingCatalog.ResolveMaterial(key);
        Assert.NotNull(material);
        Assert.Equal(cost, material.CostPerUnit, 3);
        Assert.True(material.RequiresDrying);
        var catalog = FdmRuntimeProfileCatalog.LoadEmbedded();
        foreach (var build in Enum.GetValues<BuildPreference>())
        {
            Assert.True(catalog.TryResolveTrustedProfile(key, build, out var profile));
            Assert.NotNull(profile);
            Assert.True(catalog.ResolvePolicy(key, build).AutomaticPricingEligible);
            Assert.Equal(density, profile.MaterialDensityGramsPerCm3, 3);
            Assert.Equal(flow, profile.MaximumVolumetricFlowMm3PerSecond!.Value, 3);
            Assert.Equal(cooling, profile.Motion.MinimumLayerTimeSeconds, 3);
            Assert.Equal(20, profile.Motion.MinimumCoolingSpeedMmPerSecond);
            if (key == "ABS-ESD")
            {
                Assert.True(material.RequiresHeatedEnclosure);
                Assert.Equal(200, profile.Motion.TravelSpeedMmPerSecond);
                Assert.InRange(profile.SpeedMmPerSecond[ExtrusionRole.OuterWall], 1, 150);
                Assert.InRange(profile.SpeedMmPerSecond[ExtrusionRole.SparseInfill], 1, 200);
                Assert.Equal(10, profile.BridgeMaximumSpanMm);
                if (build == BuildPreference.Standard) { Assert.Equal(3, profile.TopShellLayers); }
            }
        }
    }

    [Fact]
    public void HistoricalPolycarbonateIsNotOfferedForNewAutomaticPricing()
    {
        Assert.Null(PricingCatalog.ResolveMaterial("PC-ESD"));
        Assert.False(FdmRuntimeProfileCatalog.LoadEmbedded()
            .TryResolveTrustedProfile("PC-ESD", BuildPreference.Standard, out _));
    }

    [Fact]
    public void MaterialOverrideChangesOnlyItsSpecifiedBuildAndBindsProfileDigest()
    {
        var original = FdmRuntimeProfileCatalog.LoadEmbedded();
        var manifest = JsonNode.Parse(original.BrowserManifestJson)!.AsObject();
        manifest["materials"]!["PLA"]!["buildOverrides"] = JsonNode.Parse(
            """{"Standard":{"travel_speed":"200","bridge_max_span":"10","top_shell_layers":"3"}}""");
        var modified = FdmRuntimeProfileCatalog.Parse(manifest.ToJsonString());
        Assert.True(original.TryResolveTrustedProfile("PLA", BuildPreference.Standard, out var before));
        Assert.True(modified.TryResolveTrustedProfile("PLA", BuildPreference.Standard, out var after));
        Assert.NotNull(before);
        Assert.NotNull(after);
        Assert.Equal(200, after.Motion.TravelSpeedMmPerSecond);
        Assert.Equal(10, after.BridgeMaximumSpanMm);
        Assert.Equal(3, after.TopShellLayers);
        Assert.NotEqual(before.ResolvedProfileSha256, after.ResolvedProfileSha256);
        Assert.True(original.TryResolveTrustedProfile("PLA", BuildPreference.Quality, out var qualityBefore));
        Assert.True(modified.TryResolveTrustedProfile("PLA", BuildPreference.Quality, out var qualityAfter));
        Assert.Equal(qualityBefore!.ResolvedProfileSha256, qualityAfter!.ResolvedProfileSha256);
        Assert.True(modified.TryResolveTrustedProfile("PETG", BuildPreference.Standard, out var other));
        Assert.Equal(500, other!.Motion.TravelSpeedMmPerSecond);
    }

    [Theory]
    [InlineData("{\"Unknown\":{\"travel_speed\":\"200\"}}")]
    [InlineData("{\"Standard\":{\"travel_speed\":\"NaN\"}}")]
    [InlineData("{\"Standard\":{\"travel_speed\":\"-1\"}}")]
    [InlineData("{\"Standard\":{\"unknown_process_setting\":\"1\"}}")]
    [InlineData("{\"Standard\":{\"layer_height\":\"0\"}}")]
    [InlineData("{\"Standard\":null}")]
    public void UnverifiableMaterialOverrideCannotAuthorizePricing(string overrides)
    {
        var manifest = JsonNode.Parse(FdmRuntimeProfileCatalog.LoadEmbedded().BrowserManifestJson)!.AsObject();
        manifest["materials"]!["PLA"]!["buildOverrides"] = JsonNode.Parse(overrides);
        Assert.Throws<InvalidDataException>(() => FdmRuntimeProfileCatalog.Parse(manifest.ToJsonString()));
    }
}
