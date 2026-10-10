using System.Net;
using Legacy.Maliev.Web.Application;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Legacy.Maliev.Web.Tests;

public sealed class EmailConfirmationStaticSsrRouteTests : IClassFixture<TestingWebApplicationFactory>
{
    private readonly WebApplicationFactory<Program> factory;

    public EmailConfirmationStaticSsrRouteTests(TestingWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    public static IEnumerable<object?[]> MissingEmailCases()
    {
        foreach (var enabled in new[] { true, false })
        {
            foreach (var culture in new[] { "en", "th" })
            {
                foreach (var email in new string?[] { null, "", " \t " })
                {
                    yield return [enabled, culture, email];
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(MissingEmailCases))]
    public async Task MissingEmail_RendersLocalizedInvalidRequestWithoutAuthentication(bool enabled, string culture, string? email)
    {
        var authentication = new RecordingAuthenticationClient(false);
        await using var host = ConfirmationHost(enabled, authentication);
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });
        var url = $"/account/emailconfirmation?culture={culture}&token=confirmation-token";
        if (email is not null) url += "&email=" + Uri.EscapeDataString(email);
        using var response = await client.GetAsync(url);
        var source = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Equal(0, authentication.Calls);
        Assert.Contains("data-migration-component=\"email-confirmation-content\"", source, StringComparison.Ordinal);
        Assert.Contains(culture == "th" ? "ลิงก์ยืนยันไม่ถูกต้องหรือหมดอายุแล้ว" : "The confirmation link is invalid or expired.", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Email confirmed. You can now sign in.", source, StringComparison.Ordinal);
        Assert.DoesNotContain("confirmation-token", source, StringComparison.Ordinal);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal("no-referrer", Assert.Single(response.Headers.GetValues("Referrer-Policy")));
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public async Task ValidInput_PreservesAuthenticationResultAndExactArguments(bool enabled, bool accepted)
    {
        var authentication = new RecordingAuthenticationClient(accepted);
        await using var host = ConfirmationHost(enabled, authentication);
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });
        using var response = await client.GetAsync("/account/emailconfirmation?culture=en&email=user%40example.test&token=confirmation-token");
        Assert.Equal(1, authentication.Calls);
        Assert.Equal("user@example.test", authentication.Email);
        Assert.Equal("confirmation-token", authentication.Token);
        Assert.Equal(accepted ? HttpStatusCode.Redirect : HttpStatusCode.OK, response.StatusCode);
        if (accepted)
        {
            Assert.Equal("/Account/Login?email=user%40example.test", response.Headers.Location?.OriginalString);
        }
        else
        {
            Assert.Null(response.Headers.Location);
            Assert.Contains("The confirmation link is invalid or expired.", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }
    }

    private WebApplicationFactory<Program> ConfirmationHost(bool enabled, RecordingAuthenticationClient authentication) =>
        factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("BlazorRouting:EmailConfirmation", enabled.ToString());
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ICustomerAuthenticationClient>();
                services.AddSingleton<ICustomerAuthenticationClient>(authentication);
            });
        });

    private sealed class RecordingAuthenticationClient(bool accepted) : ICustomerAuthenticationClient
    {
        public int Calls { get; private set; }
        public string? Email { get; private set; }
        public string? Token { get; private set; }
        public Task<bool> CompleteEmailConfirmationAsync(string email, string token, CancellationToken cancellationToken)
        {
            Calls++;
            Email = email;
            Token = token;
            return Task.FromResult(accepted);
        }

        public Task<CustomerSelfIdentityResult> GetSelfIdentityAsync(string accessToken, int expectedCustomerId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CustomerAuthenticationResult> LoginAsync(string email, string password, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CustomerAuthenticationResult> RefreshAsync(string refreshToken, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task RevokeAsync(string refreshToken, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CustomerIdentityRegistration> RegisterAsync(int databaseId, string email, string password, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CustomerActionChallenge> RequestEmailConfirmationAsync(string email, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CustomerActionChallenge> RequestPasswordResetAsync(string email, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CustomerEmailChangeValidationResult> ValidateEmailChangeAsync(string email, string token, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CustomerEmailChangeCompletionResult> CompleteEmailChangeAsync(string email, string token, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> CompletePasswordResetAsync(string email, string token, string password, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CustomerCredentialOperationResult> ChangeEmailAsync(string accessToken, string currentPassword, string newEmail, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CustomerCredentialOperationResult> ChangePasswordAsync(string accessToken, string currentPassword, string newPassword, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    [Fact]
    public void Host_DeclaresTheRateLimitedEmailConfirmationRouteAndRetainsRollbackSource()
    {
        var root = FindRepositoryRoot();
        var web = Path.Combine(root, "Legacy.Maliev.Web");
        var program = File.ReadAllText(Path.Combine(web, "Program.cs"));
        var appsettings = File.ReadAllText(Path.Combine(web, "appsettings.json"));
        var route = File.ReadAllText(Path.Combine(web, "Components", "Pages", "Account", "EmailConfirmationPage.razor"));
        var fallback = File.ReadAllText(Path.Combine(web, "Pages", "Account", "EmailConfirmation.cshtml"));

        Assert.Contains("BlazorRouting:EmailConfirmation", program, StringComparison.Ordinal);
        Assert.Contains("\"EmailConfirmation\": true", appsettings, StringComparison.Ordinal);
        Assert.Contains("EnableRateLimiting(\"account\")", route, StringComparison.Ordinal);
        Assert.Contains("type=\"typeof(EmailConfirmationContent)\"", fallback, StringComparison.Ordinal);
        Assert.DoesNotContain("value=\"@Token\"", route, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DisabledEmailConfirmationRoute_UsesTheRetainedRazorFallback()
    {
        await using var fallbackFactory = factory.WithWebHostBuilder(builder => builder.UseSetting("BlazorRouting:EmailConfirmation", "false"));
        using var client = fallbackFactory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        var source = await client.GetStringAsync(
            "/account/emailconfirmation?email=user%40example.com&token=invalid-token&culture=en");

        Assert.Contains("data-migration-renderer=\"blazor-static-ssr\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("data-migration-route-owner=\"blazor-static-ssr\"", source, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Web.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
