using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Components.Pages.Member;
using Legacy.Maliev.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Legacy.Maliev.Web.Areas.Member.Pages.Account.Manage;

[Authorize]
public sealed class Profile(
    IAccountSessionManager sessionManager,
    ICustomerAccountClient customerClient) : PageModel
{
    [BindProperty, Required, StringLength(250)]
    public string FirstName { get; set; } = string.Empty;

    [BindProperty, Required, StringLength(250)]
    public string LastName { get; set; } = string.Empty;

    public string Email { get; private set; } = string.Empty;

    [BindProperty, Phone, StringLength(250)]
    public string? Telephone { get; set; }

    [BindProperty, Phone, StringLength(250)]
    public string? Mobile { get; set; }

    [BindProperty, Phone, StringLength(250)]
    public string? Fax { get; set; }

    [BindProperty]
    public string? DateOfBirth { get; set; }

    [BindProperty, StringLength(250)]
    public string? CompanyName { get; set; }

    [BindProperty, StringLength(250)]
    public string? TaxNumber { get; set; }

    [BindProperty, StringLength(250)]
    public string? Registrar { get; set; }

    [TempData]
    public string? Notification { get; set; }

    public MemberProfileDisplayModel DisplayModel { get; private set; } = MemberProfileDisplayModel.Empty;

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
