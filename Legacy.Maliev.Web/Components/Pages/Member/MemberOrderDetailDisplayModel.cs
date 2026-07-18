namespace Legacy.Maliev.Web.Components.Pages.Member;

public sealed record MemberOrderDetailDisplayModel(
    int Id,
    string? Name,
    string? Description,
    string Process,
    int Quantity,
    int Manufactured,
    string Remaining,
    string Subtotal,
    string TrackingNumber,
    bool AllowCancellation,
    string? Notification,
    IReadOnlyList<string> Errors,
    IReadOnlyList<MemberOrderStatusDisplayModel> History,
    IReadOnlyList<string> FileNames)
{
    public static MemberOrderDetailDisplayModel Empty { get; } = new(
        0, null, null, "-", 0, 0, "-", "-", "-", false, null, [], [], []);
}

public sealed record MemberOrderStatusDisplayModel(string? Name, string CreatedDate);
