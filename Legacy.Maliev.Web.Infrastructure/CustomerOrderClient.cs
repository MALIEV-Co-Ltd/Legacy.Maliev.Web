using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Legacy.Maliev.Web.Application;
using Microsoft.Extensions.Logging;

namespace Legacy.Maliev.Web.Infrastructure;

internal sealed class CustomerOrderClient(
    IHttpClientFactory clientFactory,
    IServiceAccessTokenProvider tokenProvider,
    ILogger<CustomerOrderClient> logger) : ICustomerOrderClient
{
    public async Task<CustomerOrderListResult> ListAsync(
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
            ["index"] = Math.Max(pageIndex, 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["size"] = Math.Clamp(pageSize, 1, 100).ToString(System.Globalization.CultureInfo.InvariantCulture),
        }.Select(pair => $"{pair.Key}={Uri.EscapeDataString(pair.Value)}"));

        try
        {
            using var request = Authorized(HttpMethod.Get, $"orders/customers/{customerId}?{query}", token);
            using var response = await Client().SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new(new CustomerOrderPage([], 1, 0, 0), true, true);
            }

            if (!response.IsSuccessStatusCode)
            {
                return FailureList(response.StatusCode, token);
            }

            var page = await response.Content.ReadFromJsonAsync<CustomerOrderPage>(cancellationToken);
            return new(page, true, true);
        }
        catch (Exception exception) when (IsTransient(exception, cancellationToken))
        {
            logger.LogWarning(exception, "Order service was unavailable while listing owned customer orders.");
            return new(null, false, true);
        }
    }

    public async Task<CustomerOrderDetailsResult> GetAsync(
        int customerId,
        int orderId,
        CancellationToken cancellationToken)
    {
        var token = await tokenProvider.GetAccessTokenAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(token)) return new(null, false, false);
        try
        {
            using var request = Authorized(HttpMethod.Get, $"orders/customers/{customerId}/{orderId}", token);
            using var response = await Client().SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return FailureDetails(response.StatusCode, token);
            }

            var details = await response.Content.ReadFromJsonAsync<CustomerOrderDetails>(cancellationToken);
            return new(details, true, true);
        }
        catch (Exception exception) when (IsTransient(exception, cancellationToken))
        {
            logger.LogWarning(exception, "Order service was unavailable while reading an owned customer order.");
            return new(null, false, true);
        }
    }

    public async Task<CustomerOrderOperationResult> CancelAsync(
        int customerId,
        int orderId,
        CancellationToken cancellationToken)
    {
        var token = await tokenProvider.GetAccessTokenAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(token)) return new(false, false, false, false);
        try
        {
            using var request = Authorized(HttpMethod.Post, $"orders/customers/{customerId}/{orderId}/cancel", token);
            using var response = await Client().SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode) return new(true, true, true, false);
            var authorized = response.StatusCode is not (HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden);
            if (!authorized) tokenProvider.Invalidate(token);
            return new(false, true, authorized, response.StatusCode == HttpStatusCode.Conflict);
        }
        catch (Exception exception) when (IsTransient(exception, cancellationToken))
        {
            logger.LogWarning(exception, "Order service was unavailable while cancelling an owned customer order.");
            return new(false, false, true, false);
        }
    }

    private CustomerOrderListResult FailureList(HttpStatusCode statusCode, string token)
    {
        var authorized = Authorized(statusCode, token);
        return new(null, true, authorized);
    }

    private CustomerOrderDetailsResult FailureDetails(HttpStatusCode statusCode, string token)
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

    private HttpClient Client() => clientFactory.CreateClient("orders");

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
