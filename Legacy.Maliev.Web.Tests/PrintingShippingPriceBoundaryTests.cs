namespace Legacy.Maliev.Web.Tests;

public sealed class PrintingShippingPriceBoundaryTests
{
    [Fact]
    public void PrintingContent_ContainsNoProductionPriceFiguresOutsideApprovedShippingFloor()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Web.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        var content = File.ReadAllText(Path.Combine(directory.FullName, "Legacy.Maliev.Web", "Components", "Pages", "Services", "ThreeDimensionalPrintingContent.razor"));
        Assert.Contains("Shipping within Thailand starts at THB 100", content, StringComparison.Ordinal);
        Assert.Contains("ค่าจัดส่งภายในประเทศไทยเริ่มต้น 100 บาท", content, StringComparison.Ordinal);

        Assert.False(ContainsUnapprovedPrice(content));
    }

    [Theory]
    [InlineData("Production costs THB 1000")]
    [InlineData("ค่าผลิต 1000 บาท")]
    [InlineData("Shipping within Thailand starts at THB 1000")]
    [InlineData("Shipping within Thailand starts at THB 100.50")]
    [InlineData("Thailand shipping from THB 1000 or pickup in Pak Kret")]
    public void PriceGuard_RejectsUnapprovedPriceFigures(string content)
    {
        Assert.True(ContainsUnapprovedPrice(content));
    }

    private static bool ContainsUnapprovedPrice(string content)
    {
        var withoutShippingFloor = System.Text.RegularExpressions.Regex.Replace(
            content,
            @"Shipping within Thailand starts at THB 100(?![\d,]|\.\d)|Thailand shipping from THB 100(?![\d,]|\.\d) or pickup in Pak Kret|ค่าจัดส่งภายในประเทศไทยเริ่มต้น 100 บาท|จัดส่งในไทยเริ่มต้น 100 บาท หรือรับชิ้นงานเองที่ปากเกร็ด",
            string.Empty);
        return System.Text.RegularExpressions.Regex.IsMatch(withoutShippingFloor, @"THB\s*[\d,]+")
            || System.Text.RegularExpressions.Regex.IsMatch(withoutShippingFloor, @"[\d,]+\s*บาท");
    }
}
