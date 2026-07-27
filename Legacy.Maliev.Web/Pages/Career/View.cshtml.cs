using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Components.Pages.Career;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Legacy.Maliev.Web.Pages.About.Career;

public sealed class View(ICareerClient careerClient, IConfiguration configuration) : PageModel
{
    public CareerOffer JobOffer { get; private set; } = null!;

    public CareerDetailContentModel DisplayModel => CareerDetailContentModel.Create(JobOffer);

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
    {
        if (id <= 0)
        {
            return BadRequest();
        }

        var response = await careerClient.GetOfferAsync(id, cancellationToken);
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
