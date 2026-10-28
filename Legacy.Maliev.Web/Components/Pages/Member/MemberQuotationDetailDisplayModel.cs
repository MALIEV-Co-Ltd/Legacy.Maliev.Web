namespace Legacy.Maliev.Web.Components.Pages.Member;

/// <summary>
/// Carries a member-owned quotation's formatted commercial terms, documents, and associated orders.
/// </summary>
/// <param name="Id">The quotation identifier.</param>
/// <param name="Accepted">The customer's acceptance decision, or null before a decision is recorded.</param>
/// <param name="Period">The quotation period supplied by the service.</param>
/// <param name="CreatedDate">The quotation creation instant and localized display text.</param>
/// <param name="ExpirationDate">The quotation expiration date and localized display text.</param>
/// <param name="Subtotal">The formatted subtotal with the available currency label.</param>
/// <param name="Vat">The formatted value-added tax with the available currency label.</param>
/// <param name="Total">The formatted quotation total with the available currency label.</param>
/// <param name="WithholdingTax">The formatted withholding-tax amount with the available currency label.</param>
/// <param name="QuotedAmount">The formatted quoted amount with the available currency label.</param>
/// <param name="Currency">The currency label or unavailable placeholder.</param>
/// <param name="ShippingMethod">The displayed shipping method or unavailable placeholder.</param>
/// <param name="FreeOnBoard">The displayed delivery term or unavailable placeholder.</param>
/// <param name="Terms">The displayed commercial terms or unavailable placeholder.</param>
/// <param name="Comment">The optional quotation comment.</param>
/// <param name="CanDecide">Whether the quotation is undecided and its expiration date has not elapsed.</param>
/// <param name="Notification">The quotation-operation outcome message, or null when absent.</param>
/// <param name="Customer">The customer contact and address summary, or null when unavailable.</param>
/// <param name="Invoice">The invoice summary, or null when no invoice is available.</param>
/// <param name="QuotationDocumentHref">The quotation document URL, or null when absent.</param>
/// <param name="InvoiceDocumentHref">The invoice document URL, or null when absent.</param>
/// <param name="ReceiptDocumentHref">The receipt document URL, or null when absent.</param>
/// <param name="BankAccounts">The payment account details supplied with the quotation.</param>
/// <param name="Errors">The quotation-loading or operation failures displayed to the member.</param>
/// <param name="Items">The formatted quotation line items.</param>
/// <param name="Orders">The linked manufacturing orders associated with the quotation.</param>
/// <param name="Files">The downloadable files associated with the quotation.</param>
public sealed record MemberQuotationDetailDisplayModel(
    int Id,
    bool? Accepted,
    int Period,
    MemberDateDisplayModel CreatedDate,
    MemberDateDisplayModel ExpirationDate,
    string Subtotal,
    string Vat,
    string Total,
    string WithholdingTax,
    string QuotedAmount,
    string Currency,
    string ShippingMethod,
    string FreeOnBoard,
    string Terms,
    string? Comment,
    bool CanDecide,
    string? Notification,
    MemberCustomerDisplayModel? Customer,
    MemberInvoiceDisplayModel? Invoice,
    string? QuotationDocumentHref,
    string? InvoiceDocumentHref,
    string? ReceiptDocumentHref,
    IReadOnlyList<MemberBankAccountDisplayModel> BankAccounts,
    IReadOnlyList<string> Errors,
    IReadOnlyList<MemberQuotationLineDisplayModel> Items,
    IReadOnlyList<MemberQuotationOrderDisplayModel> Orders,
    IReadOnlyList<MemberFileDisplayModel> Files)
{
    /// <summary>
    /// Provides an empty quotation view with unavailable placeholders and no decision action.
    /// </summary>
    public static MemberQuotationDetailDisplayModel Empty { get; } = new(
        0, null, 0, MemberDateDisplayModel.Empty, MemberDateDisplayModel.Empty,
        "-", "-", "-", "-", "-", "-", "-", "-", "-", null, false, null,
        null, null, null, null, null, [], [], [], [], []);
}

/// <summary>
/// Carries one formatted quotation line for member review.
/// </summary>
/// <param name="Description">The quoted item description or unavailable placeholder.</param>
/// <param name="Quantity">The formatted quantity or unavailable placeholder.</param>
/// <param name="UnitPrice">The formatted per-unit price with the available currency label.</param>
/// <param name="Subtotal">The formatted line subtotal with the available currency label.</param>
public sealed record MemberQuotationLineDisplayModel(
    string Description,
    string Quantity,
    string UnitPrice,
    string Subtotal);

/// <summary>
/// Carries a link from a member quotation to an associated manufacturing order.
/// </summary>
/// <param name="Id">The associated customer order identifier.</param>
/// <param name="Href">The member order-detail URL.</param>
public sealed record MemberQuotationOrderDisplayModel(int Id, string Href);

/// <summary>
/// Carries the customer contact and postal details shown with a member quotation.
/// </summary>
/// <param name="FullName">The customer name displayed with the quotation.</param>
/// <param name="Email">The customer's correspondence email.</param>
/// <param name="Telephone">The customer's telephone number or unavailable placeholder.</param>
/// <param name="Mobile">The customer's mobile number or unavailable placeholder.</param>
/// <param name="Fax">The customer's fax number or unavailable placeholder.</param>
/// <param name="BillingAddress">The formatted billing address, or null when absent.</param>
/// <param name="ShippingAddress">The formatted shipping address, or null when absent.</param>
public sealed record MemberCustomerDisplayModel(
    string FullName,
    string Email,
    string Telephone,
    string Mobile,
    string Fax,
    MemberPostalAddressDisplayModel? BillingAddress,
    MemberPostalAddressDisplayModel? ShippingAddress);

/// <summary>
/// Carries nonblank postal-address lines for quotation display.
/// </summary>
/// <param name="Lines">The address lines retained in their display order.</param>
public sealed record MemberPostalAddressDisplayModel(IReadOnlyList<string> Lines);

/// <summary>
/// Carries the invoice identity, payment state, and outstanding amount linked to a quotation.
/// </summary>
/// <param name="Number">The invoice number shown to the customer.</param>
/// <param name="IsPaid">Whether the invoice is marked as paid.</param>
/// <param name="PaymentDate">The recorded payment instant or unavailable date representation.</param>
/// <param name="Outstanding">The formatted unpaid balance with the available currency label.</param>
public sealed record MemberInvoiceDisplayModel(
    string Number,
    bool IsPaid,
    MemberDateDisplayModel PaymentDate,
    string Outstanding);

/// <summary>
/// Carries one payment account supplied with a member quotation.
/// </summary>
/// <param name="Bank">The bank name displayed for payment.</param>
/// <param name="Branch">The optional bank branch.</param>
/// <param name="Swift">The optional SWIFT routing identifier.</param>
/// <param name="AccountNumber">The destination account number supplied by the service.</param>
public sealed record MemberBankAccountDisplayModel(
    string Bank,
    string? Branch,
    string? Swift,
    string AccountNumber);
