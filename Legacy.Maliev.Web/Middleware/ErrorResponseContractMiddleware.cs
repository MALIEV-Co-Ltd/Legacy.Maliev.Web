using System.Globalization;
using Microsoft.AspNetCore.Diagnostics;

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

        var hasExceptionContext = context.Features.Get<IExceptionHandlerFeature>()?.Error is not null;
        var statusCodeReExecute = context.Features.Get<IStatusCodeReExecuteFeature>();
        var hasTrustedFailureContext = hasExceptionContext || statusCodeReExecute is not null;
        var statusCode = 0;
        var hasValidPreviewStatus = context.Request.Query.TryGetValue("code", out var values)
            && int.TryParse(values.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out statusCode)
            && statusCode is >= 400 and <= 599;

        if (!hasTrustedFailureContext && !hasValidPreviewStatus)
        {
            context.Response.Redirect("/");
            return Task.CompletedTask;
        }

        var responseStatusCode = statusCodeReExecute?.OriginalStatusCode
            ?? (!hasExceptionContext && hasValidPreviewStatus ? statusCode : (int?)null);
        if (responseStatusCode is not null)
        {
            context.Response.OnStarting(
                static state =>
                {
                    var (response, code) = ((HttpResponse Response, int Code))state;
                    response.StatusCode = code;
                    return Task.CompletedTask;
                },
                (context.Response, responseStatusCode.Value));
        }

        return next(context);
    }
}
