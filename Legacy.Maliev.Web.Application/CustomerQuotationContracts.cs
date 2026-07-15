namespace Legacy.Maliev.Web.Application;

public sealed record CustomerQuotation(
    int Id,
    int? CustomerId,
    int? InvoiceId,
    int Period,
    DateTime ExpirationDate,
    decimal Subtotal,
    decimal Vat,
    decimal Total,
    decimal? WithholdingTax,
    decimal? QuotedAmount,
    int CurrencyId,
    string? Comment,
    string? Fob,
    string? ShippedVia,
    string? Terms,
    bool? Accepted,
    DateTime? CreatedDate,
    DateTime? ModifiedDate);

public sealed record CustomerQuotationLine(
    int Id,
    int QuotationId,
    int? OrderId,
    string? Description,
    int? Quantity,
    decimal? UnitPrice,
    decimal? Subtotal,
    DateTime? CreatedDate,
    DateTime? ModifiedDate);

public sealed record CustomerQuotationOrder(
    int Id,
    int QuotationId,
    int OrderId,
    DateTime? CreatedDate,
    DateTime? ModifiedDate);

public sealed record CustomerQuotationFile(
    int Id,
    int QuotationId,
    string Bucket,
    string ObjectName,
    DateTime? CreatedDate,
    DateTime? ModifiedDate);

public sealed record CustomerQuotationDetails(
    CustomerQuotation Quotation,
    IReadOnlyList<CustomerQuotationLine> OrderItems,
    IReadOnlyList<CustomerQuotationOrder> Orders,
    IReadOnlyList<CustomerQuotationFile> Files);

public sealed record CustomerQuotationPage(
    IReadOnlyList<CustomerQuotation> Items,
    int PageIndex,
    int TotalPages,
    int TotalRecords)
{
    public bool HasNextPage => PageIndex < TotalPages;
    public bool HasPreviousPage => PageIndex > 1;
}

public sealed record CustomerQuotationListResult(
    CustomerQuotationPage? Page,
    bool ServiceAvailable,
    bool Authorized);

public sealed record CustomerQuotationDetailsResult(
    CustomerQuotationDetails? Details,
    bool ServiceAvailable,
    bool Authorized);

public interface ICustomerQuotationClient
{
    Task<CustomerQuotationListResult> ListAsync(
        int customerId,
        string? sort,
        string? search,
        int pageIndex,
        int pageSize,
        CancellationToken cancellationToken);

    Task<CustomerQuotationDetailsResult> GetAsync(
        int customerId,
        int quotationId,
        CancellationToken cancellationToken);
}
