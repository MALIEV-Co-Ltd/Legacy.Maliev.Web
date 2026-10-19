using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Legacy.Maliev.Web.Application.Pricing;
using Legacy.Maliev.Web.Application.Pricing.Simulation;

namespace Legacy.Maliev.Web.Tests;

public sealed class FdmRuntimeProfileCatalogTests
{
    [Theory]
    [InlineData(BuildPreference.Standard, 0.20, 2)]
    [InlineData(BuildPreference.Quality, 0.12, 2)]
    [InlineData(BuildPreference.Strength, 0.20, 6)]
    public void ApprovedBuildResolvesTrustedServerProfile(
        BuildPreference preference,
        double expectedLayerHeight,
        int expectedWalls)
    {
        var catalog = FdmRuntimeProfileCatalog.LoadEmbedded();

        Assert.True(catalog.TryResolveTrustedProfile("PLA", preference, out ResolvedSimulationProfile? profile));
        Assert.NotNull(profile);
        Assert.Equal(expectedLayerHeight, profile.LayerHeightMm, 2);
        Assert.Equal(expectedWalls, profile.WallCount);
        Assert.Equal("PLA", profile.MaterialId);
        Assert.Equal(64, profile.ResolvedProfileSha256.Length);
    }

    [Theory]
    [InlineData("UNKNOWN")]
    [InlineData("M68")]
    public void UnavailableMaterialCannotResolveTrustedServerProfile(string material)
    {
        var catalog = FdmRuntimeProfileCatalog.LoadEmbedded();

        Assert.False(catalog.TryResolveTrustedProfile(material, BuildPreference.Standard, out var profile));
        Assert.Null(profile);
    }

    [Fact]
    public void EmbeddedManifestCoversExactlyTheFdmCatalogAndApprovedBuilds()
    {
        var catalog = FdmRuntimeProfileCatalog.LoadEmbedded();
        var manifest = JsonNode.Parse(catalog.BrowserManifestJson)!.AsObject();

        Assert.Equal("1.0", manifest["schemaVersion"]!.GetValue<string>());
        Assert.Equal("bambu-x1c-2026-09-30.5", catalog.ProfileVersion);
        Assert.Equal(
            PricingCatalog.Materials.Values.Where(static material => material.Process == PrintProcess.Fdm)
                .Select(static material => material.Key).OrderBy(static key => key, StringComparer.Ordinal),
            manifest["materials"]!.AsObject().Select(static property => property.Key)
                .OrderBy(static key => key, StringComparer.Ordinal));
        Assert.True(catalog.ResolvePolicy("PLA", BuildPreference.Standard).AutomaticPricingEligible);
        Assert.True(catalog.ResolvePolicy("PETG", BuildPreference.Quality).AutomaticPricingEligible);
        Assert.False(catalog.ResolvePolicy("M68", BuildPreference.Standard).AutomaticPricingEligible);
        Assert.False(catalog.ResolvePolicy("UNKNOWN", BuildPreference.Standard).AutomaticPricingEligible);

        // Source 68aab: retained filament cooling limits resolve independently for every admitted build.
        (string Material, double LayerSeconds, double CoolingSpeed)[] coolingCases =
        [
            ("PLA", 4, 20),
            ("PETG", 12, 20),
            ("PETG-ESD", 2, 20),
            ("ABS", 3, 20),
            ("ASA", 3, 20),
            ("HIPS", 6, 20),
            ("PA12", 8, 10),
            ("PVA", 7, 20),
        ];
        foreach (var cooling in coolingCases)
        {
            foreach (BuildPreference build in Enum.GetValues<BuildPreference>())
            {
                Assert.True(catalog.TryResolveTrustedProfile(cooling.Material, build, out var profile));
                Assert.NotNull(profile);
                Assert.Equal(cooling.Material, profile.MaterialId);
                Assert.Equal(cooling.LayerSeconds, profile.Motion.MinimumLayerTimeSeconds, 6);
                Assert.Equal(cooling.CoolingSpeed, profile.Motion.MinimumCoolingSpeedMmPerSecond, 6);
            }
        }

        // Source ea074: packaged TPU settings and machine preparation retain independent source literals.
        Assert.True(catalog.TryResolveTrustedProfile("TPU", BuildPreference.Standard, out var tpuProfile));
        Assert.NotNull(tpuProfile);
        Assert.Equal(1.43, PricingCatalog.ResolveMaterial("TPU")!.DensityGramsPerCm3, 6);
        Assert.Equal(1.43, tpuProfile.MaterialDensityGramsPerCm3, 6);
        Assert.Equal(2.5, tpuProfile.MaximumVolumetricFlowMm3PerSecond!.Value, 6);
        Assert.Equal(260, tpuProfile.Motion.MachinePrepareCompensationSeconds, 6);
        Assert.Equal(29, tpuProfile.Motion.MachineLoadFilamentSeconds, 6);
        MotionLedger tpuPreparation = FeatureMotionEstimator.Estimate([], tpuProfile);
        Assert.Equal(409, tpuPreparation.PreparationSeconds, 6);
        JsonNode machineSettings = manifest["machine"]!["settings"]!;
        Assert.Equal("bambu-fdm-machine-common-01.10.02.76",
            machineSettings["preparation_sequence_version"]!.GetValue<string>());
        Assert.Equal("260", machineSettings["machine_prepare_compensation_time"]!.GetValue<string>());
        Assert.Equal("29", machineSettings["machine_load_filament_time"]!.GetValue<string>());
        Assert.Equal("D4A1B8E69A0AAE8EDFA9CB1E7A314C5A9FB0E2C5BBF6F411FE231BA3068E2AFD",
            manifest["materials"]!["TPU"]!["sourceArtifactSha256"]!.GetValue<string>());

        // Source cebda binds admitted PA12 settings to these exact packaged manufacturer-evidence bytes.
        using var source = typeof(FdmRuntimeProfileCatalog).Assembly.GetManifestResourceStream(
            "Legacy.Maliev.Web.Application.Pricing.Profiles.Sources.esun-epa12-manufacturer-profile.v1.json");
        Assert.NotNull(source);
        using var buffer = new MemoryStream();
        source.CopyTo(buffer);
        byte[] evidenceBytes = buffer.ToArray();
        Assert.Equal("139D404B9CF0E0C97E43DDD803BD379B0B0F5FD0BAA0D25BC75C1D110A146746",
            Convert.ToHexString(SHA256.HashData(evidenceBytes)));
        var evidence = JsonNode.Parse(System.Text.Encoding.UTF8.GetString(evidenceBytes))!.AsObject();
        Assert.Equal("PA12", evidence["materialKey"]!.GetValue<string>());
        Assert.Equal("PA12 — eSUN ePA12", evidence["profileId"]!.GetValue<string>());
        Assert.Equal("manufacturer_spec_conservative", evidence["sourceAuthority"]!.GetValue<string>());
        Assert.Equal(1.012, evidence["manufacturerSettings"]!["densityGramsPerCm3"]!.GetValue<double>(), 6);
        Assert.Equal(8, evidence["approvedConservativeRuntimeSettings"]!["maximumVolumetricFlowMm3PerSecond"]!.GetValue<double>(), 6);
        Assert.Equal(8, evidence["approvedConservativeRuntimeSettings"]!["minimumLayerTimeSeconds"]!.GetValue<double>(), 6);
        Assert.Equal(10, evidence["approvedConservativeRuntimeSettings"]!["minimumCoolingSpeedMmPerSecond"]!.GetValue<double>(), 6);
        JsonNode admittedPa12 = manifest["materials"]!["PA12"]!;
        Assert.Equal("139D404B9CF0E0C97E43DDD803BD379B0B0F5FD0BAA0D25BC75C1D110A146746",
            admittedPa12["sourceArtifactSha256"]!.GetValue<string>());
        Assert.Equal("PA12 — eSUN ePA12", admittedPa12["profileId"]!.GetValue<string>());
        Assert.Equal("manufacturer_spec_conservative", admittedPa12["sourceAuthority"]!.GetValue<string>());
        foreach (BuildPreference build in Enum.GetValues<BuildPreference>())
        {
            Assert.True(catalog.ResolvePolicy("PA12", build).AutomaticPricingEligible);
            Assert.True(catalog.TryResolveTrustedProfile("PA12", build, out var profile));
            Assert.NotNull(profile);
            Assert.Equal("PA12", profile.MaterialId);
            Assert.Equal(1.012, profile.MaterialDensityGramsPerCm3, 6);
            Assert.Equal(8, profile.MaximumVolumetricFlowMm3PerSecond!.Value, 6);
            Assert.Equal(8, profile.Motion.MinimumLayerTimeSeconds, 6);
            Assert.Equal(10, profile.Motion.MinimumCoolingSpeedMmPerSecond, 6);
        }
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
