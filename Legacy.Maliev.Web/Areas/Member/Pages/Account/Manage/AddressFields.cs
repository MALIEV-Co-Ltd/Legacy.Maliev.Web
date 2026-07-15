using Legacy.Maliev.Web.Application;

namespace Legacy.Maliev.Web.Areas.Member.Pages.Account.Manage;

public sealed record AddressFields(
    string Prefix,
    string? Building,
    string AddressLine1,
    string? AddressLine2,
    string? City,
    string? State,
    string? PostalCode,
    int CountryId,
    IReadOnlyList<Country> Countries);
