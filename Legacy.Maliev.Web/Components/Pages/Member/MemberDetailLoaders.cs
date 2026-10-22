using System.Globalization;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Infrastructure;

namespace Legacy.Maliev.Web.Components.Pages.Member;

/// <summary>
/// Loads and formats member-owned order and quotation details with explicit missing-resource and session outcomes.
/// </summary>
public static class MemberDetailLoaders
{
    private static readonly TimeZoneInfo BangkokTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Bangkok");

    /// <summary>
    /// Loads a customer-scoped order and combines its detail data with catalog and download supplements.
    /// </summary>
    /// <param name="context">The current authenticated request.</param>
    /// <param name="sessionManager">The server-side session accessor used to resolve the customer identifier.</param>
    /// <param name="orderClient">The customer-scoped client used to retrieve the order.</param>
    /// <param name="memberDetailClient">The client used to retrieve display labels, currency, and downloadable files.</param>
    /// <param name="orderId">The requested customer order identifier.</param>
    /// <param name="notification">The optional order-operation outcome message retained for display.</param>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <returns>The formatted order view and its not-found or unauthorized outcome when applicable.</returns>
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

    /// <summary>
    /// Loads a customer-scoped quotation and its customer, document, invoice, and payment supplements.
    /// </summary>
    /// <param name="context">The current authenticated request.</param>
    /// <param name="sessionManager">The server-side session accessor used to resolve the customer identifier.</param>
    /// <param name="quotationClient">The customer-scoped client used to retrieve the quotation.</param>
    /// <param name="memberDetailClient">The client used to retrieve quotation documents and commercial display supplements.</param>
    /// <param name="quotationId">The requested customer quotation identifier.</param>
    /// <param name="notification">The optional quotation-operation outcome message retained for display.</param>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <returns>The formatted quotation view and its not-found or unauthorized outcome when applicable.</returns>
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

    /// <summary>
    /// Formats order details without optional catalog, currency, or file supplements.
    /// </summary>
    /// <param name="details">The order detail response, or null when unavailable.</param>
    /// <param name="notification">The optional order-operation outcome message retained for display.</param>
    /// <param name="errors">The messages displayed alongside the order details.</param>
    /// <returns>The formatted order view, using unavailable placeholders for missing supplemental data.</returns>
    public static MemberOrderDetailDisplayModel CreateOrderDisplayModel(
        CustomerOrderDetails? details,
        string? notification,
        IReadOnlyList<string> errors) =>
        CreateOrderDisplayModel(details, EmptyOrderSupplement, notification, errors);

    /// <summary>
    /// Formats order details using the supplied catalog labels, currency, and downloadable files.
    /// </summary>
    /// <param name="details">The order detail response, or null when unavailable.</param>
    /// <param name="supplement">The catalog, currency, and file information associated with the order.</param>
    /// <param name="notification">The optional order-operation outcome message retained for display.</param>
    /// <param name="errors">The messages displayed alongside the order details.</param>
    /// <returns>The formatted order view, or its empty representation when details are absent.</returns>
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

    /// <summary>
    /// Formats quotation values, customer contact details, linked documents, and related orders for member review.
    /// </summary>
    /// <param name="details">The quotation detail response, or null when unavailable.</param>
    /// <param name="supplement">The customer, invoice, payment, and file information associated with the quotation.</param>
    /// <param name="notification">The optional quotation-operation outcome message retained for display.</param>
    /// <param name="errors">The messages displayed alongside the quotation details.</param>
    /// <returns>The formatted quotation view, or its empty representation when details are absent.</returns>
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
                account.Branch,
                account.Swift,
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

/// <summary>
/// Carries a member detail view together with its missing-resource and authentication outcome.
/// </summary>
/// <typeparam name="T">The order or quotation display model carried by the result.</typeparam>
/// <param name="Model">The projected view, including an empty representation on unsuccessful lookup.</param>
/// <param name="IsNotFound">Whether the requested member resource was not found.</param>
/// <param name="IsUnauthorized">Whether no authorized member session was available.</param>
public sealed record MemberDetailLoadResult<T>(T Model, bool IsNotFound, bool IsUnauthorized)
{
    /// <summary>
    /// Creates a detail result that can render the supplied view without missing-resource or session flags.
    /// </summary>
    /// <param name="model">The detail view to render.</param>
    /// <returns>A successful detail result carrying the supplied model.</returns>
    public static MemberDetailLoadResult<T> Success(T model) => new(model, false, false);
    /// <summary>
    /// Creates a missing-resource result while retaining the supplied empty detail view.
    /// </summary>
    /// <param name="model">The empty detail view associated with the missing resource.</param>
    /// <returns>A detail result marked as not found.</returns>
    public static MemberDetailLoadResult<T> NotFound(T model) => new(model, true, false);
    /// <summary>
    /// Creates an unauthorized result while retaining the supplied empty detail view.
    /// </summary>
    /// <param name="model">The empty detail view associated with the unavailable member session.</param>
    /// <returns>A detail result marked as unauthorized.</returns>
    public static MemberDetailLoadResult<T> Unauthorized(T model) => new(model, false, true);
}
