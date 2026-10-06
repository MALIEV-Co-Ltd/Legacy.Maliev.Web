using System.Threading.RateLimiting;
using Legacy.Maliev.Web.Application;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.RateLimiting;

namespace Legacy.Maliev.Web;

public static class ThaiLookupEndpoints
{
    public static IServiceCollection AddThaiLookupBoundary(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy("thai-lookup", context => RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
            { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });
        return services;
    }

    public static IEndpointRouteBuilder MapThaiLookupEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // POST keeps pasted addresses/search queries out of URLs. Existing antiforgery form token is required.
        var group = endpoints.MapGroup("/lookups").RequireRateLimiting("thai-lookup");
        group.MapPost("/addresses/search", async (HttpContext context, IAntiforgery antiforgery, IThaiLookupClient client) =>
            await Handle<ThaiAddressQuery, ThaiAddressPage>(context, antiforgery, client.SearchAddressAsync));
        group.MapPost("/addresses/resolve", async (HttpContext context, IAntiforgery antiforgery, IThaiLookupClient client) =>
            await Handle<ThaiAddressResolveRequest, ThaiAddressResolution>(context, antiforgery, client.ResolveAddressAsync));
        group.MapPost("/companies/search", async (HttpContext context, IAntiforgery antiforgery, IThaiLookupClient client) =>
            await Handle<CompanyLookupQuery, CompanyLookupPage>(context, antiforgery, client.SearchCompanyAsync));
        return endpoints;
    }

    private static async Task<IResult> Handle<TRequest, TResponse>(HttpContext context, IAntiforgery antiforgery,
        Func<TRequest, CancellationToken, Task<LookupResult<TResponse>>> send)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } body) body.MaxRequestBodySize = 16384;
        try
        {
            await antiforgery.ValidateRequestAsync(context);
            var request = await context.Request.ReadFromJsonAsync<TRequest>(context.RequestAborted);
            if (request is null) return Results.BadRequest();
            var result = await send(request, context.RequestAborted);
            return result.StatusCode == 200 ? Results.Json(result.Value) : Results.Problem(statusCode: result.StatusCode,
                title: result.StatusCode == 429 ? "Lookup is rate limited. Try again later or enter details manually."
                    : result.StatusCode == 422 ? "Lookup capability is unsupported." : result.StatusCode == 400
                    ? "Invalid lookup request." : "Lookup is temporarily unavailable. Manual entry remains available.");
        }
        catch (Exception exception) when (exception is AntiforgeryValidationException or System.Text.Json.JsonException
            or BadHttpRequestException or InvalidOperationException)
        {
            return Results.BadRequest();
        }
    }
}
