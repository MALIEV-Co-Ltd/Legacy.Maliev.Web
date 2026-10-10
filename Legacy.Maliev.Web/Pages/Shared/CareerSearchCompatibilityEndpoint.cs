using System.Globalization;
using Legacy.Maliev.Web.Application;

namespace Legacy.Maliev.Web.Pages.Shared;

internal static class CareerSearchCompatibilityEndpoint
{
    internal static bool Matches(HttpContext context) =>
        (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method))
        && context.Request.Path.Equals(new PathString("/career"), StringComparison.OrdinalIgnoreCase)
        && context.Request.Query["handler"].Count > 0
        && string.Equals(context.Request.Query["handler"][0], "Search", StringComparison.OrdinalIgnoreCase);

    internal static async Task HandleAsync(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        var query = context.Request.Query;
        var sizeValue = query["size"].FirstOrDefault();
        var indexValue = query["index"].FirstOrDefault();
        if (!PageSizeQuery.TryResolve(sizeValue, out var size)
            || (!string.IsNullOrWhiteSpace(indexValue)
                && !int.TryParse(indexValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var sort = Enum.TryParse<CareerSort>(query["sort"].FirstOrDefault(), out var parsedSort)
            ? parsedSort
            : CareerSort.JobId_Ascending;
        var searchValue = query["search"].FirstOrDefault();
        var search = string.IsNullOrWhiteSpace(searchValue) ? null : searchValue;
        var index = int.TryParse(indexValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedIndex)
            ? parsedIndex
            : 1;
        var client = context.RequestServices.GetRequiredService<ICareerClient>();
        var offers = await client.GetOffersAsync(sort, search, string.IsNullOrEmpty(search) ? index : 1, size, context.RequestAborted);
        await Results.Json((offers.Value?.Items ?? [])
            .Where(offer => offer.IsFilled == false)
            .ToArray()).ExecuteAsync(context);
    }
}
