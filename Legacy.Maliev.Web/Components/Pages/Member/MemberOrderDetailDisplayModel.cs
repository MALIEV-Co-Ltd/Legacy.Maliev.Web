namespace Legacy.Maliev.Web.Components.Pages.Member;

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
    public static MemberOrderDetailDisplayModel Empty { get; } = new(
        0, null, null, "-", "-", "-", "-", "-", 0, 0, "-", "-", "-", "-",
        MemberDateDisplayModel.Empty, MemberDateDisplayModel.Empty,
        MemberDateDisplayModel.Empty, MemberDateDisplayModel.Empty,
        "-", false, false, null, [], [], []);
}

public sealed record MemberOrderStatusDisplayModel(
    string? Name,
    string? Description,
    MemberDateDisplayModel CreatedDate);

public sealed record MemberFileDisplayModel(
    string FileName,
    string Href,
    MemberDateDisplayModel CreatedDate);

public sealed record MemberDateDisplayModel(string MachineValue, string DisplayValue)
{
    public static MemberDateDisplayModel Empty { get; } = new(string.Empty, "-");
}
