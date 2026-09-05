using System.Text.Json;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Application.Pricing;
using Legacy.Maliev.Web.Components.Pages.InstantQuotation;

namespace Legacy.Maliev.Web.Tests;

public sealed class TechnicalFilamentMinimumPricingTests
{
    [Theory]
    [InlineData("PETG-CF", true)]
    [InlineData("PETG-ESD", true)]
    [InlineData("PET-CF", true)]
    [InlineData("PC", true)]
    [InlineData("PC-FR", true)]
    [InlineData("PC-ESD", true)]
    [InlineData("PA6", true)]
    [InlineData("PA12", true)]
    [InlineData("PA-CF", true)]
    [InlineData("TPU", true)]
    [InlineData("PVA", true)]
    [InlineData("PLA", false)]
    [InlineData("PLA-CF", false)]
    [InlineData("PETG", false)]
    [InlineData("ABS", false)]
    [InlineData("ASA", false)]
    [InlineData("ASA-CF", false)]
    public void Catalog_ClassifiesDryingRequiredFilaments(string materialKey, bool expected)
    {
        Assert.Equal(expected, PricingCatalog.ResolveMaterial(materialKey)!.RequiresDrying);
    }

    [Theory]
    [InlineData(1, 500, 500)]
    [InlineData(2, 250, 500)]
    [InlineData(3, 170, 510)]
    public void QuoteItem_DryingRequiredMaterial_AppliesPerLineMinimum(
        int quantity,
        double expectedUnitPrice,
        double expectedSubtotal)
    {
        var quote = PricingEngine.QuoteItem(TinyGeometry(), PricingCatalog.ResolveMaterial("PA6")!, quantity);

        Assert.Equal(expectedUnitPrice, quote.UnitPrice, 2);
        Assert.Equal(expectedSubtotal, quote.Subtotal, 2);
        Assert.True(quote.TechnicalFilamentMinimumApplied);
        Assert.Equal(500, quote.TechnicalFilamentMinimumPrice, 2);
        Assert.True(quote.TechnicalFilamentMinimumAdjustment > 0);
        Assert.Equal(quote.UnitPrice * quantity, quote.Subtotal, 2);
    }

    [Fact]
    public void QuoteItem_OrdinaryMaterial_DoesNotApplyTechnicalMinimum()
    {
        var quote = PricingEngine.QuoteItem(TinyGeometry(), PricingCatalog.ResolveMaterial("PLA")!, 1);

        Assert.True(quote.Subtotal < PricingCatalog.TechnicalFilamentMinimumPrice);
        Assert.False(quote.TechnicalFilamentMinimumApplied);
        Assert.Equal(0, quote.TechnicalFilamentMinimumPrice, 2);
        Assert.Equal(0, quote.TechnicalFilamentMinimumAdjustment, 2);
    }

    [Fact]
    public void QuoteItem_DryingRequiredMaterialAboveFloor_DoesNotApplyAdjustment()
    {
        var quote = PricingEngine.QuoteItem(TinyGeometry(), PricingCatalog.ResolveMaterial("PA6")!, 100);

        Assert.True(quote.Subtotal >= PricingCatalog.TechnicalFilamentMinimumPrice);
        Assert.False(quote.TechnicalFilamentMinimumApplied);
        Assert.Equal(0, quote.TechnicalFilamentMinimumAdjustment, 2);
    }

    [Fact]
    public void QuoteOrder_TwoSmallTechnicalParts_EachCarriesItsOwnMinimum()
    {
        var first = PricingEngine.QuoteItem(TinyGeometry(), PricingCatalog.ResolveMaterial("PA6")!, 1);
        var second = PricingEngine.QuoteItem(TinyGeometry(), PricingCatalog.ResolveMaterial("PETG-CF")!, 1);
        var order = PricingEngine.QuoteOrder(
            [
                new OrderLine { Process = first.Process, Subtotal = first.Subtotal },
                new OrderLine { Process = second.Process, Subtotal = second.Subtotal },
            ],
            0);

        Assert.Equal(1_000, order.ItemsSubtotal, 2);
        Assert.Equal(1_000, order.Printing, 2);
        Assert.Equal(0, order.MinimumOrderSurcharge, 2);
    }

    [Fact]
    public void EstimateContract_ReturnsExplicitTechnicalFilamentAdjustment()
    {
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(
            InstantQuotationCalculator.GetEstimate("PA6", 1, 1, 1, "1", "4", "THB", 1)));
        var root = json.RootElement;

        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.Equal(500, root.GetProperty("unitPrice").GetDouble(), 2);
        Assert.Equal(500, root.GetProperty("subtotal").GetDouble(), 2);
        Assert.True(root.GetProperty("technicalFilamentMinimumApplied").GetBoolean());
        Assert.Equal(500, root.GetProperty("technicalFilamentMinimumPrice").GetDouble(), 2);
        Assert.True(root.GetProperty("technicalFilamentMinimumAdjustment").GetDouble() > 0);
    }

    [Fact]
    public void WorkflowContracts_DiscloseTechnicalFilamentAdjustment()
    {
        var review = ReadWorkspaceFile("Legacy.Maliev.Web", "Components", "Pages", "InstantQuotation", "InstantQuotationReview.razor");
        var workflow = ReadWorkspaceFile("Legacy.Maliev.Web", "Components", "Pages", "InstantQuotation", "InstantQuotationWorkflow.razor");
        var preliminary = ReadWorkspaceFile("Legacy.Maliev.Web", "Components", "Pages", "InstantQuotation", "InstantQuotationPreliminaryQuotation.razor");

        Assert.Contains("TechnicalFilamentMinimumApplied", review, StringComparison.Ordinal);
        Assert.Contains("Technical filament preparation minimum included", review, StringComparison.Ordinal);
        Assert.Contains("TechnicalFilamentMinimumApplied", workflow, StringComparison.Ordinal);
        Assert.Contains("TechnicalFilamentMinimumAdjustment", preliminary, StringComparison.Ordinal);
    }

    private static GeometryInput TinyGeometry() => new()
    {
        HeightMm = 1,
        VolumeMm3 = 1,
        FootprintMm2 = 1,
        AreaProfileMm2 = [1],
        PerimeterProfileMm = [4],
    };

    private static string ReadWorkspaceFile(params string[] pathSegments)
    {
        var relativePath = Path.Combine(pathSegments);
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }
        }

        throw new FileNotFoundException($"Unable to find workspace file: {relativePath}");
    }
}
