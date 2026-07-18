using System.ComponentModel.DataAnnotations;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Components.Pages.Member;
using Legacy.Maliev.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Legacy.Maliev.Web.Areas.Member.Pages.Account.Manage;

[Authorize]
public sealed class Address(
    IAccountSessionManager sessionManager,
    ICustomerAccountClient customerClient,
    ICountryClient countryClient) : PageModel
{
    private IReadOnlyList<Country> Countries { get; set; } = [];

    private bool CountrySelectionReady { get; set; }

    [BindProperty, StringLength(256)]
    public string? BillingBuilding { get; set; }

    [BindProperty, StringLength(256)]
    public string BillingAddress1 { get; set; } = string.Empty;

    [BindProperty, StringLength(256)]
    public string? BillingAddress2 { get; set; }

    [BindProperty, StringLength(128)]
    public string? BillingCity { get; set; }

    [BindProperty, StringLength(128)]
    public string? BillingState { get; set; }

    [BindProperty, StringLength(50)]
    public string? BillingPostalCode { get; set; }

    [BindProperty]
    public int BillingCountryId { get; set; }

    [BindProperty, StringLength(256)]
    public string? ShippingBuilding { get; set; }

    [BindProperty, StringLength(256)]
    public string ShippingAddress1 { get; set; } = string.Empty;

    [BindProperty, StringLength(256)]
    public string? ShippingAddress2 { get; set; }

    [BindProperty, StringLength(128)]
    public string? ShippingCity { get; set; }

    [BindProperty, StringLength(128)]
    public string? ShippingState { get; set; }

    [BindProperty, StringLength(50)]
    public string? ShippingPostalCode { get; set; }

    [BindProperty]
    public int ShippingCountryId { get; set; }

    [TempData]
    public string? Notification { get; set; }

    public MemberAddressDisplayModel DisplayModel { get; private set; } = MemberAddressDisplayModel.Empty;

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
