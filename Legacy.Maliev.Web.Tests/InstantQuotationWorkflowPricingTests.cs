using System.Reflection;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Application.Pricing;

namespace Legacy.Maliev.Web.Tests;

public sealed class InstantQuotationWorkflowPricingTests
{
    private static readonly IInstantQuotationPricingService PricingService = new InstantQuotationPricingService();

    public static TheoryData<string, PrintProcess, string> MaterialCompatibility => new()
    {
        { "PLA", PrintProcess.Fdm, "Red" },
        { "PLA-CF", PrintProcess.Fdm, "Black" },
        { "PETG", PrintProcess.Fdm, "#123ABC" },
        { "PETG-CF", PrintProcess.Fdm, "Natural" },
        { "PETG-ESD", PrintProcess.Fdm, "Black" },
        { "PET-CF", PrintProcess.Fdm, "Black" },
        { "ABS", PrintProcess.Fdm, "Silver" },
        { "ABS-FR", PrintProcess.Fdm, "Natural" },
        { "ASA", PrintProcess.Fdm, "Purple" },
        { "ASA-CF", PrintProcess.Fdm, "Black" },
        { "HIPS", PrintProcess.Fdm, "White" },
        { "PC", PrintProcess.Fdm, "Gray" },
        { "PC-FR", PrintProcess.Fdm, "Natural" },
        { "PC-ESD", PrintProcess.Fdm, "Black" },
        { "PA6", PrintProcess.Fdm, "White" },
        { "PA12", PrintProcess.Fdm, "Any" },
        { "PA-CF", PrintProcess.Fdm, "Natural" },
        { "TPU", PrintProcess.Fdm, "Clear" },
        { "PVA", PrintProcess.Fdm, "Natural" },
        { "M68", PrintProcess.Resin, "Gray" },
        { "K", PrintProcess.Resin, "Black" },
        { "G217", PrintProcess.Resin, "Clear" },
        { "F80", PrintProcess.Resin, "Translucent" },
        { "CASTWAX", PrintProcess.Resin, "Green" },
    };

    public static TheoryData<string, string[]> ExactMaterialColors => new()
    {
        { "PLA", ["Any", "Black", "White", "Gray", "Silver", "Red", "Orange", "Yellow", "Green", "Blue", "Purple", "Pink"] },
        { "PETG", ["Any", "Black", "White", "Gray", "Silver", "Red", "Orange", "Yellow", "Green", "Blue", "Purple", "Pink"] },
        { "ABS", ["Any", "Black", "White", "Gray", "Silver", "Red", "Orange", "Yellow", "Green", "Blue", "Purple", "Pink"] },
        { "ASA", ["Any", "Black", "White", "Gray", "Silver", "Red", "Orange", "Yellow", "Green", "Blue", "Purple", "Pink"] },
        { "HIPS", ["Black", "White"] },
        { "TPU", ["Any", "Black", "White", "Clear", "Red", "Blue"] },
        { "PC", ["Any", "Natural", "Black", "White", "Gray"] },
        { "PC-FR", ["Any", "Natural", "Black", "White", "Gray"] },
        { "PA6", ["Any", "Natural", "Black", "White", "Gray"] },
        { "PA12", ["Any", "Natural", "Black", "White", "Gray"] },
        { "ABS-FR", ["Any", "Natural", "Black", "White", "Gray"] },
        { "PLA-CF", ["Black", "Natural"] },
        { "PETG-CF", ["Black", "Natural"] },
        { "PET-CF", ["Black", "Natural"] },
        { "PA-CF", ["Black", "Natural"] },
        { "ASA-CF", ["Black", "Natural"] },
        { "PETG-ESD", ["Black", "Natural"] },
        { "PC-ESD", ["Black"] },
        { "PVA", ["Natural"] },
        { "M68", ["Gray", "Black", "White"] },
        { "K", ["Gray", "Black"] },
        { "G217", ["Clear"] },
        { "F80", ["Black", "Translucent"] },
        { "CASTWAX", ["Green"] },
    };

    [Theory]
    [MemberData(nameof(MaterialCompatibility))]
    public void Catalog_PreservesExactMaterialKeysProcessesAndCompatibleColors(
        string materialKey,
        PrintProcess expectedProcess,
        string compatibleColor)
    {
        var material = PricingCatalog.ResolveMaterial(materialKey);

        Assert.NotNull(material);
        Assert.Equal(materialKey, material.Key);
        Assert.Equal(expectedProcess, material.Process);
        Assert.True(PricingCatalog.IsColorSupported(materialKey, compatibleColor));
    }

    [Theory]
    [InlineData("PLA", "#00ff7F", true)]
    [InlineData("TPU", "#00ff7F", true)]
    [InlineData("PC", "#00ff7F", true)]
    [InlineData("PLA-CF", "#00ff7F", false)]
    [InlineData("M68", "#00ff7F", false)]
    [InlineData("M68", "Clear", false)]
    [InlineData("G217", "Clear", true)]
    [InlineData("CASTWAX", "Green", true)]
    [InlineData("CASTWAX", "green", false)]
    [InlineData("PLA", "#12345", false)]
    public void Catalog_EnforcesCapturedColorWireValuesAndCustomColorRules(
        string materialKey,
        string color,
        bool expected)
    {
        Assert.Equal(expected, PricingCatalog.IsColorSupported(materialKey, color));
    }

    [Theory]
    [MemberData(nameof(ExactMaterialColors))]
    public void Catalog_PreservesEveryExactMaterialColorList(string materialKey, string[] expectedColors)
    {
        Assert.Equal(expectedColors, PricingCatalog.MaterialColors[materialKey]);
    }

    [Fact]
    public void Catalog_PreservesExactCustomCapableSet()
    {
        var expected = new[] { "PLA", "PETG", "ABS", "ASA", "TPU", "PC", "PC-FR", "PA6", "PA12", "ABS-FR" };
        var actual = PricingCatalog.Materials.Keys
            .Where(material => PricingCatalog.IsColorSupported(material, "#123ABC"))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected.Order(StringComparer.Ordinal), actual);
    }

    [Fact]
    public void Catalog_PreservesExactMaterialAndColorKeySets()
    {
        var expected = new[]
        {
            "PLA", "PLA-CF", "PETG", "PETG-CF", "PETG-ESD", "PET-CF", "ABS", "ABS-FR",
            "ASA", "ASA-CF", "HIPS", "PC", "PC-FR", "PC-ESD", "PA6", "PA12", "PA-CF",
            "TPU", "PVA", "M68", "K", "G217", "F80", "CASTWAX",
        };

        Assert.Equal(expected.Order(StringComparer.Ordinal), PricingCatalog.Materials.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(expected.Order(StringComparer.Ordinal), PricingCatalog.MaterialColors.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Catalog_CollectionsCannotBeMutatedByCallers()
    {
        var materials = Assert.IsAssignableFrom<IDictionary<string, MaterialInfo>>(PricingCatalog.Materials);
        var colors = Assert.IsAssignableFrom<IDictionary<string, IReadOnlyList<string>>>(PricingCatalog.MaterialColors);

        Assert.Throws<NotSupportedException>(() => materials["PLA"] = materials["PETG"]);
        Assert.Throws<NotSupportedException>(() => colors["PLA"] = ["Mutated"]);
        Assert.All(PricingCatalog.MaterialColors.Values, colors => Assert.False(colors is IList<string>));
        Assert.False(PricingCatalog.DiscountTiers is IList<DiscountTier>);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(9, 1)]
    [InlineData(10, 10)]
    [InlineData(49, 10)]
    [InlineData(50, 50)]
    [InlineData(99, 50)]
    [InlineData(100, 100)]
    [InlineData(1000, 100)]
    public void Quote_PreservesQuantityRangeAndTierBoundaries(int quantity, int expectedTier)
    {
        var quote = PricingService.Quote(State(Part("PLA", "Black", quantity)));

        Assert.Equal(quantity, quote.Parts.Single().Quantity);
        Assert.True(quote.Parts.Single().Tiers.Single(tier => tier.MinQuantity == expectedTier).Active);
        Assert.Equal(0, quote.Parts.Single().Subtotal % 100, 2);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1001)]
    public void Quote_RejectsQuantitiesOutsideCapturedRange(int quantity)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => PricingService.Quote(State(Part("PLA", "Black", quantity))));
    }

    [Fact]
    public void Quote_AcceptsOnlyGeometryFromSuccessfulUploadResult()
    {
        var upload = InstantQuotationUploadResult.Succeeded(
            "upload-operation",
            new InstantQuotationUploadReference("opaque-upload"),
            Claim().Sha256);
        var id = Guid.NewGuid();
        var part = new InstantQuotationPart(
            id,
            "part.stl",
            upload.UploadReference!,
            AuthoritativeInstantQuotationGeometry.FromCompletedLegacyUpload(upload, Claim())!,
            new InstantQuotationPartConfiguration("PLA", "Black", 1));

        Assert.Single(PricingService.Quote(State(part)).Parts);
    }

    [Fact]
    public void Quote_RejectsIncompatibleMaterialColor()
    {
        Assert.Throws<ArgumentException>(
            () => PricingService.Quote(State(Part("G217", "Black", 1))));
    }

    [Fact]
    public void Quote_MixedFdmAndResinPartsUsesDerivedShippingVatAndRounding()
    {
        var state = State(
            Part("PLA", "White", 9),
            Part("M68", "Gray", 2, heightMm: 12, volumeMm3: 6_000, footprintMm2: 225));

        var result = PricingService.Quote(state);
        var expectedLines = state.Parts.Select(ExpectedLine).ToArray();
        var expectedShipping = ShippingCalculator.CustomerShippingThb(
            expectedLines.Sum(line => line.Quote.WeightGramsPerUnit * line.Part.Configuration.Quantity),
            expectedLines.Sum(line => line.Quote.BoundingCm3PerUnit * line.Part.Configuration.Quantity));
        var expectedOrder = PricingEngine.QuoteOrder(
            expectedLines.Select(line => new OrderLine
            {
                Process = line.Quote.Process,
                Subtotal = line.Quote.Subtotal,
            }),
            expectedShipping);

        Assert.Equal(2, result.Parts.Count);
        Assert.Equal(PrintProcess.Fdm, result.Parts[0].Process);
        Assert.Equal(PrintProcess.Resin, result.Parts[1].Process);
        Assert.Equal(expectedOrder.ItemsSubtotal, result.ItemsSubtotal, 2);
        Assert.Equal(expectedOrder.Printing, result.Printing, 2);
        Assert.Equal(expectedShipping, result.ShippingCost, 2);
        Assert.Equal(expectedOrder.PriceBeforeVat, result.PriceBeforeVat, 2);
        Assert.Equal(expectedOrder.PriceBeforeVat * 0.07, result.Vat, 2);
        Assert.Equal(expectedOrder.FinalOrderPrice, result.FinalOrderPrice, 2);
        Assert.Equal(0, result.FinalOrderPrice % 5, 2);
    }

    [Fact]
    public void Quote_MixedFdmAndResinOrderUsesResinMinimumFloorRule()
    {
        var result = PricingService.Quote(State(
            Part("PLA", "White", 1, heightMm: 0.1, volumeMm3: 1, footprintMm2: 1),
            Part("M68", "Gray", 1, heightMm: 0.1, volumeMm3: 1, footprintMm2: 1)));

        Assert.Equal(Math.Max(500, result.ItemsSubtotal), result.Printing, 2);
    }

    [Fact]
    public void Quote_DerivesDeterministicLeadTimeFromTotalPrintMinutes()
    {
        var state = State(
            Part("PLA", "Black", 100),
            Part("K", "Gray", 4, heightMm: 18, volumeMm3: 7_500, footprintMm2: 300));

        var result = PricingService.Quote(state);
        var totalPrintMinutes = result.Parts.Sum(part => part.PrintTimeMinutesPerUnit * part.Quantity);
        var expectedMinimumDays = Math.Max(1, (int)Math.Ceiling(totalPrintMinutes / 1_440));

        Assert.Equal(expectedMinimumDays, result.LeadTimeMinimumDays);
        Assert.Equal(expectedMinimumDays + 2, result.LeadTimeMaximumDays);
    }

    [Fact]
    public void PricingInputShape_DoesNotAcceptBrowserProvidedDerivedValues()
    {
        var forbiddenFragments = new[] { "Subtotal", "Weight", "PrintTime", "OrderTotal", "LeadTime", "Vat", "Shipping" };
        var inputTypes = new[]
        {
            typeof(InstantQuotationOrderState),
            typeof(InstantQuotationPart),
            typeof(InstantQuotationGeometry),
            typeof(InstantQuotationPartConfiguration),
        };

        var inputPropertyNames = inputTypes
            .SelectMany(type => type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            .Select(property => property.Name)
            .ToArray();

        Assert.DoesNotContain(
            inputPropertyNames,
            propertyName => forbiddenFragments.Any(
                fragment => propertyName.Contains(fragment, StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(
            typeof(InstantQuotationOrderQuote),
            typeof(IInstantQuotationPricingService).GetMethod(nameof(IInstantQuotationPricingService.Quote))!.ReturnType);
    }

    private static InstantQuotationOrderState State(params InstantQuotationPart[] parts) => new(parts);

    private static InstantQuotationPart Part(
        string materialKey,
        string color,
        int quantity,
        double heightMm = 30,
        double volumeMm3 = 20_000,
        double footprintMm2 = 400)
    {
        var id = Guid.NewGuid();
        return new InstantQuotationPart(
            id,
            $"{id:N}.stl",
            new InstantQuotationUploadReference($"upload-{id:N}"),
            Promote(heightMm, volumeMm3, footprintMm2),
            new InstantQuotationPartConfiguration(materialKey, color, quantity));
    }

    private static InstantQuotationGeometry Geometry(
        double heightMm = 30,
        double volumeMm3 = 20_000,
        double footprintMm2 = 400) => new(
            heightMm,
            volumeMm3,
            footprintMm2,
            Enumerable.Repeat(volumeMm3 / heightMm, 40).ToArray(),
            Enumerable.Repeat(80.0, 40).ToArray(),
            FacetCount: 1_024,
            BodyCount: 1,
            IsManifold: true);

    private static InstantQuotationGeometryClaim Claim(
        double heightMm = 30,
        double volumeMm3 = 20_000,
        double footprintMm2 = 400)
    {
        var physicallyValidFootprintMm2 = Math.Max(footprintMm2, volumeMm3 / heightMm);
        var side = Math.Sqrt(physicallyValidFootprintMm2);
        return new InstantQuotationGeometryClaim(
            1,
            new string('a', 64),
            side,
            side,
            heightMm,
            volumeMm3,
            2 * footprintMm2 + (4 * side * heightMm),
            Enumerable.Repeat(volumeMm3 / heightMm, 64).ToArray(),
            Enumerable.Repeat(side * 4, 64).ToArray(),
            1_024,
            1,
            true,
            false,
            false,
            0.8);
    }

    private static AuthoritativeInstantQuotationGeometry Promote(
        double heightMm,
        double volumeMm3,
        double footprintMm2)
    {
        var claim = Claim(heightMm, volumeMm3, footprintMm2);
        var upload = InstantQuotationUploadResult.Succeeded(
            "operation",
            new InstantQuotationUploadReference("opaque"),
            claim.Sha256);
        return AuthoritativeInstantQuotationGeometry.FromCompletedLegacyUpload(upload, claim)!;
    }

    private static (InstantQuotationPart Part, ItemQuote Quote) ExpectedLine(InstantQuotationPart part)
    {
        var geometry = part.Geometry;
        var quote = PricingEngine.QuoteItem(
            new GeometryInput
            {
                HeightMm = geometry.HeightMm,
                VolumeMm3 = geometry.VolumeMm3,
                FootprintMm2 = geometry.FootprintMm2,
                AreaProfileMm2 = geometry.AreaProfileMm2,
                PerimeterProfileMm = geometry.PerimeterProfileMm,
            },
            PricingCatalog.ResolveMaterial(part.Configuration.MaterialKey)!,
            part.Configuration.Quantity);
        return (part, quote);
    }
}
