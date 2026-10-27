using System.Net.Http.Json;
using System.Text.Json;
using Legacy.Maliev.Web.Application;

namespace Legacy.Maliev.Web.Infrastructure;

internal static class CareerOfferPageReader
{
    public static async Task<CareerOfferPage> ReadAsync(HttpContent content, CancellationToken cancellationToken)
    {
        using var document = await content.ReadFromJsonAsync<JsonDocument>(cancellationToken);
        if (document is null || document.RootElement.ValueKind != JsonValueKind.Object)
            throw new JsonException("The career page must be an object with a count.");
        JsonElement? canonical = null;
        JsonElement? legacy = null;
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (string.Equals(property.Name, "totalRecords", StringComparison.OrdinalIgnoreCase))
            {
                if (canonical.HasValue) throw new JsonException("The career page has duplicate canonical counts.");
                canonical = property.Value;
            }
            if (string.Equals(property.Name, "totalItems", StringComparison.OrdinalIgnoreCase))
            {
                if (legacy.HasValue) throw new JsonException("The career page has duplicate legacy counts.");
                legacy = property.Value;
            }
        }
        if (!canonical.HasValue && !legacy.HasValue)
            throw new JsonException("The career page count is missing.");
        var canonicalCount = canonical.HasValue ? ReadCount(canonical.Value) : (int?)null;
        var legacyCount = legacy.HasValue ? ReadCount(legacy.Value) : (int?)null;
        if (canonicalCount.HasValue && legacyCount.HasValue && canonicalCount.Value != legacyCount.Value)
            throw new JsonException("The career page counts conflict.");
        var page = document.RootElement.Deserialize<CareerOfferPage>(JsonSerializerOptions.Web)
            ?? throw new JsonException("The career page is missing.");
        var count = canonicalCount ?? legacyCount ?? throw new JsonException("The career page count is missing.");
        return page with { TotalItems = count };
    }

    private static int ReadCount(JsonElement value)
    {
        // Preserve the existing default Web reader's integer/numeric-string handling.
        var count = value.Deserialize<int>(JsonSerializerOptions.Web);
        if (count < 0) throw new JsonException("The career page count must be nonnegative.");
        return count;
    }
}
