using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Application.Pricing;
using Legacy.Maliev.Web.Components.Pages.InstantQuotation;

namespace Legacy.Maliev.Web.Tests;

public sealed class InstantQuotationBulkSavingsTests
{
    [Fact]
    public void MatchingBulkQuoteShowsOnlyPositiveSavingsAgainstSinglePiecePrice()
    {
        var part = Part(10, "PLA", BuildPreference.Quality, 900,
            new BulkTier { MinQuantity = 1, UnitPrice = 1_000 },
            new BulkTier { MinQuantity = 10, UnitPrice = 900, Active = true });

        Assert.Equal(1_000, InstantQuotationBulkSavings.Calculate(part, quantityBeingEdited: false));
    }

    [Theory]
    [InlineData(1, 1_000, 1_000)]
    [InlineData(10, 900, 900)]
    [InlineData(10, 1_100, 1_100)]
    public void NoPositiveBulkReductionShowsNoSavings(int quantity, double singlePrice, double activePrice)
    {
        var part = Part(quantity, "PLA", BuildPreference.Quality, activePrice,
            new BulkTier { MinQuantity = 1, UnitPrice = singlePrice, Active = quantity == 1 },
            new BulkTier { MinQuantity = 10, UnitPrice = activePrice, Active = quantity == 10 });

        Assert.Null(InstantQuotationBulkSavings.Calculate(part, quantityBeingEdited: false));
    }

    [Fact]
    public void EditingQuantityHidesOldSavingsBeforeRepricingCompletes()
    {
        var part = Part(10, "PLA", BuildPreference.Quality, 900,
            new BulkTier { MinQuantity = 1, UnitPrice = 1_000 },
            new BulkTier { MinQuantity = 10, UnitPrice = 900, Active = true });

        Assert.Null(InstantQuotationBulkSavings.Calculate(part, quantityBeingEdited: true));
    }

    [Fact]
    public void QuoteForPreviousMaterialQuantityOrBuildPreferenceShowsNoSavings()
    {
        var part = Part(10, "PLA", BuildPreference.Quality, 900,
            new BulkTier { MinQuantity = 1, UnitPrice = 1_000 },
            new BulkTier { MinQuantity = 10, UnitPrice = 900, Active = true });

        Assert.Null(InstantQuotationBulkSavings.Calculate(part with
        {
            Configuration = part.Configuration with { MaterialKey = "PETG" },
        }, quantityBeingEdited: false));
        Assert.Null(InstantQuotationBulkSavings.Calculate(part with
        {
            Configuration = part.Configuration with { Quantity = 11 },
        }, quantityBeingEdited: false));
        Assert.Null(InstantQuotationBulkSavings.Calculate(part with
        {
            Configuration = part.Configuration with { BuildPreference = BuildPreference.Strength },
        }, quantityBeingEdited: false));
        Assert.Null(InstantQuotationBulkSavings.Calculate(part with
        {
            PartId = Guid.NewGuid(),
        }, quantityBeingEdited: false));
    }

    [Fact]
    public void MissingOrInvalidTiersShowNoSavingsWhileUnsampledQuantityKeepsItsActiveTier()
    {
        var part = Part(10, "PLA", BuildPreference.Quality, 900,
            new BulkTier { MinQuantity = 1, UnitPrice = 1_000 },
            new BulkTier { MinQuantity = 10, UnitPrice = 900, Active = true });

        Assert.Null(InstantQuotationBulkSavings.Calculate(part with { Quote = part.Quote! with { Tiers = [] } }, false));
        Assert.Null(InstantQuotationBulkSavings.Calculate(part with
        {
            Quote = part.Quote! with { Tiers = [new BulkTier { MinQuantity = 10, UnitPrice = 900, Active = true }] },
        }, false));
        Assert.Equal(1_000, InstantQuotationBulkSavings.Calculate(part with
        {
            Quote = part.Quote! with { UnitPrice = 875 },
        }, false));
        Assert.Null(InstantQuotationBulkSavings.Calculate(part with
        {
            Quote = part.Quote! with
            {
                Tiers = [new BulkTier { MinQuantity = 1, UnitPrice = double.NaN },
                    new BulkTier { MinQuantity = 10, UnitPrice = 900, Active = true }],
            },
        }, false));
    }

    private static InstantQuotationWorkflowPartViewModel Part(
        int quantity,
        string material,
        BuildPreference preference,
        double activePrice,
        params BulkTier[] tiers)
    {
        var id = Guid.NewGuid();
        return new InstantQuotationWorkflowPartViewModel(
            id,
            Guid.NewGuid(),
            "part.stl",
            null!,
            new InstantQuotationPartConfiguration(material, "Black", quantity, preference),
            new InstantQuotationPartQuote(
                id, material, "Black", quantity, PrintProcess.Fdm,
                1, 1, 1, 1, 1, activePrice, activePrice * quantity,
                false, 0, 0, tiers, preference, []));
    }
}
