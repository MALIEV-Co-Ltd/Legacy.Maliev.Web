using System.Net;
using System.Net.Http.Json;
using Legacy.Maliev.Web.Application;
using Microsoft.Extensions.Logging;

namespace Legacy.Maliev.Web.Infrastructure;

internal sealed class CountryClient(
    IHttpClientFactory clientFactory,
    ILogger<CountryClient> logger) : ICountryClient
{
    public async Task<ServiceResponse<IReadOnlyList<Country>>> GetCountriesAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await clientFactory.CreateClient("countries")
                .GetAsync("Countries", cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new ServiceResponse<IReadOnlyList<Country>>([], true);
            }

            response.EnsureSuccessStatusCode();
            var countries = await response.Content
                .ReadFromJsonAsync<IReadOnlyList<Country>>(cancellationToken);
            return new ServiceResponse<IReadOnlyList<Country>>(countries ?? [], true);
        }
        catch (Exception exception) when (IsTransient(exception, cancellationToken))
        {
            logger.LogWarning(exception, "Country service was unavailable while loading the contact form.");
            return new ServiceResponse<IReadOnlyList<Country>>([], false);
        }
    }

    private static bool IsTransient(Exception exception, CancellationToken cancellationToken) =>
        exception is HttpRequestException
        || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested);
}
