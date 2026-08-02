using Legacy.Maliev.Web.Pages.Quotation;

namespace Legacy.Maliev.Web.Tests;

public sealed class FinishingColorQuotePrefillTests
{
    [Fact]
    public void Build_IncludesValidatedEnglishReference()
    {
        var result = FinishingColorQuotePrefill.Build(
            "en",
            "#A2474F",
            "H010_L40_C035",
            "40.000,34.468,6.078",
            "PANTONE 18-1540 TCX",
            "satin");

        Assert.Contains("Screen color: #A2474F", result, StringComparison.Ordinal);
        Assert.Contains("HLC reference: H010_L40_C035", result, StringComparison.Ordinal);
        Assert.Contains("CIELAB (D50): 40.000,34.468,6.078", result, StringComparison.Ordinal);
        Assert.Contains("Customer-supplied Pantone code: PANTONE 18-1540 TCX", result, StringComparison.Ordinal);
        Assert.Contains("Finish sheen: Satin", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_LocalizesThaiAndOmitsMissingPantone()
    {
        var result = FinishingColorQuotePrefill.Build(
            "th",
            "#A2474F",
            "H010_L40_C035",
            "40.000,34.468,6.078",
            null,
            "matte");

        Assert.Contains("สีจากหน้าจอ: #A2474F", result, StringComparison.Ordinal);
        Assert.Contains("รหัสอ้างอิง HLC: H010_L40_C035", result, StringComparison.Ordinal);
        Assert.Contains("ระดับความเงา: ด้าน", result, StringComparison.Ordinal);
        Assert.DoesNotContain("Pantone", result, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("#fff", "H010_L40_C035", "40.000,34.468,6.078", "matte")]
    [InlineData("#A2474F", "not-an-hlc", "40.000,34.468,6.078", "matte")]
    [InlineData("#A2474F", "H010_L40_C035", "not-lab", "matte")]
    [InlineData("#A2474F", "H010_L40_C035", "40.000,34.468,6.078", "mirror")]
    public void Build_RejectsInvalidHandoff(string hex, string hlc, string lab, string sheen)
    {
        Assert.Empty(FinishingColorQuotePrefill.Build("en", hex, hlc, lab, null, sheen));
    }
}
