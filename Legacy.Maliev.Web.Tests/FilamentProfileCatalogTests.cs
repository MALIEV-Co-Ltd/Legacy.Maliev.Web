using Maliev.AdditiveBenchmark;
using Legacy.Maliev.Web.Application.Pricing;
using System;
using System.IO;
using System.Linq;
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
        Assert.All(validation.Entries, entry => Assert.True(entry.OperatorApproved));
        Assert.Contains(validation.Entries, entry => entry.MaterialKey == "ABS-FR" && entry.ExactMaterialMatch);
        Assert.Contains(validation.Entries, entry => entry.MaterialKey == "PA12" && !entry.ExactMaterialMatch);
    }
}
