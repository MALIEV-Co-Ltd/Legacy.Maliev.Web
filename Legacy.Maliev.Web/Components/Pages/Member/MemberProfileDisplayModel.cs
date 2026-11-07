namespace Legacy.Maliev.Web.Components.Pages.Member;

/// <summary>
/// Carries editable customer identity, contact, and company fields for the member profile.
/// </summary>
/// <param name="FirstName">The customer's given name.</param>
/// <param name="LastName">The customer's family name.</param>
/// <param name="Email">The account email shown in the profile.</param>
/// <param name="Telephone">The optional fixed-line contact number.</param>
/// <param name="Mobile">The optional mobile contact number.</param>
/// <param name="Fax">The optional fax contact number.</param>
/// <param name="DateOfBirth">The birth date formatted for the date input, or null when unavailable.</param>
/// <param name="CompanyName">The optional company name associated with the customer.</param>
/// <param name="TaxNumber">The optional company tax identifier.</param>
/// <param name="Registrar">The optional company registration authority or registrar.</param>
/// <param name="Notification">The profile update outcome message, or null when absent.</param>
/// <param name="Errors">The profile validation or service failures displayed to the member.</param>
public sealed record MemberProfileDisplayModel(
    string FirstName,
    string LastName,
    string Email,
    string? Telephone,
    string? Mobile,
    string? Fax,
    string? DateOfBirth,
    string? CompanyName,
    string? TaxNumber,
    string? Registrar,
    string? Notification,
    IReadOnlyList<string> Errors)
{
    /// <summary>
    /// Provides blank profile fields when no customer profile can be displayed.
    /// </summary>
    public static MemberProfileDisplayModel Empty { get; } = new(
        string.Empty, string.Empty, string.Empty, null, null, null, null, null, null, null, null, []);
}
