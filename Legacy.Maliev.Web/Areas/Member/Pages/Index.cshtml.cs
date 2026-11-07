using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Components.Pages.Member;
using Legacy.Maliev.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Legacy.Maliev.Web.Areas.Member.Pages;

/// <summary>Loads the authenticated member overview with profile, recent orders, quotations, and service notices.</summary>
/// <param name="sessionManager">The server-side account session manager used to resolve identity and manage sign-in state.</param>
/// <param name="accountClient">The typed customer account client used to load the member profile.</param>
/// <param name="countryClient">The typed country client supplying form country options.</param>
/// <param name="orderClient">The typed client reading or updating orders owned by the current customer.</param>
/// <param name="quotationClient">The typed client reading or submitting quotation operations for this page.</param>
[Authorize]
public sealed class Index(
    IAccountSessionManager sessionManager,
    ICustomerAccountClient accountClient,
    ICountryClient countryClient,
    ICustomerOrderClient orderClient,
    ICustomerQuotationClient quotationClient) : PageModel
{
    /// <summary>The loaded customer account profile, or null when no profile was returned.</summary>
    public CustomerAccountDetails? Profile { get; private set; }

    /// <summary>The customer's recent orders loaded for the member overview.</summary>
    public IReadOnlyList<CustomerOrder> RecentOrders { get; private set; } = [];

    /// <summary>The customer's recent quotations loaded for the member overview.</summary>
    public IReadOnlyList<CustomerQuotation> RecentQuotations { get; private set; } = [];

    /// <summary>The safe service-availability notices shown on the member overview.</summary>
    public IReadOnlyList<string> Notices { get; private set; } = [];

    /// <summary>The member overview projected from the loaded profile, recent activity, and notices.</summary>
    public MemberOverviewDisplayModel DisplayModel { get; private set; } = MemberOverviewDisplayModel.Empty;

    /// <summary>Loads the member overview through the owned session boundary.</summary>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <returns>The overview page or an authentication challenge.</returns>
    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var loaded = await MemberOverviewLoader.LoadAsync(
            HttpContext,
            sessionManager,
            accountClient,
            countryClient,
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

    /// <summary>Applies a supported culture to the retained member language POST and redirects locally.</summary>
    /// <param name="culture">The untrusted language preference.</param>
    /// <param name="returnUrl">The requested local return path.</param>
    /// <returns>A safe local redirect with the normalized culture preference.</returns>
    public IActionResult OnPostSetLanguage(string? culture, string? returnUrl)
    {
        culture = CanonicalUrlPolicy.NormalizeSupportedCulture(culture);

        Response.Cookies.Append(
            CookieRequestCultureProvider.DefaultCookieName,
            CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)),
            new CookieOptions
            {
                Expires = DateTimeOffset.UtcNow.AddYears(1),
                HttpOnly = true,
                IsEssential = true,
                SameSite = SameSiteMode.Lax,
                Secure = true
            });

        return LocalRedirect(CanonicalUrlPolicy.GetLocalizedReturnUrl(returnUrl, culture));
    }
}
