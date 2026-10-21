using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Components.Pages.Career;
using Legacy.Maliev.Web.Pages.Shared;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Legacy.Maliev.Web.Pages.About.Career;

/// <summary>Loads public career listings with validated paging, search, sorting, and configured fixture visibility.</summary>
/// <param name="careerClient">The typed client supplying public career listings and offer details.</param>
/// <param name="configuration">The configuration controlling whether local Aspire career fixtures are publicly visible.</param>
public sealed class Index(ICareerClient careerClient, IConfiguration configuration) : PageModel
{
    /// <summary>The career levels returned with the public job listing.</summary>
    public IReadOnlyList<CareerLevel> CareerLevels { get; private set; } = [];

    /// <summary>The resolved ordering applied to the current career listing.</summary>
    public CareerSort CurrentSort { get; private set; }

    /// <summary>The next created-date ordering offered by the listing's sort control.</summary>
    public CareerSort JobCreatedDateSort { get; private set; }

    /// <summary>The next job-identifier ordering offered by the listing's sort control.</summary>
    public CareerSort JobIdSort { get; private set; }

    /// <summary>The filtered page of publicly visible career offers.</summary>
    public CareerOfferPage JobOffers { get; private set; } = CareerOfferPage.Empty(1);

    /// <summary>The normalized text filter applied to the public career listing.</summary>
    public string? JobSearch { get; private set; }

    /// <summary>The number of results requested per page, subject to the supported page-size policy.</summary>
    public int PageSize { get; private set; } = 25;

    /// <summary>Whether the career service returned an available listing response.</summary>
    public bool ServiceAvailable { get; private set; } = true;

    /// <summary>The public career-listing content projected from the loaded offers and query state.</summary>
    public CareerIndexContentModel DisplayModel => CareerIndexContentModel.Create(
        ServiceAvailable,
        CareerLevels,
        JobOffers,
        CurrentSort,
        JobSearch,
        PageSize);

    /// <summary>Validates listing inputs and loads the publicly visible career-offer page.</summary>
    /// <param name="sort">The requested ordering for the career listing.</param>
    /// <param name="search">The optional career-listing text filter.</param>
    /// <param name="index">The requested one-based career-listing page.</param>
    /// <param name="size">The requested number of career offers per page.</param>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <returns>The career listing page or a bad-request result for invalid paging.</returns>
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

        ConfigureQuery(sort, search, index, pageSize);
        await LoadAsync(cancellationToken);
        return Page();
    }

    /// <summary>Loads the first career-listing page with a validated result count.</summary>
    /// <param name="size">The requested number of career offers per page.</param>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <returns>The career listing page or a bad-request result for invalid paging.</returns>
    public async Task<IActionResult> OnGetChangeItemCountAsync(
        int size,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || !PageSizeQuery.TryResolve(size, out var pageSize))
        {
            return BadRequest();
        }

        ConfigureQuery(null, null, 1, pageSize);
        await LoadAsync(cancellationToken);
        return Page();
    }

    /// <summary>Loads matching public career offers for the validated listing query.</summary>
    /// <param name="sort">The requested ordering for the career listing.</param>
    /// <param name="search">The optional career-listing text filter.</param>
    /// <param name="index">The requested one-based career-listing page.</param>
    /// <param name="size">The requested number of career offers per page.</param>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <returns>JSON containing the visible offer items, or a bad-request result for invalid paging.</returns>
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

        ConfigureQuery(sort, search, index, pageSize);
        await LoadAsync(cancellationToken);
        return new JsonResult(JobOffers.Items);
    }

    private void ConfigureQuery(string? sort, string? search, int? index, int size)
    {
        PageSize = size;
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
        var hideLocalAspireFixture = configuration.GetValue<bool>("Career:HideLocalAspireFixture");
        JobOffers = listing.Offers with
        {
            Items = listing.Offers.Items
                .Where(offer => CareerOfferPresentation.IsVisibleOpenOffer(offer, hideLocalAspireFixture))
                .ToArray()
        };
        ServiceAvailable = listing.ServiceAvailable;
    }
}
