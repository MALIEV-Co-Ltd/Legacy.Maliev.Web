using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Legacy.Maliev.Web.Tests;

public sealed class LoginStaticSsrRouteTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public LoginStaticSsrRouteTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory.WithWebHostBuilder(builder => builder.UseSetting("environment", "Testing"));
    }

    [Fact]
    public void Host_DeclaresTheLoginRouteAndRetainsItsRazorPostBoundary()
    {
        var root = FindRepositoryRoot();
        var web = Path.Combine(root, "Legacy.Maliev.Web");
        var routePath = Path.Combine(web, "Components", "Pages", "Account", "LoginPage.razor");

        Assert.True(File.Exists(routePath), $"Expected routed component '{routePath}'.");

        var program = File.ReadAllText(Path.Combine(web, "Program.cs"));
        var appsettings = File.ReadAllText(Path.Combine(web, "appsettings.json"));
        var fallback = File.ReadAllText(Path.Combine(web, "Pages", "Account", "Login.cshtml"));

        Assert.Contains("BlazorRouting:Login", program, StringComparison.Ordinal);
        Assert.Contains("HttpMethodMetadata([\"POST\"])", program, StringComparison.Ordinal);
        Assert.Contains("HttpMethodMetadata([\"GET\", \"HEAD\"])", program, StringComparison.Ordinal);
        Assert.Contains("\"Login\": true", appsettings, StringComparison.Ordinal);
        Assert.Contains("type=\"typeof(LoginContent)\"", fallback, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en", "Login | MALIEV", "Email", "Password", "Remember me", "Sign in")]
    [InlineData("th", "Login | MALIEV", "อีเมล์", "รหัสผ่าน", "จำข้อมูลไว้", "ล็อคอิน")]
    public async Task LoginGet_RendersLocalizedBlazorStaticSsrWithServerPostBoundary(
        string culture,
        string title,
        string emailLabel,
        string passwordLabel,
        string rememberLabel,
        string submitLabel)
    {
        using var client = CreateClient(factory);
        using var response = await client.GetAsync(
            $"/account/login?culture={culture}&email=user%40example.com&returnUrl=%2FMember%2FOrders");
        var source = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("<!DOCTYPE html>", source.TrimStart(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"<title>{title}</title>", source, StringComparison.Ordinal);
        Assert.Contains($">{emailLabel}<", source, StringComparison.Ordinal);
        Assert.Contains($">{passwordLabel}<", source, StringComparison.Ordinal);
        Assert.Contains(rememberLabel, source, StringComparison.Ordinal);
        Assert.Contains($">{submitLabel}<", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-route-owner=\"blazor-static-ssr\"", source, StringComparison.Ordinal);
        Assert.Contains("formaction=\"/Account/Login?handler=Login\"", source, StringComparison.Ordinal);
        Assert.Contains("name=\"__RequestVerificationToken\"", source, StringComparison.Ordinal);
        Assert.Contains("name=\"ReturnUrl\" value=\"/Member/Orders\"", source, StringComparison.Ordinal);
        Assert.Contains("name=\"Email\" value=\"user@example.com\"", source, StringComparison.Ordinal);
        Assert.Contains("name=\"robots\" content=\"noindex, follow\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("blazor.web.js", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("jquery", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DisabledLoginRoute_UsesTheRetainedRazorFallback()
    {
        await using var fallbackFactory = factory.WithWebHostBuilder(builder => builder.UseSetting("BlazorRouting:Login", "false"));
        using var client = CreateClient(fallbackFactory);
        var source = await client.GetStringAsync("/account/login?culture=en");

        Assert.Contains("data-migration-renderer=\"blazor-static-ssr\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("data-migration-route-owner=\"blazor-static-ssr\"", source, StringComparison.Ordinal);
    }

    private static HttpClient CreateClient(WebApplicationFactory<Program> sourceFactory) => sourceFactory.CreateClient(
        new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });

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
