using Legacy.Maliev.Web.Application;

namespace Legacy.Maliev.Web.Components.Pages.Career;

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

public sealed record CareerLevelOption(int Id, string? Name);

public sealed record CareerOfferRow(int Id, string? Title, string? Location, string LevelName);
