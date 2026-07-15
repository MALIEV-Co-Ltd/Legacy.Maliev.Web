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
}
