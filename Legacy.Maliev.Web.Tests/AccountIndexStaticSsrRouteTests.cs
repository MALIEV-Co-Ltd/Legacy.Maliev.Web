using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Legacy.Maliev.Web.Tests;

public sealed class AccountIndexStaticSsrRouteTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public AccountIndexStaticSsrRouteTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory.WithWebHostBuilder(builder => builder.UseSetting("environment", "Testing"));
    }

    [Fact]
    public void Host_DeclaresTheAccountIndexRouteAndRetainsItsRazorRollbackSource()
    {
        var root = FindRepositoryRoot();
        var web = Path.Combine(root, "Legacy.Maliev.Web");
        var routePath = Path.Combine(web, "Components", "Pages", "Account", "AccountIndexPage.razor");

        Assert.True(File.Exists(routePath), $"Expected routed component '{routePath}'.");

        var program = File.ReadAllText(Path.Combine(web, "Program.cs"));
        var appsettings = File.ReadAllText(Path.Combine(web, "appsettings.json"));
        var fallback = File.ReadAllText(Path.Combine(web, "Pages", "Account", "Index.cshtml"));
        var login = File.ReadAllText(Path.Combine(web, "Pages", "Account", "Login.cshtml.cs"));
        var signup = File.ReadAllText(Path.Combine(web, "Pages", "Account", "Signup.cshtml.cs"));

        Assert.Contains("BlazorRouting:AccountIndex", program, StringComparison.Ordinal);
        Assert.Contains("\"/Account/Index\"", program, StringComparison.Ordinal);
        Assert.Contains("\"AccountIndex\": true", appsettings, StringComparison.Ordinal);
        Assert.Contains("type=\"typeof(AccountIndexContent)\"", fallback, StringComparison.Ordinal);
        Assert.DoesNotContain("RedirectToPage(\"/Account/Index\")", login, StringComparison.Ordinal);
        Assert.DoesNotContain("RedirectToPage(\"/Account/Index\")", signup, StringComparison.Ordinal);
        Assert.Contains("LocalRedirect(\"~/Account\")", login, StringComparison.Ordinal);
        Assert.Contains("LocalRedirect(\"~/Account\")", signup, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en", "Member account | MALIEV", "Member workspace", "Review quotations, orders, project files, and account details.", "Sign in", "Create an account", "Reset password")]
    [InlineData("th", "บัญชีสมาชิก | MALIEV", "พื้นที่สมาชิก", "ตรวจสอบใบเสนอราคา คำสั่งซื้อ ไฟล์โครงการ และข้อมูลบัญชี", "เข้าสู่ระบบ", "สร้างบัญชี", "รีเซ็ตรหัสผ่าน")]
    public async Task AnonymousAccountIndex_RendersLocalizedStaticSsrWithoutSessionSecrets(
        string culture,
        string title,
        string workspaceLabel,
        string description,
        string signInLabel,
        string createAccountLabel,
        string resetPasswordLabel)
    {
        using var client = CreateClient(factory);
        using var response = await client.GetAsync($"/account?culture={culture}");
        var source = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("<!DOCTYPE html>", source.TrimStart(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"<html lang=\"{culture}\"", source, StringComparison.Ordinal);
        Assert.Contains($"<title>{title}</title>", source, StringComparison.Ordinal);
        Assert.Contains($">{workspaceLabel}<", source, StringComparison.Ordinal);
        Assert.Contains($">{description}<", source, StringComparison.Ordinal);
        Assert.Contains($">{signInLabel}<", source, StringComparison.Ordinal);
        Assert.Contains($">{createAccountLabel}<", source, StringComparison.Ordinal);
        Assert.Contains($">{resetPasswordLabel}<", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-route-owner=\"blazor-static-ssr\"", source, StringComparison.Ordinal);
        Assert.Contains("href=\"/Account/Login\"", source, StringComparison.Ordinal);
        Assert.Contains("href=\"/Account/Signup\"", source, StringComparison.Ordinal);
        Assert.Contains("href=\"/Account/ForgotPassword\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("action=\"/Account/Logout\"", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sensitive-access-token", source, StringComparison.Ordinal);
        Assert.DoesNotContain("sensitive-refresh-token", source, StringComparison.Ordinal);
        Assert.DoesNotContain("blazor.web.js", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("_framework/", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("jquery", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DisabledAccountIndexRoute_UsesTheRetainedRazorFallback()
    {
        await using var fallbackFactory = factory.WithWebHostBuilder(builder => builder.UseSetting("BlazorRouting:AccountIndex", "false"));
        using var client = CreateClient(fallbackFactory);
        var source = WebUtility.HtmlDecode(await client.GetStringAsync("/account?culture=en"));

        Assert.Contains("<title>Member account | MALIEV</title>", source, StringComparison.Ordinal);
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
