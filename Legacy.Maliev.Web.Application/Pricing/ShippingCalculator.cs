namespace Legacy.Maliev.Web.Application.Pricing;

public static class ShippingCalculator
{
    public const double MinimumShippingThb = 100.0;
    public const double PackagingMaterialThb = 30.0;
    public const double CarrierMarkup = 1.8;
    public const double BoxTareGrams = 200.0;
    public const double VolumetricDivisor = 5_000.0;
    public const double ResinDensityGramsPerMl = 1.1;

    private static readonly double[] WeightBoundsKg = [0.5, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19];
    private static readonly double[] CarrierRatesThb = [25, 35, 35, 36, 52, 63, 73, 84, 99, 110, 128, 150, 160, 172, 182, 194, 211, 221, 233, 244];

    public static double CarrierRateThb(double weightKg)
    {
        for (var index = 0; index < WeightBoundsKg.Length; index++)
        {
            if (weightKg <= WeightBoundsKg[index])
            {
                return CarrierRatesThb[index];
            }
        }

        var lastRate = CarrierRatesThb[^1];
        var lastBound = WeightBoundsKg[^1];
        return lastRate + (Math.Ceiling(weightKg - lastBound) * 13.0);
    }

    public static double ChargeableWeightKg(double actualGrams, double boundingCm3)
    {
        var actualKg = (Math.Max(0, actualGrams) + BoxTareGrams) / 1_000.0;
        var volumetricKg = Math.Max(0, boundingCm3) / VolumetricDivisor;
        return Math.Max(actualKg, volumetricKg);
    }

    public static double CustomerShippingThb(double actualGrams, double boundingCm3)
    {
        var carrierRate = CarrierRateThb(ChargeableWeightKg(actualGrams, boundingCm3));
        var customerPrice = Math.Ceiling(((carrierRate * CarrierMarkup) + PackagingMaterialThb) / 10.0) * 10.0;
        return Math.Max(MinimumShippingThb, customerPrice);
    }
}
