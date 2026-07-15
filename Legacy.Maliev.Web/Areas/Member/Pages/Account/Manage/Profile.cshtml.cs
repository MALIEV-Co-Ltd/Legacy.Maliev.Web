using System.ComponentModel.DataAnnotations;
using Legacy.Maliev.Web.Application;
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

    [BindProperty, DataType(DataType.Date)]
    public DateTime? DateOfBirth { get; set; }

    [BindProperty, StringLength(250)]
    public string? CompanyName { get; set; }

    [BindProperty, StringLength(250)]
    public string? TaxNumber { get; set; }

    [BindProperty, StringLength(250)]
    public string? Registrar { get; set; }

    [TempData]
    public string? Notification { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var result = await LoadAsync(cancellationToken);
        if (result is not null)
        {
            Apply(result);
        }

        return Page();
    }

    public async Task<IActionResult> OnPostUpdateProfileAsync(CancellationToken cancellationToken)
    {
        var customerId = await sessionManager.GetCustomerDatabaseIdAsync(HttpContext, cancellationToken);
        if (customerId is null)
        {
            return Challenge();
        }

        if (!ModelState.IsValid)
        {
            var existing = await LoadAsync(cancellationToken);
            Email = existing?.Email ?? string.Empty;
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
                DateOfBirth,
                CompanyName,
                TaxNumber,
                Registrar),
            cancellationToken);
        if (result.Succeeded)
        {
            Notification = "Your profile was successfully updated.";
            return RedirectToPage();
        }

        var profile = await LoadAsync(cancellationToken);
        Email = profile?.Email ?? string.Empty;
        ModelState.AddModelError(
            string.Empty,
            result.ServiceAvailable
                ? "Your profile could not be updated."
                : "Profile service is temporarily unavailable.");
        return Page();
    }

    private async Task<CustomerAccountDetails?> LoadAsync(CancellationToken cancellationToken)
    {
        var customerId = await sessionManager.GetCustomerDatabaseIdAsync(HttpContext, cancellationToken);
        if (customerId is null)
        {
            return null;
        }

        var result = await customerClient.GetProfileAsync(customerId.Value, cancellationToken);
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
        DateOfBirth = profile.DateOfBirth;
        CompanyName = profile.Company?.Name;
        TaxNumber = profile.Company?.TaxNumber;
        Registrar = profile.Company?.Registrar;
    }
}
