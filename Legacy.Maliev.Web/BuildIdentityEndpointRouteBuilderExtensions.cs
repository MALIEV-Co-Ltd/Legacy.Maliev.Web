namespace Legacy.Maliev.Web;

internal static class BuildIdentityEndpointRouteBuilderExtensions
{
    internal static IEndpointConventionBuilder MapBuildIdentity(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet(
                "/web/build-identity",
                (HttpContext context, BuildIdentity identity) =>
                {
                    context.Response.Headers.CacheControl = "no-store";
                    return Results.Json(identity);
                })
            .ExcludeFromDescription();
}
