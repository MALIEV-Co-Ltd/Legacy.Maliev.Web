using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Legacy.Maliev.Web.Application;
using Microsoft.Extensions.Logging;

namespace Legacy.Maliev.Web.Infrastructure;

internal sealed class CustomerProfileClient(
    IHttpClientFactory clientFactory,
    IServiceAccessTokenProvider tokenProvider,
    ILogger<CustomerProfileClient> logger) : ICustomerProfileClient
{
    public async Task<CustomerProfileResult> CreateAsync(
        string firstName,
        string lastName,
        string email,
        CancellationToken cancellationToken)
    {
        var token = await tokenProvider.GetAccessTokenAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(token))
        {
            return new(null, false, false);
        }

        try
        {
            using var request = Authorized(
                HttpMethod.Post,
                "customers",
                token,
                new UpsertCustomerRequest(firstName, lastName, email));
            using var response = await clientFactory.CreateClient("customers").SendAsync(request, cancellationToken);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                tokenProvider.Invalidate(token);
                return new(null, true, false);
            }

            if (!response.IsSuccessStatusCode)
            {
                return new(null, true, true);
            }

            return new(
                await response.Content.ReadFromJsonAsync<CustomerProfile>(cancellationToken),
                true,
                true);
        }
        catch (Exception exception) when (IsTransient(exception, cancellationToken))
        {
            logger.LogWarning(exception, "Customer service was unavailable during account registration.");
            return new(null, false, true);
        }
    }

    public async Task<bool> DeleteAsync(int customerId, CancellationToken cancellationToken)
    {
        var token = await tokenProvider.GetAccessTokenAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        try
        {
            using var request = Authorized(HttpMethod.Delete, $"customers/{customerId}", token, null);
            using var response = await clientFactory.CreateClient("customers").SendAsync(request, cancellationToken);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                tokenProvider.Invalidate(token);
            }

            return response.IsSuccessStatusCode;
        }
        catch (Exception exception) when (IsTransient(exception, cancellationToken))
        {
            logger.LogWarning(exception, "Customer service was unavailable during registration compensation.");
            return false;
        }
    }

    private static HttpRequestMessage Authorized(
        HttpMethod method,
        string path,
        string token,
        object? content)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (content is not null)
        {
            request.Content = JsonContent.Create(content);
        }

        return request;
    }

    private static bool IsTransient(Exception exception, CancellationToken cancellationToken) =>
        exception is HttpRequestException
        || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested);

    private sealed record UpsertCustomerRequest(
        string FirstName,
        string LastName,
        string? Telephone,
        string? Mobile,
        string? Fax,
        string Email,
        DateTime? DateOfBirth,
        int? CompanyId,
        int? BillingAddressId,
        int? ShippingAddressId)
    {
        public UpsertCustomerRequest(string firstName, string lastName, string email)
            : this(firstName, lastName, null, null, null, email, null, null, null, null)
        {
        }
    }
}
