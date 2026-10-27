using Maliev.AdditiveBenchmark;
using Legacy.Maliev.Web.Application.Pricing;
using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Xunit;

namespace Legacy.Maliev.Web.Tests;

/// <summary>
/// Guards coverage and production gating for the versioned Bambu Studio filament catalog.
/// </summary>
public sealed class FilamentProfileCatalogTests
{
    private static readonly string CatalogPath = Path.Combine(
        AppContext.BaseDirectory,
        "TestAssets",
        "AdditiveBenchmark",
        "filament-profile-catalog.v1.json");

    // Historical analytical witness only; never a worker-readiness grant.
    private static readonly string HistoricalCatalogPath = Path.Combine(
        AppContext.BaseDirectory,
        "TestAssets",
        "AdditiveBenchmark",
        "filament-profile-catalog.historical-analytical.v1.json");

    /// <summary>
    /// Every FDM material offered by instant quotation must have an explicit catalog decision.
    /// </summary>
    [Fact]
    public void Catalog_CoversEverySupportedFdmMaterialExactlyOnce()
    {
        string[] expectedMaterialKeys = PricingCatalog.Materials.Values
            .Where(material => material.Process == PrintProcess.Fdm)
            .Select(material => material.Key)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();

        FilamentProfileCatalogValidation validation = FilamentProfileCatalogValidator.Validate(
            File.ReadAllText(CatalogPath),
            expectedMaterialKeys);

        Assert.True(validation.IsValid, string.Join(Environment.NewLine, validation.Errors));
        Assert.Equal(expectedMaterialKeys, validation.MaterialKeys.OrderBy(key => key, StringComparer.Ordinal));
    }

    /// <summary>
    /// Owner-approved profile evidence remains ineligible until the isolated worker is active.
    /// </summary>
    [Fact]
    public void Catalog_ApprovedProfiles_RemainDisabledUntilWorkerActivation()
    {
        FilamentProfileCatalogValidation validation = FilamentProfileCatalogValidator.Validate(
            File.ReadAllText(CatalogPath),
            PricingCatalog.Materials.Values
                .Where(material => material.Process == PrintProcess.Fdm)
                .Select(material => material.Key));

        Assert.True(validation.IsValid, string.Join(Environment.NewLine, validation.Errors));
        Assert.DoesNotContain(validation.Entries, entry => entry.AutomationEligible);
        // Source-derived runtime profiles do not inherit the historical PC-ESD
        // benchmark operator approval. Keep the two new decisions explicitly blocked.
        Assert.All(validation.Entries, entry =>
        {
            if (entry.MaterialKey is "PA612-ESD" or "ABS-ESD")
            {
                Assert.False(entry.OperatorApproved);
                Assert.False(entry.ExactMaterialMatch);
                Assert.Null(entry.ProfileId);
                Assert.Null(entry.ResolvedSha256);
            }
            else
            {
                Assert.True(entry.OperatorApproved);
            }
        });
        Assert.Contains(validation.Entries, entry => entry.MaterialKey == "ABS-FR" && entry.ExactMaterialMatch);
        Assert.Contains(validation.Entries, entry => entry.MaterialKey == "PA12" && !entry.ExactMaterialMatch);
    }

    /// <summary>
    /// Approved exact historical profiles are eligible for analytical simulation without an isolated vendor worker.
    /// </summary>
    [Fact]
    public void Catalog_ApprovedExactProfiles_AreEligibleForAnalyticalSimulation()
    {
        FilamentProfileCatalogValidation validation = FilamentProfileCatalogValidator.Validate(
            File.ReadAllText(HistoricalCatalogPath),
            PricingCatalog.Materials.Values
                .Where(material => material.Process == PrintProcess.Fdm)
                .Select(material => material.Key));

        Assert.True(validation.IsValid, string.Join(Environment.NewLine, validation.Errors));
        Assert.Equal(
            new[] { "ABS", "ABS-FR", "ASA", "PA-CF", "PA6", "PC", "PET-CF", "PETG", "PETG-CF", "PETG-ESD", "PLA", "PLA-CF", "PVA", "TPU" },
            validation.Entries.Where(entry => entry.AutomationEligible)
                .Select(entry => entry.MaterialKey).OrderBy(key => key, StringComparer.Ordinal));
        // Source-derived runtime profiles do not inherit the historical PC-ESD
        // benchmark operator approval. Keep the two new decisions explicitly blocked.
        Assert.All(validation.Entries, entry =>
        {
            if (entry.MaterialKey is "PA612-ESD" or "ABS-ESD")
            {
                Assert.False(entry.AutomationEligible);
                Assert.False(entry.OperatorApproved);
                Assert.False(entry.ExactMaterialMatch);
                Assert.Null(entry.ProfileId);
                Assert.Null(entry.ResolvedSha256);
            }
            else
            {
                Assert.True(entry.OperatorApproved);
            }
        });
        Assert.Contains(validation.Entries, entry => entry.MaterialKey == "ABS-FR" && entry.AutomationEligible);
        Assert.Contains(validation.Entries, entry => entry.MaterialKey == "PA12" && !entry.ExactMaterialMatch && !entry.AutomationEligible);
    }

    /// <summary>Eligible metadata cannot bypass an exact-match or operator-approval prerequisite.</summary>
    [Theory]
    [InlineData("operatorApproved")]
    [InlineData("exactMaterialMatch")]
    public void Catalog_EligibleProfileWithoutPrerequisite_IsInvalid(string prerequisite)
    {
        JsonObject catalog = JsonNode.Parse(File.ReadAllText(HistoricalCatalogPath))!.AsObject();
        JsonObject profile = catalog["entries"]!.AsArray()
            .Select(node => node!.AsObject())
            .Single(entry => entry["materialKey"]!.GetValue<string>() == "ABS-FR");
        profile[prerequisite] = false;
        Assert.True(profile["automationEligible"]!.GetValue<bool>());

        FilamentProfileCatalogValidation validation = FilamentProfileCatalogValidator.Validate(
            catalog.ToJsonString(),
            PricingCatalog.Materials.Values
                .Where(material => material.Process == PrintProcess.Fdm)
                .Select(material => material.Key));

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Errors,
            error => error.Contains("Material ABS-FR", StringComparison.Ordinal)
                && error.Contains("cannot be automationEligible", StringComparison.Ordinal));
    }

    /// <summary>Unreviewed or substituted profile evidence remains unavailable for automation.</summary>
    [Theory]
    [InlineData("ASA-CF")]
    [InlineData("HIPS")]
    [InlineData("PC-FR")]
    [InlineData("PA12")]
    [InlineData("PA612-ESD")]
    [InlineData("ABS-ESD")]
    public void Catalog_UnreviewedOrSubstitutedProfile_RemainsIneligible(string materialKey)
    {
        FilamentProfileCatalogValidation validation = FilamentProfileCatalogValidator.Validate(
            File.ReadAllText(HistoricalCatalogPath),
            PricingCatalog.Materials.Values
                .Where(material => material.Process == PrintProcess.Fdm)
                .Select(material => material.Key));

        Assert.True(validation.IsValid, string.Join(Environment.NewLine, validation.Errors));
        Assert.False(Assert.Single(validation.Entries, entry => entry.MaterialKey == materialKey).AutomationEligible);
    }
}
