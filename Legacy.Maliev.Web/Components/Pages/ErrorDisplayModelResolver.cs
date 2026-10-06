using Legacy.Maliev.Web.Middleware;
using Microsoft.AspNetCore.Diagnostics;

namespace Legacy.Maliev.Web.Components.Pages;

internal static class ErrorDisplayModelResolver
{
    internal static ErrorDisplayModel? Resolve(HttpContext context, int? requestedStatusCode)
    {
        ArgumentNullException.ThrowIfNull(context);

        var hasException = context.Features.Get<IExceptionHandlerFeature>()?.Error is not null;
        var isReExecuted = context.Features.Get<IStatusCodeReExecuteFeature>() is not null;
        var statusCode = hasException
            ? StatusCodes.Status500InternalServerError
            : isReExecuted ? context.Response.StatusCode : requestedStatusCode;

        if (statusCode is null or < 400 or > 599)
        {
            return null;
        }

        ErrorIncidentDetails? incident = null;
        if (hasException && ErrorIncidentHandler.TryGet(context, out incident) && incident is not null)
        {
            // Exception-handler re-execution clears response headers while retaining
            // request items. Restore the existing incident without creating or logging one.
            incident = ErrorIncidentHandler.GetOrCreate(context, incident.OccurredAtUtc);
        }

        return new ErrorDisplayModel(
            statusCode == StatusCodes.Status404NotFound,
            incident?.IncidentId,
            incident?.OccurredAtUtc ?? default,
            statusCode,
            IsPreview: !hasException && !isReExecuted);
    }
}
