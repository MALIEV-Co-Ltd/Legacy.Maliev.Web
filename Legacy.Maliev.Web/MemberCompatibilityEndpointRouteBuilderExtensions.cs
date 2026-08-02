using Legacy.Maliev.Web.Application;

namespace Legacy.Maliev.Web;

public static class MemberCompatibilityEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapMemberCompatibilityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
                "/member/orders/material-options",
                async (int materialId, ICustomerOrderCatalogClient catalogClient, CancellationToken cancellationToken) =>
                {
                    if (materialId <= 0) return Results.BadRequest();

                    var result = await catalogClient.GetMaterialOptionsAsync(materialId, cancellationToken);
                    if (!result.Authorized) return Results.Forbid();
                    if (!result.ServiceAvailable || result.Options is null)
                    {
                        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
                    }

                    return Results.Json(new
                    {
                        colors = result.Options.Colors.Select(static option => new { option.Id, option.Name }),
                        surfaceFinishes = result.Options.SurfaceFinishes.Select(static option => new { option.Id, option.Name }),
                    });
                })
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
