using System.Security.Claims;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Components.Pages.InstantQuotation;
using Microsoft.AspNetCore.Authentication;

namespace Legacy.Maliev.Web.Infrastructure;

// This principal is presentation-only and never becomes the default authenticated User.
internal static class DevelopmentCustomerFixture
{
    internal const string Scheme = "Maliev.DevelopmentCustomerFixture";
    internal const string CookieName = "__Host-Maliev.Legacy.DevelopmentCustomerFixture";
    internal const string Email = "customer.fixture@localhost";
    private const string ClaimType = "maliev:development-customer-fixture";
    private static readonly object PrincipalKey = new();

    internal static void AddServices(IServiceCollection services, IWebHostEnvironment environment)
    {
        if (!environment.IsDevelopment()) return;
        services.AddAuthentication().AddCookie(Scheme, options =>
        {
            options.Cookie.Name = CookieName;
            options.Cookie.Path = "/";
            options.Cookie.HttpOnly = true;
            options.Cookie.IsEssential = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.SlidingExpiration = false;
            options.Events.OnValidatePrincipal = context =>
            {
                if (!IsFixturePrincipal(context.Principal)) context.RejectPrincipal();
                return Task.CompletedTask;
            };
        });
    }

    internal static bool IsAvailable(HttpContext context) =>
        context.RequestServices.GetService<IWebHostEnvironment>()?.IsDevelopment() is true
        && !context.User.Identities.Any(identity => identity.IsAuthenticated);

    internal static bool IsActive(HttpContext context) =>
        context.Items.TryGetValue(PrincipalKey, out var value)
        && value is ClaimsPrincipal principal && IsFixturePrincipal(principal) && IsAvailable(context);

    internal static async Task ObserveAsync(HttpContext context, Func<Task> next)
    {
        if (IsAvailable(context))
        {
            var result = await context.AuthenticateAsync(Scheme);
            if (result.Succeeded && IsFixturePrincipal(result.Principal))
                context.Items[PrincipalKey] = result.Principal;
        }
        await next();
    }

    internal static async Task<bool> TrySignInAsync(HttpContext context)
    {
        if (!IsAvailable(context)) return false;
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, "development-customer-fixture"),
                new Claim(ClaimTypes.Name, Email),
                new Claim(ClaimTypes.Email, Email),
                new Claim(ClaimType, "true"),
            ], Scheme));
        await context.SignInAsync(Scheme, principal, new AuthenticationProperties
        {
            IsPersistent = false,
            AllowRefresh = false,
        });
        return true;
    }

    internal static async Task<bool> TrySignOutAsync(HttpContext context)
    {
        if (!IsActive(context)) return false;
        await context.SignOutAsync(Scheme);
        return true;
    }

    private static bool IsFixturePrincipal(ClaimsPrincipal? principal)
    {
        if (principal?.Identities.Count() != 1 || principal.Identity is not ClaimsIdentity identity
            || !identity.IsAuthenticated || identity.AuthenticationType != Scheme) return false;
        var claims = identity.Claims.ToArray();
        return claims.Length == 4
            && HasSingle(ClaimTypes.NameIdentifier, "development-customer-fixture")
            && HasSingle(ClaimTypes.Name, Email)
            && HasSingle(ClaimTypes.Email, Email)
            && HasSingle(ClaimType, "true");

        bool HasSingle(string type, string value) =>
            claims.Count(claim => claim.Type == type && claim.Value == value) == 1;
    }

    internal static InstantQuotationProfileDetails MergeProfile(InstantQuotationProfileDetails draft) => draft with
    {
        FirstName = "ทดสอบ",
        LastName = "ลูกค้า",
        Email = Email,
        Mobile = "0890000000",
        BillingBuilding = "อาคารทดสอบ",
        BillingStreet1 = "99 ถนนทดสอบ",
        BillingCity = "เมืองนนทบุรี",
        BillingProvince = "นนทบุรี",
        BillingPostalCode = "11000",
        Country = "Thailand",
    };

    internal static async Task<InstantQuotationCustomerDisplayModel> CreateDisplayAsync(
        HttpContext context, CancellationToken cancellationToken)
    {
        var tokens = context.RequestServices.GetRequiredService<Microsoft.AspNetCore.Antiforgery.IAntiforgery>()
            .GetAndStoreTokens(context);
        IReadOnlyList<Country> countries = [];
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            var result = await context.RequestServices.GetRequiredService<ICountryClient>()
                .GetCountriesAsync(budget.Token).WaitAsync(budget.Token);
            countries = result.Value ?? [];
        }
        catch (Exception exception) when (exception is HttpRequestException or TimeoutException
            or Polly.Timeout.TimeoutRejectedException
            || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        { }
        var options = countries.Where(country => !string.IsNullOrWhiteSpace(country.Name))
            .Select(country => new InstantQuotationCountryOption(country.Name,
                InstantQuotationCountryLabels.DisplayName(country, System.Globalization.CultureInfo.CurrentUICulture)))
            .DistinctBy(country => country.Name, StringComparer.OrdinalIgnoreCase)
            .OrderBy(country => country.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        if (options.Length == 0) options = [new InstantQuotationCountryOption("Thailand")];
        return ApplyProfile(InstantQuotationCustomerDisplayModel.Empty with
        {
            AntiforgeryFieldName = tokens.FormFieldName,
            AntiforgeryRequestToken = tokens.RequestToken ?? string.Empty,
            Countries = options,
        });
    }

    internal static InstantQuotationCustomerDisplayModel ApplyProfile(InstantQuotationCustomerDisplayModel model)
    {
        var profile = MergeProfile(new InstantQuotationProfileDetails());
        return model with
        {
            IsDevelopmentCustomerFixture = true,
            FirstName = profile.FirstName,
            LastName = profile.LastName,
            Email = profile.Email,
            Mobile = profile.Mobile,
            BillingBuilding = profile.BillingBuilding,
            BillingStreet1 = profile.BillingStreet1,
            BillingCity = profile.BillingCity,
            BillingProvince = profile.BillingProvince,
            BillingPostalCode = profile.BillingPostalCode,
            Country = profile.Country,
            ShipToBillingAddress = true,
            LockedFields = new HashSet<string>(StringComparer.Ordinal)
            {
                "FirstName", "LastName", "Email", "Mobile", "BillingBuilding",
                "BillingStreet1", "BillingCity", "BillingProvince", "BillingPostalCode", "Country",
            },
        };
    }
}
