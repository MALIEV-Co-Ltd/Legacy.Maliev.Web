using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Legacy.Maliev.Web.Application;
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

        try
        {
            using var response = await CreateClient().GetAsync($"Jobs/{offerId}", cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new ServiceResponse<CareerOffer>(null, true);
            }

            response.EnsureSuccessStatusCode();
            var offer = await response.Content.ReadFromJsonAsync<CareerOffer>(cancellationToken);
            if (offer is { Level: null, LevelId: > 0 })
                offer = await ResolveMissingDetailLevelAsync(offer, cancellationToken);
            return new ServiceResponse<CareerOffer>(offer, true);
        }
        catch (Exception exception) when (IsTransient(exception, cancellationToken))
        {
            logger.LogWarning(exception, "Career service was unavailable while loading offer {OfferId}.", offerId);
            return new ServiceResponse<CareerOffer>(null, false);
        }
    }

    private async Task<CareerOffer> ResolveMissingDetailLevelAsync(CareerOffer offer, CancellationToken cancellationToken)
    {
        try
        {
            var levels = await GetLevelsAsync(cancellationToken);
            if (!levels.ServiceAvailable || levels.Value is null || levels.Value.Any(level => level is null))
                return offer;
            var matches = levels.Value.Where(level => level.Id == offer.LevelId).Take(2).ToArray();
            return matches.Length == 1 ? offer with { Level = matches[0] } : offer;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or InvalidOperationException)
        {
            logger.LogWarning("Career level metadata could not be read for offer {OfferId}; {FailureType}.", offer.Id, exception.GetType().Name);
            return offer;
        }
    }

    private async Task<ServiceResponse<CareerOfferPage>> GetOffersAsync(
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

        try
        {
            using var response = await CreateClient().GetAsync($"Jobs?{query}", cancellationToken);
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
            logger.LogWarning(exception, "Career service was unavailable while loading the public job listing.");
            return new ServiceResponse<CareerOfferPage>(CareerOfferPage.Empty(pageIndex), false);
        }
    }

    private async Task<ServiceResponse<IReadOnlyList<CareerLevel>>> GetLevelsAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await CreateClient().GetAsync("jobs/levels", cancellationToken);
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
            logger.LogWarning(exception, "Career service was unavailable while loading job levels.");
            return new ServiceResponse<IReadOnlyList<CareerLevel>>([], false);
        }
    }

    private HttpClient CreateClient() => clientFactory.CreateClient("careers");

    private static bool IsTransient(Exception exception, CancellationToken cancellationToken) =>
        exception is HttpRequestException
        || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested);
}
