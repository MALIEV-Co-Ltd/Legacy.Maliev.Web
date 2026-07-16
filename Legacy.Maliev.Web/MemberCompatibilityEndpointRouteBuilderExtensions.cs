namespace Legacy.Maliev.Web;

public static class MemberCompatibilityEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapMemberCompatibilityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
                "/member/orders/cnc-machining",
                static () => Results.Redirect("/Quotation?item=CNC-Machining"))
            .RequireAuthorization()
            .ExcludeFromDescription();
        endpoints.MapGet(
                "/member/orders/3d-printing",
                static () => Results.Redirect("/Quotation?item=3D-Printing"))
            .RequireAuthorization()
            .ExcludeFromDescription();
        endpoints.MapGet(
                "/member/orders/3d-scanning",
                static () => Results.Redirect("/Quotation?item=3D-Scanning"))
            .RequireAuthorization()
            .ExcludeFromDescription();
        endpoints.MapGet(
                "/member/account/manage/createpassword",
                static () => Results.Redirect("/Member/Account/Manage/ChangePassword"))
            .RequireAuthorization()
            .ExcludeFromDescription();
        endpoints.MapGet(
                "/member/quotations/paymentsuccess",
                static () => Results.Redirect("/Member/Quotations"))
            .RequireAuthorization()
            .ExcludeFromDescription();

        return endpoints;
    }
}
