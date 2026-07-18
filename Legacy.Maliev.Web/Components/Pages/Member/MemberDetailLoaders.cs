using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Infrastructure;

namespace Legacy.Maliev.Web.Components.Pages.Member;

public static class MemberDetailLoaders
{
    public static async Task<MemberDetailLoadResult<MemberOrderDetailDisplayModel>> LoadOrderAsync(
        HttpContext context,
        IAccountSessionManager sessionManager,
        ICustomerOrderClient orderClient,
        int orderId,
        string? notification,
        CancellationToken cancellationToken)
    {
        if (orderId <= 0)
        {
            return MemberDetailLoadResult<MemberOrderDetailDisplayModel>.NotFound(MemberOrderDetailDisplayModel.Empty);
        }

        var customerId = await sessionManager.GetCustomerDatabaseIdAsync(context, cancellationToken);
        if (customerId is null)
        {
            return MemberDetailLoadResult<MemberOrderDetailDisplayModel>.Unauthorized(MemberOrderDetailDisplayModel.Empty);
        }

        var result = await orderClient.GetAsync(customerId.Value, orderId, cancellationToken);
        if (result.Details is null && result.ServiceAvailable && result.Authorized)
        {
            return MemberDetailLoadResult<MemberOrderDetailDisplayModel>.NotFound(MemberOrderDetailDisplayModel.Empty);
        }

        var errors = result.Details is null
            ? new[] { result.ServiceAvailable ? "Your order could not be loaded." : "Order service is temporarily unavailable." }
            : [];
        var model = CreateOrderDisplayModel(result.Details, notification, errors);
        return MemberDetailLoadResult<MemberOrderDetailDisplayModel>.Success(model);
    }

    public static async Task<MemberDetailLoadResult<MemberQuotationDetailDisplayModel>> LoadQuotationAsync(
        HttpContext context,
        IAccountSessionManager sessionManager,
        ICustomerQuotationClient quotationClient,
        int quotationId,
        CancellationToken cancellationToken)
    {
        if (quotationId <= 0)
        {
            return MemberDetailLoadResult<MemberQuotationDetailDisplayModel>.NotFound(MemberQuotationDetailDisplayModel.Empty);
        }

        var customerId = await sessionManager.GetCustomerDatabaseIdAsync(context, cancellationToken);
        if (customerId is null)
        {
            return MemberDetailLoadResult<MemberQuotationDetailDisplayModel>.Unauthorized(MemberQuotationDetailDisplayModel.Empty);
        }

        var result = await quotationClient.GetAsync(customerId.Value, quotationId, cancellationToken);
        if (result.Details is null && result.ServiceAvailable && result.Authorized)
        {
            return MemberDetailLoadResult<MemberQuotationDetailDisplayModel>.NotFound(MemberQuotationDetailDisplayModel.Empty);
        }

        var errors = result.Details is null
            ? new[] { result.ServiceAvailable ? "Your quotation could not be loaded." : "Quotation service is temporarily unavailable." }
            : [];
        return MemberDetailLoadResult<MemberQuotationDetailDisplayModel>.Success(
            CreateQuotationDisplayModel(result.Details, errors));
    }

    private static MemberOrderDetailDisplayModel CreateOrderDisplayModel(
        CustomerOrderDetails? details,
        string? notification,
        IReadOnlyList<string> errors)
    {
        if (details is null)
        {
            return MemberOrderDetailDisplayModel.Empty with { Notification = notification, Errors = errors };
        }

        var order = details.Order;
        return new MemberOrderDetailDisplayModel(
            order.Id,
            order.Name,
            order.Description,
            details.Process?.Name ?? "-",
            order.Quantity,
            order.Manufactured,
            order.Remaining?.ToString() ?? "-",
            order.Subtotal?.ToString("N2") ?? "-",
            string.IsNullOrWhiteSpace(order.TrackingNumber) ? "-" : order.TrackingNumber,
            order.AllowCancellation,
            notification,
            errors,
            details.History.Select(status => new MemberOrderStatusDisplayModel(
                status.Name,
                status.CreatedDate?.ToString("yyyy-MM-dd HH:mm") ?? "-")).ToArray(),
            details.Files.Select(file => GetDisplayFileName(file.ObjectName)).ToArray());
    }

    private static MemberQuotationDetailDisplayModel CreateQuotationDisplayModel(
        CustomerQuotationDetails? details,
        IReadOnlyList<string> errors)
    {
        if (details is null)
        {
            return MemberQuotationDetailDisplayModel.Empty with { Errors = errors };
        }

        var quotation = details.Quotation;
        return new MemberQuotationDetailDisplayModel(
            quotation.Id,
            quotation.Accepted,
            quotation.Period,
            quotation.ExpirationDate.ToString("yyyy-MM-dd"),
            quotation.Subtotal.ToString("N2"),
            quotation.Vat.ToString("N2"),
            quotation.Total.ToString("N2"),
            quotation.WithholdingTax?.ToString("N2") ?? "-",
            quotation.QuotedAmount?.ToString("N2") ?? "-",
            quotation.CurrencyId,
            string.IsNullOrWhiteSpace(quotation.ShippedVia) ? "-" : quotation.ShippedVia,
            string.IsNullOrWhiteSpace(quotation.Fob) ? "-" : quotation.Fob,
            string.IsNullOrWhiteSpace(quotation.Terms) ? "-" : quotation.Terms,
            quotation.Comment,
            errors,
            details.OrderItems.Select(item => new MemberQuotationLineDisplayModel(
                item.Description ?? "-",
                item.Quantity?.ToString() ?? "-",
                item.UnitPrice?.ToString("N2") ?? "-",
                item.Subtotal?.ToString("N2") ?? "-")).ToArray(),
            details.Orders.Select(order => new MemberQuotationOrderDisplayModel(
                order.OrderId,
                $"/member/orders/view?itemID={order.OrderId}")).ToArray(),
            details.Files.Select(file => GetDisplayFileName(file.ObjectName)).ToArray());
    }

    private static string GetDisplayFileName(string objectName) =>
        objectName.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "-";
}

public sealed record MemberDetailLoadResult<T>(T Model, bool IsNotFound, bool IsUnauthorized)
{
    public static MemberDetailLoadResult<T> Success(T model) => new(model, false, false);
    public static MemberDetailLoadResult<T> NotFound(T model) => new(model, true, false);
    public static MemberDetailLoadResult<T> Unauthorized(T model) => new(model, false, true);
}
