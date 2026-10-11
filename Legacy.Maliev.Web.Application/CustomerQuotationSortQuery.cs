namespace Legacy.Maliev.Web.Application;

/// <summary>Matches the six defined sort values accepted by the quotation producer.</summary>
public static class CustomerQuotationSortQuery
{
    /// <summary>Resolves omitted, enum-name or defined numeric query values to a canonical wire name.</summary>
    /// <param name="value">The original query value.</param>
    /// <param name="canonical">The producer enum name, or an empty string when invalid.</param>
    /// <returns>Whether the value belongs to the defined producer contract.</returns>
    public static bool TryResolve(string? value, out string canonical)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            canonical = nameof(Sort.QuotationCreatedDate_Descending);
            return true;
        }
        if (!value.Contains(',')
            && Enum.TryParse<Sort>(value, true, out var parsed) && Enum.IsDefined(parsed))
        {
            canonical = parsed.ToString();
            return true;
        }
        canonical = string.Empty;
        return false;
    }

    // Immutable producer contract: QuotationSortType at d928d2bad1231c05ec9aba7ba99f51195d48fb2e.
    private enum Sort
    {
        QuotationId_Ascending,
        QuotationId_Descending,
        QuotationCreatedDate_Ascending,
        QuotationCreatedDate_Descending,
        QuotationModifiedDate_Ascending,
        QuotationModifiedDate_Descending,
    }
}
