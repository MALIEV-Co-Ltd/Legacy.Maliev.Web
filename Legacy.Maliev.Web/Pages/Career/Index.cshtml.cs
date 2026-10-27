using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Components.Pages.Career;
using Legacy.Maliev.Web.Pages.Shared;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Legacy.Maliev.Web.Pages.About.Career;

public sealed class Index(ICareerClient careerClient, IConfiguration configuration) : PageModel
{
    public IReadOnlyList<CareerLevel> CareerLevels { get; private set; } = [];

    public CareerSort CurrentSort { get; private set; }

    public CareerSort JobCreatedDateSort { get; private set; }

    public CareerSort JobIdSort { get; private set; }

    public CareerOfferPage JobOffers { get; private set; } = CareerOfferPage.Empty(1);

    public string? JobSearch { get; private set; }

    public int PageSize { get; private set; } = 25;

    public bool ServiceAvailable { get; private set; } = true;

    public CareerIndexContentModel DisplayModel => CareerIndexContentModel.Create(
        ServiceAvailable,
        CareerLevels,
        JobOffers,
        CurrentSort,
        JobSearch,
        PageSize);

    public async Task<IActionResult> OnGetAsync(
        string? sort,
        string? search,
        int? index,
        int? size,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || !PageSizeQuery.TryResolve(size, out var pageSize))
        {
            return BadRequest();
        }

        ConfigureSearchQuery(sort, search, index, pageSize);
        await LoadAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnGetChangeItemCountAsync(
        int size,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || !PageSizeQuery.TryResolve(size, out var pageSize))
        {
            return BadRequest();
        }

        ConfigureSearchQuery(null, null, 0, pageSize);
        await LoadAsync(cancellationToken, hideFixture: false);
        return Page();
    }

    public async Task<IActionResult> OnGetSearchAsync(
        string? sort,
        string? search,
        int? index,
        int? size,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || !PageSizeQuery.TryResolve(size, out var pageSize))
        {
            return BadRequest();
        }

        ConfigureSearchQuery(sort, search, index, pageSize);
        var offers = await careerClient.GetOffersAsync(
            CurrentSort, JobSearch, JobOffers.PageIndex, PageSize, cancellationToken);
        JobOffers = (offers.Value ?? CareerOfferPage.Empty(JobOffers.PageIndex)) with
        {
            Items = (offers.Value?.Items ?? []).Where(offer => offer.IsFilled == false).ToArray()
        };
        ServiceAvailable = offers.ServiceAvailable;
        return new JsonResult(JobOffers.Items);
    }

    private void ConfigureSearchQuery(string? sort, string? search, int? index, int size)
    {
        PageSize = size;
        CurrentSort = Enum.TryParse<CareerSort>(sort, out var parsedSort)
            ? parsedSort
            : CareerSort.JobId_Ascending;
        JobSearch = search;
        JobOffers = CareerOfferPage.Empty(string.IsNullOrEmpty(search) ? index ?? 1 : 1);
        JobIdSort = CurrentSort == CareerSort.JobId_Ascending
            ? CareerSort.JobId_Descending
            : CareerSort.JobId_Ascending;
        JobCreatedDateSort = CurrentSort == CareerSort.JobCreatedDate_Ascending
            ? CareerSort.JobCreatedDate_Descending
            : CareerSort.JobCreatedDate_Ascending;
    }

    private async Task LoadAsync(CancellationToken cancellationToken, bool hideFixture = true)
    {
        var listing = await careerClient.GetListingAsync(
            CurrentSort,
            JobSearch,
            JobOffers.PageIndex,
            PageSize,
            cancellationToken);

        CareerLevels = listing.Levels;
        var hideLocalAspireFixture = hideFixture && configuration.GetValue<bool>("Career:HideLocalAspireFixture");
        JobOffers = listing.Offers with
        {
            Items = listing.Offers.Items
                .Where(offer => CareerOfferPresentation.IsVisibleOpenOffer(offer, hideLocalAspireFixture))
                .ToArray()
        };
        ServiceAvailable = listing.ServiceAvailable;
    }
}
