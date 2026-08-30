using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Application.Pricing;
using Legacy.Maliev.Web.Infrastructure;

namespace Legacy.Maliev.Web.Tests;

public sealed class InstantQuotationFulfillmentMappingTests
{
    [Theory]
    [InlineData("CASTWAX", "Castable Wax Resin")]
    [InlineData("TPU", "TPU (Shore 95A)")]
    [InlineData("PC", "Polycarbonate (PC)")]
    public void DatabaseMaterialName_PreservesProductionCatalogMappings(string key, string expected) =>
        Assert.Equal(expected, InstantQuotationFulfillmentClient.DatabaseMaterialName(key));

    [Theory]
    [InlineData("C:\\untrusted\\")]
    [InlineData("/untrusted/")]
    public void OrderName_UsesSafeFileNameAndOrderServiceLimit(string untrustedPath)
    {
        var name = new string('a', 110) + ".stl";

        var result = InstantQuotationFulfillmentClient.OrderName($"{untrustedPath}{name}");

        Assert.Equal(100, result.Length);
        Assert.DoesNotContain("untrusted", result, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildOrderComment_PreservesManufacturingMetadataWithoutStorageIdentifiers()
    {
        var geometry = AuthoritativeInstantQuotationGeometry.RestoreFromProtectedSession(
            1,
            new string('a', 64),
            10,
            20,
            30,
            1_000,
            700,
            Enumerable.Repeat(100.0, 64).ToArray(),
            Enumerable.Repeat(60.0, 64).ToArray(),
            12,
            1,
            true,
            false,
            false,
            0.8);
        var part = new InstantQuotationPart(
            Guid.NewGuid(),
            "part.stl",
            new InstantQuotationUploadReference(Guid.NewGuid().ToString("D")),
            geometry,
            new InstantQuotationPartConfiguration("ABS", "Black", 2, BuildPreference.Strength));
        var quote = new InstantQuotationPricingService().Quote(new InstantQuotationOrderState([part])).Parts[0];

        var result = InstantQuotationFulfillmentClient.BuildOrderComment(
            new string('f', 64), 0, part, quote, 7, "Check tapped holes");

        Assert.Contains("Material key: ABS", result, StringComparison.Ordinal);
        Assert.Contains("Build: Strength", result, StringComparison.Ordinal);
        Assert.Contains("Dimensions: 10 x 20 x 30 mm", result, StringComparison.Ordinal);
        Assert.Contains("Quantity: 2", result, StringComparison.Ordinal);
        Assert.Contains("Submitted unit estimate:", result, StringComparison.Ordinal);
        Assert.Contains("Total print time:", result, StringComparison.Ordinal);
        Assert.Contains("Estimated lead time: up to 7 days", result, StringComparison.Ordinal);
        Assert.Contains("Customer notes: Check tapped holes", result, StringComparison.Ordinal);
        Assert.DoesNotContain(part.UploadReference.Value, result, StringComparison.Ordinal);
        Assert.DoesNotContain(geometry.Sha256, result, StringComparison.Ordinal);
    }
}
