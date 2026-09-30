using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Components.Pages.InstantQuotation;
using System.Text.Json;

namespace Legacy.Maliev.Web.Tests;

public sealed class InstantQuotationAuthenticatedProfileTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InteractiveDisplayParameter_JsonRoundTripsGuestAndAuthenticatedOrdinalLocks(bool authenticated)
    {
        var model = InstantQuotationCustomerDisplayModel.Empty;
        if (authenticated)
        {
            var profile = InstantQuotationAuthenticatedProfile.Create(Customer(Address(10, "Billing", "Bangkok"), Address(20, "Shipping", "Other city")),
                [new Country(66, "Thailand", null, null, null, null, null, null)], "owner@example.test", "0812345678");
            model = InstantQuotationCustomerDisplayModel.FromProfile(model, profile);
        }
        var roundTrip = JsonSerializer.Deserialize<InstantQuotationCustomerDisplayModel>(JsonSerializer.Serialize(model));
        Assert.NotNull(roundTrip);
        Assert.Equal(model.FirstName, roundTrip.FirstName);
        Assert.True(model.LockedFields.SetEquals(roundTrip.LockedFields));
        Assert.False(roundTrip.IsLocked("firstname"));
        Assert.Equal(authenticated, roundTrip.IsLocked("FirstName"));
        Assert.Equal(authenticated, roundTrip.IsLocked("ShipToBillingAddress"));
    }
    [Fact]
    public void Merge_PreservesPopulatedOwnerValuesAndDistinctShipping_AndFillsOnlyMissingFields()
    {
        var billing = Address(10, "Stored billing", "");
        var shipping = Address(20, "Stored shipping", "Other city");
        var profile = InstantQuotationAuthenticatedProfile.Create(Customer(billing, shipping),
            [new Country(66, "Thailand", null, null, null, null, null, null)], "owner@example.test", null);
        var merged = profile.MergeMissing(new InstantQuotationProfileDetails
        {
            FirstName = "attacker",
            Email = "forged@example.test",
            BillingStreet1 = "overwrite",
            BillingCity = "Filled city",
            ShippingStreet1 = "overwrite shipping",
            ShipToBillingAddress = true,
        });
        Assert.Equal("Owner", merged.FirstName);
        Assert.Equal("owner@example.test", merged.Email);
        Assert.Equal("Stored billing", merged.BillingStreet1);
        Assert.Equal("Filled city", merged.BillingCity);
        Assert.Equal("Stored shipping", merged.ShippingStreet1);
        Assert.False(merged.ShipToBillingAddress);
        Assert.True(profile.IsLocked(nameof(merged.Email)));
        Assert.False(profile.IsLocked(nameof(merged.BillingCity)));
    }

    [Theory]
    [InlineData("0115562011815 (สาขาที่ 3)", "branch", "00003")]
    [InlineData("0115562011815 (สำนักงานใหญ่)", "head-office", "")]
    public void StoredTaxSuffix_IsParsedAndLockedWithoutLosingBranch(string stored, string branch, string code)
    {
        var customer = Customer(null, null) with { Company = new CustomerCompany(1, "Stored company", stored, null, null, null), CompanyId = 1 };
        var profile = InstantQuotationAuthenticatedProfile.Create(customer, [], null, null);
        var merged = profile.MergeMissing(new InstantQuotationProfileDetails { TaxNumber = "9999999999999", TaxBranch = "head-office", TaxBranchCode = "00004" });
        Assert.Equal("0115562011815", merged.TaxNumber);
        Assert.Equal(branch, merged.TaxBranch);
        Assert.Equal(code, merged.TaxBranchCode);
        Assert.True(profile.IsLocked(nameof(merged.TaxBranch)));
    }

    [Fact]
    public void RawStoredTax_LocksNumberButAllowsSourceBranchChoice()
    {
        var customer = Customer(null, null) with { Company = new CustomerCompany(1, "", "0115562011815", null, null, null), CompanyId = 1 };
        var profile = InstantQuotationAuthenticatedProfile.Create(customer, [], null, null);
        var merged = profile.MergeMissing(new InstantQuotationProfileDetails { TaxBranch = "branch", TaxBranchCode = "00003" });
        Assert.Equal("0115562011815 (สาขาที่ 00003)", merged.FormattedTaxNumber());
        Assert.False(profile.IsLocked(nameof(merged.TaxBranch)));
    }

    [Fact]
    public void DanglingOrUnknownCountryGraph_FailsClosed()
    {
        Assert.Throws<ArgumentException>(() => InstantQuotationAuthenticatedProfile.Create(Customer(Address(10, "", ""), null), [], null, null));
        Assert.Throws<ArgumentException>(() => InstantQuotationAuthenticatedProfile.Create(Customer(null, null) with { BillingAddressId = 10 }, [], null, null));
    }

    private static CustomerAddress Address(int id, string street, string city) => new(id, null, street, null, city, "Bangkok", "10110", 66, null, null);
    private static CustomerAccountDetails Customer(CustomerAddress? billing, CustomerAddress? shipping) =>
        new(7, "Owner", "Name", "Owner Name", null, null, null, "", null, null, billing?.Id, shipping?.Id, null, null, billing, null, shipping);
}
