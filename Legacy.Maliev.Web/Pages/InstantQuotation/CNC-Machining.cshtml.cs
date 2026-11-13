using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Components.Pages.InstantQuotation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.DependencyInjection;

namespace Legacy.Maliev.Web.Pages.InstantQuotation;

/// <summary>Serves the migrated CNC instant-quotation document through service-owned BFF contracts.</summary>
/// <param name="countryClient">The typed country client supplying form country options.</param>
/// <param name="environment">The hosting environment used by the existing CNC availability policy.</param>
/// <param name="configuration">The configuration containing the CNC availability flag and approved commercial-rules version.</param>
/// <param name="services">The request service provider resolving protected CNC bindings, receipt storage, sessions, and profile loading.</param>
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class CncMachiningModel(
    ICountryClient countryClient,
    IWebHostEnvironment environment,
    IConfiguration configuration,
    IServiceProvider services) : PageModel
{
    private static readonly TimeSpan DependencyBudget = TimeSpan.FromSeconds(2);
    private CncAuthenticatedProfile? authenticatedProfile;

    /// <summary>Gets the customer-visible solid-CAD upload limit.</summary>
    public int MaximumCncModelUploadSizeMegabytes => CncUploadAdmissionPolicy.MaximumModelFileSizeBytes / (1024 * 1024);

    /// <summary>Gets the customer-visible PDF drawing upload limit.</summary>
    public int MaximumCncDrawingUploadSizeMegabytes => CncUploadAdmissionPolicy.MaximumDrawingFileSizeBytes / (1024 * 1024);

    /// <summary>Gets available countries.</summary>
    public IReadOnlyList<Country> Countries { get; private set; } = [];

    /// <summary>Gets the protected server-created journey identifier used by the upload transport.</summary>
    public string InstantQuotationSessionId { get; private set; } = string.Empty;

    /// <summary>Gets the protected per-page CNC form token.</summary>
    [BindProperty]
    public string QuotationFormToken { get; set; } = string.Empty;

    /// <summary>The customer's given name used in the account or request contact details.</summary>
    [BindProperty] public string FirstName { get; set; } = string.Empty;
    /// <summary>The customer's family name used in the account or request contact details.</summary>
    [BindProperty] public string LastName { get; set; } = string.Empty;
    /// <summary>The customer's email address used by this account or request workflow.</summary>
    [BindProperty] public string Email { get; set; } = string.Empty;
    /// <summary>The mobile contact number supplied with the customer's details.</summary>
    [BindProperty] public string Mobile { get; set; } = string.Empty;
    /// <summary>The optional landline contact number supplied with the customer's details.</summary>
    [BindProperty] public string Telephone { get; set; } = string.Empty;
    /// <summary>The optional company name associated with the submitted customer request.</summary>
    [BindProperty] public string Company { get; set; } = string.Empty;
    /// <summary>The optional tax identifier associated with the customer's billing or company details.</summary>
    [BindProperty] public string TaxNumber { get; set; } = string.Empty;
    /// <summary>The selected head-office or branch designation used when formatting the tax identifier.</summary>
    [BindProperty] public string TaxBranch { get; set; } = string.Empty;
    /// <summary>The five-digit branch code required when a tax identifier belongs to a branch.</summary>
    [BindProperty] public string TaxBranchCode { get; set; } = string.Empty;
    /// <summary>The customer's country name used for the submitted contact or billing details.</summary>
    [BindProperty] public string Country { get; set; } = "Thailand";
    /// <summary>The optional building name for the customer's billing address, separate from street and locality fields.</summary>
    [BindProperty] public string BillingBuilding { get; set; } = string.Empty;
    /// <summary>The required first street-address line for quotation billing.</summary>
    [BindProperty] public string BillingStreet1 { get; set; } = string.Empty;
    /// <summary>The optional second street-address line for quotation billing.</summary>
    [BindProperty] public string BillingStreet2 { get; set; } = string.Empty;
    /// <summary>The city or district used for customer billing.</summary>
    [BindProperty] public string BillingCity { get; set; } = string.Empty;
    /// <summary>The state or province used for quotation billing.</summary>
    [BindProperty] public string BillingProvince { get; set; } = string.Empty;
    /// <summary>The postal code used for customer billing.</summary>
    [BindProperty] public string BillingPostalCode { get; set; } = string.Empty;
    /// <summary>Whether the quotation's shipping destination reuses its billing address.</summary>
    [BindProperty] public bool ShipToBillingAddress { get; set; } = true;
    /// <summary>The optional building name for the shipping destination, separate from street and locality fields.</summary>
    [BindProperty] public string ShippingBuilding { get; set; } = string.Empty;
    /// <summary>The first shipping street-address line required when billing and shipping destinations differ.</summary>
    [BindProperty] public string ShippingStreet1 { get; set; } = string.Empty;
    /// <summary>The optional second street-address line of the separate shipping destination.</summary>
    [BindProperty] public string ShippingStreet2 { get; set; } = string.Empty;
    /// <summary>The city or district of the shipping destination.</summary>
    [BindProperty] public string ShippingCity { get; set; } = string.Empty;
    /// <summary>The state or province of the separate shipping destination.</summary>
    [BindProperty] public string ShippingProvince { get; set; } = string.Empty;
    /// <summary>The postal code of the shipping destination.</summary>
    [BindProperty] public string ShippingPostalCode { get; set; } = string.Empty;
    /// <summary>The country name required for a separate shipping destination.</summary>
    [BindProperty] public string ShippingCountry { get; set; } = "Thailand";
    /// <summary>The optional customer instructions accompanying the manufacturing request.</summary>
    [BindProperty] public string Description { get; set; } = string.Empty;
    /// <summary>The submitted display estimate for CNC lead time; it is not an authoritative production commitment.</summary>
    [BindProperty] public string EstimatedLeadTime { get; set; } = string.Empty;
    /// <summary>The submitted display estimate for CNC cost; it is not authoritative quotation pricing.</summary>
    [BindProperty] public string EstimatedTotalPrice { get; set; } = string.Empty;

    /// <summary>Gets or sets the terminal submission message.</summary>
    [TempData]
    public string? Notification { get; set; }

    /// <summary>Gets or sets whether the terminal submission failed.</summary>
    [TempData]
    public bool SubmissionFailed { get; set; }

    /// <summary>Gets or sets the persisted request reference.</summary>
    [TempData]
    public int? SubmittedRequestId { get; set; }

    /// <summary>Production-derived fixture identities are never rendered by this migration.</summary>
    public bool IsDevelopmentCustomerFixture => false;

    /// <summary>Returns whether an authenticated value came from the verified customer profile.</summary>
    /// <param name="propertyName">The customer field name whose verified-profile lock state is queried.</param>
    /// <returns>True when the named value came from the verified customer profile; otherwise, false.</returns>
    public bool IsAccountValueLocked(string propertyName) => authenticatedProfile?.IsLocked(propertyName) == true;

    /// <summary>Loads safe display state and creates a tab-scoped protected form.</summary>
    /// <returns>The CNC quotation page when the existing availability policy permits it; otherwise, not found.</returns>
    public async Task<IActionResult> OnGetAsync()
    {
        var receipts = services.GetService<ICncUploadReceiptStore>();
        if (!CncQuotationAvailability.IsAvailable(
                environment.IsDevelopment() || environment.IsEnvironment("Testing"),
                configuration.GetValue<bool>("CncQuotation:Enabled"),
                configuration["CncQuotation:ApprovedCommercialRulesVersion"],
                receipts is not null,
                receipts?.IsSharedDistributedAtomic == true))
        {
            return NotFound();
        }

        var sessions = services.GetRequiredService<CncQuotationSession>();
        var bindings = services.GetRequiredService<CncProtectedUploadBindings>();
        var profileLoader = services.GetRequiredService<CncAuthenticatedProfileLoader>();
        InstantQuotationSessionId = sessions.GetOrCreate(HttpContext);
        QuotationFormToken = bindings.CreateFormToken(InstantQuotationSessionId);
        await GetCountriesAsync();

        var errors = new ModelStateDictionary();
        using var profileTimeout = CancellationTokenSource.CreateLinkedTokenSource(HttpContext.RequestAborted);
        profileTimeout.CancelAfter(DependencyBudget);
        CncAuthenticatedProfileLoadResult profile;
        try
        {
            profile = await profileLoader.LoadAsync(HttpContext, errors, profileTimeout.Token).WaitAsync(profileTimeout.Token);
        }
        catch (Exception exception) when (exception is HttpRequestException or TimeoutException or OperationCanceledException)
        {
            profile = new(CncAuthenticatedProfileLoadOutcome.Unavailable);
        }
        if (profile.Outcome == CncAuthenticatedProfileLoadOutcome.Loaded && profile.Profile is not null)
        {
            authenticatedProfile = profile.Profile;
            Apply(profile.Profile.Details);
        }

        return Page();
    }

    /// <summary>Loads the country list without making the public page depend on optional profile data.</summary>
    /// <returns>A task that completes after country options or the retained Thailand fallback have been assigned.</returns>
    public async Task GetCountriesAsync()
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(HttpContext.RequestAborted);
            timeout.CancelAfter(DependencyBudget);
            ServiceResponse<IReadOnlyList<Country>> result = await countryClient.GetCountriesAsync(timeout.Token).WaitAsync(timeout.Token);
            Countries = result.ServiceAvailable && result.Value is not null ? result.Value : [];
        }
        catch (Exception exception) when (exception is HttpRequestException or TimeoutException or OperationCanceledException)
        {
            Countries = [];
        }

        if (Countries.Count == 0)
        {
            Countries = [new Country(0, "Thailand", null, "TH", "TH", "THA", null, null)];
        }
    }

    private void Apply(CncSubmission value)
    {
        FirstName = value.FirstName; LastName = value.LastName; Email = value.Email; Mobile = value.Mobile;
        Telephone = value.Telephone; Company = value.Company; TaxNumber = value.TaxNumber;
        TaxBranch = value.TaxBranch; TaxBranchCode = value.TaxBranchCode; Country = value.Country;
        BillingBuilding = value.BillingBuilding; BillingStreet1 = value.BillingStreet1; BillingStreet2 = value.BillingStreet2;
        BillingCity = value.BillingCity; BillingProvince = value.BillingProvince; BillingPostalCode = value.BillingPostalCode;
        ShipToBillingAddress = value.ShipToBillingAddress; ShippingBuilding = value.ShippingBuilding;
        ShippingStreet1 = value.ShippingStreet1; ShippingStreet2 = value.ShippingStreet2;
        ShippingCity = value.ShippingCity; ShippingProvince = value.ShippingProvince;
        ShippingPostalCode = value.ShippingPostalCode; ShippingCountry = value.ShippingCountry;
    }
}
