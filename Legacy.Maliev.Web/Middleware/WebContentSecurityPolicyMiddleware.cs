namespace Legacy.Maliev.Web.Middleware;

/// <summary>
/// Defines document and model-worker content-security policies and assembly-versioned worker URLs.
/// </summary>
public static class WebContentSecurityPolicy
{
    /// <summary>
    /// Specifies allowed public-document resource origins while forbidding objects and embedding by other sites.
    /// </summary>
    public const string DocumentPolicy =
        "default-src 'self'; " +
        "script-src 'self' 'unsafe-inline' 'wasm-unsafe-eval' https://www.googletagmanager.com https://*.googletagmanager.com https://tagmanager.google.com https://www.googleadservices.com https://pagead2.googlesyndication.com https://googleads.g.doubleclick.net https://www.google.com https://www.gstatic.com https://static.cloudflareinsights.com https://static.line-scdn.net; " +
        "style-src 'self' 'unsafe-inline' https://www.googletagmanager.com https://tagmanager.google.com; " +
        "img-src 'self' data: blob: https://storage.googleapis.com https://www.googletagmanager.com https://www.google-analytics.com https://www.google.com https://www.google.co.th https://www.googleadservices.com https://googleads.g.doubleclick.net https://pagead2.googlesyndication.com; " +
        "font-src 'self' data:; " +
        "connect-src 'self' https://www.googletagmanager.com https://*.googletagmanager.com https://www.google-analytics.com https://*.google-analytics.com https://*.analytics.google.com https://www.googleadservices.com https://pagead2.googlesyndication.com https://googleads.g.doubleclick.net https://*.g.doubleclick.net https://ad.doubleclick.net https://stats.g.doubleclick.net https://www.google.com https://*.google.com https://google.com https://www.google.co.th https://google.co.th https://cloudflareinsights.com https://static.cloudflareinsights.com https://127.51.68.120:8181 wss://127.51.68.120:*; " +
        "frame-src 'self' https://www.googletagmanager.com https://www.google.com https://recaptcha.google.com https://www.recaptcha.net; " +
        "worker-src 'self' blob:; " +
        "object-src 'none'; " +
        "frame-ancestors 'none'; " +
        "form-action 'self'; " +
        "base-uri 'self'";

    /// <summary>
    /// Extends the document policy with evaluation support required by the model worker.
    /// </summary>
    public static readonly string ModelWorkerPolicy = DocumentPolicy.Replace(
        "'wasm-unsafe-eval'",
        "'unsafe-eval' 'wasm-unsafe-eval'",
        StringComparison.Ordinal);

    /// <summary>
    /// Identifies this assembly build in worker URLs to invalidate stale browser assets.
    /// </summary>
    public static readonly string AssetVersion =
        typeof(WebContentSecurityPolicy).Assembly.ManifestModule.ModuleVersionId.ToString("N")[..16];

    /// <summary>
    /// Provides the model-viewer worker URL with this assembly's cache version.
    /// </summary>
    public static string ModelWorkerUrl =>
        "/src/app/js/model-viewer/model-viewer.worker.js?v=" + AssetVersion;

    /// <summary>
    /// Provides the CNC quotation worker URL with this assembly's cache version.
    /// </summary>
    public static string CncQuotationWorkerUrl =>
        "/src/app/js/cnc-quotation/cnc-quotation.worker.js?v=" + AssetVersion;
}

/// <summary>
/// Applies the document or model-worker security policy and strict-transport header before the response starts.
/// </summary>
/// <param name="next">The next request delegate invoked after security headers are registered.</param>
public sealed class WebContentSecurityPolicyMiddleware(RequestDelegate next)
{
    private const string ModelWorkerPath = "/src/app/js/model-viewer/model-viewer.worker.js";

    /// <summary>
    /// Selects the model-worker policy only for its exact path and registers security headers for the response.
    /// </summary>
    /// <param name="context">The current request whose path selects the response security policy.</param>
    /// <returns>The asynchronous downstream request after header registration.</returns>
    public Task InvokeAsync(HttpContext context)
    {
        bool isModelWorker = context.Request.Path.Equals(
            ModelWorkerPath,
            StringComparison.OrdinalIgnoreCase);
        string policy = isModelWorker
            ? WebContentSecurityPolicy.ModelWorkerPolicy
            : WebContentSecurityPolicy.DocumentPolicy;

        context.Response.OnStarting(
            static state =>
            {
                var (response, responsePolicy) = ((HttpResponse Response, string Policy))state;
                response.Headers.ContentSecurityPolicy = responsePolicy;
                response.Headers.StrictTransportSecurity = "max-age=63072000; includeSubDomains";
                return Task.CompletedTask;
            },
            (context.Response, policy));
        return next(context);
    }
}
