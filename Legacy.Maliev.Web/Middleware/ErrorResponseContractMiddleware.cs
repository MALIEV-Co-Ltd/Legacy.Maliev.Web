using System.Globalization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Routing;

namespace Legacy.Maliev.Web.Middleware;

/// <summary>
/// Enforces bounded error-page entry and preserves the trusted failure status without caching error responses.
/// </summary>
/// <param name="next">The next request delegate invoked after the error-response contract is applied.</param>
public sealed class ErrorResponseContractMiddleware(RequestDelegate next)
{
    /// <summary>
    /// Allows trusted failures or explicit valid previews into the error page and redirects unrelated error-page requests.
    /// </summary>
    /// <param name="context">The current request and response carrying trusted failure features or a preview status.</param>
    /// <returns>The asynchronous downstream request, or a completed task after redirecting an unsupported error-page entry.</returns>
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

        var responseStatusCode = hasExceptionContext
            ? StatusCodes.Status500InternalServerError
            : statusCodeReExecute?.OriginalStatusCode ?? (hasValidPreviewStatus ? statusCode : (int?)null);
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

        return hasExceptionContext && !HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method)
            ? RenderExceptionAsync(context)
            : next(context);
    }

    private async Task RenderExceptionAsync(HttpContext context)
    {
        var originalMethod = context.Request.Method;
        var originalEndpoint = context.GetEndpoint();
        var originalRouteValues = context.Request.RouteValues;
        try
        {
            // Exception reexecution renders a read-only error document, not another form submission.
            context.Request.Method = HttpMethods.Get;
            // WebApplication reroutes before this middleware. Discard its method-specific
            // selection so the downstream UseRouting can select the error document again.
            context.SetEndpoint(null);
            context.Request.RouteValues = new RouteValueDictionary();
            await next(context);
        }
        finally
        {
            context.Request.Method = originalMethod;
            context.SetEndpoint(originalEndpoint);
            context.Request.RouteValues = originalRouteValues;
        }
    }
}
