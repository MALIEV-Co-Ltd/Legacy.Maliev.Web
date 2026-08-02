namespace Legacy.Maliev.Web.Application;

public sealed record CustomerDownloadFile(
    string FileName,
    Uri Href,
    DateTime? CreatedDate);

public sealed record CustomerOrderSupplement(
    string? MaterialName,
    string? MaterialGroupName,
    string? SurfaceFinishName,
    string? ColorName,
    string? CurrencyName,
    IReadOnlyList<CustomerDownloadFile> Files,
    IReadOnlyList<string> Warnings);

public sealed record CustomerAddressSummary(
    string? Building,
    string? AddressLine1,
    string? AddressLine2,
    string? City,
    string? State,
    string? PostalCode,
    string? Country);

public sealed record CustomerContactSummary(
    string FullName,
    string Email,
    string? Telephone,
    string? Mobile,
    string? Fax,
    CustomerAddressSummary? BillingAddress,
    CustomerAddressSummary? ShippingAddress);

public sealed record CustomerInvoiceSummary(
    int Id,
    string Number,
    string? Currency,
    bool IsPaid,
    int? ReceiptId,
    DateTime? PaymentDate,
    decimal? Outstanding);

public sealed record CustomerBankAccountSummary(
    string Bank,
    string? Branch,
    string? Swift,
    string AccountNumber);

public sealed record CustomerQuotationSupplement(
    string? CurrencyName,
    CustomerContactSummary? Customer,
    CustomerInvoiceSummary? Invoice,
    Uri? QuotationDocument,
    Uri? InvoiceDocument,
    Uri? ReceiptDocument,
    IReadOnlyList<CustomerDownloadFile> QuotationFiles,
    IReadOnlyList<CustomerBankAccountSummary> BankAccounts,
    IReadOnlyList<string> Warnings);

public interface ICustomerMemberDetailClient
{
    Task<CustomerOrderSupplement> GetOrderSupplementAsync(
        CustomerOrderDetails details,
        CancellationToken cancellationToken);

    Task<CustomerQuotationSupplement> GetQuotationSupplementAsync(
        int customerId,
        CustomerQuotationDetails details,
        CancellationToken cancellationToken);
}
