namespace Legacy.Maliev.Web.Application;

// Consumer projection of Catalog #44/#45/#46 proposals. Confirm against producer OpenAPI before release.
public sealed record ThaiProvince(string Code, string NameTh, string? NameEn);
public sealed record ThaiDistrict(string Code, string ProvinceCode, string NameTh, string? NameEn);
public sealed record ThaiSubdistrict(string Code, string DistrictCode, string NameTh, string? NameEn);
public sealed record ThaiAddressCombination(ThaiProvince Province, ThaiDistrict District, ThaiSubdistrict Subdistrict, string Postcode);
public sealed record ThaiAddressPage(string DatasetVersion, IReadOnlyList<ThaiAddressCombination> Items, bool HasMore, string? NextCursor);
public sealed record ThaiAddressConstraints(string? ProvinceCode = null, string? DistrictCode = null, string? SubdistrictCode = null, string? Postcode = null);
public sealed record ThaiAddressQuery(string Q, ThaiAddressConstraints? Constraints = null, int Limit = 20, string? Cursor = null);
public sealed record ThaiAddressResolveRequest(string Text, ThaiAddressConstraints? Constraints = null);
public sealed record ThaiAddressUniqueFields(ThaiProvince? Province, ThaiDistrict? District, ThaiSubdistrict? Subdistrict, string? Postcode);
public sealed record ThaiAddressSpan(int Start, int Length, string Kind, string Text);
public sealed record ThaiAddressResolution(string DatasetVersion, string OriginalText, string NormalizedText, string Outcome,
    IReadOnlyList<ThaiAddressCombination> Candidates, bool HasMore, ThaiAddressUniqueFields UniqueFields,
    string DetailText, IReadOnlyList<ThaiAddressSpan> ExtractedSpans, IReadOnlyList<string> Conflicts);
public sealed record CompanyLookupQuery(string Q, string QueryType = "name", string Language = "th", int Limit = 20);
public sealed record CompanyLookupItem(string? NameTh, string? NameEn, string? TaxId, string? Status,
    string? CompanyType, string? Objectives, string? RegisteredAddress, string? SourceUrl, DateTimeOffset? RetrievedAt);
public sealed record CompanyLookupPage(string Outcome, string Provider, string Capability, IReadOnlyList<CompanyLookupItem> Items, bool HasMore);
public sealed record LookupResult<T>(T? Value, int StatusCode);

public interface IThaiLookupClient
{
    Task<LookupResult<ThaiAddressPage>> SearchAddressAsync(ThaiAddressQuery query, CancellationToken cancellationToken);
    Task<LookupResult<ThaiAddressResolution>> ResolveAddressAsync(ThaiAddressResolveRequest request, CancellationToken cancellationToken);
    Task<LookupResult<CompanyLookupPage>> SearchCompanyAsync(CompanyLookupQuery query, CancellationToken cancellationToken);
}

public static class ThaiLookupValidation
{
    public static string NormalizeDigits(string value) => string.Concat(value.Select(c => c is >= '๐' and <= '๙' ? (char)('0' + c - '๐') : c));
    private static bool Digits(string? value, int length) => value is not null && NormalizeDigits(value).Length == length
        && NormalizeDigits(value).All(c => c is >= '0' and <= '9');
    private static bool Code(string? value, int length) => value is not null && value.Length == length
        && value.All(c => c is >= '0' and <= '9');
    public static bool Valid(ThaiAddressConstraints? value) => value is null ||
        ((value.ProvinceCode is null || Code(value.ProvinceCode, 2)) &&
         (value.DistrictCode is null || Code(value.DistrictCode, 4)) &&
         (value.SubdistrictCode is null || Code(value.SubdistrictCode, 6)) &&
         (value.Postcode is null || Digits(value.Postcode, 5)));
    public static bool Valid(ThaiAddressQuery value) => value.Q is not null && value.Q.Length <= 128
        && value.Limit is >= 1 and <= 50 && (value.Cursor is null || value.Cursor.Length <= 512) && Valid(value.Constraints);
    public static bool Valid(ThaiAddressResolveRequest value) => !string.IsNullOrWhiteSpace(value.Text)
        && value.Text.Length <= 2048 && Valid(value.Constraints);
    public static bool Valid(CompanyLookupQuery value) => value.Q is not null && value.Limit is >= 1 and <= 50
        && value.Language is "th" or "en" && (value.QueryType == "tax-id" ? Digits(value.Q, 13)
            : value.QueryType == "name" && value.Q.Trim().Length is >= 2 and <= 128);
}
