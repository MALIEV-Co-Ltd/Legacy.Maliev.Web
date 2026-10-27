using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Components.Pages.Career;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Legacy.Maliev.Web.Pages.About.Career;

/// <summary>Loads a public career offer while respecting service availability and configured fixture visibility.</summary>
/// <param name="careerClient">The typed client supplying public career listings and offer details.</param>
/// <param name="configuration">The configuration controlling whether local Aspire career fixtures are publicly visible.</param>
public sealed class View(ICareerClient careerClient, IConfiguration configuration) : PageModel
{
    /// <summary>The loaded career offer displayed after identifier and visibility checks.</summary>
    public CareerOffer JobOffer { get; private set; } = null!;

    /// <summary>The available career levels used to display the offer.</summary>
    public IReadOnlyList<CareerLevel> CareerLevels { get; private set; } = [];

    /// <summary>The public career-offer content projected from the loaded offer.</summary>
    public CareerDetailContentModel DisplayModel => CareerDetailContentModel.Create(JobOffer, CareerLevels);

    /// <summary>Loads a career offer after validating its identifier and configured public visibility.</summary>
    /// <param name="id">The identifier of the public career offer to display.</param>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <returns>The offer page, bad request, not found, or service-unavailable response.</returns>
    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
    {
        if (id <= 0)
        {
            return BadRequest();
        }

        var offerTask = careerClient.GetOfferAsync(id, cancellationToken);
        var levelsTask = careerClient.GetLevelsAsync(cancellationToken);
        await Task.WhenAll(offerTask, levelsTask);
        var response = await offerTask;
        CareerLevels = (await levelsTask).Value ?? [];
        if (!response.ServiceAvailable)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        if (response.Value is null)
        {
            return NotFound();
        }

        if (configuration.GetValue<bool>("Career:HideLocalAspireFixture")
            && CareerOfferPresentation.IsLocalAspireFixture(response.Value))
        {
            return NotFound();
        }

        JobOffer = response.Value;
        return Page();
    }
}
