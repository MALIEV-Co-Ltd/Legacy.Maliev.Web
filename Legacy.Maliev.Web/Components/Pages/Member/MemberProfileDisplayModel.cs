namespace Legacy.Maliev.Web.Components.Pages.Member;

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
    public static MemberProfileDisplayModel Empty { get; } = new(
        string.Empty, string.Empty, string.Empty, null, null, null, null, null, null, null, null, []);
}
