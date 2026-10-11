using System.Net;
using System.Net.Http.Json;
using Legacy.Maliev.Web.Application;
using Maliev.Aspire.ServiceDefaults.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Legacy.Maliev.Web.Infrastructure;

internal sealed class CountryClient(
    IHttpClientFactory clientFactory,
    ILogger<CountryClient> logger) : ICountryClient
{
    public async Task<ServiceResponse<IReadOnlyList<Country>>> GetCountriesAsync(
        CancellationToken cancellationToken)
    {
        var observation = new PrivateDependencyFailureObservation();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "Countries");
            using var response = await clientFactory.CreateClient("countries")
                .SendWithPrivateFailureObservationAsync(request, cancellationToken, observation,
                    HttpCompletionOption.ResponseContentRead);
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
            if (!observation.WasObserved)
            {
                logger.LogWarning("Country service was unavailable while loading the contact form.");
            }
            return new ServiceResponse<IReadOnlyList<Country>>([], false);
        }
    }

    private static bool IsTransient(Exception exception, CancellationToken cancellationToken) =>
        exception is HttpRequestException
        || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested);
}
