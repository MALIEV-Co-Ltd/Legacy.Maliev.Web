using System.Globalization;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Infrastructure;
using Microsoft.AspNetCore.Http;

namespace Legacy.Maliev.Web.Components.Pages.Member;

public static class MemberListLoaders
{
    private const string InvalidQueryValuesMessage = "One or more query values are invalid.";

    public static async Task<MemberOrderHistoryDisplayModel> LoadOrdersAsync(
        HttpContext context,
        IAccountSessionManager sessionManager,
        ICustomerOrderClient orderClient,
        string? search,
        string? sort,
        string? index,
        string? size,
        CancellationToken cancellationToken)
    {
        var (pageIndex, pageSize, queryErrors) = ParsePaging(index, size);
        var customerId = await sessionManager.GetCustomerDatabaseIdAsync(context, cancellationToken);
        if (customerId is null)
        {
            return MemberOrderHistoryDisplayModel.Empty with { Errors = ["Your account session has expired."] };
        }

        var result = await orderClient.ListAsync(
            customerId.Value,
            string.IsNullOrWhiteSpace(sort) ? "OrderCreatedDate_Descending" : sort,
            search,
            pageIndex,
            pageSize,
            cancellationToken);
        var page = result.Page ?? new CustomerOrderPage([], pageIndex, 0, 0);
        var errors = queryErrors.ToList();
        if (result.Page is null)
        {
            errors.Add(result.ServiceAvailable
                ? "Your order history could not be loaded."
                : "Order service is temporarily unavailable.");
        }

        return new MemberOrderHistoryDisplayModel(
            search,
            sort,
            pageSize,
            errors.Distinct(StringComparer.Ordinal).ToArray(),
            page.Items.Select(order => new MemberOrderHistoryItemDisplayModel(
                order.Id,
                order.Name,
                order.Description,
                order.Quantity,
                order.Subtotal?.ToString("N2") ?? "-",
                order.CreatedDate?.ToString("yyyy-MM-dd") ?? "-")).ToArray(),
            page.PageIndex > 1 ? BuildPageHref("/member/orders/history", page.PageIndex - 1, pageSize, sort, search) : null,
            page.PageIndex < page.TotalPages ? BuildPageHref("/member/orders/history", page.PageIndex + 1, pageSize, sort, search) : null);
    }

    public static async Task<MemberQuotationsIndexDisplayModel> LoadQuotationsAsync(
        HttpContext context,
        IAccountSessionManager sessionManager,
        ICustomerQuotationClient quotationClient,
        string? search,
        string? sort,
        string? index,
        string? size,
        CancellationToken cancellationToken)
    {
        var (pageIndex, pageSize, queryErrors) = ParsePaging(index, size);
        var customerId = await sessionManager.GetCustomerDatabaseIdAsync(context, cancellationToken);
        if (customerId is null)
        {
            return MemberQuotationsIndexDisplayModel.Empty with { Errors = ["Your account session has expired."] };
        }

        var result = await quotationClient.ListAsync(
            customerId.Value,
            string.IsNullOrWhiteSpace(sort) ? "QuotationCreatedDate_Descending" : sort,
            search,
            pageIndex,
            pageSize,
            cancellationToken);
        var page = result.Page ?? new CustomerQuotationPage([], pageIndex, 0, 0);
        var errors = queryErrors.ToList();
        if (result.Page is null)
        {
            errors.Add(result.ServiceAvailable
                ? "Your quotation history could not be loaded."
                : "Quotation service is temporarily unavailable.");
        }

        return new MemberQuotationsIndexDisplayModel(
            search,
            sort,
            pageSize,
            errors.Distinct(StringComparer.Ordinal).ToArray(),
            page.Items.Select(quotation => new MemberQuotationListItemDisplayModel(
                quotation.Id,
                quotation.Accepted,
                quotation.QuotedAmount?.ToString("N2") ?? "-",
                quotation.CurrencyId,
                quotation.ExpirationDate.ToString("yyyy-MM-dd"),
                quotation.CreatedDate?.ToString("yyyy-MM-dd") ?? "-")).ToArray(),
            page.HasPreviousPage ? BuildPageHref("/member/quotations", page.PageIndex - 1, pageSize, sort, search) : null,
            page.HasNextPage ? BuildPageHref("/member/quotations", page.PageIndex + 1, pageSize, sort, search) : null);
    }

    private static (int PageIndex, int PageSize, IReadOnlyList<string> Errors) ParsePaging(string? index, string? size)
    {
        var validIndex = string.IsNullOrWhiteSpace(index)
            || int.TryParse(index, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);
        var validSize = string.IsNullOrWhiteSpace(size)
            || int.TryParse(size, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);
        var pageIndex = int.TryParse(index, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedIndex)
            ? Math.Max(parsedIndex, 1)
            : 1;
        var pageSize = int.TryParse(size, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedSize)
            ? Math.Clamp(parsedSize, 1, 100)
            : 25;
        return (pageIndex, pageSize, validIndex && validSize ? [] : [InvalidQueryValuesMessage]);
    }

    private static string BuildPageHref(
        string path,
        int pageIndex,
        int pageSize,
        string? sort,
        string? search)
    {
        var values = new List<KeyValuePair<string, string?>>
        {
            new("index", pageIndex.ToString(CultureInfo.InvariantCulture)),
            new("size", pageSize.ToString(CultureInfo.InvariantCulture)),
        };
        if (!string.IsNullOrWhiteSpace(sort)) values.Add(new("sort", sort));
        if (!string.IsNullOrWhiteSpace(search)) values.Add(new("search", search));
        return path + QueryString.Create(values);
    }
}
