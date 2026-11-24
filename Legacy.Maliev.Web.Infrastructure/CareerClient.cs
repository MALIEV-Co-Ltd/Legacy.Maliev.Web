using System.Net;
using System.Net.Http.Json;
using Legacy.Maliev.Web.Application;
using Maliev.Aspire.ServiceDefaults.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Legacy.Maliev.Web.Infrastructure;

internal sealed class CareerClient(
    IHttpClientFactory clientFactory,
    ILogger<CareerClient> logger) : ICareerClient
{
    public async Task<CareerListing> GetListingAsync(
        CareerSort sort,
        string? search,
        int pageIndex,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var offersTask = GetOffersAsync(sort, search, pageIndex, pageSize, cancellationToken);
        var levelsTask = GetLevelsAsync(cancellationToken);
        await Task.WhenAll(offersTask, levelsTask);

        var offers = await offersTask;
        var levels = await levelsTask;
        return new CareerListing(
            levels.Value ?? [],
            offers.Value ?? CareerOfferPage.Empty(pageIndex),
            offers.ServiceAvailable && levels.ServiceAvailable);
    }

    public async Task<ServiceResponse<CareerOffer>> GetOfferAsync(
        int offerId,
        CancellationToken cancellationToken)
    {
        if (offerId <= 0)
        {
            return new ServiceResponse<CareerOffer>(null, true);
        }

        var observation = new PrivateDependencyFailureObservation();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"Jobs/{offerId}");
            using var response = await CreateClient().SendWithPrivateFailureObservationAsync(
                request, cancellationToken, observation, HttpCompletionOption.ResponseContentRead);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new ServiceResponse<CareerOffer>(null, true);
            }

            response.EnsureSuccessStatusCode();
            var offer = await response.Content.ReadFromJsonAsync<CareerOffer>(cancellationToken);
            return new ServiceResponse<CareerOffer>(offer, true);
        }
        catch (Exception exception) when (IsTransient(exception, cancellationToken))
        {
            if (!observation.WasObserved)
            {
                logger.LogWarning("Career service was unavailable while loading a job offer.");
            }
            return new ServiceResponse<CareerOffer>(null, false);
        }
    }

    public async Task<ServiceResponse<CareerOfferPage>> GetOffersAsync(
        CareerSort sort,
        string? search,
        int pageIndex,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var query = string.Join(
            '&',
            $"sort={Uri.EscapeDataString(sort.ToString())}",
            $"search={Uri.EscapeDataString(search ?? string.Empty)}",
            $"index={pageIndex}",
            $"size={pageSize}");

        var observation = new PrivateDependencyFailureObservation();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"Jobs?{query}");
            using var response = await CreateClient().SendWithPrivateFailureObservationAsync(
                request, cancellationToken, observation, HttpCompletionOption.ResponseContentRead);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new ServiceResponse<CareerOfferPage>(CareerOfferPage.Empty(pageIndex), true);
            }

            response.EnsureSuccessStatusCode();
            var page = await CareerOfferPageReader.ReadAsync(response.Content, cancellationToken);
            return new ServiceResponse<CareerOfferPage>(page, true);
        }
        catch (Exception exception) when (IsTransient(exception, cancellationToken))
        {
            if (!observation.WasObserved)
            {
                logger.LogWarning("Career service was unavailable while loading the public job listing.");
            }
            return new ServiceResponse<CareerOfferPage>(CareerOfferPage.Empty(pageIndex), false);
        }
    }

    public async Task<ServiceResponse<IReadOnlyList<CareerLevel>>> GetLevelsAsync(
        CancellationToken cancellationToken)
    {
        var observation = new PrivateDependencyFailureObservation();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "jobs/levels");
            using var response = await CreateClient().SendWithPrivateFailureObservationAsync(
                request, cancellationToken, observation, HttpCompletionOption.ResponseContentRead);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new ServiceResponse<IReadOnlyList<CareerLevel>>([], true);
            }

            response.EnsureSuccessStatusCode();
            var levels = await response.Content.ReadFromJsonAsync<IReadOnlyList<CareerLevel>>(cancellationToken);
            return new ServiceResponse<IReadOnlyList<CareerLevel>>(levels ?? [], true);
        }
        catch (Exception exception) when (IsTransient(exception, cancellationToken))
        {
            if (!observation.WasObserved)
            {
                logger.LogWarning("Career service was unavailable while loading job levels.");
            }
            return new ServiceResponse<IReadOnlyList<CareerLevel>>([], false);
        }
    }

    private HttpClient CreateClient() => clientFactory.CreateClient("careers");

    private static bool IsTransient(Exception exception, CancellationToken cancellationToken) =>
        exception is HttpRequestException
        || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested);
}
