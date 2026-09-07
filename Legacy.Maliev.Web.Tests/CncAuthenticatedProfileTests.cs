using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Components.Pages.InstantQuotation;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Legacy.Maliev.Web.Tests;

public sealed class CncAuthenticatedProfileTests
{
    private static readonly Country[] Countries =
    [new(1, "Thailand", null, null, null, null, null, null), new(2, "Singapore", null, null, null, null, null, null)];

    [Fact]
    public void Create_StoredCustomerCompanyAndAddresses_MapsAndLocksEveryPopulatedValue()
    {
        var billing = Address(11, "Billing", 1);
        var shipping = Address(12, "Shipping", 2);
        var company = new CustomerCompany(7, " บริษัทตัวอย่าง ", "0100000000000 (สาขาที่ 3)", null, null, null);
        var customer = Customer() with
        {
            FirstName = " ทดสอบ ",
            LastName = " ลูกค้า ",
            Email = " customer@example.test ",
            Mobile = " 0890000000 ",
            Telephone = " 020000000 ",
            CompanyId = 7,
            Company = company,
            BillingAddressId = 11,
            BillingAddress = billing,
            ShippingAddressId = 12,
            ShippingAddress = shipping,
        };
        var profile = Create(customer);
        var expected = new Dictionary<string, string>
        {
            [nameof(CncSubmission.FirstName)] = "ทดสอบ",
            [nameof(CncSubmission.LastName)] = "ลูกค้า",
            [nameof(CncSubmission.Email)] = "customer@example.test",
            [nameof(CncSubmission.Mobile)] = "0890000000",
            [nameof(CncSubmission.Telephone)] = "020000000",
            [nameof(CncSubmission.Company)] = "บริษัทตัวอย่าง",
            [nameof(CncSubmission.TaxNumber)] = "0100000000000",
            [nameof(CncSubmission.TaxBranch)] = "branch",
            [nameof(CncSubmission.TaxBranchCode)] = "00003",
            [nameof(CncSubmission.BillingBuilding)] = "Billing Building",
            [nameof(CncSubmission.BillingStreet1)] = "Billing Road",
            [nameof(CncSubmission.BillingStreet2)] = "Billing Street 2",
            [nameof(CncSubmission.BillingCity)] = "Billing City",
            [nameof(CncSubmission.BillingProvince)] = "Billing Province",
            [nameof(CncSubmission.BillingPostalCode)] = "11000",
            [nameof(CncSubmission.Country)] = "Thailand",
            [nameof(CncSubmission.ShippingBuilding)] = "Shipping Building",
            [nameof(CncSubmission.ShippingStreet1)] = "Shipping Road",
            [nameof(CncSubmission.ShippingStreet2)] = "Shipping Street 2",
            [nameof(CncSubmission.ShippingCity)] = "Shipping City",
            [nameof(CncSubmission.ShippingProvince)] = "Shipping Province",
            [nameof(CncSubmission.ShippingPostalCode)] = "11000",
            [nameof(CncSubmission.ShippingCountry)] = "Singapore",
        };
        var posted = ValidSubmission();
        foreach (var field in expected.Keys) typeof(CncSubmission).GetProperty(field)!.SetValue(posted, "tampered");
        posted.ShipToBillingAddress = true;
        var merged = profile.MergeMissing(posted);
        foreach (var (field, value) in expected)
        {
            Assert.True(profile.IsLocked(field));
            Assert.Equal(value, typeof(CncSubmission).GetProperty(field)!.GetValue(profile.Details));
            Assert.Equal(value, typeof(CncSubmission).GetProperty(field)!.GetValue(merged));
        }
        Assert.Equal(42, profile.CustomerId);
        Assert.False(merged.ShipToBillingAddress);
        Assert.True(profile.IsLocked(nameof(CncSubmission.ShipToBillingAddress)));
        Assert.False(profile.IsLocked("firstname"));
        Assert.False(profile.IsLocked("unknown"));
    }

    [Fact]
    public void Create_MissingCustomerContact_UsesIdentityAccountValues()
    {
        var profile = Create(Customer() with { Email = "  ", Mobile = null }, new(42, " identity@example.test ", " 0810000000 "));
        Assert.Equal("identity@example.test", profile.Details.Email);
        Assert.Equal("0810000000", profile.Details.Mobile);
        Assert.True(profile.IsLocked(nameof(CncSubmission.Email)));
        Assert.True(profile.IsLocked(nameof(CncSubmission.Mobile)));
        Assert.False(profile.IsLocked(nameof(CncSubmission.Telephone)));
        Assert.False(profile.IsLocked(nameof(CncSubmission.Company)));
        Assert.False(profile.IsLocked(nameof(CncSubmission.BillingStreet1)));
    }

    [Fact]
    public void Create_PartialAddress_LeavesOnlyMissingComponentsEditable()
    {
        var address = Address(11, "Billing", 1) with { Building = null, City = " ", PostalCode = null, AddressLine2 = null };
        var profile = Create(Customer() with { BillingAddressId = 11, BillingAddress = address });
        Assert.True(profile.IsLocked(nameof(CncSubmission.BillingStreet1)));
        Assert.True(profile.IsLocked(nameof(CncSubmission.BillingProvince)));
        Assert.True(profile.IsLocked(nameof(CncSubmission.Country)));
        Assert.False(profile.IsLocked(nameof(CncSubmission.BillingBuilding)));
        Assert.False(profile.IsLocked(nameof(CncSubmission.BillingCity)));
        Assert.False(profile.IsLocked(nameof(CncSubmission.BillingPostalCode)));
        var posted = ValidSubmission();
        posted.BillingBuilding = "  New Building  ";
        var merged = profile.MergeMissing(posted);
        Assert.Equal("New Building", merged.BillingBuilding);
        Assert.Equal("Billing Road", merged.BillingStreet1);
        Assert.Equal("Bangkok", merged.BillingCity);
        Assert.Equal("10110", merged.BillingPostalCode);
        Assert.Equal("  New Building  ", posted.BillingBuilding);
    }

    [Fact]
    public void MergeMissing_TamperedValuesCannotReplaceStoredAccountValues()
    {
        var profile = Create(Customer() with { FirstName = "Stored", LastName = "Customer", Mobile = "0890000000" });
        var posted = ValidSubmission();
        posted.FirstName = "Tampered"; posted.LastName = "Attacker"; posted.Mobile = "0800000000";
        posted.Email = "attacker@example.test"; posted.Telephone = " 020000000 "; posted.Company = " New Company ";
        posted.Description = " untouched description ";
        var merged = profile.MergeMissing(posted);
        Assert.Equal("Stored", merged.FirstName);
        Assert.Equal("Customer", merged.LastName);
        Assert.Equal("customer@example.test", merged.Email);
        Assert.Equal("0890000000", merged.Mobile);
        Assert.Equal("020000000", merged.Telephone);
        Assert.Equal("New Company", merged.Company);
        Assert.Equal(posted.Description, merged.Description);
        Assert.Same(posted.OrderItems, merged.OrderItems);
        profile.Details.FirstName = "Mutated copy";
        Assert.Equal("Stored", profile.MergeMissing(posted).FirstName);
    }

    [Fact]
    public void MergeMissing_DistinctStoredShippingCannotBecomeSameAsBilling()
    {
        var profile = Create(Customer() with
        {
            BillingAddressId = 11,
            BillingAddress = Address(11, "Billing", 1),
            ShippingAddressId = 12,
            ShippingAddress = Address(12, "Shipping", 2),
        });
        var posted = ValidSubmission();
        posted.ShippingStreet1 = "tampered";
        var merged = profile.MergeMissing(posted);
        Assert.False(merged.ShipToBillingAddress);
        Assert.Equal("Shipping Road", merged.ShippingStreet1);
        Assert.Equal("Singapore", merged.ShippingCountry);
    }

    [Fact]
    public void Create_StoredShippingWithoutBilling_PreservesDistinctShippingSelection()
    {
        var profile = Create(Customer() with { ShippingAddressId = 12, ShippingAddress = Address(12, "Shipping", 2) });
        Assert.False(profile.Details.ShipToBillingAddress);
        Assert.True(profile.IsLocked(nameof(CncSubmission.ShipToBillingAddress)));
        var errors = new ModelStateDictionary();
        Assert.True(profile.TryMergeAndValidate(ValidSubmission(), errors, out var merged));
        Assert.NotNull(merged);
        Assert.False(merged.ShipToBillingAddress);
        Assert.Equal("Shipping Road", merged.ShippingStreet1);
        Assert.Equal("Billing Road", merged.BillingStreet1);
        Assert.Equal("Thailand", merged.Country);
        Assert.Equal("Singapore", merged.ShippingCountry);
    }

    [Fact]
    public void SameStoredAddress_LocksSameAsBillingAndUsesAuthoritativeBilling()
    {
        var address = Address(11, "Billing", 1);
        var profile = Create(Customer() with { BillingAddressId = 11, ShippingAddressId = 11, BillingAddress = address, ShippingAddress = address });
        var merged = profile.MergeMissing(new CncSubmission { ShipToBillingAddress = false, ShippingStreet1 = "Tampered" });
        Assert.True(merged.ShipToBillingAddress);
        Assert.Equal("Billing Road", merged.ShippingStreet1);
        Assert.Equal(merged.BillingAddressLines, merged.ShippingAddressLines);
    }

    [Fact]
    public void NoStoredShipping_DefaultsToBillingButSelectionRemainsEditable()
    {
        var profile = Create(Customer() with { BillingAddressId = 11, BillingAddress = Address(11, "Billing", 1) });
        Assert.True(profile.Details.ShipToBillingAddress);
        Assert.False(profile.IsLocked(nameof(CncSubmission.ShipToBillingAddress)));
        var merged = profile.MergeMissing(new CncSubmission { ShipToBillingAddress = false, ShippingStreet1 = "Submitted" });
        // Source maps effective billing into populated shipping fields even while the selection is editable.
        Assert.False(merged.ShipToBillingAddress);
        Assert.True(profile.IsLocked(nameof(CncSubmission.ShippingStreet1)));
        Assert.Equal("Billing Road", merged.ShippingStreet1);
    }

    [Theory]
    [InlineData(" 0100000000000 (สำนักงานใหญ่) ", "0100000000000", "head-office", "")]
    [InlineData("0100000000000 (สาขาที่ 1)", "0100000000000", "branch", "00001")]
    [InlineData("0100000000000 (สาขาที่00012)", "0100000000000", "branch", "00012")]
    [InlineData("0100000000000 (สาขาที่ 12345)", "0100000000000", "branch", "12345")]
    [InlineData("0100000000000 (สาขาที่ 123456)", "0100000000000 (สาขาที่ 123456)", "", "")]
    [InlineData("plain-tax", "plain-tax", "", "")]
    [InlineData(" ", "", "", "")]
    [InlineData(null, "", "", "")]
    public void StoredThaiTax_ParsesSourceBranchGrammar(string? storedTax, string number, string branch, string code)
    {
        var profile = Create(Customer() with { CompanyId = 7, Company = new(7, "", storedTax, null, null, null) });
        Assert.Equal(number, profile.Details.TaxNumber);
        Assert.Equal(branch, profile.Details.TaxBranch);
        Assert.Equal(code, profile.Details.TaxBranchCode);
        Assert.Equal(number.Length > 0, profile.IsLocked(nameof(CncSubmission.TaxNumber)));
        Assert.Equal(branch.Length > 0, profile.IsLocked(nameof(CncSubmission.TaxBranch)));
        Assert.Equal(code.Length > 0, profile.IsLocked(nameof(CncSubmission.TaxBranchCode)));
        Assert.False(profile.IsLocked(nameof(CncSubmission.Company)));
    }

    [Theory]
    [InlineData(nameof(CncSubmission.FirstName), "First name is required")]
    [InlineData(nameof(CncSubmission.LastName), "Last name is required")]
    [InlineData(nameof(CncSubmission.Email), "Email is required")]
    [InlineData(nameof(CncSubmission.Mobile), "Mobile number is required")]
    [InlineData(nameof(CncSubmission.Country), "Please select your country")]
    [InlineData(nameof(CncSubmission.BillingStreet1), "Billing street address is required")]
    [InlineData(nameof(CncSubmission.BillingCity), "Billing city is required")]
    [InlineData(nameof(CncSubmission.BillingProvince), "Billing province is required")]
    [InlineData(nameof(CncSubmission.BillingPostalCode), "Billing postal code is required")]
    public void MergedRequiredFieldMissing_ReturnsNoAdmittedSubmission(string field, string message)
    {
        var profile = Create(Customer() with { FirstName = "", LastName = "", Email = "" }, new(42, null, null));
        var posted = ValidSubmission();
        typeof(CncSubmission).GetProperty(field)!.SetValue(posted, " ");
        var errors = new ModelStateDictionary();
        Assert.False(profile.TryMergeAndValidate(posted, errors, out var merged));
        Assert.Null(merged);
        Assert.Equal(message, Assert.Single(errors[field]!.Errors).ErrorMessage);
    }

    [Fact]
    public void MergeValidation_ReplacesPostedContactErrorsButPreservesReceiptErrors()
    {
        var profile = Create(Customer());
        var errors = new ModelStateDictionary();
        errors.AddModelError(nameof(CncSubmission.Email), "Invalid posted value");
        errors.AddModelError(nameof(CncSubmission.OrderItems), "Receipt is invalid");
        Assert.False(profile.TryMergeAndValidate(ValidSubmission(), errors, out var merged));
        Assert.Null(merged);
        Assert.False(errors.ContainsKey(nameof(CncSubmission.Email)));
        Assert.Equal("Receipt is invalid", Assert.Single(errors[nameof(CncSubmission.OrderItems)]!.Errors).ErrorMessage);
        errors.Remove(nameof(CncSubmission.OrderItems));
        Assert.True(profile.TryMergeAndValidate(ValidSubmission(), errors, out merged));
        Assert.NotNull(merged);
    }

    [Fact]
    public void OptionalAndSourceNonFormatValidatedFields_AreNotInventedConstraints()
    {
        var profile = Create(Customer() with { Email = "source-nonblank-value", Mobile = "source-phone" });
        var submitted = ValidSubmission();
        submitted.BillingPostalCode = "source-postcode";
        submitted.TaxNumber = "source-tax";
        submitted.ShipToBillingAddress = false;
        var errors = new ModelStateDictionary();
        Assert.True(profile.TryMergeAndValidate(submitted, errors, out var merged));
        Assert.NotNull(merged);
        Assert.Equal("source-nonblank-value", merged.Email);
        Assert.Equal("source-phone", merged.Mobile);
        Assert.Equal("source-postcode", merged.BillingPostalCode);
        Assert.Equal("source-tax", merged.TaxNumber);
        Assert.Equal("", merged.ShippingStreet1);
    }

    [Fact]
    public void MissingCountryLookup_LeavesCountryEditableAndRequired()
    {
        var customer = Customer() with { BillingAddressId = 11, BillingAddress = Address(11, "Billing", 99) };
        Assert.True(CncAuthenticatedProfile.TryCreate(new(42, null, null), customer, null, new(), out var profile));
        Assert.NotNull(profile);
        Assert.False(profile.IsLocked(nameof(CncSubmission.Country)));
        Assert.Equal("", profile.Details.Country);
        Assert.True(profile.TryMergeAndValidate(ValidSubmission(), new(), out var merged));
        Assert.Equal("Thailand", merged?.Country);
    }

    [Fact]
    public void NullSubmission_IsEmptyCompletionNotSyntheticSuccess()
    {
        var profile = Create(Customer());
        Assert.Equal("customer@example.test", profile.MergeMissing(null).Email);
        Assert.False(profile.TryMergeAndValidate(null, new(), out var merged));
        Assert.Null(merged);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(43)]
    public void IdentityDatabaseMismatch_FailsClosed(int id)
    {
        var errors = new ModelStateDictionary();
        Assert.False(CncAuthenticatedProfile.TryCreate(new(id, "identity@example.test", null), Customer(), Countries, errors, out var profile));
        Assert.Null(profile);
        Assert.False(errors.IsValid);
    }

    [Fact]
    public void MissingIdentityOrCustomer_FailsClosed()
    {
        Assert.False(CncAuthenticatedProfile.TryCreate(null, Customer(), Countries, new(), out _));
        Assert.False(CncAuthenticatedProfile.TryCreate(new(42, null, null), null, Countries, new(), out _));
    }

    [Theory]
    [MemberData(nameof(InvalidRelationships))]
    public void MissingOrMismatchedNestedRelationships_FailClosed(CustomerAccountDetails customer)
    {
        var errors = new ModelStateDictionary();
        Assert.False(CncAuthenticatedProfile.TryCreate(new(42, null, null), customer, Countries, errors, out var profile));
        Assert.Null(profile);
        Assert.False(errors.IsValid);
    }

    public static IEnumerable<object[]> InvalidRelationships()
    {
        yield return [Customer() with { CompanyId = 7 }];
        yield return [Customer() with { CompanyId = 7, Company = new(8, "Company", null, null, null, null) }];
        yield return [Customer() with { Company = new(7, "Company", null, null, null, null) }];
        yield return [Customer() with { CompanyId = 0, Company = new(0, "Company", null, null, null, null) }];
        yield return [Customer() with { BillingAddressId = 11 }];
        yield return [Customer() with { BillingAddressId = 11, BillingAddress = Address(12, "Other", 1) }];
        yield return [Customer() with { BillingAddress = Address(11, "Unexpected", 1) }];
        yield return [Customer() with { ShippingAddressId = 12 }];
        yield return [Customer() with { ShippingAddressId = 12, ShippingAddress = Address(11, "Other", 1) }];
        yield return [Customer() with { ShippingAddress = Address(12, "Unexpected", 1) }];
    }

    private static CncAuthenticatedProfile Create(CustomerAccountDetails customer, CncAuthenticatedIdentity? identity = null)
    {
        Assert.True(CncAuthenticatedProfile.TryCreate(identity ?? new(42, "identity@example.test", "0800000000"), customer, Countries, new(), out var profile));
        return Assert.IsType<CncAuthenticatedProfile>(profile);
    }

    private static CustomerAccountDetails Customer() => new(42, "Stored", "Customer", "Stored Customer", null, null, null,
        "customer@example.test", null, null, null, null, null, null, null, null, null);

    private static CustomerAddress Address(int id, string prefix, int country) => new(id, prefix + " Building", prefix + " Road",
        prefix + " Street 2", prefix + " City", prefix + " Province", "11000", country, null, null);

    private static CncSubmission ValidSubmission() => new()
    {
        FirstName = "First",
        LastName = "Last",
        Email = "submitted@example.test",
        Mobile = "0800000000",
        Country = "Thailand",
        BillingStreet1 = "Billing Road",
        BillingCity = "Bangkok",
        BillingProvince = "Bangkok",
        BillingPostalCode = "10110",
    };
}
