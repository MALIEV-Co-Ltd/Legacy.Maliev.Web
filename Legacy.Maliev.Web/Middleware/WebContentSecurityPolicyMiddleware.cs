namespace Legacy.Maliev.Web.Middleware;

public sealed class WebContentSecurityPolicyMiddleware(RequestDelegate next)
{
    private const string Policy =
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

    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(
            static state =>
            {
                var response = (HttpResponse)state;
                response.Headers.ContentSecurityPolicy = Policy;
                return Task.CompletedTask;
            },
            context.Response);
        return next(context);
    }
}
