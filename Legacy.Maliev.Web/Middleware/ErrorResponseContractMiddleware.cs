using System.Globalization;

namespace Legacy.Maliev.Web.Middleware;

public sealed class ErrorResponseContractMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/Error", StringComparison.OrdinalIgnoreCase))
        {
            return next(context);
        }

        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";

        if (context.Request.Query.TryGetValue("code", out var values)
            && int.TryParse(values.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var statusCode)
            && statusCode is >= 400 and <= 599)
        {
            context.Response.OnStarting(
                static state =>
                {
                    var (response, code) = ((HttpResponse Response, int Code))state;
                    response.StatusCode = code;
                    return Task.CompletedTask;
                },
                (context.Response, statusCode));
        }

        return next(context);
    }
}
