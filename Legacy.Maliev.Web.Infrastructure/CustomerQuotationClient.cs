using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Legacy.Maliev.Web.Application;
using Microsoft.Extensions.Logging;

namespace Legacy.Maliev.Web.Infrastructure;

internal sealed class CustomerQuotationClient(
    IHttpClientFactory clientFactory,
    IServiceAccessTokenProvider tokenProvider,
    ILogger<CustomerQuotationClient> logger) : ICustomerQuotationClient
{
    public async Task<CustomerQuotationListResult> ListAsync(
        int customerId,
        string? sort,
        string? search,
        int pageIndex,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var token = await tokenProvider.GetAccessTokenAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(token)) return new(null, false, false);
        var query = string.Join('&', new Dictionary<string, string>
        {
            ["sort"] = sort ?? string.Empty,
            ["search"] = search ?? string.Empty,
            ["index"] = Math.Max(pageIndex, 1).ToString(CultureInfo.InvariantCulture),
            ["size"] = Math.Clamp(pageSize, 1, 100).ToString(CultureInfo.InvariantCulture),
        }.Select(pair => $"{pair.Key}={Uri.EscapeDataString(pair.Value)}"));

        try
        {
            using var request = Authorized(
                HttpMethod.Get,
                $"quotations/customers/{customerId}?{query}",
                token);
            using var response = await Client().SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new(new CustomerQuotationPage([], 1, 0, 0), true, true);
            }

            if (!response.IsSuccessStatusCode)
            {
                return FailureList(response.StatusCode, token);
            }

            var page = await response.Content.ReadFromJsonAsync<CustomerQuotationPage>(cancellationToken);
            return new(page, true, true);
        }
        catch (Exception exception) when (IsTransient(exception, cancellationToken))
        {
            logger.LogWarning(exception, "Quotation service was unavailable while listing owned customer quotations.");
            return new(null, false, true);
        }
    }

    public async Task<CustomerQuotationDetailsResult> GetAsync(
        int customerId,
        int quotationId,
        CancellationToken cancellationToken)
    {
        var token = await tokenProvider.GetAccessTokenAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(token)) return new(null, false, false);
        try
        {
            using var request = Authorized(
                HttpMethod.Get,
                $"quotations/{quotationId}?customerId={customerId}",
                token);
            using var response = await Client().SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return FailureDetails(response.StatusCode, token);
            }

            var details = await response.Content.ReadFromJsonAsync<CustomerQuotationDetails>(cancellationToken);
            return new(details, true, true);
        }
        catch (Exception exception) when (IsTransient(exception, cancellationToken))
        {
            logger.LogWarning(exception, "Quotation service was unavailable while reading an owned customer quotation.");
            return new(null, false, true);
        }
    }

    public async Task<CustomerQuotationDecisionResult> DecideAsync(
        int customerId,
        int quotationId,
        bool accepted,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        if (customerId <= 0 || quotationId <= 0 || operationId == Guid.Empty)
        {
            return new(false, true, true, false, null);
        }

        var token = await tokenProvider.GetAccessTokenAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(token)) return new(false, false, false, false, null);

        try
        {
            var owned = await GetOwnedQuotationAsync(customerId, quotationId, token, cancellationToken);
            if (owned.Details is null)
            {
                return new(false, owned.ServiceAvailable, owned.Authorized, false, null);
            }

            int? invoiceId = null;
            if (accepted)
            {
                var invoice = await CreateInvoiceAsync(customerId, quotationId, operationId, token, cancellationToken);
                if (!invoice.Succeeded)
                {
                    return invoice;
                }

                invoiceId = invoice.InvoiceId;
            }

            using var request = Authorized(HttpMethod.Put, $"quotations/{quotationId}/decision", token);
            request.Content = JsonContent.Create(new QuotationDecisionRequest(accepted));
            using var response = await Client().SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return new(true, true, true, false, invoiceId);
            }

            var authorized = response.StatusCode is not (HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden);
            if (!authorized) tokenProvider.Invalidate(token);
            return new(false, true, authorized, response.StatusCode == HttpStatusCode.Conflict, invoiceId);
        }
        catch (Exception exception) when (IsTransient(exception, cancellationToken))
        {
            logger.LogWarning(exception, "A quotation decision dependency was unavailable.");
            return new(false, false, true, false, null);
        }
    }

    private async Task<CustomerQuotationDetailsResult> GetOwnedQuotationAsync(
        int customerId,
        int quotationId,
        string token,
        CancellationToken cancellationToken)
    {
        using var request = Authorized(HttpMethod.Get, $"quotations/{quotationId}?customerId={customerId}", token);
        using var response = await Client().SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return FailureDetails(response.StatusCode, token);
        }

        var details = await response.Content.ReadFromJsonAsync<CustomerQuotationDetails>(cancellationToken);
        return details?.Quotation.CustomerId == customerId
            ? new(details, true, true)
            : new(null, true, true);
    }

    private async Task<CustomerQuotationDecisionResult> CreateInvoiceAsync(
        int customerId,
        int quotationId,
        Guid operationId,
        string token,
        CancellationToken cancellationToken)
    {
        var accounting = clientFactory.CreateClient("accounting");
        using var previewRequest = Authorized(HttpMethod.Get, $"invoices/from-quotation/{quotationId}/preview", token);
        using var previewResponse = await accounting.SendAsync(previewRequest, cancellationToken);
        if (!previewResponse.IsSuccessStatusCode)
        {
            return DecisionFailure(previewResponse.StatusCode, token);
        }

        var preview = await previewResponse.Content.ReadFromJsonAsync<InvoiceCreationPreview>(cancellationToken);
        if (preview is null
            || preview.QuotationId != quotationId
            || preview.CustomerId != customerId)
        {
            logger.LogWarning(
                "Accounting invoice preview did not match the authenticated quotation ownership boundary.");
            return new(false, true, true, false, null);
        }

        var input = new CreateInvoiceFromQuotationRequest(
            preview.InvoiceNumber,
            preview.Comment,
            null,
            null,
            preview.ShippedVia,
            preview.Fob,
            preview.Terms,
            preview.BillingAddress,
            preview.ShippingAddress,
            preview.TaxIdentification,
            preview.CommercialRegistration,
            preview.AvailableWithholdingTax > 0m,
            true);
        using var createRequest = Authorized(HttpMethod.Post, $"invoices/from-quotation/{quotationId}", token);
        createRequest.Headers.TryAddWithoutValidation("Idempotency-Key", operationId.ToString("D", CultureInfo.InvariantCulture));
        createRequest.Content = JsonContent.Create(input);
        using var createResponse = await accounting.SendAsync(createRequest, cancellationToken);
        if (!createResponse.IsSuccessStatusCode)
        {
            return DecisionFailure(createResponse.StatusCode, token);
        }

        var result = await createResponse.Content.ReadFromJsonAsync<InvoiceCreationResult>(cancellationToken);
        return result is { InvoiceId: > 0 }
            ? new(true, true, true, false, result.InvoiceId)
            : new(false, false, true, false, null);
    }

    private CustomerQuotationDecisionResult DecisionFailure(HttpStatusCode statusCode, string token)
    {
        var authorized = Authorized(statusCode, token);
        var available = statusCode is not (HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout);
        return new(false, available, authorized, statusCode == HttpStatusCode.Conflict, null);
    }

    private CustomerQuotationListResult FailureList(HttpStatusCode statusCode, string token)
    {
        var authorized = Authorized(statusCode, token);
        return new(null, true, authorized);
    }

    private CustomerQuotationDetailsResult FailureDetails(HttpStatusCode statusCode, string token)
    {
        var authorized = Authorized(statusCode, token);
        return new(null, true, authorized);
    }

    private bool Authorized(HttpStatusCode statusCode, string token)
    {
        var authorized = statusCode is not (HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden);
        if (!authorized) tokenProvider.Invalidate(token);
        return authorized;
    }

    private HttpClient Client() => clientFactory.CreateClient("quotations");

    private static HttpRequestMessage Authorized(HttpMethod method, string path, string token)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static bool IsTransient(Exception exception, CancellationToken cancellationToken) =>
        exception is HttpRequestException or JsonException
        || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested);

    private sealed record QuotationDecisionRequest(bool Accepted);

    private sealed record InvoiceAddressInput(
        string? Recipient,
        string? Company,
        string? Building,
        string? Line1,
        string? Line2,
        string? City,
        string? State,
        string? PostalCode,
        string? Country,
        string? Telephone = null);

    private sealed record InvoiceCreationPreview(
        int QuotationId,
        int CustomerId,
        string InvoiceNumber,
        string SalesPerson,
        string Currency,
        string? Comment,
        string? ShippedVia,
        string? Fob,
        string? Terms,
        InvoiceAddressInput BillingAddress,
        InvoiceAddressInput ShippingAddress,
        string? TaxIdentification,
        string? CommercialRegistration,
        decimal Subtotal,
        decimal Vat,
        decimal Total,
        decimal AvailableWithholdingTax,
        decimal Outstanding,
        IReadOnlyList<InvoiceCreationOrderItem> OrderItems);

    private sealed record InvoiceCreationOrderItem(
        int Id,
        int QuotationId,
        int? OrderId,
        string? Description,
        int? Quantity,
        decimal? UnitPrice,
        decimal? Subtotal);

    private sealed record CreateInvoiceFromQuotationRequest(
        string InvoiceNumber,
        string? Comment,
        string? PurchaseOrderNumber,
        string? Requisitioner,
        string? ShippedVia,
        string? Fob,
        string? Terms,
        InvoiceAddressInput BillingAddress,
        InvoiceAddressInput ShippingAddress,
        string? TaxIdentification,
        string? CommercialRegistration,
        bool DeductWithholdingTax,
        bool SendEmail);

    private sealed record InvoiceCreationResult(int InvoiceId);
}
