using System.Globalization;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Infrastructure;
using Legacy.Maliev.Web.Pages.Shared;
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
        if (!PageSizeQuery.TryResolve(size, out var pageSize))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            context.Response.Headers.CacheControl = "no-store";
            return MemberOrderHistoryDisplayModel.Empty;
        }

        var (pageIndex, queryErrors) = ParsePageIndex(index);
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

        return CreateOrderDisplayModel(
            page,
            search,
            sort,
            pageSize,
            errors.Distinct(StringComparer.Ordinal).ToArray());
    }

    internal static MemberOrderHistoryDisplayModel CreateOrderDisplayModel(
        CustomerOrderPage page,
        string? search,
        string? sort,
        int pageSize,
        IReadOnlyList<string> errors)
    {
        const string path = "/member/orders/history";
        return new(
            search,
            sort,
            pageSize,
            errors,
            page.Items.Select(order => new MemberOrderHistoryItemDisplayModel(
                order.Id,
                order.Name,
                order.Description,
                order.Quantity,
                order.Subtotal?.ToString("N2") ?? "-",
                order.CreatedDate?.ToString("O") ?? "-")).ToArray(),
            page.PageIndex,
            page.TotalPages,
            page.TotalRecords,
            BuildPageLinks(path, page.PageIndex, page.TotalPages, pageSize, sort, search),
            page.PageIndex > 1 ? BuildPageHref(path, 1, pageSize, sort, search) : null,
            page.PageIndex > 1 ? BuildPageHref(path, page.PageIndex - 1, pageSize, sort, search) : null,
            page.PageIndex < page.TotalPages ? BuildPageHref(path, page.PageIndex + 1, pageSize, sort, search) : null,
            page.PageIndex < page.TotalPages ? BuildPageHref(path, page.TotalPages, pageSize, sort, search) : null);
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
        if (!PageSizeQuery.TryResolve(size, out var pageSize))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            context.Response.Headers.CacheControl = "no-store";
            return MemberQuotationsIndexDisplayModel.Empty;
        }

        var (pageIndex, queryErrors) = ParsePageIndex(index);
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

        return CreateQuotationDisplayModel(
            page,
            search,
            sort,
            pageSize,
            errors.Distinct(StringComparer.Ordinal).ToArray());
    }

    internal static MemberQuotationsIndexDisplayModel CreateQuotationDisplayModel(
        CustomerQuotationPage page,
        string? search,
        string? sort,
        int pageSize,
        IReadOnlyList<string> errors)
    {
        const string path = "/member/quotations";
        return new(
            search,
            sort,
            pageSize,
            errors,
            page.Items.Select(quotation => new MemberQuotationListItemDisplayModel(
                quotation.Id,
                quotation.Accepted,
                quotation.QuotedAmount?.ToString("N2") ?? "-",
                quotation.CurrencyId,
                quotation.ExpirationDate.ToString("O"),
                quotation.CreatedDate?.ToString("O") ?? "-")).ToArray(),
            page.PageIndex,
            page.TotalPages,
            page.TotalRecords,
            BuildPageLinks(path, page.PageIndex, page.TotalPages, pageSize, sort, search),
            page.HasPreviousPage ? BuildPageHref(path, 1, pageSize, sort, search) : null,
            page.HasPreviousPage ? BuildPageHref(path, page.PageIndex - 1, pageSize, sort, search) : null,
            page.HasNextPage ? BuildPageHref(path, page.PageIndex + 1, pageSize, sort, search) : null,
            page.HasNextPage ? BuildPageHref(path, page.TotalPages, pageSize, sort, search) : null);
    }

    private static (int PageIndex, IReadOnlyList<string> Errors) ParsePageIndex(string? index)
    {
        var validIndex = string.IsNullOrWhiteSpace(index)
            || int.TryParse(index, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);
        var pageIndex = int.TryParse(index, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedIndex)
            ? Math.Max(parsedIndex, 1)
            : 1;
        return (pageIndex, validIndex ? [] : [InvalidQueryValuesMessage]);
    }


    private static IReadOnlyList<MemberPageLinkDisplayModel> BuildPageLinks(
        string path,
        int pageIndex,
        int totalPages,
        int pageSize,
        string? sort,
        string? search)
    {
        if (totalPages <= 0) return [];

        var first = Math.Max(1, Math.Min(pageIndex - 2, totalPages - 4));
        var last = Math.Min(totalPages, first + 4);
        return Enumerable.Range(first, last - first + 1)
            .Select(number => new MemberPageLinkDisplayModel(
                number,
                BuildPageHref(path, number, pageSize, sort, search),
                number == pageIndex))
            .ToArray();
    }

    internal static string BuildPageHref(
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
