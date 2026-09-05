using Legacy.Maliev.Web.Pages.InstantQuotation;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Legacy.Maliev.Web.Tests;

public sealed class InstantQuotationAddressValidationTests
{
    [Fact]
    public void BuildingContainsAddressComponents_ReportedFullThaiAddress_IsRejected()
    {
        var result = ThreeDimensionalPrinting.BuildingContainsAddressComponents(
            "หอพักชายอุทัยวรรณ2, เลขที่ 155, ซอย วงศ์สว่าง11, แขวงวงศ์สว่าง, เขตบางซื่อ จังหวัดกรุงเทพมหานคร 10800",
            "155, ซอย วงศ์สว่าง11",
            "แขวงวงศ์สว่าง",
            "บางซื่อ",
            "กรุงเทพมหานคร",
            "10800");

        Assert.True(result);
    }

    [Fact]
    public void BuildingContainsAddressComponents_LongThaiOrganizationName_IsAccepted()
    {
        var result = ThreeDimensionalPrinting.BuildingContainsAddressComponents(
            "โรงเรียนวรนารีเฉลิม จังหวัดสงขลา ในพระอุปถัมภ์สมเด็จพระเจ้าบรมวงศ์เธอ เจ้าฟ้ากัลยาณิวัฒนา กรมพระนราธิวาสราชนครินทร์ บดินทรเชษฐภคินี",
            "เลขที่ 1 ถนนปละท่า",
            "บ่อยาง",
            "เมืองสงขลา",
            "สงขลา",
            "90000");

        Assert.False(result);
    }

    [Fact]
    public void BuildingContainsAddressComponents_RepeatedSameComponent_IsCountedOnce()
    {
        var result = ThreeDimensionalPrinting.BuildingContainsAddressComponents(
            "อาคารบางซื่อ",
            null,
            "บางซื่อ",
            "บางซื่อ",
            null,
            null);

        Assert.False(result);
    }

    [Fact]
    public void ValidateBuildingFields_SameAsBilling_DoesNotValidateUnusedShippingFields()
    {
        var page = new ThreeDimensionalPrinting
        {
            PageContext = new PageContext(),
            ShipToBillingAddress = true,
            ShippingBuilding = "Warehouse 99 Shipping Road Chiang Mai 50000",
            ShippingStreet1 = "99 Shipping Road",
            ShippingCity = "Chiang Mai",
            ShippingProvince = "Chiang Mai",
            ShippingPostalCode = "50000",
        };

        page.ValidateBuildingFields();

        Assert.False(page.ModelState.ContainsKey(nameof(ThreeDimensionalPrinting.ShippingBuilding)));
    }
}
