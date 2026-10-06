using System.Globalization;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Components.Pages.Account;
using Legacy.Maliev.Web.Infrastructure;
using Legacy.Maliev.Web.Pages.Account;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.RazorPages.Infrastructure;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;

namespace Legacy.Maliev.Web.Tests;

public sealed class PublicAccountWorkflowTests
{
    [Theory]
    [InlineData("en", "Reset your MALIEV password")]
    [InlineData("th", "รีเซ็ตรหัสผ่าน MALIEV ของคุณ")]
    public async Task ForgotPassword_EmailUsesCanonicalOriginInsteadOfRequestHost(string culture, string expectedSubject)
    {
        var originalCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            var notification = new RecordingNotificationClient();
            var page = new ForgotPassword(
                new AccountClientStub(),
                notification,
                new StringLocalizer<ForgotPasswordContent>(new ResourceManagerStringLocalizerFactory(
                    Options.Create(new LocalizationOptions { ResourcesPath = "Resources" }),
                    NullLoggerFactory.Instance)),
                NullLogger<ForgotPassword>.Instance)
            {
                Email = "customer@example.com",
            };
            Configure(page, "attacker.example");

            var result = Assert.IsType<RedirectToPageResult>(await page.OnPostPasswordResetAsync(CancellationToken.None));

            Assert.Contains("https://www.maliev.com/Account/ResetPassword", notification.Notification?.Body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("attacker.example", notification.Notification?.Body, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(expectedSubject, notification.Notification?.Subject);
            Assert.Equal(culture, result.RouteValues?["culture"]);
            Assert.Contains($"culture={culture}", notification.Notification?.Body, StringComparison.Ordinal);
            Assert.Equal(culture == "th"
                ? "หากพบบัญชีที่เข้าเงื่อนไข ระบบได้ส่งลิงก์รีเซ็ตรหัสผ่านแล้ว"
                : "If an eligible account exists, a password reset link has been sent.", page.Notification);
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalCulture;
        }
    }

    [Theory]
    [InlineData("en", "Email Confirmation")]
    [InlineData("th", "ยืนยันอีเมล")]
    public async Task LoginResend_EmailUsesCanonicalOriginInsteadOfRequestHost(string culture, string subject)
    {
        var originalCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            var notification = new RecordingNotificationClient();
            var page = new Login(
                new SessionManagerStub(),
                new AccountClientStub(),
                notification,
                new StringLocalizer<LoginContent>(new ResourceManagerStringLocalizerFactory(
                    Options.Create(new LocalizationOptions { ResourcesPath = "Resources" }),
                    NullLoggerFactory.Instance)),
                NullLogger<Login>.Instance)
            {
                Email = "customer@example.com",
                EmailConfirmationRecoveryToken = "opaque-recovery-token-12345678901234567890",
            };
            Configure(page, "attacker.example");

            var result = Assert.IsType<RedirectToPageResult>(await page.OnPostResendEmailConfirmationAsync(CancellationToken.None));

            Assert.Contains("https://www.maliev.com/Account/EmailConfirmation", notification.Notification?.Body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("attacker.example", notification.Notification?.Body, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(subject, notification.Notification?.Subject);
            Assert.Equal(culture, result.RouteValues?["culture"]);
            Assert.Contains($"culture={culture}", notification.Notification?.Body, StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalCulture;
        }
    }

    [Fact]
    public void LoginAccountCreated_PreservesEmailAndUsesFixedStatus()
    {
        var page = new Login(
            new SessionManagerStub(),
            new AccountClientStub(),
            new RecordingNotificationClient(),
            new EchoLocalizer<LoginContent>(),
            NullLogger<Login>.Instance);
        Configure(page, "attacker.example");

        page.OnGet(" customer@example.com ", null, accountCreated: true);

        Assert.Equal("customer@example.com", page.Email);
        Assert.Equal(
            "Account created. Check your email and follow the link to confirm your address before signing in.",
            page.Notification);
    }

    [Theory]
    [InlineData("en", "Unrecognized server validation message.")]
    [InlineData("th", "ข้อมูลที่ส่งมาไม่ถูกต้อง")]
    public void UnknownServerValidation_UsesSafeThaiFallbackAndRetainsEnglishCompatibility(
        string culture, string expected)
    {
        var originalCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            var page = new ForgotPassword(
                new AccountClientStub(),
                new RecordingNotificationClient(),
                new StringLocalizer<ForgotPasswordContent>(new ResourceManagerStringLocalizerFactory(
                    Options.Create(new LocalizationOptions { ResourcesPath = "Resources" }),
                    NullLoggerFactory.Instance)),
                NullLogger<ForgotPassword>.Instance);
            Configure(page, "attacker.example");
            page.ModelState.AddModelError("Email", "Unrecognized server validation message.");

            Assert.Equal(expected, page.DisplayModel.FirstErrorFor("Email"));
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalCulture;
        }
    }

    private static void Configure(PageModel page, string host)
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";
        context.Request.Host = new HostString(host);
        page.PageContext = new PageContext
        {
            HttpContext = context,
            RouteData = new RouteData(),
            ActionDescriptor = new CompiledPageActionDescriptor(),
        };
        page.Url = new UrlHelper(page.PageContext);
        page.TempData = new TempDataDictionary(context, new TempDataProviderStub());
    }

    private sealed class TempDataProviderStub : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    private sealed class EchoLocalizer<T> : IStringLocalizer<T>
    {
        public LocalizedString this[string name] => new(name, name);
        public LocalizedString this[string name, params object[] arguments] => new(name, string.Format(name, arguments));
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }

    private sealed class RecordingNotificationClient : INotificationClient
    {
        public EmailNotification? Notification { get; private set; }
        public Task<NotificationResult> SendAsync(NotificationChannel channel, EmailNotification notification, CancellationToken cancellationToken)
        {
            Notification = notification;
            return Task.FromResult(new NotificationResult(true, true, true));
        }
    }

    private sealed class SessionManagerStub : IAccountSessionManager
    {
        public Task<AccountSignInStatus> SignInAsync(HttpContext context, string email, string password, bool rememberMe, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SignOutAsync(HttpContext context, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<string?> GetAccessTokenAsync(HttpContext context, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int?> GetCustomerDatabaseIdAsync(HttpContext context, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class AccountClientStub : ICustomerAuthenticationClient
    {
        public Task<CustomerActionChallenge> RequestPasswordResetAsync(string email, CancellationToken cancellationToken) =>
            Task.FromResult(new CustomerActionChallenge(true, "opaque-reset-token", true, true));
        public Task<CustomerActionChallenge> RecoverEmailConfirmationAsync(string email, string recoveryToken, CancellationToken cancellationToken) =>
            Task.FromResult(new CustomerActionChallenge(true, "opaque-confirmation-token", true, true));
        public Task<CustomerSelfIdentityResult> GetSelfIdentityAsync(string accessToken, int expectedCustomerId, CancellationToken cancellationToken) =>
            Task.FromResult(new CustomerSelfIdentityResult(CustomerSelfIdentityStatus.Unavailable));

        public Task<CustomerAuthenticationResult> LoginAsync(string email, string password, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CustomerAuthenticationResult> RefreshAsync(string refreshToken, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task RevokeAsync(string refreshToken, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CustomerIdentityRegistration> RegisterAsync(int databaseId, string email, string password, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CustomerActionChallenge> RequestEmailConfirmationAsync(string email, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> CompleteEmailConfirmationAsync(string email, string token, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CustomerEmailChangeValidationResult> ValidateEmailChangeAsync(string email, string token, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CustomerEmailChangeCompletionResult> CompleteEmailChangeAsync(string email, string token, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> CompletePasswordResetAsync(string email, string token, string password, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CustomerCredentialOperationResult> ChangeEmailAsync(string accessToken, string currentPassword, string newEmail, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CustomerCredentialOperationResult> ChangePasswordAsync(string accessToken, string currentPassword, string newPassword, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
