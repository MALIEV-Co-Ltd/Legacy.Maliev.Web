using Legacy.Maliev.Web.Application;

namespace Legacy.Maliev.Web.Components.Pages.Career;

/// <summary>
/// Carries career search results, available level filters, and pagination state for the public listing.
/// </summary>
/// <param name="ServiceAvailable">Whether the career service supplied an available response.</param>
/// <param name="Levels">The level filter choices available to applicants.</param>
/// <param name="Offers">The projected career offers on the current page.</param>
/// <param name="CurrentSort">The current sort selection rendered by the listing.</param>
/// <param name="Search">The submitted search text, or null when no filter is applied.</param>
/// <param name="PageSize">The requested number of offers per page.</param>
/// <param name="PageIndex">The current result page index.</param>
/// <param name="TotalPages">The total number of pages reported by the service.</param>
/// <param name="HasPreviousPage">Whether a preceding results page is available.</param>
/// <param name="HasNextPage">Whether a following results page is available.</param>
public sealed record CareerIndexContentModel(
    bool ServiceAvailable,
    IReadOnlyList<CareerLevelOption> Levels,
    IReadOnlyList<CareerOfferRow> Offers,
    string CurrentSort,
    string? Search,
    int PageSize,
    int PageIndex,
    int TotalPages,
    bool HasPreviousPage,
    bool HasNextPage)
{
    /// <summary>
    /// Projects career results and resolves each offer's displayed level without changing paging metadata.
    /// </summary>
    /// <param name="serviceAvailable">Whether the career service is available.</param>
    /// <param name="levels">The level lookup used for filtering and offer labels.</param>
    /// <param name="offers">The current paginated career response.</param>
    /// <param name="currentSort">The validated sort selected for the listing.</param>
    /// <param name="search">The optional search text retained in the listing form.</param>
    /// <param name="pageSize">The selected results page size.</param>
    /// <returns>The listing content, filter state, and navigation availability.</returns>
    public static CareerIndexContentModel Create(
        bool serviceAvailable,
        IReadOnlyList<CareerLevel> levels,
        CareerOfferPage offers,
        CareerSort currentSort,
        string? search,
        int pageSize)
    {
        var rows = offers.Items
            .Select(offer =>
            {
                var level = levels.SingleOrDefault(item => item.Id == offer.LevelId);
                return new CareerOfferRow(
                    offer.Id,
                    offer.Title,
                    offer.Location,
                    level?.Name ?? "-");
            })
            .ToArray();

        return new CareerIndexContentModel(
            serviceAvailable,
            levels.Select(level => new CareerLevelOption(level.Id, level.Name)).ToArray(),
            rows,
            currentSort.ToString(),
            search,
            pageSize,
            offers.PageIndex,
            offers.TotalPages,
            offers.HasPreviousPage,
            offers.HasNextPage);
    }
}

/// <summary>
/// Represents one career-level filter choice displayed to applicants.
/// </summary>
/// <param name="Id">The level identifier submitted by the filter.</param>
/// <param name="Name">The career-level label supplied by the service.</param>
public sealed record CareerLevelOption(int Id, string? Name);

/// <summary>
/// Carries the title, location, and level of one offer in the career listing.
/// </summary>
/// <param name="Id">The offer identifier used to open its detail page.</param>
/// <param name="Title">The advertised role title, or null when absent.</param>
/// <param name="Location">The advertised work location, or null when absent.</param>
/// <param name="LevelName">The resolved career-level label or the unavailable placeholder.</param>
public sealed record CareerOfferRow(int Id, string? Title, string? Location, string LevelName);
