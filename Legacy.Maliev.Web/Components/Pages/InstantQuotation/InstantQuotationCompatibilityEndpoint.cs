using System.Globalization;

namespace Legacy.Maliev.Web.Components.Pages.InstantQuotation;

public static class InstantQuotationCompatibilityEndpoint
{
    public static bool Matches(HttpContext context)
    {
        if (!HttpMethods.IsGet(context.Request.Method)
            || !context.Request.Path.Equals("/InstantQuotation/3D-Printing", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var handler = context.Request.Query["handler"].ToString();
        return string.Equals(handler, "GetEstimate", StringComparison.OrdinalIgnoreCase)
            || string.Equals(handler, "GetOrderTotal", StringComparison.OrdinalIgnoreCase);
    }

    public static Task HandleAsync(HttpContext context)
    {
        var query = context.Request.Query;
        var payload = string.Equals(query["handler"], "GetEstimate", StringComparison.OrdinalIgnoreCase)
            ? InstantQuotationCalculator.GetEstimate(
                query["material"],
                ParseDouble(query["dimensionZ"]),
                ParseDouble(query["volume"]),
                ParseDouble(query["footprint"]),
                query["areaProfile"],
                query["perimeterProfile"],
                query["currency"],
                ParseInt32(query["quantity"]))
            : InstantQuotationCalculator.GetOrderTotal(
                query["processes"],
                query["subtotals"],
                ParseDouble(query["totalWeightGrams"]),
                ParseDouble(query["totalBoundingCm3"]),
                query["currency"]);

        return context.Response.WriteAsJsonAsync(payload, context.RequestAborted);
    }

    private static double ParseDouble(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;

    private static int ParseInt32(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
}
