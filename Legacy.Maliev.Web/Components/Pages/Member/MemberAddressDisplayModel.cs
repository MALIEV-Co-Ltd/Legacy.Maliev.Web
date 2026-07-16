namespace Legacy.Maliev.Web.Components.Pages.Member;

public sealed record MemberAddressDisplayModel(
    MemberAddressFieldsDisplayModel Billing,
    MemberAddressFieldsDisplayModel Shipping,
    IReadOnlyList<MemberAddressCountryOption> Countries,
    string? Notification,
    IReadOnlyList<MemberAddressError> Errors)
{
    public static MemberAddressDisplayModel Empty { get; } = new(
        MemberAddressFieldsDisplayModel.Empty,
        MemberAddressFieldsDisplayModel.Empty,
        [],
        null,
        []);
}

public sealed record MemberAddressFieldsDisplayModel(
    string? Building,
    string AddressLine1,
    string? AddressLine2,
    string? City,
    string? State,
    string? PostalCode,
    int CountryId)
{
    public static MemberAddressFieldsDisplayModel Empty { get; } = new(
        null,
        string.Empty,
        null,
        null,
        null,
        null,
        0);
}

public sealed record MemberAddressCountryOption(int Id, string Name);

public sealed record MemberAddressError(string? Field, string Message);
