namespace Legacy.Maliev.Web.Middleware;

public static class WebContentSecurityPolicy
{
    public const string DocumentPolicy =
        "default-src 'self'; " +
        "script-src 'self' 'unsafe-inline' 'wasm-unsafe-eval' https://www.googletagmanager.com https://*.googletagmanager.com https://tagmanager.google.com https://www.googleadservices.com https://pagead2.googlesyndication.com https://googleads.g.doubleclick.net https://www.google.com https://www.gstatic.com https://static.cloudflareinsights.com https://static.line-scdn.net; " +
        "style-src 'self' 'unsafe-inline' https://www.googletagmanager.com https://tagmanager.google.com; " +
        "img-src 'self' data: blob: https://storage.googleapis.com https://www.googletagmanager.com https://www.google-analytics.com https://www.google.com https://www.google.co.th https://www.googleadservices.com https://googleads.g.doubleclick.net https://pagead2.googlesyndication.com; " +
        "font-src 'self' data:; " +
        "connect-src 'self' https://www.googletagmanager.com https://*.googletagmanager.com https://www.google-analytics.com https://*.google-analytics.com https://*.analytics.google.com https://www.googleadservices.com https://pagead2.googlesyndication.com https://googleads.g.doubleclick.net https://*.g.doubleclick.net https://ad.doubleclick.net https://stats.g.doubleclick.net https://www.google.com https://*.google.com https://google.com https://www.google.co.th https://google.co.th https://cloudflareinsights.com https://static.cloudflareinsights.com; " +
        "frame-src 'self' https://www.googletagmanager.com https://www.google.com https://recaptcha.google.com https://www.recaptcha.net; " +
        "worker-src 'self' blob:; " +
        "object-src 'none'; " +
        "frame-ancestors 'none'; " +
        "form-action 'self'; " +
        "base-uri 'self'";

    public static readonly string ModelWorkerPolicy = DocumentPolicy.Replace(
        "'wasm-unsafe-eval'",
        "'unsafe-eval' 'wasm-unsafe-eval'",
        StringComparison.Ordinal);

    public static readonly string AssetVersion =
        typeof(WebContentSecurityPolicy).Assembly.ManifestModule.ModuleVersionId.ToString("N")[..16];

    public static string ModelWorkerUrl =>
        "/src/app/js/model-viewer/model-viewer.worker.js?v=" + AssetVersion;

    public static string CncQuotationWorkerUrl =>
        "/src/app/js/cnc-quotation/cnc-quotation.worker.js?v=" + AssetVersion;
}

public sealed class WebContentSecurityPolicyMiddleware(RequestDelegate next)
{
    private const string ModelWorkerPath = "/src/app/js/model-viewer/model-viewer.worker.js";

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
                return Task.CompletedTask;
            },
            (context.Response, policy));
        return next(context);
    }
}
