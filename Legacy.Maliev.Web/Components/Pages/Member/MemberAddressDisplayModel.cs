namespace Legacy.Maliev.Web.Components.Pages.Member;

/// <summary>
/// Carries the member's billing and shipping addresses with editable country choices and submission feedback.
/// </summary>
/// <param name="Billing">The billing address fields displayed in the account form.</param>
/// <param name="Shipping">The shipping address fields displayed in the account form.</param>
/// <param name="Countries">The country lookup choices available to both addresses.</param>
/// <param name="Notification">The address update outcome message, or null when absent.</param>
/// <param name="Errors">Field-specific and form-level address validation messages.</param>
public sealed record MemberAddressDisplayModel(
    MemberAddressFieldsDisplayModel Billing,
    MemberAddressFieldsDisplayModel Shipping,
    IReadOnlyList<MemberAddressCountryOption> Countries,
    string? Notification,
    IReadOnlyList<MemberAddressError> Errors)
{
    /// <summary>
    /// Provides an unpopulated address form when account address data is unavailable.
    /// </summary>
    public static MemberAddressDisplayModel Empty { get; } = new(
        MemberAddressFieldsDisplayModel.Empty,
        MemberAddressFieldsDisplayModel.Empty,
        [],
        null,
        []);
}

/// <summary>
/// Carries one billing or shipping address for editing in the member account.
/// </summary>
/// <param name="Building">The optional building or organization-specific address line.</param>
/// <param name="AddressLine1">The required first street-address line.</param>
/// <param name="AddressLine2">The optional additional street-address line.</param>
/// <param name="City">The optional city or district.</param>
/// <param name="State">The optional province or region.</param>
/// <param name="PostalCode">The optional postal delivery code.</param>
/// <param name="CountryId">The country lookup identifier selected for this address.</param>
public sealed record MemberAddressFieldsDisplayModel(
    string? Building,
    string AddressLine1,
    string? AddressLine2,
    string? City,
    string? State,
    string? PostalCode,
    int CountryId)
{
    /// <summary>
    /// Provides blank address fields with no selected country.
    /// </summary>
    public static MemberAddressFieldsDisplayModel Empty { get; } = new(
        null,
        string.Empty,
        null,
        null,
        null,
        null,
        0);
}

/// <summary>
/// Represents one country choice in the member address editor.
/// </summary>
/// <param name="Id">The country identifier submitted with the address.</param>
/// <param name="Name">The country label displayed to the member.</param>
public sealed record MemberAddressCountryOption(int Id, string Name);

/// <summary>
/// Carries an address validation message and its optional field association.
/// </summary>
/// <param name="Field">The affected address field key, or null for a form-level failure.</param>
/// <param name="Message">The message displayed to the member.</param>
public sealed record MemberAddressError(string? Field, string Message);
