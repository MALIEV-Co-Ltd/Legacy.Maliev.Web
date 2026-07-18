namespace Legacy.Maliev.Web.Components.Pages.Member;

public sealed record MemberQuotationDetailDisplayModel(
    int Id,
    bool? Accepted,
    int Period,
    string ExpirationDate,
    string Subtotal,
    string Vat,
    string Total,
    string WithholdingTax,
    string QuotedAmount,
    int CurrencyId,
    string ShippingMethod,
    string FreeOnBoard,
    string Terms,
    string? Comment,
    IReadOnlyList<string> Errors,
    IReadOnlyList<MemberQuotationLineDisplayModel> Items,
    IReadOnlyList<MemberQuotationOrderDisplayModel> Orders,
    IReadOnlyList<string> FileNames)
{
    public static MemberQuotationDetailDisplayModel Empty { get; } = new(
        0, null, 0, "-", "-", "-", "-", "-", "-", 0, "-", "-", "-", null, [], [], [], []);
}

public sealed record MemberQuotationLineDisplayModel(
    string Description,
    string Quantity,
    string UnitPrice,
    string Subtotal);

public sealed record MemberQuotationOrderDisplayModel(int Id, string Href);
