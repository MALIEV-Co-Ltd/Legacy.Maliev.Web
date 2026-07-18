using System.ComponentModel.DataAnnotations;
using Legacy.Maliev.Web.Components.Pages.Account;
using Legacy.Maliev.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace Legacy.Maliev.Web.Pages.Account;

[EnableRateLimiting("account")]
public sealed class Login(IAccountSessionManager sessionManager) : PageModel
{
    [BindProperty]
    [Required]
    [EmailAddress]
    [StringLength(320)]
    public string Email { get; set; } = string.Empty;

    [BindProperty]
    [Required]
    [DataType(DataType.Password)]
    [StringLength(1024)]
    public string Password { get; set; } = string.Empty;

    [BindProperty]
    public bool RememberMe { get; set; }

    [BindProperty]
    public string? ReturnUrl { get; set; }

    [TempData]
    public string? Notification { get; set; }

    public LoginFormDisplayModel DisplayModel => new(
        Email,
        RememberMe,
        ReturnUrl,
        Notification,
        ModelState
            .Where(entry => entry.Value?.Errors.Count > 0)
            .ToDictionary(
                entry => entry.Key,
                entry => (IReadOnlyList<string>)entry.Value!.Errors
                    .Select(error => string.IsNullOrEmpty(error.ErrorMessage)
                        ? "The submitted value is invalid."
                        : error.ErrorMessage)
                    .ToArray(),
                StringComparer.Ordinal));

    public IActionResult OnGet(string? email, string? returnUrl)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return LocalRedirect("~/Account");
        }

        Email = email?.Trim() ?? string.Empty;
        ReturnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl : null;
        return Page();
    }

    public async Task<IActionResult> OnPostLoginAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var status = await sessionManager.SignInAsync(
            HttpContext,
            Email,
            Password,
            RememberMe,
            cancellationToken);
        if (status == AccountSignInStatus.Succeeded)
        {
            return Url.IsLocalUrl(ReturnUrl)
                ? LocalRedirect(ReturnUrl!)
                : LocalRedirect("~/Account");
        }

        ModelState.AddModelError(
            string.Empty,
            status == AccountSignInStatus.ServiceUnavailable
                ? "Sign in is temporarily unavailable. Please try again."
                : "The email or password is invalid, or the email has not been confirmed.");
        return Page();
    }
}
