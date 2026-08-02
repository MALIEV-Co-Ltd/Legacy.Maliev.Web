using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Legacy.Maliev.Web.Application;
using Microsoft.Extensions.Logging;

namespace Legacy.Maliev.Web.Infrastructure;

internal sealed class CustomerMemberDetailClient(
    IHttpClientFactory clientFactory,
    IServiceAccessTokenProvider tokenProvider,
    ILogger<CustomerMemberDetailClient> logger) : ICustomerMemberDetailClient
{
    public async Task<CustomerOrderSupplement> GetOrderSupplementAsync(
        CustomerOrderDetails details,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(details);
        var warnings = new List<string>();
        var token = await tokenProvider.GetAccessTokenAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(token))
        {
            return new(null, null, null, null, null, [], ["Some order details are temporarily unavailable."]);
        }

        var order = details.Order;
        var material = await ReadOptionalAsync<MaterialResponse>(
            "catalog", order.MaterialId is > 0 ? $"materials/{order.MaterialId.Value}" : null,
            token, warnings, "Material details are temporarily unavailable.", cancellationToken);
        var finish = await ReadOptionalAsync<NamedResponse>(
            "catalog", order.SurfaceFinishId is > 0 ? $"materials/surfacefinishes/{order.SurfaceFinishId.Value}" : null,
            token, warnings, "Surface-finish details are temporarily unavailable.", cancellationToken);
        var color = await ReadOptionalAsync<NamedResponse>(
            "catalog", order.ColorId is > 0 ? $"materials/colors/{order.ColorId.Value}" : null,
            token, warnings, "Color details are temporarily unavailable.", cancellationToken);
        var currency = await ReadOptionalAsync<CurrencyResponse>(
            "catalog", order.CurrencyId is > 0 ? $"currencies/{order.CurrencyId.Value}" : null,
            token, warnings, "Currency details are temporarily unavailable.", cancellationToken);
        var files = await ResolveFilesAsync(details.Files.Select(file => new StoredFile(
            file.Bucket, file.ObjectName, file.CreatedDate)), token, warnings, cancellationToken);

        return new(
            material?.Name,
            material?.MaterialGroup?.Name,
            finish?.Name,
            color?.Name,
            currency?.ShortName,
            files,
            warnings.Distinct(StringComparer.Ordinal).ToArray());
    }

    public async Task<CustomerQuotationSupplement> GetQuotationSupplementAsync(
        int customerId,
        CustomerQuotationDetails details,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(details);
        var warnings = new List<string>();
        var token = await tokenProvider.GetAccessTokenAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(token))
        {
            return EmptyQuotation(["Some quotation details are temporarily unavailable."]);
        }

        var customer = await ReadOptionalAsync<CustomerResponse>(
            "customers", $"customers/{customerId}", token, warnings,
            "Customer and address details are temporarily unavailable.", cancellationToken);
        if (customer is not null && customer.Id != customerId)
        {
            customer = null;
            AddWarning(warnings, "Customer and address details are temporarily unavailable.");
        }

        var countries = customer is null
            ? []
            : await ReadListAsync<CountryResponse>(
                "countries", "countries", token, warnings,
                "Country names are temporarily unavailable.", cancellationToken);
        var countryNames = countries.ToDictionary(country => country.Id, country => country.Name);

        var currency = await ReadOptionalAsync<CurrencyResponse>(
            "catalog", details.Quotation.CurrencyId > 0 ? $"currencies/{details.Quotation.CurrencyId}" : null,
            token, warnings, "Currency details are temporarily unavailable.", cancellationToken);
        var quotationFiles = await ResolveFilesAsync(details.Files.Select(file => new StoredFile(
            file.Bucket, file.ObjectName, file.CreatedDate)), token, warnings, cancellationToken);

        InvoiceResponse? invoice = null;
        IReadOnlyList<CustomerDownloadFile> invoiceFiles = [];
        IReadOnlyList<CustomerDownloadFile> receiptFiles = [];
        IReadOnlyList<CustomerBankAccountSummary> bankAccounts = [];
        if (details.Quotation.InvoiceId is > 0)
        {
            invoice = await ReadOptionalAsync<InvoiceResponse>(
                "accounting", $"invoices/{details.Quotation.InvoiceId.Value}", token, warnings,
                "Invoice details are temporarily unavailable.", cancellationToken);
            if (invoice is not null && invoice.CustomerId != customerId)
            {
                logger.LogWarning(
                    "Accounting returned invoice {InvoiceId} for a different customer while composing an owned quotation.",
                    invoice.Id);
                invoice = null;
                AddWarning(warnings, "Invoice details are temporarily unavailable.");
            }

            if (invoice is not null)
            {
                var invoiceRecords = await ReadListAsync<StoredFileResponse>(
                    "accounting", $"invoices/{invoice.Id}/files", token, warnings,
                    "Invoice document is temporarily unavailable.", cancellationToken);
                invoiceFiles = await ResolveFilesAsync(invoiceRecords.Select(ToStoredFile), token, warnings, cancellationToken);

                if (invoice.ReceiptId is > 0)
                {
                    var receiptRecords = await ReadListAsync<StoredFileResponse>(
                        "accounting", $"receipts/{invoice.ReceiptId.Value}/files", token, warnings,
                        "Receipt document is temporarily unavailable.", cancellationToken);
                    receiptFiles = await ResolveFilesAsync(receiptRecords.Select(ToStoredFile), token, warnings, cancellationToken);
                }

                if (!invoice.IsPaid)
                {
                    bankAccounts = (await ReadListAsync<BankAccountResponse>(
                        "accounting", "payments/accounts", token, warnings,
                        "Bank-transfer details are temporarily unavailable.", cancellationToken))
                        .Select(account => new CustomerBankAccountSummary(
                            account.Bank, account.Branch, account.Swift, account.AccountNumber))
                        .ToArray();
                }
            }
        }

        return new(
            currency?.ShortName ?? invoice?.Currency,
            customer is null ? null : new CustomerContactSummary(
                customer.FullName,
                customer.Email,
                customer.Telephone,
                customer.Mobile,
                customer.Fax,
                ToAddress(customer.BillingAddress, countryNames),
                ToAddress(customer.ShippingAddress, countryNames)),
            invoice is null ? null : new CustomerInvoiceSummary(
                invoice.Id,
                invoice.Number,
                invoice.Currency,
                invoice.IsPaid,
                invoice.ReceiptId,
                invoice.PaymentDate,
                invoice.Outstanding),
            quotationFiles.LastOrDefault()?.Href,
            invoiceFiles.LastOrDefault()?.Href,
            receiptFiles.LastOrDefault()?.Href,
            quotationFiles,
            bankAccounts,
            warnings.Distinct(StringComparer.Ordinal).ToArray());
    }

    private static CustomerQuotationSupplement EmptyQuotation(IReadOnlyList<string> warnings) =>
        new(null, null, null, null, null, null, [], [], warnings);

    private async Task<IReadOnlyList<CustomerDownloadFile>> ResolveFilesAsync(
        IEnumerable<StoredFile> records,
        string token,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var resolved = new List<CustomerDownloadFile>();
        foreach (var file in records)
        {
            if (string.IsNullOrWhiteSpace(file.Bucket) || string.IsNullOrWhiteSpace(file.ObjectName))
            {
                continue;
            }

            var query = $"uploads/signedurl?bucket={Uri.EscapeDataString(file.Bucket)}&objectName={Uri.EscapeDataString(file.ObjectName)}";
            var uri = await ReadOptionalAsync<Uri>(
                "files", query, token, warnings, "One or more files are temporarily unavailable.", cancellationToken);
            if (uri is not null && uri.IsAbsoluteUri && uri.Scheme is "https" or "http")
            {
                resolved.Add(new CustomerDownloadFile(DisplayFileName(file.ObjectName), uri, file.CreatedDate));
            }
        }

        return resolved;
    }

    private async Task<T?> ReadOptionalAsync<T>(
        string clientName,
        string? path,
        string token,
        List<string> warnings,
        string warning,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return default;
        }

        try
        {
            using var request = Authorized(path, token);
            using var response = await clientFactory.CreateClient(clientName).SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return default;
            }

            if (!response.IsSuccessStatusCode)
            {
                HandleFailure(response.StatusCode, token, warnings, warning);
                return default;
            }

            return await response.Content.ReadFromJsonAsync<T>(cancellationToken);
        }
        catch (Exception exception) when (IsTransient(exception, cancellationToken))
        {
            logger.LogWarning(exception, "A member detail dependency was unavailable while reading {ClientName}.", clientName);
            AddWarning(warnings, warning);
            return default;
        }
    }

    private async Task<IReadOnlyList<T>> ReadListAsync<T>(
        string clientName,
        string path,
        string token,
        List<string> warnings,
        string warning,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = Authorized(path, token);
            using var response = await clientFactory.CreateClient(clientName).SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return [];
            }

            if (!response.IsSuccessStatusCode)
            {
                HandleFailure(response.StatusCode, token, warnings, warning);
                return [];
            }

            return await response.Content.ReadFromJsonAsync<IReadOnlyList<T>>(cancellationToken) ?? [];
        }
        catch (Exception exception) when (IsTransient(exception, cancellationToken))
        {
            logger.LogWarning(exception, "A member detail dependency was unavailable while listing {ClientName}.", clientName);
            AddWarning(warnings, warning);
            return [];
        }
    }

    private void HandleFailure(HttpStatusCode statusCode, string token, List<string> warnings, string warning)
    {
        if (statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            tokenProvider.Invalidate(token);
        }

        AddWarning(warnings, warning);
    }

    private static HttpRequestMessage Authorized(string path, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static CustomerAddressSummary? ToAddress(
        AddressResponse? address,
        IReadOnlyDictionary<int, string> countryNames) =>
        address is null
            ? null
            : new CustomerAddressSummary(
                address.Building,
                address.AddressLine1,
                address.AddressLine2,
                address.City,
                address.State,
                address.PostalCode,
                countryNames.GetValueOrDefault(address.CountryId));

    private static StoredFile ToStoredFile(StoredFileResponse file) =>
        new(file.Bucket, file.ObjectName, file.CreatedDate);

    private static string DisplayFileName(string objectName) =>
        objectName.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "file";

    private static void AddWarning(List<string> warnings, string warning)
    {
        if (!warnings.Contains(warning, StringComparer.Ordinal))
        {
            warnings.Add(warning);
        }
    }

    private static bool IsTransient(Exception exception, CancellationToken cancellationToken) =>
        exception is HttpRequestException or JsonException
        || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested);

    private sealed record NamedResponse(int Id, string Name);

    private sealed record CurrencyResponse(int Id, string ShortName, string LongName);

    private sealed record MaterialGroupResponse(int Id, string Name);

    private sealed record MaterialResponse(int Id, int MaterialGroupId, string Name)
    {
        public MaterialGroupResponse? MaterialGroup { get; init; }
    }

    private sealed record CountryResponse(int Id, string Name);

    private sealed record AddressResponse(
        int Id,
        string? Building,
        string AddressLine1,
        string? AddressLine2,
        string? City,
        string? State,
        string? PostalCode,
        int CountryId);

    private sealed record CustomerResponse(
        int Id,
        string FirstName,
        string LastName,
        string FullName,
        string? Telephone,
        string? Mobile,
        string? Fax,
        string Email,
        int? BillingAddressId,
        int? ShippingAddressId)
    {
        public AddressResponse? BillingAddress { get; init; }

        public AddressResponse? ShippingAddress { get; init; }
    }

    private sealed record InvoiceResponse(
        int Id,
        string Number,
        int CustomerId,
        string? Currency,
        bool IsPaid,
        int? ReceiptId,
        DateTime? PaymentDate,
        decimal? Outstanding);

    private sealed record StoredFileResponse(
        int Id,
        string Bucket,
        string ObjectName,
        DateTime? CreatedDate);

    private sealed record BankAccountResponse(
        int Id,
        string Bank,
        string AccountNumber,
        string? Swift,
        string? Branch);

    private sealed record StoredFile(string Bucket, string ObjectName, DateTime? CreatedDate);
}
