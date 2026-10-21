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

    [Theory]
    [InlineData(true, "en", "missing-email")]
    [InlineData(true, "th", "missing-email")]
    [InlineData(false, "en", "missing-email")]
    [InlineData(false, "th", "missing-email")]
    [InlineData(true, "en", "missing-token")]
    [InlineData(true, "th", "missing-token")]
    [InlineData(false, "en", "missing-token")]
    [InlineData(false, "th", "missing-token")]
    [InlineData(true, "en", "blank-input")]
    [InlineData(true, "th", "blank-input")]
    [InlineData(false, "en", "blank-input")]
    [InlineData(false, "th", "blank-input")]
    public async Task IncompleteLinks_RenderLocalizedInvalidStateWithoutAuthentication(
        bool useBlazor, string culture, string inputCase)
    {
        var authentication = new RejectUnexpectedAuthenticationClient();
        await using var routeFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("BlazorRouting:EmailConfirmation", useBlazor.ToString());
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ICustomerAuthenticationClient>();
                services.AddSingleton<ICustomerAuthenticationClient>(authentication);
            });
        });
        using var client = routeFactory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        var query = inputCase switch
        {
            "missing-email" => "token=private-confirmation-token-73",
            "missing-token" => "email=private-confirmation-email%40example.test",
            _ => "email=%20&token=%20",
        };

        using var response = await client.GetAsync($"/account/emailconfirmation?culture={culture}&{query}");
        var html = await response.Content.ReadAsStringAsync();
        var decoded = WebUtility.HtmlDecode(html);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal("no-referrer", Assert.Single(response.Headers.GetValues("Referrer-Policy")));
        Assert.Contains("data-migration-component=\"email-confirmation-content\"", html, StringComparison.Ordinal);
        Assert.Equal(useBlazor, html.Contains("data-migration-route-owner=\"blazor-static-ssr\"", StringComparison.Ordinal));
        Assert.Contains("role=\"alert\"", html, StringComparison.Ordinal);
        Assert.Contains(culture == "th"
            ? "ไม่สามารถยืนยันอีเมลได้"
            : "We could not confirm your email", decoded, StringComparison.Ordinal);
        Assert.Contains(culture == "th"
            ? "ลิงก์ยืนยันไม่ถูกต้องหรือหมดอายุแล้ว"
            : "The confirmation link is invalid or expired.", decoded, StringComparison.Ordinal);
        Assert.DoesNotContain("private-confirmation-token-73", decoded, StringComparison.Ordinal);
        Assert.DoesNotContain("private-confirmation-email", decoded, StringComparison.Ordinal);
        Assert.DoesNotContain("name=\"token\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, authentication.Calls);
    }

    private sealed class RejectUnexpectedAuthenticationClient : ICustomerAuthenticationClient
    {
        public int Calls { get; private set; }

        public Task<bool> CompleteEmailConfirmationAsync(string email, string token, CancellationToken cancellationToken)
        {
            Calls++;
            throw new InvalidOperationException("Incomplete input must not reach authentication.");
        }

        public Task<CustomerAuthenticationResult> LoginAsync(string email, string password, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CustomerAuthenticationResult> RefreshAsync(string refreshToken, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task RevokeAsync(string refreshToken, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CustomerIdentityRegistration> RegisterAsync(int databaseId, string email, string password, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CustomerActionChallenge> RequestEmailConfirmationAsync(string email, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CustomerActionChallenge> RecoverEmailConfirmationAsync(string email, string recoveryToken, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CustomerActionChallenge> RequestPasswordResetAsync(string email, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CustomerEmailChangeValidationResult> ValidateEmailChangeAsync(string email, string token, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CustomerEmailChangeCompletionResult> CompleteEmailChangeAsync(string email, string token, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> CompletePasswordResetAsync(string email, string token, string password, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CustomerCredentialOperationResult> ChangeEmailAsync(string accessToken, string currentPassword, string newEmail, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CustomerCredentialOperationResult> ChangePasswordAsync(string accessToken, string currentPassword, string newPassword, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CustomerSelfIdentityResult> GetSelfIdentityAsync(string accessToken, int expectedCustomerId, CancellationToken cancellationToken) => throw new NotSupportedException();
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
