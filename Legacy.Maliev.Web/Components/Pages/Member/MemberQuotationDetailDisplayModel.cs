namespace Legacy.Maliev.Web.Components.Pages.Member;

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
    public static MemberQuotationDetailDisplayModel Empty { get; } = new(
        0, null, 0, MemberDateDisplayModel.Empty, MemberDateDisplayModel.Empty,
        "-", "-", "-", "-", "-", "-", "-", "-", "-", null, false, null,
        null, null, null, null, null, [], [], [], [], []);
}

public sealed record MemberQuotationLineDisplayModel(
    string Description,
    string Quantity,
    string UnitPrice,
    string Subtotal);

public sealed record MemberQuotationOrderDisplayModel(int Id, string Href);

public sealed record MemberCustomerDisplayModel(
    string FullName,
    string Email,
    string Telephone,
    string Mobile,
    string Fax,
    MemberPostalAddressDisplayModel? BillingAddress,
    MemberPostalAddressDisplayModel? ShippingAddress);

public sealed record MemberPostalAddressDisplayModel(IReadOnlyList<string> Lines);

public sealed record MemberInvoiceDisplayModel(
    string Number,
    bool IsPaid,
    MemberDateDisplayModel PaymentDate,
    string Outstanding);

public sealed record MemberBankAccountDisplayModel(
    string Bank,
    string Branch,
    string Swift,
    string AccountNumber);
