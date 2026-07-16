using System.ComponentModel.DataAnnotations;
using System.Net;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Components.Pages.Member;
using Legacy.Maliev.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;

namespace Legacy.Maliev.Web.Areas.Member.Pages.Account.Manage;

[Authorize]
[EnableRateLimiting("account")]
public sealed class ChangeEmail(
    IAccountSessionManager sessionManager,
    ICustomerAuthenticationClient authenticationClient,
    ICustomerAccountClient customerClient,
    INotificationClient notificationClient,
    ILogger<ChangeEmail> logger) : PageModel
{
    [BindProperty, Required, DataType(DataType.Password), StringLength(1024)]
    public string CurrentPassword { get; set; } = string.Empty;

    [BindProperty, Required, EmailAddress, StringLength(320)]
    public string NewEmail { get; set; } = string.Empty;

    [TempData]
    public string? Notification { get; set; }

    public MemberChangeEmailDisplayModel DisplayModel { get; private set; } = MemberChangeEmailDisplayModel.Empty;

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var session = await GetOwnedSessionAsync(cancellationToken);
        if (session is null)
        {
            return Challenge();
        }

        if (await LoadCurrentEmailAsync(session.CustomerId, cancellationToken) is null)
        {
            BuildDisplayModel();
            return Page();
        }

        BuildDisplayModel();
        return Page();
    }

    public async Task<IActionResult> OnPostChangeEmailAsync(CancellationToken cancellationToken)
    {
        var session = await GetOwnedSessionAsync(cancellationToken);
        if (session is null)
        {
            return Challenge();
        }

        var currentEmail = await LoadCurrentEmailAsync(session.CustomerId, cancellationToken);
        if (currentEmail is null)
        {
            BuildDisplayModel();
            return Page();
        }

        NewEmail = NewEmail?.Trim() ?? string.Empty;
        if (string.Equals(currentEmail, NewEmail, StringComparison.OrdinalIgnoreCase))
        {
            ModelState.AddModelError(nameof(NewEmail), "Enter a different email address.");
        }

        if (!ModelState.IsValid)
        {
            BuildDisplayModel();
            return Page();
        }

        var customerUpdate = await customerClient.UpdateEmailAsync(
            session.CustomerId,
            NewEmail,
            cancellationToken);
        if (!customerUpdate.Succeeded)
        {
            ModelState.AddModelError(string.Empty, "The email address could not be changed.");
            BuildDisplayModel();
            return Page();
        }

        CustomerCredentialOperationResult identityUpdate;
        try
        {
            identityUpdate = await authenticationClient.ChangeEmailAsync(
                session.AccessToken,
                CurrentPassword,
                NewEmail,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await RollBackProfileAsync(session.CustomerId, currentEmail);
            throw;
        }
        catch (Exception)
        {
            await RollBackProfileAsync(session.CustomerId, currentEmail);
            logger.LogWarning("Identity email change failed unexpectedly after the profile update.");
            ModelState.AddModelError(string.Empty, "Account security is temporarily unavailable.");
            BuildDisplayModel();
            return Page();
        }

        if (!identityUpdate.Succeeded || string.IsNullOrWhiteSpace(identityUpdate.Token))
        {
            await RollBackProfileAsync(session.CustomerId, currentEmail);
            if (!identityUpdate.Authorized)
            {
                await sessionManager.SignOutAsync(HttpContext, CancellationToken.None);
                return Challenge();
            }

            ModelState.AddModelError(
                string.Empty,
                identityUpdate.ServiceAvailable
                    ? "The current password is invalid or the email address is already in use."
                    : "Account security is temporarily unavailable.");
            BuildDisplayModel();
            return Page();
        }

        await sessionManager.SignOutAsync(HttpContext, CancellationToken.None);

        var callback = QueryHelpers.AddQueryString(
            $"{CanonicalUrlPolicy.CanonicalOrigin}/account/changeemailconfirmation",
            new Dictionary<string, string?>
            {
                ["email"] = NewEmail,
                ["token"] = identityUpdate.Token,
            });
        var sent = false;
        try
        {
            sent = (await notificationClient.SendAsync(
                NotificationChannel.NoReply,
                new EmailNotification(
                    NewEmail,
                    "Confirm your new MALIEV email address",
                    $"<p>Confirm your new email address using this single-use link:</p><p><a href=\"{WebUtility.HtmlEncode(callback)}\">Confirm email</a></p>",
                    null,
                    null,
                    ["mail-tracking@maliev.com"]),
                cancellationToken)).Sent;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            logger.LogWarning("Change-email confirmation delivery failed unexpectedly.");
        }

        Notification = sent
            ? "Check your new email address to confirm the change."
            : "Your email was changed, but confirmation delivery failed. Contact info@maliev.com.";
        return RedirectToPage("/Account/Login", new { area = string.Empty, email = NewEmail });
    }

    private async Task<OwnedSession?> GetOwnedSessionAsync(
        CancellationToken cancellationToken)
    {
        var accessToken = await sessionManager.GetAccessTokenAsync(HttpContext, cancellationToken);
        var customerId = await sessionManager.GetCustomerDatabaseIdAsync(HttpContext, cancellationToken);
        return string.IsNullOrWhiteSpace(accessToken) || customerId is null
            ? null
            : new OwnedSession(accessToken, customerId.Value);
    }

    private async Task<string?> LoadCurrentEmailAsync(
        int customerId,
        CancellationToken cancellationToken)
    {
        var profile = await customerClient.GetProfileAsync(customerId, cancellationToken);
        if (profile.Profile is not null && !string.IsNullOrWhiteSpace(profile.Profile.Email))
        {
            return profile.Profile.Email;
        }

        ModelState.AddModelError(
            string.Empty,
            profile.ServiceAvailable
                ? "Your account email could not be loaded."
                : "Customer profile service is temporarily unavailable.");
        return null;
    }

    private async Task RollBackProfileAsync(int customerId, string oldEmail)
    {
        try
        {
            var rolledBack = await customerClient.UpdateEmailAsync(
                customerId,
                oldEmail,
                CancellationToken.None);
            if (rolledBack.Succeeded)
            {
                return;
            }
        }
        catch (Exception)
        {
        }

        logger.LogCritical(
            "Customer profile {CustomerId} requires manual email reconciliation after identity change rejection.",
            customerId);
    }

    private void BuildDisplayModel()
    {
        DisplayModel = new MemberChangeEmailDisplayModel(NewEmail, ProjectSafeErrors());
    }

    private IReadOnlyList<string> ProjectSafeErrors() => ModelState
        .Where(entry => entry.Value is not null)
        .SelectMany(entry => entry.Value!.Errors.Select(error => entry.Key switch
        {
            nameof(CurrentPassword) when string.IsNullOrWhiteSpace(CurrentPassword) => "Current password is required.",
            nameof(NewEmail) when string.IsNullOrWhiteSpace(NewEmail) => "New email is required.",
            nameof(NewEmail) when error.ErrorMessage == "Enter a different email address." => error.ErrorMessage,
            nameof(NewEmail) => "Enter a valid email address.",
            "" when error.Exception is null && error.ErrorMessage is
                "The email address could not be changed."
                or "The current password is invalid or the email address is already in use."
                or "Account security is temporarily unavailable."
                or "Your account email could not be loaded."
                or "Customer profile service is temporarily unavailable." => error.ErrorMessage,
            _ => "One or more account values are invalid.",
        }))
        .Distinct(StringComparer.Ordinal)
        .ToArray();

    private sealed record OwnedSession(string AccessToken, int CustomerId);
}
