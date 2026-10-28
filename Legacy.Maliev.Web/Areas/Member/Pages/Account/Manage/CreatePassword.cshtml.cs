using System.ComponentModel.DataAnnotations;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Components.Pages.Member;
using Legacy.Maliev.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace Legacy.Maliev.Web.Areas.Member.Pages.Account.Manage;

/// <summary>Creates an initial password for a signed-in account that does not already have one.</summary>
/// <param name="sessionManager">The server-side account session manager used to resolve identity and manage sign-in state.</param>
/// <param name="authenticationClient">The typed identity client handling customer credential and challenge operations.</param>
/// <param name="logger">The logger instance for diagnostic output.</param>
[Authorize]
[EnableRateLimiting("account")]
public sealed class CreatePassword(
    IAccountSessionManager sessionManager,
    ICustomerAuthenticationClient authenticationClient,
    ILogger<CreatePassword> logger) : PageModel
{
    /// <summary>The password submitted to the identity workflow; it is excluded from the form display projection.</summary>
    [BindProperty, Required, DataType(DataType.Password), StringLength(1024, MinimumLength = 8)]
    public string Password { get; set; } = string.Empty;

    /// <summary>The repeated password used to detect a mismatched password entry before submission.</summary>
    [BindProperty, Required, DataType(DataType.Password), StringLength(1024), Compare(nameof(Password), ErrorMessage = "Passwords do not match.")]
    public string ConfirmPassword { get; set; } = string.Empty;

    /// <summary>The safe initial-password validation feedback without password values.</summary>
    public MemberCreatePasswordDisplayModel DisplayModel { get; private set; } = MemberCreatePasswordDisplayModel.Empty;

    /// <summary>Requires a usable session before displaying the initial-password form.</summary>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <returns>The password-creation page or an authentication challenge.</returns>
    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var accessToken = await sessionManager.GetAccessTokenAsync(HttpContext, cancellationToken);
        if (string.IsNullOrWhiteSpace(accessToken)) return Challenge();
        BuildDisplayModel();
        return Page();
    }

    /// <summary>Creates the identity password when permitted and ends the current session after success.</summary>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <returns>A sign-in redirect, the form with safe errors, or an authentication challenge.</returns>
    public async Task<IActionResult> OnPostCreatePasswordAsync(CancellationToken cancellationToken)
    {
        var accessToken = await sessionManager.GetAccessTokenAsync(HttpContext, cancellationToken);
        if (string.IsNullOrWhiteSpace(accessToken)) return Challenge();
        if (!ModelState.IsValid) { BuildDisplayModel(); return Page(); }

        var result = await authenticationClient.CreatePasswordAsync(accessToken, Password, cancellationToken);
        if (!result.Authorized) return Challenge();
        if (!result.Succeeded)
        {
            var error = result.AlreadyExists
                ? "This account already has a password. Use change password instead."
                : "Account security is temporarily unavailable.";
            ModelState.AddModelError(string.Empty, error);
            logger.LogWarning("Customer password creation was rejected. AlreadyExists={AlreadyExists}; ServiceAvailable={ServiceAvailable}", result.AlreadyExists, result.ServiceAvailable);
            BuildDisplayModel();
            return Page();
        }

        await sessionManager.SignOutAsync(HttpContext, CancellationToken.None);
        return RedirectToPage("/Account/Login", new { area = string.Empty });
    }

    private void BuildDisplayModel() => DisplayModel = new MemberCreatePasswordDisplayModel(
        ModelState.Where(entry => entry.Value is not null)
            .SelectMany(entry => entry.Value!.Errors.Select(error => entry.Key switch
            {
                nameof(Password) when string.IsNullOrWhiteSpace(Password) => "New password is required.",
                nameof(Password) => "New password must contain at least 8 characters.",
                nameof(ConfirmPassword) when string.IsNullOrWhiteSpace(ConfirmPassword) => "Please confirm the new password.",
                nameof(ConfirmPassword) => "Passwords do not match.",
                "" when error.Exception is null && error.ErrorMessage is
                    "This account already has a password. Use change password instead."
                    or "Account security is temporarily unavailable." => error.ErrorMessage,
                _ => "One or more password values are invalid.",
            })).Distinct(StringComparer.Ordinal).ToArray());
}
