using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Components.Pages.Career;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Legacy.Maliev.Web.Pages.About.Career;

public sealed class Index(ICareerClient careerClient) : PageModel
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
        int size,
        CancellationToken cancellationToken)
    {
        ConfigureQuery(sort, search, index, size);
        await LoadAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnGetChangeItemCountAsync(
        int size,
        CancellationToken cancellationToken)
    {
        ConfigureQuery(null, null, 1, size);
        await LoadAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnGetSearchAsync(
        string? sort,
        string? search,
        int? index,
        int size,
        CancellationToken cancellationToken)
    {
        ConfigureQuery(sort, search, index, size);
        await LoadAsync(cancellationToken);
        return new JsonResult(JobOffers.Items);
    }

    private void ConfigureQuery(string? sort, string? search, int? index, int size)
    {
        PageSize = Math.Clamp(size > 0 ? size : 25, 1, 100);
        CurrentSort = Enum.TryParse<CareerSort>(sort, out var parsedSort)
            ? parsedSort
            : CareerSort.JobCreatedDate_Descending;
        JobSearch = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        var targetPageIndex = JobSearch is null ? Math.Max(index ?? 1, 1) : 1;
        JobOffers = CareerOfferPage.Empty(targetPageIndex);
        JobIdSort = CurrentSort == CareerSort.JobId_Ascending
            ? CareerSort.JobId_Descending
            : CareerSort.JobId_Ascending;
        JobCreatedDateSort = CurrentSort == CareerSort.JobCreatedDate_Ascending
            ? CareerSort.JobCreatedDate_Descending
            : CareerSort.JobCreatedDate_Ascending;
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        var listing = await careerClient.GetListingAsync(
            CurrentSort,
            JobSearch,
            JobOffers.PageIndex,
            PageSize,
            cancellationToken);

        CareerLevels = listing.Levels;
        JobOffers = listing.Offers with
        {
            Items = listing.Offers.Items.Where(offer => offer.IsFilled != true).ToArray()
        };
        ServiceAvailable = listing.ServiceAvailable;
    }
}
