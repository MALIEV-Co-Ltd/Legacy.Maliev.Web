using static Legacy.Maliev.Web.Components.Pages.InstantQuotation.CncSubmissionAdmission;
namespace Legacy.Maliev.Web.Components.Pages.InstantQuotation;
/// <summary>Untrusted posted contact and item fields; no persistence or authenticated-account authority.</summary>
internal sealed class CncSubmission
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Company { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Mobile { get; set; } = string.Empty;
    public string Telephone { get; set; } = string.Empty;
    public string TaxNumber { get; set; } = string.Empty;
    public string TaxBranch { get; set; } = string.Empty;
    public string TaxBranchCode { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string BillingBuilding { get; set; } = string.Empty;
    public string BillingStreet1 { get; set; } = string.Empty;
    public string BillingStreet2 { get; set; } = string.Empty;
    public string BillingCity { get; set; } = string.Empty;
    public string BillingProvince { get; set; } = string.Empty;
    public string BillingPostalCode { get; set; } = string.Empty;
    public string ShippingBuilding { get; set; } = string.Empty;
    public string ShippingStreet1 { get; set; } = string.Empty;
    public string ShippingStreet2 { get; set; } = string.Empty;
    public string ShippingCity { get; set; } = string.Empty;
    public string ShippingProvince { get; set; } = string.Empty;
    public string ShippingPostalCode { get; set; } = string.Empty;
    public string ShippingCountry { get; set; } = string.Empty;
    public List<ItemDetail> OrderItems { get; set; } = [];
    public bool ShipToBillingAddress { get; set; } = true;
    public IReadOnlyList<string> BillingAddressLines => ComposeAddressLines(
        this.BillingBuilding, this.BillingStreet1, this.BillingStreet2,
        this.BillingCity, this.BillingProvince, this.BillingPostalCode, this.Country);

    /// <summary>
    /// Gets the address the parts actually ship to, resolving the "same as billing"
    /// case so callers never have to.
    /// </summary>
    /// <value>The address lines, outermost last.</value>
    public IReadOnlyList<string> ShippingAddressLines => this.ShipToBillingAddress
        ? this.BillingAddressLines
        : ComposeAddressLines(
            this.ShippingBuilding, this.ShippingStreet1, this.ShippingStreet2,
            this.ShippingCity, this.ShippingProvince, this.ShippingPostalCode,
            string.IsNullOrWhiteSpace(this.ShippingCountry) ? this.Country : this.ShippingCountry);

    /// <summary>
    /// Gets the tax number exactly as the intranet records it: the number with its
    /// branch designation in parentheses, e.g. "0115562011815 (สำนักงานใหญ่)". Keeping
    /// the same shape here means the value transcribes without reinterpretation.
    /// </summary>
    /// <value>
    /// The tax number with its branch, or an empty string when no tax number was given.
    /// </value>
    public string FormattedTaxNumber
    {
        get
        {
            if (string.IsNullOrWhiteSpace(this.TaxNumber))
            {
                return string.Empty;
            }

            bool isBranch = string.Equals(this.TaxBranch, "branch", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(this.TaxBranchCode);

            string branch = isBranch
                ? $"สาขาที่ {this.TaxBranchCode.Trim()}"
                : "สำนักงานใหญ่";

            return $"{this.TaxNumber.Trim()} ({branch})";
        }
    }

    /// <summary>
    /// Drops the parts of an address the customer left blank, so an optional building
    /// or second street line never becomes an empty line in an email or a record.
    /// </summary>
    /// <param name="parts">The address components, outermost last.</param>
    /// <returns>The non-empty components, trimmed.</returns>
    private static IReadOnlyList<string> ComposeAddressLines(params string[] parts)
    {
        return parts
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => part.Trim())
            .ToList();
    }


}
