using Legacy.Maliev.Web.Application;

namespace Legacy.Maliev.Web.Components.Pages.Member;

public sealed record MemberOrderCreationDisplayModel(
    CustomerOrderKind Kind,
    CustomerOrderCatalog? Catalog,
    IReadOnlyList<CustomerOrderCatalogOption> Colors,
    IReadOnlyList<CustomerOrderCatalogOption> SurfaceFinishes,
    string Name,
    string? Description,
    int Quantity,
    int ProcessId,
    int MaterialId,
    int ColorId,
    int SurfaceFinishId,
    bool AllowSocialMedia,
    bool AcceptTermsAndConditions,
    bool IsMetric,
    decimal? Width,
    decimal? Length,
    decimal? Height,
    string OperationId,
    string? Notification,
    IReadOnlyList<string> Errors)
{
    public string AcceptedExtensions => string.Join(
        ',',
        Catalog?.FileFormats
            .Select(format => format.Extension?.Trim())
            .Where(extension => !string.IsNullOrWhiteSpace(extension) && extension.StartsWith(".", StringComparison.Ordinal))
            .Distinct(StringComparer.OrdinalIgnoreCase)
        ?? []);

    public string FormAction => Kind switch
    {
        CustomerOrderKind.Additive => "/Member/Orders/3D-Printing?handler=Submit",
        CustomerOrderKind.Scanning => "/Member/Orders/3D-Scanning?handler=Submit",
        CustomerOrderKind.Machining => "/Member/Orders/CNC-Machining?handler=Submit",
        _ => throw new ArgumentOutOfRangeException(nameof(Kind)),
    };

    public string Title => Kind switch
    {
        CustomerOrderKind.Additive => "Additive Manufacturing",
        CustomerOrderKind.Scanning => "3D Scanning",
        CustomerOrderKind.Machining => "CNC Manufacturing",
        _ => throw new ArgumentOutOfRangeException(nameof(Kind)),
    };
}
