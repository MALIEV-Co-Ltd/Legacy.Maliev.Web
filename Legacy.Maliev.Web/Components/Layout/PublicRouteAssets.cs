namespace Legacy.Maliev.Web.Components.Layout;

/// <summary>
/// Selects the production assets owned by the current public route.
/// </summary>
public static class PublicRouteAssets
{
    /// <summary>
    /// Gets route-scoped stylesheets for a request path.
    /// </summary>
    public static IReadOnlyList<string> GetStyles(PathString requestPath)
    {
        var path = Normalize(requestPath);
        if (path == "/")
        {
            return ["route-home.css"];
        }

        if (path == "/about")
        {
            return ["route-about.css"];
        }

        if (path == "/error")
        {
            return ["route-error.css"];
        }

        if (path == "/contact" || path.StartsWith("/quotation", StringComparison.OrdinalIgnoreCase))
        {
            return ["route-inquiry.css"];
        }

        if (IsInstantQuotation(path))
        {
            return ["route-instant-quotation.css"];
        }

        if (path == "/services")
        {
            return ["route-services-index.css"];
        }

        return path.StartsWith("/services/", StringComparison.OrdinalIgnoreCase)
            ? ["route-services.css"]
            : [];
    }

    /// <summary>
    /// Gets route-scoped scripts for a request path.
    /// </summary>
    public static IReadOnlyList<string> GetScripts(PathString requestPath)
    {
        var path = Normalize(requestPath);
        if (path == "/error")
        {
            return ["route-error.js"];
        }

        if (path == "/contact" || path.StartsWith("/quotation", StringComparison.OrdinalIgnoreCase))
        {
            return ["route-inquiry.js"];
        }

        if (IsInstantQuotation(path))
        {
            return ["route-instant-quotation.js"];
        }

        if (path == "/services")
        {
            return ["route-service-finder.js"];
        }

        if (path == "/services/3d-printing")
        {
            return ["route-service-printing.js"];
        }

        if (path == "/services/cnc-machining")
        {
            return ["route-service-cnc.js"];
        }

        if (path == "/services/3d-scanning")
        {
            return ["route-service-scanning.js"];
        }

        if (path == "/services/finishing-and-color")
        {
            return ["route-service-finishing.js"];
        }

        if (path.StartsWith("/services/", StringComparison.OrdinalIgnoreCase))
        {
            return ["route-service-toc.js"];
        }

        return path.StartsWith("/member/orders/", StringComparison.OrdinalIgnoreCase)
            && path != "/member/orders/history"
            ? ["route-member-order.js"]
            : [];
    }

    private static bool IsInstantQuotation(string path) =>
        path == "/instantquotation/3d-printing";

    private static string Normalize(PathString requestPath)
    {
        var path = requestPath.Value?.TrimEnd('/');
        return string.IsNullOrEmpty(path) ? "/" : path.ToLowerInvariant();
    }
}
