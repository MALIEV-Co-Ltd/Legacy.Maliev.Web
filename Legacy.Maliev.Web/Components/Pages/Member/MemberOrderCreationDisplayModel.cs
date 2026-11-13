using Legacy.Maliev.Web.Application;

namespace Legacy.Maliev.Web.Components.Pages.Member;

/// <summary>
/// Carries a member manufacturing-order draft, validated catalog choices, and form feedback.
/// </summary>
/// <param name="Kind">The manufacturing process family that selects the order form.</param>
/// <param name="Catalog">The available process and material catalog, or null when unavailable.</param>
/// <param name="Colors">The color choices offered for the selected material.</param>
/// <param name="SurfaceFinishes">The finish choices offered for the selected material.</param>
/// <param name="Name">The submitted order name.</param>
/// <param name="Description">The optional manufacturing requirements supplied by the customer.</param>
/// <param name="Quantity">The requested number of manufactured parts.</param>
/// <param name="ProcessId">The selected manufacturing process identifier.</param>
/// <param name="MaterialId">The selected material identifier.</param>
/// <param name="ColorId">The selected material color identifier.</param>
/// <param name="SurfaceFinishId">The selected surface finish identifier.</param>
/// <param name="AllowSocialMedia">Whether the customer permits sharing this order on social media.</param>
/// <param name="AcceptTermsAndConditions">Whether the customer accepted the order terms.</param>
/// <param name="IsMetric">Whether the supplied dimensions use the metric unit choice.</param>
/// <param name="Width">The optional width supplied for the order.</param>
/// <param name="Length">The optional length supplied for the order.</param>
/// <param name="Height">The optional height supplied for the order.</param>
/// <param name="OperationId">The operation identifier retained across submission attempts.</param>
/// <param name="Notification">The submission outcome message, or null when absent.</param>
/// <param name="Errors">The order validation or service failures displayed by the form.</param>
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
    /// <summary>
    /// Lists distinct catalog file extensions accepted by the order upload control.
    /// </summary>
    public string AcceptedExtensions => string.Join(
        ',',
        Catalog?.FileFormats
            .Select(format => format.Extension?.Trim())
            .Where(extension => !string.IsNullOrWhiteSpace(extension) && extension.StartsWith(".", StringComparison.Ordinal))
            .Distinct(StringComparer.OrdinalIgnoreCase)
        ?? []);

    /// <summary>
    /// Selects the member submission route for the requested manufacturing process family.
    /// </summary>
    public string FormAction => Kind switch
    {
        CustomerOrderKind.Additive => "/Member/Orders/3D-Printing?handler=Submit",
        CustomerOrderKind.Scanning => "/Member/Orders/3D-Scanning?handler=Submit",
        CustomerOrderKind.Machining => "/Member/Orders/CNC-Machining?handler=Submit",
        _ => throw new ArgumentOutOfRangeException(nameof(Kind)),
    };

    /// <summary>
    /// Selects the displayed order-form title for the manufacturing process family.
    /// </summary>
    public string Title => Kind switch
    {
        CustomerOrderKind.Additive => "Additive Manufacturing",
        CustomerOrderKind.Scanning => "3D Scanning",
        CustomerOrderKind.Machining => "CNC Manufacturing",
        _ => throw new ArgumentOutOfRangeException(nameof(Kind)),
    };
}
