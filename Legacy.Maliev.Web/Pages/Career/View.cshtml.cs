using Legacy.Maliev.Web.Application;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Legacy.Maliev.Web.Pages.About.Career;

public sealed class View(ICareerClient careerClient) : PageModel
{
    public CareerOffer JobOffer { get; private set; } = null!;

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

        JobOffer = response.Value;
        return Page();
    }
}
