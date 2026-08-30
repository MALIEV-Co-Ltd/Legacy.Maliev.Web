using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Legacy.Maliev.Web.Application;
using Microsoft.Extensions.Logging;
using Polly.Timeout;

namespace Legacy.Maliev.Web.Infrastructure;

internal sealed class CustomerOrderCatalogClient(
    IHttpClientFactory clientFactory,
    IServiceAccessTokenProvider tokenProvider,
    ILogger<CustomerOrderCatalogClient> logger) : ICustomerOrderCatalogClient
{
    public async Task<CustomerOrderCatalogResult> GetAsync(
        CustomerOrderKind kind,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(kind))
        {
            return new(null, true, false);
        }

        var token = await tokenProvider.GetAccessTokenAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(token))
        {
            return new(null, false, false);
        }

        try
        {
            var processRoute = kind switch
            {
                CustomerOrderKind.Additive => "orders/processes/additive",
                CustomerOrderKind.Scanning => "orders/processes/scanning",
                CustomerOrderKind.Machining => "orders/processes/machining",
                _ => throw new ArgumentOutOfRangeException(nameof(kind)),
            };
            var processes = await ReadListAsync<CustomerOrderCatalogProcess>(
                "orders",
                processRoute,
                token,
                cancellationToken);
            var fileFormats = await ReadListAsync<CustomerOrderFileFormat>(
                "orders",
                "orders/fileformats",
                token,
                cancellationToken);

            ReadResult<CustomerOrderCatalogMaterial> materials;
            ReadResult<CustomerOrderCatalogMaterialGroup> groups;
            if (kind == CustomerOrderKind.Scanning)
            {
                materials = ReadResult<CustomerOrderCatalogMaterial>.Empty;
                groups = ReadResult<CustomerOrderCatalogMaterialGroup>.Empty;
            }
            else
            {
                var materialRoute = kind == CustomerOrderKind.Additive
                    ? "materials/printable"
                    : "materials/machinable";
                materials = await ReadListAsync<CustomerOrderCatalogMaterial>(
                    "catalog",
                    materialRoute,
                    token,
                    cancellationToken);
                groups = await ReadListAsync<CustomerOrderCatalogMaterialGroup>(
                    "catalog",
                    "materials/materialgroups",
                    token,
                    cancellationToken);
            }

            var all = new IReadResult[] { processes, fileFormats, materials, groups };
            if (all.Any(result => !result.Authorized))
            {
                tokenProvider.Invalidate(token);
                return new(null, all.All(result => result.ServiceAvailable), false);
            }

            if (all.Any(result => !result.ServiceAvailable))
            {
                return new(null, false, true);
            }

            return new(
                new CustomerOrderCatalog(
                    Sort(processes.Items),
                    Sort(groups.Items),
                    Sort(materials.Items),
                    fileFormats.Items.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray()),
                true,
                true);
        }
        catch (Exception exception) when (IsTransient(exception, cancellationToken))
        {
            logger.LogWarning("Catalog services were unavailable while loading a member order form.");
            return new(null, false, true);
        }
    }

    public async Task<CustomerOrderMaterialOptionsResult> GetMaterialOptionsAsync(
        int materialId,
        CancellationToken cancellationToken)
    {
        if (materialId <= 0)
        {
            return new(null, true, false);
        }

        var token = await tokenProvider.GetAccessTokenAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(token))
        {
            return new(null, false, false);
        }

        try
        {
            var colors = await ReadListAsync<CustomerOrderCatalogOption>(
                "catalog",
                $"materials/{materialId}/colors",
                token,
                cancellationToken);
            var finishes = await ReadListAsync<CustomerOrderCatalogOption>(
                "catalog",
                $"materials/{materialId}/surfacefinishes",
                token,
                cancellationToken);
            if (!colors.Authorized || !finishes.Authorized)
            {
                tokenProvider.Invalidate(token);
                return new(null, colors.ServiceAvailable && finishes.ServiceAvailable, false);
            }

            if (!colors.ServiceAvailable || !finishes.ServiceAvailable)
            {
                return new(null, false, true);
            }

            return new(
                new CustomerOrderMaterialOptions(Sort(colors.Items), Sort(finishes.Items)),
                true,
                true);
        }
        catch (Exception exception) when (IsTransient(exception, cancellationToken))
        {
            logger.LogWarning("Catalog service was unavailable while loading material options.");
            return new(null, false, true);
        }
    }

    public async Task<ServiceResponse<CustomerOrderCurrency>> GetCurrencyAsync(
        string shortName,
        CancellationToken cancellationToken)
    {
        var token = await tokenProvider.GetAccessTokenAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(token))
        {
            return new(null, false);
        }

        try
        {
            var currencies = await ReadListAsync<CustomerOrderCurrency>(
                "catalog",
                "currencies",
                token,
                cancellationToken);
            if (!currencies.Authorized)
            {
                tokenProvider.Invalidate(token);
            }

            return new(
                currencies.Items.FirstOrDefault(currency => string.Equals(
                    currency.ShortName,
                    shortName,
                    StringComparison.OrdinalIgnoreCase)),
                currencies.ServiceAvailable && currencies.Authorized);
        }
        catch (Exception exception) when (IsTransient(exception, cancellationToken))
        {
            logger.LogWarning("Catalog service was unavailable while resolving a currency.");
            return new(null, false);
        }
    }

    private async Task<ReadResult<T>> ReadListAsync<T>(
        string clientName,
        string route,
        string token,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, route);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await clientFactory.CreateClient(clientName).SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return ReadResult<T>.Empty;
        }

        if (!response.IsSuccessStatusCode)
        {
            var authorized = response.StatusCode is not (HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden);
            return new([], (int)response.StatusCode < 500, authorized);
        }

        var items = await response.Content.ReadFromJsonAsync<IReadOnlyList<T>>(cancellationToken);
        return new(items ?? [], true, true);
    }

    private static IReadOnlyList<T> Sort<T>(IReadOnlyList<T> items) where T : notnull =>
        items.OrderBy(item => item switch
        {
            CustomerOrderCatalogProcess value => value.Name,
            CustomerOrderCatalogMaterialGroup value => value.Name,
            CustomerOrderCatalogMaterial value => value.Name,
            CustomerOrderCatalogOption value => value.Name,
            _ => string.Empty,
        }, StringComparer.OrdinalIgnoreCase).ToArray();

    private static bool IsTransient(Exception exception, CancellationToken cancellationToken) =>
        exception is HttpRequestException or JsonException or TimeoutRejectedException
        || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested);

    private interface IReadResult
    {
        bool ServiceAvailable { get; }
        bool Authorized { get; }
    }

    private sealed record ReadResult<T>(
        IReadOnlyList<T> Items,
        bool ServiceAvailable,
        bool Authorized) : IReadResult
    {
        public static ReadResult<T> Empty { get; } = new([], true, true);
    }
}
