using Legacy.Maliev.Web.Application.Pricing;

namespace Legacy.Maliev.Web.Tests;

public sealed class CncDeliveryCarrierPricingTests
{
    [Fact]
    public void BrowserCarrierTables_CannotChangeLaterDeliveryCalculations()
    {
        var bands = Assert.IsAssignableFrom<IList<double>>(ShippingCalculator.ClientWeightBoundsKg);
        var rates = Assert.IsAssignableFrom<IList<double>>(ShippingCalculator.ClientCarrierRatesThb);

        Assert.True(bands.IsReadOnly);
        Assert.True(rates.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => bands[0] = 19);
        Assert.Throws<NotSupportedException>(() => rates[0] = 0);

        Assert.Equal(25, ShippingCalculator.CarrierRateThb(0.5));
        Assert.Equal(35, ShippingCalculator.CarrierRateThb(0.501));
        Assert.Equal(270, ShippingCalculator.CarrierRateThb(20.1));
        Assert.Equal(100, ShippingCalculator.CustomerShippingThb(300, 100));
        Assert.Equal(150, ShippingCalculator.CustomerShippingThb(100, 25_000));
    }

    [Fact]
    public void BrowserCarrierTables_PreserveEveryOriginalWeightBandAndRate()
    {
        double[] bands = [0.5, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19];
        double[] rates = [25, 35, 35, 36, 52, 63, 73, 84, 99, 110, 128, 150, 160, 172, 182, 194, 211, 221, 233, 244];

        Assert.Equal(bands, ShippingCalculator.ClientWeightBoundsKg);
        Assert.Equal(rates, ShippingCalculator.ClientCarrierRatesThb);
        for (var index = 0; index < bands.Length; index++)
        {
            Assert.Equal(rates[index], ShippingCalculator.CarrierRateThb(bands[index]));
        }
    }
}
