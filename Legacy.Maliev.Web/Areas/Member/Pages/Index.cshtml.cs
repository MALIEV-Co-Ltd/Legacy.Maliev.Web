using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Components.Pages.Member;
using Legacy.Maliev.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Legacy.Maliev.Web.Areas.Member.Pages;

[Authorize]
public sealed class Index(
    IAccountSessionManager sessionManager,
    ICustomerAccountClient accountClient,
    ICustomerOrderClient orderClient,
    ICustomerQuotationClient quotationClient) : PageModel
{
    public CustomerAccountDetails? Profile { get; private set; }

    public IReadOnlyList<CustomerOrder> RecentOrders { get; private set; } = [];

    public IReadOnlyList<CustomerQuotation> RecentQuotations { get; private set; } = [];

    public IReadOnlyList<string> Notices { get; private set; } = [];

    public MemberOverviewDisplayModel DisplayModel { get; private set; } = MemberOverviewDisplayModel.Empty;

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var loaded = await MemberOverviewLoader.LoadAsync(
            HttpContext,
            sessionManager,
            accountClient,
            orderClient,
            quotationClient,
            cancellationToken);
        if (loaded is null)
        {
            return Challenge();
        }

        Profile = loaded.Profile;
        RecentOrders = loaded.RecentOrders;
        RecentQuotations = loaded.RecentQuotations;
        Notices = loaded.Notices;
        DisplayModel = loaded.DisplayModel;
        return Page();
    }
}
