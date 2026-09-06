using Microsoft.Extensions.Logging;

namespace Legacy.Maliev.Web.Middleware;

internal static class ErrorIncidentHandler
{
    internal const string HeaderName = "X-Incident-Id";

    private static readonly object IncidentDetailsKey = new();

    internal static ErrorIncidentDetails GetOrCreate(
        HttpContext context,
        DateTimeOffset occurredAtUtc)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Items.TryGetValue(IncidentDetailsKey, out var value)
            && value is ErrorIncidentDetails existingIncident)
        {
            AddResponseHeader(context, existingIncident.IncidentId);
            return existingIncident;
        }

        var incident = new ErrorIncidentDetails(
            Guid.NewGuid().ToString("N"),
            occurredAtUtc.ToUniversalTime());
        context.Items[IncidentDetailsKey] = incident;
        AddResponseHeader(context, incident.IncidentId);
        return incident;
    }

    internal static ErrorIncidentDetails LogUnhandledFailure(
        HttpContext context,
        ILogger logger,
        Exception exception,
        DateTimeOffset occurredAtUtc)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(exception);

        var incident = GetOrCreate(context, occurredAtUtc);
        var statusCode = context.Response.StatusCode is >= 400 and <= 599
            ? context.Response.StatusCode
            : StatusCodes.Status500InternalServerError;

        logger.LogCritical(
            "{EventName} Service={Service} Method={Method} Path={Path} StatusCode={StatusCode} ExceptionType={ExceptionType} IncidentId={IncidentId} OccurredAtUtc={OccurredAtUtc}",
            "UnhandledRequestFailure",
            typeof(Program).Assembly.GetName().Name ?? "Legacy.Maliev.Web",
            context.Request.Method,
            context.Request.Path.Value ?? "/",
            statusCode,
            exception.GetType().Name,
            incident.IncidentId,
            incident.OccurredAtUtc.ToUniversalTime().ToString("O"));

        return incident;
    }

    internal static bool TryGet(HttpContext context, out ErrorIncidentDetails? incident)
    {
        ArgumentNullException.ThrowIfNull(context);
        incident = context.Items.TryGetValue(IncidentDetailsKey, out var value)
            ? value as ErrorIncidentDetails
            : null;
        return incident is not null;
    }

    private static void AddResponseHeader(HttpContext context, string incidentId)
    {
        if (!context.Response.HasStarted)
        {
            context.Response.Headers[HeaderName] = incidentId;
        }
    }
}

internal sealed record ErrorIncidentDetails(string IncidentId, DateTimeOffset OccurredAtUtc);

internal sealed class ErrorIncidentMiddleware(
    RequestDelegate next,
    ILogger<ErrorIncidentMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception exception)
        {
            ErrorIncidentHandler.LogUnhandledFailure(
                context,
                logger,
                exception,
                DateTimeOffset.UtcNow);
            throw;
        }
    }
}
