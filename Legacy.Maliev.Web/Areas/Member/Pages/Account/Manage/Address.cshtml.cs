using System.ComponentModel.DataAnnotations;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Components.Pages.Member;
using Legacy.Maliev.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Legacy.Maliev.Web.Areas.Member.Pages.Account.Manage;

/// <summary>Loads and updates the signed-in customer's billing and shipping addresses using the available country catalogue.</summary>
/// <param name="sessionManager">The server-side account session manager used to resolve identity and manage sign-in state.</param>
/// <param name="customerClient">The typed customer client used by this page to read or update customer details.</param>
/// <param name="countryClient">The typed country client supplying form country options.</param>
[Authorize]
public sealed class Address(
    IAccountSessionManager sessionManager,
    ICustomerAccountClient customerClient,
    ICountryClient countryClient) : PageModel
{
    private IReadOnlyList<Country> Countries { get; set; } = [];

    private bool CountrySelectionReady { get; set; }

    /// <summary>The optional building name for the customer's billing address, separate from street and locality fields.</summary>
    [BindProperty, StringLength(256)]
    public string? BillingBuilding { get; set; }

    /// <summary>The required first street-address line used for customer billing.</summary>
    [BindProperty, StringLength(256)]
    public string BillingAddress1 { get; set; } = string.Empty;

    /// <summary>The optional second street-address line used for customer billing.</summary>
    [BindProperty, StringLength(256)]
    public string? BillingAddress2 { get; set; }

    /// <summary>The city or district used for customer billing.</summary>
    [BindProperty, StringLength(128)]
    public string? BillingCity { get; set; }

    /// <summary>The state or province used for customer billing.</summary>
    [BindProperty, StringLength(128)]
    public string? BillingState { get; set; }

    /// <summary>The postal code used for customer billing.</summary>
    [BindProperty, StringLength(50)]
    public string? BillingPostalCode { get; set; }

    /// <summary>The billing country's identifier selected from the loaded country catalogue.</summary>
    [BindProperty]
    public int BillingCountryId { get; set; }

    /// <summary>The optional building name for the shipping destination, separate from street and locality fields.</summary>
    [BindProperty, StringLength(256)]
    public string? ShippingBuilding { get; set; }

    /// <summary>The first street-address line for the customer's shipping destination.</summary>
    [BindProperty, StringLength(256)]
    public string ShippingAddress1 { get; set; } = string.Empty;

    /// <summary>The optional second street-address line for the customer's shipping destination.</summary>
    [BindProperty, StringLength(256)]
    public string? ShippingAddress2 { get; set; }

    /// <summary>The city or district of the shipping destination.</summary>
    [BindProperty, StringLength(128)]
    public string? ShippingCity { get; set; }

    /// <summary>The state or province of the shipping destination.</summary>
    [BindProperty, StringLength(128)]
    public string? ShippingState { get; set; }

    /// <summary>The postal code of the shipping destination.</summary>
    [BindProperty, StringLength(50)]
    public string? ShippingPostalCode { get; set; }

    /// <summary>The shipping country's identifier selected from the loaded country catalogue.</summary>
    [BindProperty]
    public int ShippingCountryId { get; set; }

    /// <summary>The one-time outcome message carried across this workflow's post/redirect/get boundary.</summary>
    [TempData]
    public string? Notification { get; set; }

    /// <summary>The billing and shipping fields, country options, and safe address-validation feedback.</summary>
    public MemberAddressDisplayModel DisplayModel { get; private set; } = MemberAddressDisplayModel.Empty;

    /// <summary>Loads the owned address profile and country options for editing.</summary>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <returns>The populated address page or an authentication challenge.</returns>
    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var customerId = await sessionManager.GetCustomerDatabaseIdAsync(HttpContext, cancellationToken);
        if (customerId is null)
        {
            return Challenge();
        }

        var countriesTask = countryClient.GetCountriesAsync(cancellationToken);
        var profileTask = customerClient.GetAddressProfileAsync(customerId.Value, cancellationToken);
        await Task.WhenAll(countriesTask, profileTask);

        ApplyCountries(await countriesTask);
        var result = await profileTask;
        if (result.Profile is null)
        {
            ModelState.AddModelError(
                string.Empty,
                result.ServiceAvailable
                    ? "Your address profile could not be loaded."
                    : "Address service is temporarily unavailable.");
            BuildDisplayModel();
            return Page();
        }

        Apply(result.Profile.Customer.BillingAddress, billing: true);
        Apply(result.Profile.Customer.ShippingAddress, billing: false);
        BuildDisplayModel();
        return Page();
    }

    /// <summary>Validates both addresses against the current country catalogue and submits the owned address update.</summary>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <returns>A redirect after success, the form with safe errors, or an authentication challenge.</returns>
    public async Task<IActionResult> OnPostUpdateAddressAsync(CancellationToken cancellationToken)
    {
        var customerId = await sessionManager.GetCustomerDatabaseIdAsync(HttpContext, cancellationToken);
        if (customerId is null)
        {
            return Challenge();
        }

        ApplyCountries(await countryClient.GetCountriesAsync(cancellationToken));
        ValidateAddress(BillingAddress1, BillingCountryId, nameof(BillingAddress1), nameof(BillingCountryId), "billing");
        ValidateAddress(ShippingAddress1, ShippingCountryId, nameof(ShippingAddress1), nameof(ShippingCountryId), "shipping");
        if (!ModelState.IsValid)
        {
            BuildDisplayModel();
            return Page();
        }

        var result = await customerClient.UpdateAddressesAsync(
            customerId.Value,
            new CustomerAddressUpdate(
                new CustomerAddressInput(
                    NormalizeOptional(BillingBuilding),
                    BillingAddress1.Trim(),
                    NormalizeOptional(BillingAddress2),
                    NormalizeOptional(BillingCity),
                    NormalizeOptional(BillingState),
                    NormalizeOptional(BillingPostalCode),
                    BillingCountryId),
                new CustomerAddressInput(
                    NormalizeOptional(ShippingBuilding),
                    ShippingAddress1.Trim(),
                    NormalizeOptional(ShippingAddress2),
                    NormalizeOptional(ShippingCity),
                    NormalizeOptional(ShippingState),
                    NormalizeOptional(ShippingPostalCode),
                    ShippingCountryId)),
            cancellationToken);
        if (result.Succeeded)
        {
            Notification = "Address successfully updated";
            return RedirectToPage();
        }

        ModelState.AddModelError(
            string.Empty,
            result.ServiceAvailable
                ? "The address could not be updated."
                : "Address service is temporarily unavailable.");
        BuildDisplayModel();
        return Page();
    }

    private void ApplyCountries(ServiceResponse<IReadOnlyList<Country>> result)
    {
        Countries = result.Value ?? [];
        CountrySelectionReady = result.ServiceAvailable && Countries.Count > 0;
        if (!CountrySelectionReady)
        {
            ModelState.AddModelError(string.Empty, "Country list is temporarily unavailable.");
        }
    }

    private void Apply(CustomerAddress? address, bool billing)
    {
        if (address is null)
        {
            return;
        }

        if (billing)
        {
            BillingBuilding = address.Building;
            BillingAddress1 = address.AddressLine1;
            BillingAddress2 = address.AddressLine2;
            BillingCity = address.City;
            BillingState = address.State;
            BillingPostalCode = address.PostalCode;
            BillingCountryId = address.CountryId;
            return;
        }

        ShippingBuilding = address.Building;
        ShippingAddress1 = address.AddressLine1;
        ShippingAddress2 = address.AddressLine2;
        ShippingCity = address.City;
        ShippingState = address.State;
        ShippingPostalCode = address.PostalCode;
        ShippingCountryId = address.CountryId;
    }

    private void ValidateAddress(
        string addressLine1,
        int countryId,
        string addressField,
        string countryField,
        string label)
    {
        if (string.IsNullOrWhiteSpace(addressLine1))
        {
            ModelState.AddModelError(addressField, $"Address line 1 is required for the {label} address.");
        }

        if (CountrySelectionReady
            && (countryId <= 0 || Countries.All(country => country.Id != countryId)))
        {
            ModelState.AddModelError(countryField, $"Country must be selected for the {label} address.");
        }
    }

    private void BuildDisplayModel()
    {
        DisplayModel = new MemberAddressDisplayModel(
            new MemberAddressFieldsDisplayModel(
                BillingBuilding,
                BillingAddress1,
                BillingAddress2,
                BillingCity,
                BillingState,
                BillingPostalCode,
                BillingCountryId),
            new MemberAddressFieldsDisplayModel(
                ShippingBuilding,
                ShippingAddress1,
                ShippingAddress2,
                ShippingCity,
                ShippingState,
                ShippingPostalCode,
                ShippingCountryId),
            Countries.Select(country => new MemberAddressCountryOption(country.Id, country.Name)).ToArray(),
            Notification,
            ProjectSafeErrors());
    }

    private IReadOnlyList<MemberAddressError> ProjectSafeErrors() => ModelState
        .Where(entry => entry.Value is not null)
        .SelectMany(entry => entry.Value!.Errors.Select(error => ProjectSafeError(entry.Key, error)))
        .Distinct()
        .ToArray();

    private MemberAddressError ProjectSafeError(string field, ModelError error) => field switch
    {
        nameof(BillingAddress1) when string.IsNullOrWhiteSpace(BillingAddress1) => new(
            field,
            "Address line 1 is required for the billing address."),
        nameof(ShippingAddress1) when string.IsNullOrWhiteSpace(ShippingAddress1) => new(
            field,
            "Address line 1 is required for the shipping address."),
        nameof(BillingCountryId) => new(field, "Country must be selected for the billing address."),
        nameof(ShippingCountryId) => new(field, "Country must be selected for the shipping address."),
        "" when error.Exception is null && error.ErrorMessage is
            "Your address profile could not be loaded."
            or "Address service is temporarily unavailable."
            or "Country list is temporarily unavailable."
            or "The address could not be updated." => new(null, error.ErrorMessage),
        _ when AddressFields.Contains(field) => new(field, "One or more address values are invalid."),
        _ => new(null, "One or more address values are invalid."),
    };

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static readonly HashSet<string> AddressFields =
    [
        nameof(BillingBuilding),
        nameof(BillingAddress1),
        nameof(BillingAddress2),
        nameof(BillingCity),
        nameof(BillingState),
        nameof(BillingPostalCode),
        nameof(BillingCountryId),
        nameof(ShippingBuilding),
        nameof(ShippingAddress1),
        nameof(ShippingAddress2),
        nameof(ShippingCity),
        nameof(ShippingState),
        nameof(ShippingPostalCode),
        nameof(ShippingCountryId),
    ];
}
