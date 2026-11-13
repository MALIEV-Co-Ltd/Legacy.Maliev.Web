using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Components.Pages.Member;
using Legacy.Maliev.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Legacy.Maliev.Web.Areas.Member.Pages.Account.Manage;

/// <summary>Loads and updates the signed-in customer's personal and company profile without changing identity credentials.</summary>
/// <param name="sessionManager">The server-side account session manager used to resolve identity and manage sign-in state.</param>
/// <param name="customerClient">The typed customer client used by this page to read or update customer details.</param>
[Authorize]
public sealed class Profile(
    IAccountSessionManager sessionManager,
    ICustomerAccountClient customerClient) : PageModel
{
    /// <summary>The customer's given name used in the account or request contact details.</summary>
    [BindProperty, Required, StringLength(250)]
    public string FirstName { get; set; } = string.Empty;

    /// <summary>The customer's family name used in the account or request contact details.</summary>
    [BindProperty, Required, StringLength(250)]
    public string LastName { get; set; } = string.Empty;

    /// <summary>The customer's email address used by this account or request workflow.</summary>
    public string Email { get; private set; } = string.Empty;

    /// <summary>The optional landline contact number supplied with the customer's details.</summary>
    [BindProperty, Phone, StringLength(250)]
    public string? Telephone { get; set; }

    /// <summary>The mobile contact number supplied with the customer's details.</summary>
    [BindProperty, Phone, StringLength(250)]
    public string? Mobile { get; set; }

    /// <summary>The optional fax number stored in the customer's profile.</summary>
    [BindProperty, Phone, StringLength(250)]
    public string? Fax { get; set; }

    /// <summary>The optional customer birth date entered in invariant yyyy-MM-dd form.</summary>
    [BindProperty]
    public string? DateOfBirth { get; set; }

    /// <summary>The company name associated with the customer's profile.</summary>
    [BindProperty, StringLength(250)]
    public string? CompanyName { get; set; }

    /// <summary>The optional tax identifier associated with the customer's billing or company details.</summary>
    [BindProperty, StringLength(250)]
    public string? TaxNumber { get; set; }

    /// <summary>The company registration information retained in the customer's profile.</summary>
    [BindProperty, StringLength(250)]
    public string? Registrar { get; set; }

    /// <summary>The one-time outcome message carried across this workflow's post/redirect/get boundary.</summary>
    [TempData]
    public string? Notification { get; set; }

    /// <summary>The customer profile fields, outcome message, and safe validation feedback.</summary>
    public MemberProfileDisplayModel DisplayModel { get; private set; } = MemberProfileDisplayModel.Empty;

    /// <summary>Loads the signed-in customer's editable profile and display projection.</summary>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <returns>The profile page or an authentication challenge.</returns>
    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var customerId = await sessionManager.GetCustomerDatabaseIdAsync(HttpContext, cancellationToken);
        if (customerId is null)
        {
            return Challenge();
        }

        var result = await LoadAsync(customerId.Value, cancellationToken);
        if (result is not null)
        {
            Apply(result);
        }

        BuildDisplayModel();

        return Page();
    }

    /// <summary>Validates profile fields and birth-date format before updating the owned customer profile.</summary>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <returns>A redirect after success, the form with safe errors, or an authentication challenge.</returns>
    public async Task<IActionResult> OnPostUpdateProfileAsync(CancellationToken cancellationToken)
    {
        var customerId = await sessionManager.GetCustomerDatabaseIdAsync(HttpContext, cancellationToken);
        if (customerId is null)
        {
            return Challenge();
        }

        DateTime? parsedDateOfBirth = null;
        if (!string.IsNullOrWhiteSpace(DateOfBirth))
        {
            if (DateOnly.TryParseExact(
                DateOfBirth,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date))
            {
                parsedDateOfBirth = date.ToDateTime(TimeOnly.MinValue);
            }
            else
            {
                ModelState.AddModelError(nameof(DateOfBirth), "One or more profile values are invalid.");
            }
        }

        if (!ModelState.IsValid)
        {
            var existing = await LoadAsync(customerId.Value, cancellationToken);
            Email = existing?.Email ?? string.Empty;
            BuildDisplayModel();
            return Page();
        }

        var result = await customerClient.UpdateProfileAsync(
            customerId.Value,
            new CustomerProfileUpdate(
                FirstName,
                LastName,
                Telephone,
                Mobile,
                Fax,
                parsedDateOfBirth,
                CompanyName,
                TaxNumber,
                Registrar),
            cancellationToken);
        if (result.Succeeded)
        {
            Notification = "Your profile was successfully updated.";
            return RedirectToPage();
        }

        var profile = await LoadAsync(customerId.Value, cancellationToken);
        Email = profile?.Email ?? string.Empty;
        ModelState.AddModelError(
            string.Empty,
            result.ServiceAvailable
                ? "Your profile could not be updated."
                : "Profile service is temporarily unavailable.");
        BuildDisplayModel();
        return Page();
    }

    private async Task<CustomerAccountDetails?> LoadAsync(
        int customerId,
        CancellationToken cancellationToken)
    {
        var result = await customerClient.GetProfileAsync(customerId, cancellationToken);
        if (result.Profile is null)
        {
            ModelState.AddModelError(
                string.Empty,
                result.ServiceAvailable
                    ? "Your profile could not be loaded."
                    : "Profile service is temporarily unavailable.");
        }

        return result.Profile;
    }

    private void Apply(CustomerAccountDetails profile)
    {
        FirstName = profile.FirstName;
        LastName = profile.LastName;
        Email = profile.Email;
        Telephone = profile.Telephone;
        Mobile = profile.Mobile;
        Fax = profile.Fax;
        DateOfBirth = profile.DateOfBirth?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        CompanyName = profile.Company?.Name;
        TaxNumber = profile.Company?.TaxNumber;
        Registrar = profile.Company?.Registrar;
    }

    private void BuildDisplayModel()
    {
        DisplayModel = new MemberProfileDisplayModel(
            FirstName,
            LastName,
            Email,
            Telephone,
            Mobile,
            Fax,
            DateOfBirth,
            CompanyName,
            TaxNumber,
            Registrar,
            Notification,
            ProjectSafeErrors());
    }

    private IReadOnlyList<string> ProjectSafeErrors() => ModelState
        .Where(entry => entry.Value is not null)
        .SelectMany(entry => entry.Value!.Errors.Select(error => entry.Key switch
        {
            nameof(FirstName) when string.IsNullOrWhiteSpace(FirstName) => "First name is required.",
            nameof(LastName) when string.IsNullOrWhiteSpace(LastName) => "Last name is required.",
            "" when error.Exception is null && !string.IsNullOrWhiteSpace(error.ErrorMessage) => error.ErrorMessage,
            _ => "One or more profile values are invalid.",
        }))
        .Distinct(StringComparer.Ordinal)
        .ToArray();
}
