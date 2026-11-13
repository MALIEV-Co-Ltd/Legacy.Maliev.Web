namespace Legacy.Maliev.Web.Components.Pages.Member;

/// <summary>
/// Carries formatted manufacturing, fulfillment, history, and file details for one member-owned order.
/// </summary>
/// <param name="Id">The customer order identifier.</param>
/// <param name="Name">The optional customer-supplied order name.</param>
/// <param name="Description">The optional order requirements or description.</param>
/// <param name="Process">The displayed manufacturing process label.</param>
/// <param name="Material">The displayed material label.</param>
/// <param name="MaterialGroup">The displayed material category label.</param>
/// <param name="SurfaceFinish">The displayed surface finish label.</param>
/// <param name="Color">The displayed color label.</param>
/// <param name="Quantity">The total number of parts requested.</param>
/// <param name="Manufactured">The number of parts reported as manufactured.</param>
/// <param name="Remaining">The formatted remaining part quantity or unavailable placeholder.</param>
/// <param name="UnitPrice">The formatted per-part price with the available currency label.</param>
/// <param name="Subtotal">The formatted order subtotal with the available currency label.</param>
/// <param name="LeadTime">The displayed promised production duration or unavailable placeholder.</param>
/// <param name="PromisedDate">The promised completion date in machine-readable and display forms.</param>
/// <param name="FinishedDate">The recorded completion date in machine-readable and display forms.</param>
/// <param name="CreatedDate">The creation instant with its localized display representation.</param>
/// <param name="ModifiedDate">The latest modification instant with its localized display representation.</param>
/// <param name="TrackingNumber">The shipment tracking identifier or unavailable placeholder.</param>
/// <param name="AllowCancellation">Whether the order currently permits a cancellation request.</param>
/// <param name="HasShippedStatus">Whether the order history contains a shipped status.</param>
/// <param name="Notification">The latest order-operation outcome message, or null when absent.</param>
/// <param name="Errors">The detail-loading or order-operation failures displayed to the member.</param>
/// <param name="History">The ordered status entries supplied for this order.</param>
/// <param name="Files">The downloadable files made available with this order.</param>
public sealed record MemberOrderDetailDisplayModel(
    int Id,
    string? Name,
    string? Description,
    string Process,
    string Material,
    string MaterialGroup,
    string SurfaceFinish,
    string Color,
    int Quantity,
    int Manufactured,
    string Remaining,
    string UnitPrice,
    string Subtotal,
    string LeadTime,
    MemberDateDisplayModel PromisedDate,
    MemberDateDisplayModel FinishedDate,
    MemberDateDisplayModel CreatedDate,
    MemberDateDisplayModel ModifiedDate,
    string TrackingNumber,
    bool AllowCancellation,
    bool HasShippedStatus,
    string? Notification,
    IReadOnlyList<string> Errors,
    IReadOnlyList<MemberOrderStatusDisplayModel> History,
    IReadOnlyList<MemberFileDisplayModel> Files)
{
    /// <summary>
    /// Provides an empty order-detail view with unavailable placeholders and no actions.
    /// </summary>
    public static MemberOrderDetailDisplayModel Empty { get; } = new(
        0, null, null, "-", "-", "-", "-", "-", 0, 0, "-", "-", "-", "-",
        MemberDateDisplayModel.Empty, MemberDateDisplayModel.Empty,
        MemberDateDisplayModel.Empty, MemberDateDisplayModel.Empty,
        "-", false, false, null, [], [], []);
}

/// <summary>
/// Carries one manufacturing-order status entry for the member timeline.
/// </summary>
/// <param name="Name">The status label reported by the order service.</param>
/// <param name="Description">The optional explanation attached to the status.</param>
/// <param name="CreatedDate">The recorded status instant and its localized display text.</param>
public sealed record MemberOrderStatusDisplayModel(
    string? Name,
    string? Description,
    MemberDateDisplayModel CreatedDate);

/// <summary>
/// Carries a member-visible file link and its creation time.
/// </summary>
/// <param name="FileName">The file name displayed to the member.</param>
/// <param name="Href">The validated download URL associated with the file.</param>
/// <param name="CreatedDate">The file creation instant and its localized display text.</param>
public sealed record MemberFileDisplayModel(
    string FileName,
    string Href,
    MemberDateDisplayModel CreatedDate);

/// <summary>
/// Carries a date or instant as machine-readable markup and localized display text.
/// </summary>
/// <param name="MachineValue">The invariant value used by the rendered time element.</param>
/// <param name="DisplayValue">The localized date or time displayed to the member.</param>
public sealed record MemberDateDisplayModel(string MachineValue, string DisplayValue)
{
    /// <summary>
    /// Provides an unavailable date with no machine-readable value.
    /// </summary>
    public static MemberDateDisplayModel Empty { get; } = new(string.Empty, "-");
}
