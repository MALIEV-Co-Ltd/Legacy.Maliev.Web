namespace Legacy.Maliev.Web.Middleware;

public sealed class WebContentSecurityPolicyMiddleware(RequestDelegate next)
{
    private const string Policy =
        "default-src 'self'; " +
        "script-src 'self' 'unsafe-inline' https://www.googletagmanager.com https://*.googletagmanager.com https://tagmanager.google.com https://www.googleadservices.com https://pagead2.googlesyndication.com https://googleads.g.doubleclick.net https://www.google.com https://www.gstatic.com; " +
        "style-src 'self' 'unsafe-inline' https://www.googletagmanager.com https://tagmanager.google.com; " +
        "img-src 'self' data: https:; " +
        "font-src 'self' data:; " +
        "connect-src 'self' https://www.googletagmanager.com https://*.googletagmanager.com https://www.google-analytics.com https://*.google-analytics.com https://*.analytics.google.com https://www.googleadservices.com https://pagead2.googlesyndication.com https://googleads.g.doubleclick.net https://*.g.doubleclick.net https://ad.doubleclick.net https://stats.g.doubleclick.net https://www.google.com https://*.google.com https://google.com https://www.google.co.th https://google.co.th; " +
        "frame-src 'self' https://www.googletagmanager.com https://www.google.com https://recaptcha.google.com; " +
        "worker-src 'self' blob:; " +
        "object-src 'none'; " +
        "frame-ancestors 'none'; " +
        "form-action 'self'; " +
        "base-uri 'self'";

    public Task InvokeAsync(HttpContext context)
    {
        context.Response.Headers.ContentSecurityPolicy = Policy;
        return next(context);
    }
}
