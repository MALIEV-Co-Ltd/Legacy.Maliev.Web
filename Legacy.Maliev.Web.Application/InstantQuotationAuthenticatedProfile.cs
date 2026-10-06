using System.Text.RegularExpressions;

namespace Legacy.Maliev.Web.Application;

public sealed record InstantQuotationProfileDetails
{
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string Mobile { get; init; } = string.Empty;
    public string Telephone { get; init; } = string.Empty;
    public string Company { get; init; } = string.Empty;
    public string TaxNumber { get; init; } = string.Empty;
    public string TaxBranch { get; init; } = "head-office";
    public string TaxBranchCode { get; init; } = string.Empty;
    public string BillingBuilding { get; init; } = string.Empty;
    public string BillingStreet1 { get; init; } = string.Empty;
    public string BillingStreet2 { get; init; } = string.Empty;
    public string BillingCity { get; init; } = string.Empty;
    public string BillingProvince { get; init; } = string.Empty;
    public string BillingPostalCode { get; init; } = string.Empty;
    public string Country { get; init; } = string.Empty;
    public string ShippingBuilding { get; init; } = string.Empty;
    public string ShippingStreet1 { get; init; } = string.Empty;
    public string ShippingStreet2 { get; init; } = string.Empty;
    public string ShippingCity { get; init; } = string.Empty;
    public string ShippingProvince { get; init; } = string.Empty;
    public string ShippingPostalCode { get; init; } = string.Empty;
    public string ShippingCountry { get; init; } = string.Empty;
    public bool ShipToBillingAddress { get; init; } = true;

    public string FormattedTaxNumber() => string.IsNullOrWhiteSpace(TaxNumber) ? string.Empty
        : TaxBranch == "branch" ? $"{TaxNumber.Trim()} (สาขาที่ {TaxBranchCode.Trim()})"
        : $"{TaxNumber.Trim()} (สำนักงานใหญ่)";

    public InstantQuotationCustomerSubmission ToSubmission(string? description) => new(
        FirstName, LastName, Email, Telephone, Country, Company, FormattedTaxNumber(), description,
        Mobile, BillingBuilding, BillingStreet1, BillingStreet2, BillingCity, BillingProvince, BillingPostalCode,
        ShipToBillingAddress, ShippingBuilding, ShippingStreet1, ShippingStreet2, ShippingCity, ShippingProvince,
        ShippingPostalCode, ShippingCountry);

    public static InstantQuotationProfileDetails FromSubmission(InstantQuotationCustomerSubmission customer) => new()
    {
        FirstName = customer.FirstName,
        LastName = customer.LastName,
        Email = customer.Email,
        Mobile = customer.MobileNumber ?? "",
        Telephone = customer.TelephoneNumber ?? "",
        Country = customer.Country,
        Company = customer.CompanyName ?? "",
        TaxNumber = customer.ProfileCompletion?.Body.TaxNumber ?? "",
        TaxBranch = customer.ProfileCompletion?.Body.TaxBranch ?? "head-office",
        TaxBranchCode = customer.ProfileCompletion?.Body.TaxBranchCode ?? "",
        BillingBuilding = customer.BillingBuilding ?? "",
        BillingStreet1 = customer.BillingAddressLine1 ?? "",
        BillingStreet2 = customer.BillingAddressLine2 ?? "",
        BillingCity = customer.BillingCity ?? "",
        BillingProvince = customer.BillingProvince ?? "",
        BillingPostalCode = customer.BillingPostalCode ?? "",
        ShipToBillingAddress = customer.ShipToBillingAddress,
        ShippingBuilding = customer.ShippingBuilding ?? "",
        ShippingStreet1 = customer.ShippingAddressLine1 ?? "",
        ShippingStreet2 = customer.ShippingAddressLine2 ?? "",
        ShippingCity = customer.ShippingCity ?? "",
        ShippingProvince = customer.ShippingProvince ?? "",
        ShippingPostalCode = customer.ShippingPostalCode ?? "",
        ShippingCountry = customer.ShippingCountry ?? "",
    };
}

/// <summary>Server-owned, fieldwise fill-missing projection; posted fields never establish identity.</summary>
public sealed class InstantQuotationAuthenticatedProfile
{
    private readonly HashSet<string> locked = new(StringComparer.Ordinal);
    public InstantQuotationProfileDetails Details { get; private set; } = new();
    public IReadOnlySet<string> LockedFields => locked;
    public bool IsLocked(string field) => locked.Contains(field);

    public static InstantQuotationAuthenticatedProfile Create(CustomerAccountDetails customer,
        IReadOnlyList<Country> countries, string? trustedEmail, string? trustedMobile)
    {
        ArgumentNullException.ThrowIfNull(customer);
        if (customer.Id <= 0
            || !LinkMatches(customer.CompanyId, customer.Company?.Id)
            || !LinkMatches(customer.BillingAddressId, customer.BillingAddress?.Id)
            || !LinkMatches(customer.ShippingAddressId, customer.ShippingAddress?.Id))
        {
            throw new ArgumentException("The authoritative customer graph is incomplete.", nameof(customer));
        }

        var result = new InstantQuotationAuthenticatedProfile();
        string Value(string field, string? value)
        {
            var trimmed = value?.Trim() ?? string.Empty;
            if (trimmed.Length > 0) result.locked.Add(field);
            return trimmed;
        }
        string CountryName(string field, CustomerAddress? address)
        {
            if (address is null || address.CountryId <= 0) return string.Empty;
            var matches = countries.Where(item => item.Id == address.CountryId).ToArray();
            if (matches.Length != 1 || string.IsNullOrWhiteSpace(matches[0].Name))
                throw new ArgumentException("An authoritative address country is unavailable.", nameof(countries));
            return Value(field, matches[0].Name);
        }

        var tax = customer.Company?.TaxNumber?.Trim() ?? string.Empty;
        var branch = "head-office";
        var branchCode = string.Empty;
        var match = Regex.Match(tax, @"^(?<number>.*?)\s*\((?<branch>สำนักงานใหญ่|สาขาที่\s*(?<code>[0-9]{1,5}))\)\s*$",
            RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        if (match.Success)
        {
            tax = match.Groups["number"].Value.Trim();
            branch = match.Groups["code"].Success ? "branch" : "head-office";
            branchCode = match.Groups["code"].Success ? match.Groups["code"].Value.PadLeft(5, '0') : string.Empty;
            result.locked.Add(nameof(InstantQuotationProfileDetails.TaxBranch));
            result.locked.Add(nameof(InstantQuotationProfileDetails.TaxBranchCode));
        }
        if (customer.ShippingAddressId is not null)
            result.locked.Add(nameof(InstantQuotationProfileDetails.ShipToBillingAddress));

        var billing = customer.BillingAddress;
        var shipping = customer.ShippingAddress;
        result.Details = new()
        {
            FirstName = Value("FirstName", customer.FirstName),
            LastName = Value("LastName", customer.LastName),
            Email = Value("Email", string.IsNullOrWhiteSpace(customer.Email) ? trustedEmail : customer.Email),
            Mobile = Value("Mobile", string.IsNullOrWhiteSpace(customer.Mobile) ? trustedMobile : customer.Mobile),
            Telephone = Value("Telephone", customer.Telephone),
            Company = Value("Company", customer.Company?.Name),
            TaxNumber = Value("TaxNumber", tax),
            TaxBranch = branch,
            TaxBranchCode = branchCode,
            BillingBuilding = Value("BillingBuilding", billing?.Building),
            BillingStreet1 = Value("BillingStreet1", billing?.AddressLine1),
            BillingStreet2 = Value("BillingStreet2", billing?.AddressLine2),
            BillingCity = Value("BillingCity", billing?.City),
            BillingProvince = Value("BillingProvince", billing?.State),
            BillingPostalCode = Value("BillingPostalCode", billing?.PostalCode),
            Country = CountryName("Country", billing),
            ShipToBillingAddress = customer.ShippingAddressId is null || customer.ShippingAddressId == customer.BillingAddressId,
            ShippingBuilding = Value("ShippingBuilding", shipping?.Building),
            ShippingStreet1 = Value("ShippingStreet1", shipping?.AddressLine1),
            ShippingStreet2 = Value("ShippingStreet2", shipping?.AddressLine2),
            ShippingCity = Value("ShippingCity", shipping?.City),
            ShippingProvince = Value("ShippingProvince", shipping?.State),
            ShippingPostalCode = Value("ShippingPostalCode", shipping?.PostalCode),
            ShippingCountry = CountryName("ShippingCountry", shipping),
        };
        return result;
    }

    public InstantQuotationProfileDetails MergeMissing(InstantQuotationProfileDetails posted)
    {
        ArgumentNullException.ThrowIfNull(posted);
        string Merge(string field, string authoritative, string proposed) =>
            IsLocked(field) ? authoritative : proposed?.Trim() ?? string.Empty;
        return new()
        {
            FirstName = Merge(nameof(posted.FirstName), Details.FirstName, posted.FirstName),
            LastName = Merge(nameof(posted.LastName), Details.LastName, posted.LastName),
            Email = Merge(nameof(posted.Email), Details.Email, posted.Email),
            Mobile = Merge(nameof(posted.Mobile), Details.Mobile, posted.Mobile),
            Telephone = Merge(nameof(posted.Telephone), Details.Telephone, posted.Telephone),
            Company = Merge(nameof(posted.Company), Details.Company, posted.Company),
            TaxNumber = Merge(nameof(posted.TaxNumber), Details.TaxNumber, posted.TaxNumber),
            TaxBranch = Merge(nameof(posted.TaxBranch), Details.TaxBranch, posted.TaxBranch),
            TaxBranchCode = Merge(nameof(posted.TaxBranchCode), Details.TaxBranchCode, posted.TaxBranchCode),
            BillingBuilding = Merge(nameof(posted.BillingBuilding), Details.BillingBuilding, posted.BillingBuilding),
            BillingStreet1 = Merge(nameof(posted.BillingStreet1), Details.BillingStreet1, posted.BillingStreet1),
            BillingStreet2 = Merge(nameof(posted.BillingStreet2), Details.BillingStreet2, posted.BillingStreet2),
            BillingCity = Merge(nameof(posted.BillingCity), Details.BillingCity, posted.BillingCity),
            BillingProvince = Merge(nameof(posted.BillingProvince), Details.BillingProvince, posted.BillingProvince),
            BillingPostalCode = Merge(nameof(posted.BillingPostalCode), Details.BillingPostalCode, posted.BillingPostalCode),
            Country = Merge(nameof(posted.Country), Details.Country, posted.Country),
            ShippingBuilding = Merge(nameof(posted.ShippingBuilding), Details.ShippingBuilding, posted.ShippingBuilding),
            ShippingStreet1 = Merge(nameof(posted.ShippingStreet1), Details.ShippingStreet1, posted.ShippingStreet1),
            ShippingStreet2 = Merge(nameof(posted.ShippingStreet2), Details.ShippingStreet2, posted.ShippingStreet2),
            ShippingCity = Merge(nameof(posted.ShippingCity), Details.ShippingCity, posted.ShippingCity),
            ShippingProvince = Merge(nameof(posted.ShippingProvince), Details.ShippingProvince, posted.ShippingProvince),
            ShippingPostalCode = Merge(nameof(posted.ShippingPostalCode), Details.ShippingPostalCode, posted.ShippingPostalCode),
            ShippingCountry = Merge(nameof(posted.ShippingCountry), Details.ShippingCountry, posted.ShippingCountry),
            ShipToBillingAddress = IsLocked(nameof(posted.ShipToBillingAddress)) ? Details.ShipToBillingAddress : posted.ShipToBillingAddress,
        };
    }

    private static bool LinkMatches(int? linkId, int? recordId) => linkId is null ? recordId is null : linkId > 0 && linkId == recordId;
}
