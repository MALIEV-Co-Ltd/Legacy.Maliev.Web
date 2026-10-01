using Legacy.Maliev.Web.Pages;

namespace Legacy.Maliev.Web;

internal static class SitemapEndpointRouteBuilderExtensions
{
    internal static IEndpointConventionBuilder MapLegacySitemap(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet(
                "/Sitemap",
                (HttpContext context) =>
                {
                    context.Response.Headers.CacheControl = "public,max-age=3600";
                    return Results.Text(
                        SitemapXmlRenderer.Render(PublicSearchRouteCatalog.Routes),
                        "application/xml; charset=utf-8");
                })
            .ExcludeFromDescription();
}
