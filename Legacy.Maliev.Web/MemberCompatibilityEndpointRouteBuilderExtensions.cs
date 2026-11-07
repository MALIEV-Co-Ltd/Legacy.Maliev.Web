using Legacy.Maliev.Web.Application;

namespace Legacy.Maliev.Web;

/// <summary>
/// Maps retained member URLs to customer-scoped catalog responses and quotation navigation.
/// </summary>
public static class MemberCompatibilityEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Registers protected material-option and payment-success compatibility endpoints.
    /// </summary>
    /// <param name="endpoints">The endpoint builder to which the retained member routes are added.</param>
    /// <returns>The original endpoint builder with compatibility routes registered.</returns>
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
