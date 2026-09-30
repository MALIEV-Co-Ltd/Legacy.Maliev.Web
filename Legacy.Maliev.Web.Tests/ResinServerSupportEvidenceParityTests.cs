using Legacy.Maliev.Web.Application.Pricing;
using Legacy.Maliev.Web.Application;
using Microsoft.AspNetCore.DataProtection;
using System.Globalization;
using System.Text.Json;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Synthetic estimator arithmetic contracts, not manufacturing calibration proof.</summary>
public sealed class ResinServerSupportEvidenceParityTests
{
    [Theory]
    [InlineData(64)]
    [InlineData(24)]
    public void EstimateResin_ExplicitZeroDemand_ChargesModelAndFifteenPercentWasteOnly(int count)
    {
        var result = PrintTimeCalculator.EstimateResin(Geometry(new double[count]), Evidence());

        Assert.Equal(5, result.ModelResinMl, 6);
        Assert.Equal(0, result.SupportResinMl, 6);
        Assert.Equal(0, result.RaftResinMl, 6);
        Assert.Equal(.75, result.WasteResinMl, 6);
        Assert.Equal(5.75, result.TotalResinMl, 6);
        Assert.Equal(48.02, result.PrintMinutes, 6);
    }

    [Theory]
    [InlineData(64)]
    [InlineData(24)]
    public void EstimateResin_LastUnsupportedSample_ProjectsSupportAndAddsRaftLayers(int count)
    {
        var samples = new double[count];
        samples[^1] = 120;
        var result = PrintTimeCalculator.EstimateResin(Geometry(samples), Evidence());

        Assert.Equal(.1791, result.SupportResinMl, 6);
        Assert.Equal(.55, result.RaftResinMl, 6);
        Assert.Equal(.859365, result.WasteResinMl, 6);
        Assert.Equal(6.588465, result.TotalResinMl, 6);
        Assert.Equal(52.52, result.PrintMinutes, 6);
    }

    [Fact]
    public void EstimateResin_OverlappingDemands_CapsEnvelopeAtFootprintInsteadOfSummingTwice()
    {
        var samples = new double[64];
        samples[32] = 400;
        samples[63] = 400;
        var result = PrintTimeCalculator.EstimateResin(Geometry(samples), Evidence());

        // Demand layers 101 and 199: 101*500 + 98*400 mm2-layers, not .9ml.
        Assert.Equal(.67275, result.SupportResinMl, 6);
        Assert.Equal(.55, result.RaftResinMl, 6);
        Assert.Equal(.9334125, result.WasteResinMl, 7);
        Assert.Equal(7.1561625, result.TotalResinMl, 7);
        Assert.Equal(52.52, result.PrintMinutes, 6);
    }

    [Fact]
    public void EstimateResin_DemandExceedsFootprint_CapsSingleDemandBeforeProjection()
    {
        var samples = new double[64];
        samples[^1] = 900;
        var result = PrintTimeCalculator.EstimateResin(Geometry(samples), Evidence());

        Assert.Equal(.74625, result.SupportResinMl, 6);
        Assert.Equal(.9444375, result.WasteResinMl, 7);
        Assert.Equal(7.2406875, result.TotalResinMl, 7);
    }

    [Theory]
    [InlineData(64)]
    [InlineData(24)]
    public void EstimateResin_AbsentDemand_UsesNamedAreaGrowthEstimateNotExplicitZeros(int count)
    {
        var risingAreas = Enumerable.Range(0, count)
            .Select(index => 500d * index / (count - 1)).ToArray();
        var absent = new GeometryInput
        {
            HeightMm = 10,
            VolumeMm3 = 2500,
            FootprintMm2 = 500,
            AreaProfileMm2 = risingAreas,
        };
        var explicitZeros = new GeometryInput
        {
            HeightMm = 10,
            VolumeMm3 = 2500,
            FootprintMm2 = 500,
            AreaProfileMm2 = risingAreas,
            UnsupportedAreaProfileMm2 = new double[count],
        };

        var estimated = PrintTimeCalculator.EstimateResin(absent, Evidence());
        var direct = PrintTimeCalculator.EstimateResin(explicitZeros, Evidence());

        // Per-layer growth2.5, sum ceil(demandLayer/2)=10000, occupied density.15.
        Assert.Equal(.1875, estimated.SupportResinMl, 6);
        Assert.Equal(.55, estimated.RaftResinMl, 6);
        Assert.Equal(.485625, estimated.WasteResinMl, 6);
        Assert.Equal(3.723125, estimated.TotalResinMl, 6);
        Assert.Equal(52.52, estimated.PrintMinutes, 6);
        Assert.Equal(0, direct.SupportResinMl, 6);
        Assert.Equal(0, direct.RaftResinMl, 6);
        Assert.Equal(2.875, direct.TotalResinMl, 6);
        Assert.Equal(48.02, direct.PrintMinutes, 6);
    }

    [Theory]
    [InlineData("area")]
    [InlineData("area-absent-demand")]
    [InlineData("unsupported")]
    [InlineData("both")]
    public void EstimateResin_NullOptionalProfiles_RetainsValidatedAbsentSemantics(string missing)
    {
        var geometry = new GeometryInput
        {
            HeightMm = 10,
            VolumeMm3 = 5000,
            FootprintMm2 = 500,
            AreaProfileMm2 = missing is "area" or "area-absent-demand" or "both" ? null! : Enumerable.Repeat(500d, 64).ToArray(),
            UnsupportedAreaProfileMm2 = missing is "unsupported" or "both" ? null! : new double[missing == "area-absent-demand" ? 0 : 64],
        };
        Assert.True(AdditiveGeometryValidator.Validate(geometry, 1).IsValid);

        var result = PrintTimeCalculator.EstimateResin(geometry, Evidence());

        Assert.Equal(0, result.SupportResinMl, 6);
        Assert.Equal(0, result.RaftResinMl, 6);
        Assert.Equal(5.75, result.TotalResinMl, 6);
        Assert.Equal(48.02, result.PrintMinutes, 6);
    }

    [Fact]
    public void EstimateResin_ConstantAreaWithAbsentDemand_DoesNotInventSupportOrRaftFromRoundingNoise()
    {
        var geometry = new GeometryInput
        {
            HeightMm = 10,
            VolumeMm3 = 5000,
            FootprintMm2 = 500,
            AreaProfileMm2 = Enumerable.Repeat(500d, 64).ToArray(),
        };
        var result = PrintTimeCalculator.EstimateResin(geometry, Evidence());

        Assert.Equal(0, result.SupportResinMl);
        Assert.Equal(0, result.RaftResinMl);
        Assert.Equal(5.75, result.TotalResinMl, 6);
        Assert.Equal(48.02, result.PrintMinutes, 6);
    }

    [Fact]
    public void EstimateResin_NullUnsupportedProfile_PreservesAreaGrowthRatherThanExplicitZeroDemand()
    {
        var geometry = new GeometryInput
        {
            HeightMm = 10,
            VolumeMm3 = 2500,
            FootprintMm2 = 500,
            AreaProfileMm2 = Enumerable.Range(0, 64).Select(index => 500d * index / 63).ToArray(),
            UnsupportedAreaProfileMm2 = null!,
        };
        Assert.True(AdditiveGeometryValidator.Validate(geometry, 1).IsValid);

        var result = PrintTimeCalculator.EstimateResin(geometry, Evidence());

        Assert.Equal(.1875, result.SupportResinMl, 6);
        Assert.Equal(.55, result.RaftResinMl, 6);
        Assert.Equal(3.723125, result.TotalResinMl, 6);
        Assert.Equal(52.52, result.PrintMinutes, 6);
    }

    [Fact]
    public void EstimateResin_NullAreaProfile_DoesNotDiscardExplicitUnsupportedDemand()
    {
        var samples = new double[64];
        samples[^1] = 120;
        var geometry = new GeometryInput
        {
            HeightMm = 10,
            VolumeMm3 = 5000,
            FootprintMm2 = 500,
            AreaProfileMm2 = null!,
            UnsupportedAreaProfileMm2 = samples,
        };
        Assert.True(AdditiveGeometryValidator.Validate(geometry, 1).IsValid);

        var result = PrintTimeCalculator.EstimateResin(geometry, Evidence());

        Assert.Equal(.1791, result.SupportResinMl, 6);
        Assert.Equal(.55, result.RaftResinMl, 6);
        Assert.Equal(6.588465, result.TotalResinMl, 6);
        Assert.Equal(52.52, result.PrintMinutes, 6);
    }

    [Fact]
    public void EstimateResin_CompleteSyntheticComposition_ProducesNonemptyEstimate()
    {
        var profile = Evidence();
        Assert.True(ResinBuildProfileEvidenceValidator.Assess(profile).IsReady);

        var result = PrintTimeCalculator.EstimateResin(Geometry(new double[64]), profile);

        Assert.Equal(5.75, result.TotalResinMl, 6);
    }

    [Theory]
    [InlineData("sourceMissing")]
    [InlineData("densityMissing")]
    [InlineData("densityZero")]
    [InlineData("densityNegative")]
    [InlineData("densityAboveOne")]
    [InlineData("ratioMissing")]
    [InlineData("ratioZero")]
    [InlineData("ratioNegative")]
    [InlineData("thicknessMissing")]
    [InlineData("thicknessZero")]
    [InlineData("thicknessNegative")]
    public void Assess_IncompleteSupportComposition_IsUnavailableNotAnInventedZero(string defect)
    {
        var profile = Evidence();
        switch (defect)
        {
            case "sourceMissing": profile.SupportSourceUri = null; break;
            case "densityMissing": profile.SupportEnvelopeDensity = null; break;
            case "densityZero": profile.SupportEnvelopeDensity = 0; break;
            case "densityNegative": profile.SupportEnvelopeDensity = -.1m; break;
            case "densityAboveOne": profile.SupportEnvelopeDensity = 1.1m; break;
            case "ratioMissing": profile.RaftAreaRatio = null; break;
            case "ratioZero": profile.RaftAreaRatio = 0; break;
            case "ratioNegative": profile.RaftAreaRatio = -.1m; break;
            case "thicknessMissing": profile.RaftThicknessMm = null; break;
            case "thicknessZero": profile.RaftThicknessMm = 0; break;
            case "thicknessNegative": profile.RaftThicknessMm = -.1m; break;
            default: throw new ArgumentOutOfRangeException(nameof(defect));
        }

        Assert.False(ResinBuildProfileEvidenceValidator.Assess(profile).IsReady);
        AssertUnavailable(PrintTimeCalculator.EstimateResin(Geometry(new double[64]), profile));
    }

    [Fact]
    public void EstimateResin_MissingEvidence_RemainsUnavailable()
    {
        Assert.False(ResinBuildProfileEvidenceValidator.Assess(null).IsReady);
        AssertUnavailable(PrintTimeCalculator.EstimateResin(Geometry(new double[64]), null));
    }

    [Fact]
    public void Assess_ExistingV1Catalog_RemainsReadyWithoutInventingSupportComposition()
    {
        var profile = ResinBuildProfileCatalog.ConservativeGeneric;

        Assert.True(ResinBuildProfileEvidenceValidator.Assess(profile).IsReady);
        Assert.Null(profile.SupportSourceUri);
        Assert.Null(profile.SupportEnvelopeDensity);
        Assert.Null(profile.RaftAreaRatio);
        Assert.Null(profile.RaftThicknessMm);
        AssertUnavailable(PrintTimeCalculator.EstimateResin(Geometry(new double[64]), profile));
    }

    [Fact]
    public void EstimateResin_NullGeometry_IsUnavailable()
    {
        AssertUnavailable(PrintTimeCalculator.EstimateResin(null, Evidence()));
    }

    [Theory]
    [InlineData("height", double.NaN)]
    [InlineData("height", double.PositiveInfinity)]
    [InlineData("height", -1d)]
    [InlineData("height", 0d)]
    [InlineData("volume", double.NaN)]
    [InlineData("volume", double.PositiveInfinity)]
    [InlineData("volume", -1d)]
    [InlineData("volume", 0d)]
    [InlineData("volume", 1_000_000_001d)]
    [InlineData("footprint", double.NaN)]
    [InlineData("footprint", double.PositiveInfinity)]
    [InlineData("footprint", -1d)]
    [InlineData("footprint", 0d)]
    [InlineData("footprint", 1_000_001d)]
    public void EstimateResin_InvalidScalar_IsUnavailableNotCoerced(string field, double value)
    {
        var geometry = new GeometryInput
        {
            HeightMm = field == "height" ? value : 10,
            VolumeMm3 = field == "volume" ? value : 5000,
            FootprintMm2 = field == "footprint" ? value : 500,
            AreaProfileMm2 = Enumerable.Repeat(500d, 64).ToArray(),
            UnsupportedAreaProfileMm2 = new double[64],
        };

        Assert.False(AdditiveGeometryValidator.Validate(geometry, 1).IsValid);
        AssertUnavailable(PrintTimeCalculator.EstimateResin(geometry, Evidence()));
    }

    [Theory]
    [InlineData("area", double.NaN)]
    [InlineData("area", double.PositiveInfinity)]
    [InlineData("area", -1d)]
    [InlineData("area", 1_000_001d)]
    [InlineData("unsupported", double.NaN)]
    [InlineData("unsupported", double.PositiveInfinity)]
    [InlineData("unsupported", -1d)]
    [InlineData("unsupported", 1_000_001d)]
    public void EstimateResin_InvalidSample_IsUnavailableNotDropped(string field, double value)
    {
        var areas = Enumerable.Repeat(500d, 64).ToArray();
        var demands = new double[64];
        (field == "area" ? areas : demands)[32] = value;
        var geometry = new GeometryInput
        {
            HeightMm = 10,
            VolumeMm3 = 5000,
            FootprintMm2 = 500,
            AreaProfileMm2 = areas,
            UnsupportedAreaProfileMm2 = demands,
        };

        Assert.False(AdditiveGeometryValidator.Validate(geometry, 1).IsValid);
        AssertUnavailable(PrintTimeCalculator.EstimateResin(geometry, Evidence()));
    }

    [Theory]
    [InlineData("area")]
    [InlineData("unsupported")]
    public void EstimateResin_ProfileExceedsExistingSampleBudget_IsUnavailableNotTruncated(string field)
    {
        var geometry = new GeometryInput
        {
            HeightMm = 10,
            VolumeMm3 = 5000,
            FootprintMm2 = 500,
            AreaProfileMm2 = Enumerable.Repeat(500d, field == "area" ? 10001 : 64).ToArray(),
            UnsupportedAreaProfileMm2 = new double[field == "unsupported" ? 10001 : 64],
        };

        Assert.False(AdditiveGeometryValidator.Validate(geometry, 1).IsValid);
        AssertUnavailable(PrintTimeCalculator.EstimateResin(geometry, Evidence()));
    }

    [Fact]
    public void EstimateResin_FiniteCycleAtLayerBudget_RemainsEligible()
    {
        var profile = Evidence();
        profile.LayerHeightMm = .0005m;
        Assert.True(ResinBuildProfileEvidenceValidator.Assess(profile).IsReady);

        var atBudget = PrintTimeCalculator.EstimateResin(Geometry(new double[64]), profile);
        Assert.Equal(5.75, atBudget.TotalResinMl, 6);
        Assert.Equal(4503.02, atBudget.PrintMinutes, 6);

    }

    [Fact]
    public void EstimateResin_FiniteCycleOneLayerBeyondBudget_IsUnavailableNotTruncated()
    {
        var profile = Evidence();
        profile.LayerHeightMm = .00049999m;
        Assert.True(ResinBuildProfileEvidenceValidator.Assess(profile).IsReady);
        AssertUnavailable(PrintTimeCalculator.EstimateResin(Geometry(new double[64]), profile));
    }

    [Fact]
    public void EstimateResin_InvalidCycle_RemainsUnavailable()
    {
        var profile = Evidence();
        profile.LayerHeightMm = 0;

        Assert.False(ResinBuildProfileEvidenceValidator.Assess(profile).IsReady);
        AssertUnavailable(PrintTimeCalculator.EstimateResin(Geometry(new double[64]), profile));
    }

    [Fact]
    public void EstimateResin_MaximumModelLayers_PreservesAllModelAndWaste()
    {
        var geometry = Geometry(new double[64], height: 1000, volume: 500000);
        var result = PrintTimeCalculator.EstimateResin(geometry, Evidence());

        Assert.Equal(500, result.ModelResinMl, 6);
        Assert.Equal(75, result.WasteResinMl, 6);
        Assert.Equal(575, result.TotalResinMl, 6);
        Assert.Equal(4503.02, result.PrintMinutes, 6);
    }

    [Fact]
    public void EstimateResin_MaximumModelHeightWithSupport_AddsTwentyBoundedRaftLayers()
    {
        var samples = new double[64];
        samples[^1] = 120;
        var result = PrintTimeCalculator.EstimateResin(
            Geometry(samples, height: 1000, volume: 500000), Evidence());

        Assert.Equal(17.9991, result.SupportResinMl, 6);
        Assert.Equal(.55, result.RaftResinMl, 6);
        Assert.Equal(77.782365, result.WasteResinMl, 6);
        Assert.Equal(596.331465, result.TotalResinMl, 6);
        Assert.Equal(4507.52, result.PrintMinutes, 6);
    }

    [Fact]
    public void EstimateResin_BeyondMaximumHeight_IsUnavailableNotTruncated()
    {
        AssertUnavailable(PrintTimeCalculator.EstimateResin(
            Geometry(new double[64], height: 1000.05), Evidence()));
    }

    [Fact]
    public void EstimateResin_MaximumHeightAbsentDemand_RetainsDenseAreaGrowthEnvelope()
    {
        var geometry = new GeometryInput
        {
            HeightMm = 1000,
            VolumeMm3 = 250000,
            FootprintMm2 = 500,
            AreaProfileMm2 = Enumerable.Range(0, 64).Select(index => 500d * index / 63).ToArray(),
        };
        var result = PrintTimeCalculator.EstimateResin(geometry, Evidence());

        // .025mm2 growth * sum ceil(layer/2)=100000000 * .15*.05 /1000.
        Assert.Equal(18.75, result.SupportResinMl, 6);
        Assert.Equal(.55, result.RaftResinMl, 6);
        Assert.Equal(40.395, result.WasteResinMl, 6);
        Assert.Equal(309.695, result.TotalResinMl, 6);
        Assert.Equal(4507.52, result.PrintMinutes, 6);
    }

    [Fact]
    public void EstimateResin_PositiveCycleBeyondLayerBudget_IsUnavailableNotTruncated()
    {
        var profile = Evidence();
        profile.LayerHeightMm = .00001m;
        Assert.True(ResinBuildProfileEvidenceValidator.Assess(profile).IsReady);

        AssertUnavailable(PrintTimeCalculator.EstimateResin(Geometry(new double[64]), profile));
    }

    [Fact]
    public void QuoteItem_RealResinConsumer_UnsupportedSamplesChangeChargeableMaterial()
    {
        var samples = new double[64];
        samples[^1] = 120;
        var quote = PricingEngine.QuoteItem(
            Geometry(samples), PricingCatalog.ResolveMaterial("M68")!, 1);

        // This is the current real consumer, not a scaffold result or a mock.
        Assert.Equal(6.588465, quote.MaterialPerUnit, 6);
    }

    [Fact]
    public void EstimateResin_FinalRaftBudgetEdge_PreservesEveryRaftLayer()
    {
        var profile = Evidence();
        profile.RaftThicknessMm = 1000;
        var samples = new double[64];
        samples[^1] = 120;

        var estimate = PrintTimeCalculator.EstimateResin(Geometry(samples), profile);

        Assert.Equal(550, estimate.RaftResinMl, 6);
        Assert.Equal(638.455965, estimate.TotalResinMl, 6);
        Assert.Equal(4548.02, estimate.PrintMinutes, 6);
    }

    [Theory]
    [InlineData("nextLayer")]
    [InlineData("hugeFinite")]
    public void EstimateResin_FinalRaftBudgetExceeded_IsUnavailableNotTruncated(string fixture)
    {
        var profile = Evidence();
        profile.RaftThicknessMm = fixture == "nextLayer" ? 1000.05m : decimal.MaxValue;
        var samples = new double[64];
        samples[^1] = 120;

        AssertUnavailable(PrintTimeCalculator.EstimateResin(Geometry(samples), profile));
    }

    [Fact]
    public void EstimateResin_HugePositiveFiniteCycle_DoesNotOverflowDecimalDivisionOrInventNormalTime()
    {
        var profile = Evidence();
        profile.LiftDistanceMm = decimal.MaxValue;
        profile.LiftSpeedMmPerSecond = .0000000000000000000000000001m;

        var estimate = PrintTimeCalculator.EstimateResin(Geometry(new double[64]), profile);

        // Deliberately outside plausible pricing-time admission; finite estimator arithmetic only.
        Assert.InRange(estimate.PrintMinutes, 2.64093875047547e57, 2.64093875047549e57);
        Assert.True(double.IsFinite(estimate.PrintMinutes));
        Assert.Equal(5.75, estimate.TotalResinMl, 6);
    }

    [Theory]
    [InlineData(1, 880, 880)]
    [InlineData(10, 370, 3700)]
    [InlineData(30, 350, 10500)]
    [InlineData(31, 360, 11160)]
    [InlineData(500, 250, 125000)]
    [InlineData(1000, 240, 240000)]
    [InlineData(10000, 240, 2400000)]
    public void Consumer_QuoteItem_SourceCommercialMoney_RoundsUnitBeforeQuantity(int quantity, double unit, double subtotal)
    {
        var samples = new double[64];
        samples[^1] = 120;
        var quote = PricingEngine.QuoteItem(Geometry(samples), PricingCatalog.ResolveMaterial("M68")!, quantity);

        Assert.Equal(unit, quote.UnitPrice);
        Assert.Equal(subtotal, quote.Subtotal);
        Assert.Equal(52.52, quote.PrintTimeMinutesPerUnit, 6);
        Assert.Equal(6.588465, quote.MaterialPerUnit, 6);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10000)]
    public void Consumer_QuoteItem_RepeatedTerminalTiersCollapseWithoutLosingChosenQuantity(int quantity)
    {
        var samples = new double[64];
        samples[^1] = 120;
        var quote = PricingEngine.QuoteItem(Geometry(samples), PricingCatalog.ResolveMaterial("M68")!, quantity);

        Assert.Equal(quantity == 1 ? new[] { 1, 10, 50, 100, 500, 1000 } : new[] { 1, 10, 50, 100, 500, 1000, 10000 },
            quote.Tiers.Select(tier => tier.MinQuantity));
        Assert.Single(quote.Tiers, tier => tier.Active);
        Assert.Equal(quantity, quote.Tiers.Single(tier => tier.Active).MinQuantity);
    }

    [Fact]
    public void Consumer_QuoteItem_UnavailableEstimate_CannotBecomePostProcessingOnlyCharge()
    {
        Assert.Throws<ArgumentException>(() => PricingEngine.QuoteItem(
            Geometry(new double[64], height: 1000.05), PricingCatalog.ResolveMaterial("M68")!, 1));
    }

    [Fact]
    public void Consumer_ActualOrderPricing_BatchAndComparisonCardsPreserveRoundedMoney()
    {
        var session = ConsumerSession();
        var quote = new InstantQuotationPricingService().Quote(session.RequestState);

        Assert.Equal(4580, quote.ItemsSubtotal);
        Assert.Equal(100, quote.ShippingCost);
        Assert.Equal(327.60, quote.Vat, 2);
        Assert.Equal(5007.60, quote.FinalOrderPrice, 2);
        Assert.Equal(new[] { 880d, 3700d }, quote.Parts.Select(part => part.Subtotal));
        Assert.Equal(new[] { 962.16, 4045.44 }, quote.AllocatedLineTotals!);
        Assert.Equal(880, quote.Parts[0].MaterialPrices.Single(price => price.MaterialKey == "M68").UnitPrice);
        Assert.Equal(370, quote.Parts[1].MaterialPrices.Single(price => price.MaterialKey == "M68").UnitPrice);
    }

    [Fact]
    public void Consumer_ProtectedLineIdentity_IsResinCompositionNotCommercialPolicyOrVendorHash()
    {
        var session = ConsumerSession();
        var quote = new InstantQuotationPricingService().Quote(session.RequestState);
        var tickets = new AdditiveQuoteTicketService(new EphemeralDataProtectionProvider());
        var now = session.CreatedAt;
        var authorization = tickets.Issue(session, quote, now);
        var payload = tickets.UnprotectLine(authorization.LineTickets[0], now.AddMinutes(1));

        Assert.Equal("generic-msla-mighty4k-aqua-gray-2026-09-20.v2", payload.ProfileVersion);
        Assert.Equal(64, payload.ProfileSha256.Length);
        Assert.NotEqual(ResinBuildProfileCatalog.ConservativeGeneric.ProfileSha256, payload.ProfileSha256);
        Assert.NotEqual(payload.PolicyVersion, payload.ProfileVersion);
        Assert.Equal(880m, payload.UnitPriceThb);
        Assert.Equal(880m, payload.SubtotalThb);
        Assert.True(tickets.Validate(session, quote, authorization, now.AddMinutes(1)));
    }

    [Fact]
    public void Composition_ResolvedV2Catalog_HasExactCommittedProvisionalSupportProvenance()
    {
        var profile = ResinBuildProfileCatalog.ResolveProvisionalSupportProfile();

        Assert.Equal("generic-msla-mighty4k-aqua-gray-2026-09-20.v2", profile.ProfileVersion);
        Assert.Equal("research_provisional", profile.EvidenceClass);
        Assert.Equal("https://info.phrozen3d.com/pages/resin-sonic-mighty-4k", profile.SourceUri);
        Assert.Equal("https://helpcenter.phrozen3d.com/hc/en-us/articles/6371322306073-Suggested-support-settings", profile.SupportSourceUri);
        Assert.Equal(.15m, profile.SupportEnvelopeDensity);
        Assert.Equal(1.10m, profile.RaftAreaRatio);
        Assert.Equal(1m, profile.RaftThicknessMm);
        Assert.True(ResinBuildProfileEvidenceValidator.HasCompleteSupportComposition(profile));
        Assert.True(ResinBuildProfileEvidenceValidator.Assess(profile).IsReady);
        Assert.Equal("4CA2897D2D2A1DF72C17E959848979D33BAA74EB3310EF523F981F6950BCBBC7", profile.ProfileSha256);
    }

    public static IEnumerable<object[]> CompositionFields => new[]
    {
        "version", "class", "cycleSource", "machine", "material", "slicer", "slicerVersion", "vendorHash",
        "approval", "approvalDate", "layerHeight", "bottomCount", "bottomExposure", "normalExposure", "rest",
        "lift", "liftSpeed", "retract", "retractSpeed", "plateWidth", "plateDepth", "prepare", "wash", "cure",
        "buildLabor", "partLabor", "consumables", "supportStrategy", "supportSource", "density", "raftRatio",
        "raftThickness", "hollowPolicy",
    }.Select(name => new object[] { name });

    [Theory]
    [MemberData(nameof(CompositionFields))]
    public void Composition_EachResolvedFieldMutation_ChangesCanonicalIdentity(string field)
    {
        var profile = Evidence();
        var before = ResinBuildProfileComposition.CreateSha256(profile);
        switch (field)
        {
            case "version": profile.ProfileVersion += "x"; break;
            case "class": profile.EvidenceClass += "x"; break;
            case "cycleSource": profile.SourceUri += "x"; break;
            case "machine": profile.MachineId += "x"; break;
            case "material": profile.MaterialSku += "x"; break;
            case "slicer": profile.SlicerName += "x"; break;
            case "slicerVersion": profile.SlicerVersion += "x"; break;
            case "vendorHash": profile.ProfileSha256 = new string('B', 64); break;
            case "approval": profile.ApprovedBy += "x"; break;
            case "approvalDate": profile.ApprovedAtUtc = profile.ApprovedAtUtc!.Value.AddSeconds(1); break;
            case "layerHeight": profile.LayerHeightMm = .1m; break;
            case "bottomCount": profile.BottomLayerCount = 7; break;
            case "bottomExposure": profile.BottomExposureSeconds = 33m; break;
            case "normalExposure": profile.NormalExposureSeconds = 3m; break;
            case "rest": profile.RestSecondsPerLayer = 1m; break;
            case "lift": profile.LiftDistanceMm = 9m; break;
            case "liftSpeed": profile.LiftSpeedMmPerSecond = 2m; break;
            case "retract": profile.RetractDistanceMm = 9m; break;
            case "retractSpeed": profile.RetractSpeedMmPerSecond = 3m; break;
            case "plateWidth": profile.UsablePlateWidthMm = 201m; break;
            case "plateDepth": profile.UsablePlateDepthMm = 126m; break;
            case "prepare": profile.PreparationSecondsPerBuild = 901m; break;
            case "wash": profile.WashElapsedSeconds = 121m; break;
            case "cure": profile.CureElapsedSeconds = 1801m; break;
            case "buildLabor": profile.AttendedLaborSecondsPerBuild = 901m; break;
            case "partLabor": profile.AttendedLaborSecondsPerPart = 1801m; break;
            case "consumables": profile.ConsumablesThbPerBuild = 101m; break;
            case "supportStrategy": profile.SupportStrategyVersion += "x"; break;
            case "supportSource": profile.SupportSourceUri += "x"; break;
            case "density": profile.SupportEnvelopeDensity = .16m; break;
            case "raftRatio": profile.RaftAreaRatio = 1.11m; break;
            case "raftThickness": profile.RaftThicknessMm = 1.01m; break;
            case "hollowPolicy": profile.HollowingDrainPolicyVersion += "x"; break;
            default: throw new ArgumentOutOfRangeException(nameof(field));
        }

        Assert.NotEqual(before, ResinBuildProfileComposition.CreateSha256(profile));
    }

    [Fact]
    public void Composition_CultureIndependentCanonicalHash_IsNotVendorProvenanceHash()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("th-TH");
            var thai = ResinBuildProfileComposition.CreateSha256(Evidence());
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var french = ResinBuildProfileComposition.CreateSha256(Evidence());

            Assert.Matches("^[A-Fa-f0-9]{64}$", thai);
            Assert.Equal(thai, french);
            Assert.NotEqual(Evidence().ProfileSha256, thai);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Composition_FieldBoundaries_CannotCollideThroughUnseparatedConcatenation()
    {
        var left = Evidence();
        left.ProfileVersion = "ab";
        left.MachineId = "c";
        var right = Evidence();
        right.ProfileVersion = "a";
        right.MachineId = "bc";

        Assert.NotEqual(ResinBuildProfileComposition.CreateSha256(left), ResinBuildProfileComposition.CreateSha256(right));
    }

    [Theory]
    [InlineData("class", null)]
    [InlineData("class", "")]
    [InlineData("source", null)]
    [InlineData("source", "")]
    public void Composition_MissingProvenanceNotRequiredByOldAssess_CannotHash(string field, string? value)
    {
        var profile = Evidence();
        if (field == "class")
        {
            profile.EvidenceClass = value;
        }
        else
        {
            profile.SourceUri = value;
        }

        Assert.True(ResinBuildProfileEvidenceValidator.Assess(profile).IsReady);
        Assert.ThrowsAny<ArgumentException>(() => ResinBuildProfileComposition.CreateSha256(profile));
    }

    [Theory]
    [InlineData("decimalScale")]
    [InlineData("utcOffset")]
    public void Composition_EquivalentNumericAndUtcRepresentations_PreserveCanonicalIdentity(string fixture)
    {
        var original = Evidence();
        var equivalent = Evidence();
        if (fixture == "decimalScale")
        {
            equivalent.RaftAreaRatio = 1.1m;
        }
        else
        {
            equivalent.ApprovedAtUtc = original.ApprovedAtUtc!.Value.ToOffset(TimeSpan.FromHours(7));
        }

        var digest = ResinBuildProfileComposition.CreateSha256(original);
        Assert.Matches("^[A-Fa-f0-9]{64}$", digest);
        Assert.Equal(digest, ResinBuildProfileComposition.CreateSha256(equivalent));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("v1")]
    [InlineData("partial")]
    public void Composition_MissingRequiredEvidence_CannotProduceIdentity(string fixture)
    {
        var profile = fixture == "null" ? null : fixture == "v1" ? ResinBuildProfileCatalog.ConservativeGeneric : Evidence();
        if (fixture == "partial")
        {
            profile!.SupportSourceUri = null;
        }

        Assert.ThrowsAny<ArgumentException>(() => ResinBuildProfileComposition.CreateSha256(profile));
    }

    [Fact]
    public void Composition_InternalQuoteMetadata_IsPopulatedByActualPricingButNotSerialized()
    {
        var session = ConsumerSession();
        var quote = new InstantQuotationPricingService().Quote(session.RequestState);
        var item = PricingEngine.QuoteItem(Geometry(new double[64]), PricingCatalog.ResolveMaterial("M68")!, 1);

        Assert.NotNull(item.ResinProfile);
        Assert.NotNull(quote.Parts[0].ResinProfile);
        Assert.Equal("generic-msla-mighty4k-aqua-gray-2026-09-20.v2", item.ResinProfile.ProfileVersion);
        Assert.Equal(item.ResinProfile, quote.Parts[0].ResinProfile);
    }

    [Fact]
    public void Composition_InternalMetadata_NeverExpandsPublicJsonShapeEvenWhenPresent()
    {
        var identity = new ResinQuoteProfileIdentity("internal-only-resin-profile", new string('c', 64));
        var item = new ItemQuote { ResinProfile = identity };
        var part = new InstantQuotationPricingService().Quote(ConsumerSession().RequestState).Parts[0] with { ResinProfile = identity };
        using var itemJson = JsonDocument.Parse(JsonSerializer.Serialize(item));
        using var partJson = JsonDocument.Parse(JsonSerializer.Serialize(part));

        Assert.False(itemJson.RootElement.TryGetProperty("ResinProfile", out _));
        Assert.False(partJson.RootElement.TryGetProperty("ResinProfile", out _));
    }

    [Fact]
    public void Policy_NewResinEconomics_IsV11WithoutChangingProtectedV4Purpose()
    {
        Assert.Equal("additive-2026-10-01.v11", PricingCatalog.AdditivePricingPolicyVersion);
        Assert.Equal("additive-line-quote.v4", AdditiveQuoteTicketService.LineSchemaVersion);
        Assert.Equal("additive-order-quote.v2", AdditiveQuoteTicketService.OrderSchemaVersion);
        Assert.Equal("additive-upload-receipt.v1", AdditiveQuoteTicketService.UploadSchemaVersion);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Policy_OldV4V10LineAndOrderReplay_RejectsRatherThanRepricingHistory(bool orderTicket)
    {
        var session = ConsumerSession();
        var quote = new InstantQuotationPricingService().Quote(session.RequestState);
        var tickets = new AdditiveQuoteTicketService(new EphemeralDataProtectionProvider());
        var authorization = tickets.Issue(session, quote, session.CreatedAt);
        var line = tickets.UnprotectLine(authorization.LineTickets[0], session.CreatedAt);
        var order = tickets.UnprotectOrder(authorization.OrderTicket, session.CreatedAt);
        line.SchemaVersion = "additive-line-quote.v4";
        line.PolicyVersion = "additive-2026-09-30.v10";
        order.PolicyVersion = "additive-2026-09-30.v10";
        var historicalMoney = line.SubtotalThb;

        if (orderTicket)
        {
            Assert.Throws<AdditiveQuoteTicketException>(() => tickets.UnprotectOrder(tickets.ProtectOrder(order), session.CreatedAt.AddMinutes(1)));
        }
        else
        {
            Assert.Throws<AdditiveQuoteTicketException>(() => tickets.UnprotectLine(tickets.ProtectLine(line), session.CreatedAt.AddMinutes(1)));
        }
        Assert.Equal(historicalMoney, line.SubtotalThb);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Policy_CurrentV4V11LineAndOrder_RoundTripCanonicalMoney(bool orderTicket)
    {
        var session = ConsumerSession();
        var quote = new InstantQuotationPricingService().Quote(session.RequestState);
        var tickets = new AdditiveQuoteTicketService(new EphemeralDataProtectionProvider());
        var authorization = tickets.Issue(session, quote, session.CreatedAt);
        var line = tickets.UnprotectLine(authorization.LineTickets[0], session.CreatedAt);
        var order = tickets.UnprotectOrder(authorization.OrderTicket, session.CreatedAt);
        line.PolicyVersion = "additive-2026-10-01.v11";
        order.PolicyVersion = "additive-2026-10-01.v11";

        if (orderTicket)
        {
            Assert.Equal(order.FinalOrderPriceThb, tickets.UnprotectOrder(tickets.ProtectOrder(order), session.CreatedAt).FinalOrderPriceThb);
        }
        else
        {
            Assert.Equal(line.SubtotalThb, tickets.UnprotectLine(tickets.ProtectLine(line), session.CreatedAt).SubtotalThb);
        }
    }

    [Fact]
    public void Policy_V4ResinGeometryMutation_InvalidatesExistingAuthorization()
    {
        var session = ConsumerSession();
        var quote = new InstantQuotationPricingService().Quote(session.RequestState);
        var tickets = new AdditiveQuoteTicketService(new EphemeralDataProtectionProvider());
        var authorization = tickets.Issue(session, quote, session.CreatedAt);
        var differentGeometry = ConsumerSession(121);
        var changed = session with
        {
            RequestState = new InstantQuotationOrderState(session.Parts.Select((part, index) => part with
            {
                Geometry = differentGeometry.Parts[index].Geometry,
            }).ToArray()),
        };

        Assert.True(tickets.Validate(session, quote, authorization, session.CreatedAt.AddMinutes(1)));
        Assert.False(tickets.Validate(changed, quote, authorization, session.CreatedAt.AddMinutes(1)));
    }

    [Theory]
    [InlineData("issue")]
    [InlineData("validate")]
    public void Ticket_ResinWithStaleFdmPhysicalReceipt_RejectsBeforeIssuance(string boundary)
    {
        var session = ConsumerSession();
        var quote = new InstantQuotationPricingService().Quote(session.RequestState);
        var part = session.Parts[0];
        var stale = new InstantQuotationPhysicalAnalysisReceipt(session.SessionId, session.OwnerIdentity,
            part.PartId, Guid.Parse(part.UploadReference.Value), part.Geometry.Sha256,
            "PLA", "PLA", BuildPreference.Standard, 1, "stale-fdm-profile", new string('d', 64),
            "stale-fdm-analysis", new string('e', 64), 1000, 100, 60, 5);
        var mixed = quote with
        {
            Parts = quote.Parts.Select((line, index) => index == 0
            ? line with { PhysicalReceipt = stale } : line).ToArray()
        };
        var tickets = new AdditiveQuoteTicketService(new EphemeralDataProtectionProvider());

        if (boundary == "issue")
        {
            Assert.Throws<ArgumentException>(() => tickets.Issue(session, mixed, session.CreatedAt));
        }
        else
        {
            var authorization = tickets.Issue(session, quote, session.CreatedAt);
            Assert.False(tickets.Validate(session, mixed, authorization, session.CreatedAt.AddMinutes(1)));
        }
    }

    [Theory]
    [InlineData("analysis")]
    [InlineData("physicalHash")]
    public void Ticket_ResinWithNonemptyFdmPhysicalPayload_RejectsUnprotect(string field)
    {
        var session = ConsumerSession();
        var quote = new InstantQuotationPricingService().Quote(session.RequestState);
        var tickets = new AdditiveQuoteTicketService(new EphemeralDataProtectionProvider());
        var authorization = tickets.Issue(session, quote, session.CreatedAt);
        var line = tickets.UnprotectLine(authorization.LineTickets[0], session.CreatedAt);
        if (field == "analysis")
        {
            line.PhysicalAnalysisVersion = "stale-fdm-analysis";
        }
        else
        {
            line.PhysicalSha256 = new string('e', 64);
        }

        Assert.Throws<AdditiveQuoteTicketException>(() => tickets.UnprotectLine(
            tickets.ProtectLine(line), session.CreatedAt.AddMinutes(1)));
    }

    private static InstantQuotationSessionState ConsumerSession(double lastDemand = 120)
    {
        var parts = new[] { 1, 10 }.Select(quantity =>
        {
            var samples = new double[64];
            samples[^1] = lastDemand;
            var claim = new InstantQuotationGeometryClaim(1, new string('a', 64), 25, 20, 10,
                5000, 1200, Enumerable.Repeat(500d, 64).ToArray(), Enumerable.Repeat(90d, 64).ToArray(),
                1024, 1, true, false, false, .8, samples);
            var upload = InstantQuotationUploadResult.Succeeded("synthetic-operation",
                new InstantQuotationUploadReference(Guid.NewGuid().ToString("D")), claim.Sha256);
            return new InstantQuotationPart(Guid.NewGuid(), "synthetic.stl", upload.UploadReference!,
                AuthoritativeInstantQuotationGeometry.FromCompletedLegacyUpload(upload, claim)!,
                new InstantQuotationPartConfiguration("M68", "Gray", quantity));
        }).ToArray();
        var now = DateTimeOffset.Parse("2026-10-01T00:00:00Z");
        return new InstantQuotationSessionState("synthetic-resin-session", new string('b', 64),
            new InstantQuotationOrderState(parts), now, now, OwnerIdentity: "synthetic-owner");
    }

    private static GeometryInput Geometry(double[] samples, double height = 10, double volume = 5000) => new()
    {
        HeightMm = height,
        VolumeMm3 = volume,
        FootprintMm2 = 500,
        AreaProfileMm2 = Enumerable.Repeat(500d, samples.Length).ToArray(),
        PerimeterProfileMm = Enumerable.Repeat(90d, samples.Length).ToArray(),
        UnsupportedAreaProfileMm2 = samples,
    };

    private static ResinBuildProfileEvidence Evidence() => new()
    {
        ProfileVersion = "synthetic-resin-support-v2-test-only",
        EvidenceClass = "synthetic_test_only",
        SourceUri = "https://example.invalid/synthetic-resin-cycle",
        MachineId = "synthetic-machine",
        MaterialSku = "synthetic-material",
        SlicerName = "synthetic-slicer",
        SlicerVersion = "test-only",
        ProfileSha256 = new string('A', 64),
        ApprovedBy = "synthetic-test-fixture",
        ApprovedAtUtc = DateTimeOffset.Parse("2026-10-01T00:00:00Z"),
        LayerHeightMm = .05m,
        BottomLayerCount = 6,
        BottomExposureSeconds = 32.5m,
        NormalExposureSeconds = 2.3m,
        RestSecondsPerLayer = 0,
        LiftDistanceMm = 8,
        LiftSpeedMmPerSecond = 1,
        RetractDistanceMm = 8,
        RetractSpeedMmPerSecond = 2.5m,
        UsablePlateWidthMm = 200,
        UsablePlateDepthMm = 125,
        PreparationSecondsPerBuild = 900,
        WashElapsedSeconds = 120,
        CureElapsedSeconds = 1800,
        AttendedLaborSecondsPerBuild = 900,
        AttendedLaborSecondsPerPart = 1800,
        ConsumablesThbPerBuild = 100,
        SupportStrategyVersion = "synthetic-projected-envelope-v1",
        SupportSourceUri = "https://example.invalid/synthetic-resin-support",
        SupportEnvelopeDensity = .15m,
        RaftAreaRatio = 1.10m,
        RaftThicknessMm = 1,
        HollowingDrainPolicyVersion = "synthetic-solid-only",
    };

    private static void AssertUnavailable(ResinEstimate result)
    {
        Assert.Equal(0, result.PrintMinutes);
        Assert.Equal(0, result.ModelResinMl);
        Assert.Equal(0, result.SupportResinMl);
        Assert.Equal(0, result.RaftResinMl);
        Assert.Equal(0, result.WasteResinMl);
        Assert.Equal(0, result.TotalResinMl);
    }
}
