using System.Globalization;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Infrastructure;

namespace Legacy.Maliev.Web.Components.Pages.Member;

public static class MemberDetailLoaders
{
    private static readonly TimeZoneInfo BangkokTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Bangkok");

    public static async Task<MemberDetailLoadResult<MemberOrderDetailDisplayModel>> LoadOrderAsync(
        HttpContext context,
        IAccountSessionManager sessionManager,
        ICustomerOrderClient orderClient,
        ICustomerMemberDetailClient memberDetailClient,
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
        var supplement = result.Details is null
            ? EmptyOrderSupplement
            : await memberDetailClient.GetOrderSupplementAsync(result.Details, cancellationToken);
        var model = CreateOrderDisplayModel(
            result.Details,
            supplement,
            notification,
            errors.Concat(supplement.Warnings).Distinct(StringComparer.Ordinal).ToArray());
        return MemberDetailLoadResult<MemberOrderDetailDisplayModel>.Success(model);
    }

    public static async Task<MemberDetailLoadResult<MemberQuotationDetailDisplayModel>> LoadQuotationAsync(
        HttpContext context,
        IAccountSessionManager sessionManager,
        ICustomerQuotationClient quotationClient,
        ICustomerMemberDetailClient memberDetailClient,
        int quotationId,
        string? notification,
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
        var supplement = result.Details is null
            ? EmptyQuotationSupplement
            : await memberDetailClient.GetQuotationSupplementAsync(customerId.Value, result.Details, cancellationToken);
        return MemberDetailLoadResult<MemberQuotationDetailDisplayModel>.Success(
            CreateQuotationDisplayModel(
                result.Details,
                supplement,
                notification,
                errors.Concat(supplement.Warnings).Distinct(StringComparer.Ordinal).ToArray()));
    }

    public static MemberOrderDetailDisplayModel CreateOrderDisplayModel(
        CustomerOrderDetails? details,
        string? notification,
        IReadOnlyList<string> errors) =>
        CreateOrderDisplayModel(details, EmptyOrderSupplement, notification, errors);

    public static MemberOrderDetailDisplayModel CreateOrderDisplayModel(
        CustomerOrderDetails? details,
        CustomerOrderSupplement supplement,
        string? notification,
        IReadOnlyList<string> errors)
    {
        if (details is null)
        {
            return MemberOrderDetailDisplayModel.Empty with { Notification = notification, Errors = errors };
        }

        var order = details.Order;
        var currency = supplement.CurrencyName;
        return new MemberOrderDetailDisplayModel(
            order.Id,
            order.Name,
            order.Description,
            details.Process?.Name ?? "-",
            supplement.MaterialName ?? "-",
            supplement.MaterialGroupName ?? "-",
            supplement.SurfaceFinishName ?? "-",
            supplement.ColorName ?? "-",
            order.Quantity,
            order.Manufactured,
            order.Remaining?.ToString(CultureInfo.CurrentCulture) ?? "-",
            FormatMoney(order.UnitPrice, currency),
            FormatMoney(order.Subtotal, currency),
            order.LeadTime is null
                ? "-"
                : string.Create(CultureInfo.CurrentCulture, $"{order.LeadTime.Value} days"),
            FormatDate(order.PromisedDate),
            FormatDate(order.FinishedDate),
            FormatInstant(order.CreatedDate),
            FormatInstant(order.ModifiedDate),
            string.IsNullOrWhiteSpace(order.TrackingNumber) ? "-" : order.TrackingNumber,
            order.AllowCancellation,
            details.History.Any(status => string.Equals(status.Name, "Shipped", StringComparison.OrdinalIgnoreCase)),
            notification,
            errors,
            details.History.Select(status => new MemberOrderStatusDisplayModel(
                status.Name,
                status.Description,
                FormatInstant(status.CreatedDate))).ToArray(),
            supplement.Files.Select(ToFileDisplay).ToArray());
    }

    public static MemberQuotationDetailDisplayModel CreateQuotationDisplayModel(
        CustomerQuotationDetails? details,
        CustomerQuotationSupplement supplement,
        string? notification,
        IReadOnlyList<string> errors)
    {
        if (details is null)
        {
            return MemberQuotationDetailDisplayModel.Empty with { Notification = notification, Errors = errors };
        }

        var quotation = details.Quotation;
        var currency = supplement.CurrencyName;
        return new MemberQuotationDetailDisplayModel(
            quotation.Id,
            quotation.Accepted,
            quotation.Period,
            FormatInstant(quotation.CreatedDate),
            FormatDate(quotation.ExpirationDate),
            FormatMoney(quotation.Subtotal, currency),
            FormatMoney(quotation.Vat, currency),
            FormatMoney(quotation.Total, currency),
            FormatMoney(quotation.WithholdingTax, currency),
            FormatMoney(quotation.QuotedAmount, currency),
            currency ?? "-",
            string.IsNullOrWhiteSpace(quotation.ShippedVia) ? "-" : quotation.ShippedVia,
            string.IsNullOrWhiteSpace(quotation.Fob) ? "-" : quotation.Fob,
            string.IsNullOrWhiteSpace(quotation.Terms) ? "-" : quotation.Terms,
            quotation.Comment,
            quotation.Accepted is null && quotation.ExpirationDate.Date >= DateTime.UtcNow.Date,
            notification,
            ToCustomerDisplay(supplement.Customer),
            supplement.Invoice is null
                ? null
                : new MemberInvoiceDisplayModel(
                    supplement.Invoice.Number,
                    supplement.Invoice.IsPaid,
                    FormatInstant(supplement.Invoice.PaymentDate),
                    FormatMoney(supplement.Invoice.Outstanding, supplement.Invoice.Currency ?? currency)),
            supplement.QuotationDocument?.AbsoluteUri,
            supplement.InvoiceDocument?.AbsoluteUri,
            supplement.ReceiptDocument?.AbsoluteUri,
            supplement.BankAccounts.Select(account => new MemberBankAccountDisplayModel(
                account.Bank,
                account.Branch ?? "-",
                account.Swift ?? "-",
                account.AccountNumber)).ToArray(),
            errors,
            details.OrderItems.Select(item => new MemberQuotationLineDisplayModel(
                item.Description ?? "-",
                item.Quantity?.ToString(CultureInfo.CurrentCulture) ?? "-",
                FormatMoney(item.UnitPrice, currency),
                FormatMoney(item.Subtotal, currency))).ToArray(),
            details.Orders.Select(order => new MemberQuotationOrderDisplayModel(
                order.OrderId,
                $"/member/orders/view?itemID={order.OrderId}")).ToArray(),
            supplement.QuotationFiles.Select(ToFileDisplay).ToArray());
    }

    private static MemberCustomerDisplayModel? ToCustomerDisplay(CustomerContactSummary? customer) =>
        customer is null
            ? null
            : new MemberCustomerDisplayModel(
                customer.FullName,
                customer.Email,
                customer.Telephone ?? "-",
                customer.Mobile ?? "-",
                customer.Fax ?? "-",
                ToAddressDisplay(customer.BillingAddress),
                ToAddressDisplay(customer.ShippingAddress));

    private static MemberPostalAddressDisplayModel? ToAddressDisplay(CustomerAddressSummary? address)
    {
        if (address is null)
        {
            return null;
        }

        var lines = new[]
        {
            address.Building,
            address.AddressLine1,
            address.AddressLine2,
            address.City,
            string.Join(' ', new[] { address.PostalCode, address.State }.Where(value => !string.IsNullOrWhiteSpace(value))),
            address.Country,
        }.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!).ToArray();
        return new MemberPostalAddressDisplayModel(lines);
    }

    private static MemberFileDisplayModel ToFileDisplay(CustomerDownloadFile file) =>
        new(file.FileName, file.Href.AbsoluteUri, FormatInstant(file.CreatedDate));

    private static string FormatMoney(decimal? amount, string? currency) =>
        amount is null
            ? "-"
            : string.IsNullOrWhiteSpace(currency)
                ? amount.Value.ToString("N2", CultureInfo.CurrentCulture)
                : string.Create(CultureInfo.CurrentCulture, $"{amount.Value:N2} {currency}");

    private static MemberDateDisplayModel FormatDate(DateTime? value) =>
        value is null
            ? MemberDateDisplayModel.Empty
            : new(
                value.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                value.Value.ToString("d MMM yyyy", CultureInfo.CurrentCulture));

    private static MemberDateDisplayModel FormatInstant(DateTime? value)
    {
        if (value is null)
        {
            return MemberDateDisplayModel.Empty;
        }

        var utc = value.Value.Kind == DateTimeKind.Utc
            ? value.Value
            : DateTime.SpecifyKind(value.Value, DateTimeKind.Utc);
        var bangkok = TimeZoneInfo.ConvertTimeFromUtc(utc, BangkokTimeZone);
        return new(
            utc.ToString("O", CultureInfo.InvariantCulture),
            string.Create(CultureInfo.CurrentCulture, $"{bangkok:d MMM yyyy HH:mm} ICT"));
    }

    private static CustomerOrderSupplement EmptyOrderSupplement { get; } =
        new(null, null, null, null, null, [], []);

    private static CustomerQuotationSupplement EmptyQuotationSupplement { get; } =
        new(null, null, null, null, null, null, [], [], []);
}

public sealed record MemberDetailLoadResult<T>(T Model, bool IsNotFound, bool IsUnauthorized)
{
    public static MemberDetailLoadResult<T> Success(T model) => new(model, false, false);
    public static MemberDetailLoadResult<T> NotFound(T model) => new(model, true, false);
    public static MemberDetailLoadResult<T> Unauthorized(T model) => new(model, false, true);
}
