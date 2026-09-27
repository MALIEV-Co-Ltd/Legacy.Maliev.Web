using Legacy.Maliev.Web.Components.Pages.InstantQuotation;

namespace Legacy.Maliev.Web.Tests;

public sealed class InstantQuotationSelectedPrintTimeTests
{
    [Theory]
    [InlineData(null, false, false)]
    [InlineData(0.0, false, false)]
    [InlineData(-1.0, false, false)]
    [InlineData(45.0, false, true)]
    [InlineData(45.0, true, false)]
    [InlineData(double.PositiveInfinity, false, false)]
    [InlineData(double.NaN, false, false)]
    public void OnlyShowsACompletedCurrentEstimate(double? minutes, bool isRepricing, bool expected)
    {
        Assert.Equal(expected, InstantQuotationSelectedPrintTime.IsVisible(minutes, isRepricing));
    }
}
