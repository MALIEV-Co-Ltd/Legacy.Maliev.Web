using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Legacy.Maliev.Web.Application;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Legacy.Maliev.Web.Components.Pages.InstantQuotation;

/// <summary>
/// Trusted input projected only from a successful claim-bound Auth self-profile read,
/// with its database identifier checked against the current server-side login session.
/// Never construct this projection from posted form fields.
/// </summary>
internal sealed record CncAuthenticatedIdentity(int DatabaseId, string? Email, string? MobileNumber);

/// <summary>
/// Maps stored account fields and merges only missing CNC contact values. This is
/// preparation, not authentication, receipt admission, profile persistence or fulfillment.
/// </summary>
internal sealed class CncAuthenticatedProfile
{
    private readonly HashSet<string> lockedFields = new(StringComparer.Ordinal);
    private readonly CncSubmission stored = new();

    private CncAuthenticatedProfile(int customerId) => CustomerId = customerId;

    internal int CustomerId { get; }

    // Return a copy so callers cannot mutate the authority used for subsequent merges.
    internal CncSubmission Details => MergeMissing(new CncSubmission { ShipToBillingAddress = stored.ShipToBillingAddress });

    internal bool IsLocked(string propertyName) => lockedFields.Contains(propertyName);

    internal static bool TryCreate(
        CncAuthenticatedIdentity? identity,
        CustomerAccountDetails? customer,
        IReadOnlyList<Country>? countries,
        ModelStateDictionary errors,
        out CncAuthenticatedProfile? profile)
    {
        profile = null;
        if (identity is null || identity.DatabaseId <= 0)
        {
            errors.AddModelError(string.Empty, "Your signed-in account is not linked to a customer profile. Please contact info@maliev.com.");
            return false;
        }

        if (customer is null || customer.Id != identity.DatabaseId)
        {
            errors.AddModelError(string.Empty, "We could not load your customer profile. Please try again or contact info@maliev.com.");
            return false;
        }

        if (!MatchesReference(customer.CompanyId, customer.Company?.Id))
        {
            errors.AddModelError(string.Empty, "We could not load the company linked to your account.");
            return false;
        }

        if (!MatchesReference(customer.BillingAddressId, customer.BillingAddress?.Id)
            || !MatchesReference(customer.ShippingAddressId, customer.ShippingAddress?.Id))
        {
            errors.AddModelError(string.Empty, "We could not load an address linked to your account.");
            return false;
        }

        var created = profile = new CncAuthenticatedProfile(customer.Id);
        profile.Set(nameof(CncSubmission.FirstName), customer.FirstName, value => created.stored.FirstName = value);
        profile.Set(nameof(CncSubmission.LastName), customer.LastName, value => created.stored.LastName = value);
        profile.Set(nameof(CncSubmission.Email), FirstValue(customer.Email, identity.Email), value => created.stored.Email = value);
        profile.Set(nameof(CncSubmission.Mobile), FirstValue(customer.Mobile, identity.MobileNumber), value => created.stored.Mobile = value);
        profile.Set(nameof(CncSubmission.Telephone), customer.Telephone, value => created.stored.Telephone = value);
        profile.Set(nameof(CncSubmission.Company), customer.Company?.Name, value => created.stored.Company = value);
        ParseTaxNumber(customer.Company?.TaxNumber, out var taxNumber, out var branch, out var code);
        profile.Set(nameof(CncSubmission.TaxNumber), taxNumber, value => created.stored.TaxNumber = value);
        profile.Set(nameof(CncSubmission.TaxBranch), branch, value => created.stored.TaxBranch = value);
        profile.Set(nameof(CncSubmission.TaxBranchCode), code, value => created.stored.TaxBranchCode = value);
        profile.ApplyBilling(customer.BillingAddress, countries);

        var hasShipping = customer.ShippingAddressId.HasValue;
        profile.stored.ShipToBillingAddress = hasShipping
            && customer.BillingAddressId.HasValue
            && customer.BillingAddressId.Value == customer.ShippingAddressId;
        if (hasShipping) profile.lockedFields.Add(nameof(CncSubmission.ShipToBillingAddress));
        else profile.stored.ShipToBillingAddress = true;
        profile.ApplyShipping(profile.stored.ShipToBillingAddress ? customer.BillingAddress : customer.ShippingAddress, countries);
        return true;
    }

    internal CncSubmission MergeMissing(CncSubmission? submitted)
    {
        submitted ??= new CncSubmission();
        return new CncSubmission
        {
            FirstName = Value(nameof(CncSubmission.FirstName), stored.FirstName, submitted.FirstName),
            LastName = Value(nameof(CncSubmission.LastName), stored.LastName, submitted.LastName),
            Email = Value(nameof(CncSubmission.Email), stored.Email, submitted.Email),
            Mobile = Value(nameof(CncSubmission.Mobile), stored.Mobile, submitted.Mobile),
            Telephone = Value(nameof(CncSubmission.Telephone), stored.Telephone, submitted.Telephone),
            Company = Value(nameof(CncSubmission.Company), stored.Company, submitted.Company),
            TaxNumber = Value(nameof(CncSubmission.TaxNumber), stored.TaxNumber, submitted.TaxNumber),
            TaxBranch = Value(nameof(CncSubmission.TaxBranch), stored.TaxBranch, submitted.TaxBranch),
            TaxBranchCode = Value(nameof(CncSubmission.TaxBranchCode), stored.TaxBranchCode, submitted.TaxBranchCode),
            BillingBuilding = Value(nameof(CncSubmission.BillingBuilding), stored.BillingBuilding, submitted.BillingBuilding),
            BillingStreet1 = Value(nameof(CncSubmission.BillingStreet1), stored.BillingStreet1, submitted.BillingStreet1),
            BillingStreet2 = Value(nameof(CncSubmission.BillingStreet2), stored.BillingStreet2, submitted.BillingStreet2),
            BillingCity = Value(nameof(CncSubmission.BillingCity), stored.BillingCity, submitted.BillingCity),
            BillingProvince = Value(nameof(CncSubmission.BillingProvince), stored.BillingProvince, submitted.BillingProvince),
            BillingPostalCode = Value(nameof(CncSubmission.BillingPostalCode), stored.BillingPostalCode, submitted.BillingPostalCode),
            Country = Value(nameof(CncSubmission.Country), stored.Country, submitted.Country),
            ShipToBillingAddress = IsLocked(nameof(CncSubmission.ShipToBillingAddress)) ? stored.ShipToBillingAddress : submitted.ShipToBillingAddress,
            ShippingBuilding = Value(nameof(CncSubmission.ShippingBuilding), stored.ShippingBuilding, submitted.ShippingBuilding),
            ShippingStreet1 = Value(nameof(CncSubmission.ShippingStreet1), stored.ShippingStreet1, submitted.ShippingStreet1),
            ShippingStreet2 = Value(nameof(CncSubmission.ShippingStreet2), stored.ShippingStreet2, submitted.ShippingStreet2),
            ShippingCity = Value(nameof(CncSubmission.ShippingCity), stored.ShippingCity, submitted.ShippingCity),
            ShippingProvince = Value(nameof(CncSubmission.ShippingProvince), stored.ShippingProvince, submitted.ShippingProvince),
            ShippingPostalCode = Value(nameof(CncSubmission.ShippingPostalCode), stored.ShippingPostalCode, submitted.ShippingPostalCode),
            ShippingCountry = Value(nameof(CncSubmission.ShippingCountry), stored.ShippingCountry, submitted.ShippingCountry),
            Description = submitted.Description,
            OrderItems = submitted.OrderItems,
        };
    }

    /// <summary>
    /// Revalidates source-required contact/billing fields after the account merge.
    /// Other admission errors are retained and prevent a successful result.
    /// Item/receipt/country resolution and request budgets still require their own gates.
    /// </summary>
    internal bool TryMergeAndValidate(CncSubmission? submitted, ModelStateDictionary errors, out CncSubmission? merged)
    {
        var candidate = MergeMissing(submitted);
        foreach (var field in ContactFields) errors.Remove(field);
        foreach (var (field, message) in RequiredFields)
        {
            var value = typeof(CncSubmission).GetProperty(field)!.GetValue(candidate);
            if (!new RequiredAttribute().IsValid(value)) errors.AddModelError(field, message);
        }

        merged = errors.IsValid ? candidate : null;
        return merged is not null;
    }

    private static readonly string[] ContactFields =
    [
        nameof(CncSubmission.FirstName), nameof(CncSubmission.LastName), nameof(CncSubmission.Email),
        nameof(CncSubmission.Mobile), nameof(CncSubmission.Telephone), nameof(CncSubmission.Company),
        nameof(CncSubmission.TaxNumber), nameof(CncSubmission.TaxBranch), nameof(CncSubmission.TaxBranchCode),
        nameof(CncSubmission.BillingBuilding), nameof(CncSubmission.BillingStreet1), nameof(CncSubmission.BillingStreet2),
        nameof(CncSubmission.BillingCity), nameof(CncSubmission.BillingProvince), nameof(CncSubmission.BillingPostalCode),
        nameof(CncSubmission.Country), nameof(CncSubmission.ShipToBillingAddress), nameof(CncSubmission.ShippingBuilding),
        nameof(CncSubmission.ShippingStreet1), nameof(CncSubmission.ShippingStreet2), nameof(CncSubmission.ShippingCity),
        nameof(CncSubmission.ShippingProvince), nameof(CncSubmission.ShippingPostalCode), nameof(CncSubmission.ShippingCountry),
    ];

    private static readonly (string Field, string Message)[] RequiredFields =
    [
        (nameof(CncSubmission.FirstName), "First name is required"),
        (nameof(CncSubmission.LastName), "Last name is required"),
        (nameof(CncSubmission.Email), "Email is required"),
        (nameof(CncSubmission.Mobile), "Mobile number is required"),
        (nameof(CncSubmission.Country), "Please select your country"),
        (nameof(CncSubmission.BillingStreet1), "Billing street address is required"),
        (nameof(CncSubmission.BillingCity), "Billing city is required"),
        (nameof(CncSubmission.BillingProvince), "Billing province is required"),
        (nameof(CncSubmission.BillingPostalCode), "Billing postal code is required"),
    ];

    private static bool MatchesReference(int? expected, int? actual) =>
        expected.HasValue ? expected.Value > 0 && actual == expected : actual is null;

    private static string? FirstValue(params string?[] values) => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private static string? CountryName(int id, IReadOnlyList<Country>? countries) => countries?.FirstOrDefault(country => country.Id == id)?.Name;

    private static void ParseTaxNumber(string? storedValue, out string? number, out string? branch, out string? code)
    {
        number = storedValue?.Trim();
        branch = null;
        code = null;
        if (string.IsNullOrWhiteSpace(number)) return;
        var match = Regex.Match(number, @"^(?<number>.*?)\s*\((?<branch>สำนักงานใหญ่|สาขาที่\s*(?<code>\d{1,5}))\)\s*$", RegexOptions.CultureInvariant);
        if (!match.Success) return;
        number = match.Groups["number"].Value.Trim();
        if (match.Groups["code"].Success)
        {
            branch = "branch";
            code = match.Groups["code"].Value.PadLeft(5, '0');
        }
        else branch = "head-office";
    }

    private void ApplyBilling(CustomerAddress? address, IReadOnlyList<Country>? countries)
    {
        if (address is null) return;
        Set(nameof(CncSubmission.BillingBuilding), address.Building, value => stored.BillingBuilding = value);
        Set(nameof(CncSubmission.BillingStreet1), address.AddressLine1, value => stored.BillingStreet1 = value);
        Set(nameof(CncSubmission.BillingStreet2), address.AddressLine2, value => stored.BillingStreet2 = value);
        Set(nameof(CncSubmission.BillingCity), address.City, value => stored.BillingCity = value);
        Set(nameof(CncSubmission.BillingProvince), address.State, value => stored.BillingProvince = value);
        Set(nameof(CncSubmission.BillingPostalCode), address.PostalCode, value => stored.BillingPostalCode = value);
        Set(nameof(CncSubmission.Country), CountryName(address.CountryId, countries), value => stored.Country = value);
    }

    private void ApplyShipping(CustomerAddress? address, IReadOnlyList<Country>? countries)
    {
        if (address is null) return;
        Set(nameof(CncSubmission.ShippingBuilding), address.Building, value => stored.ShippingBuilding = value);
        Set(nameof(CncSubmission.ShippingStreet1), address.AddressLine1, value => stored.ShippingStreet1 = value);
        Set(nameof(CncSubmission.ShippingStreet2), address.AddressLine2, value => stored.ShippingStreet2 = value);
        Set(nameof(CncSubmission.ShippingCity), address.City, value => stored.ShippingCity = value);
        Set(nameof(CncSubmission.ShippingProvince), address.State, value => stored.ShippingProvince = value);
        Set(nameof(CncSubmission.ShippingPostalCode), address.PostalCode, value => stored.ShippingPostalCode = value);
        Set(nameof(CncSubmission.ShippingCountry), CountryName(address.CountryId, countries), value => stored.ShippingCountry = value);
    }

    private void Set(string property, string? value, Action<string> assign)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        assign(normalized ?? string.Empty);
        if (normalized is not null) lockedFields.Add(property);
    }

    private string Value(string property, string storedValue, string? submittedValue) =>
        IsLocked(property) ? storedValue : submittedValue?.Trim() ?? string.Empty;
}
