using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Legacy.Maliev.Web.Tests;

public sealed class SignupStaticSsrRouteTests : IClassFixture<TestingWebApplicationFactory>
{
    private readonly WebApplicationFactory<Program> factory;

    public SignupStaticSsrRouteTests(TestingWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public void Host_DeclaresTheSignupRouteAndRetainsItsServerPostBoundary()
    {
        var root = FindRepositoryRoot();
        var web = Path.Combine(root, "Legacy.Maliev.Web");
        var program = File.ReadAllText(Path.Combine(web, "Program.cs"));
        var appsettings = File.ReadAllText(Path.Combine(web, "appsettings.json"));
        var fallback = File.ReadAllText(Path.Combine(web, "Pages", "Account", "Signup.cshtml"));
        var route = File.ReadAllText(Path.Combine(web, "Components", "Pages", "Account", "SignupPage.razor"));

        Assert.Contains("BlazorRouting:Signup", program, StringComparison.Ordinal);
        Assert.Contains("\"/Account/Signup\"", program, StringComparison.Ordinal);
        Assert.Contains("HttpMethodMetadata([\"POST\"])", program, StringComparison.Ordinal);
        Assert.Contains("\"Signup\": true", appsettings, StringComparison.Ordinal);
        Assert.Contains("type=\"typeof(SignupContent)\"", fallback, StringComparison.Ordinal);
        Assert.Contains("data-recaptcha-enterprise-form", route, StringComparison.Ordinal);
        Assert.DoesNotContain("IAntiBotVerifier", route, StringComparison.Ordinal);
        Assert.DoesNotContain("ICustomerProfileClient", route, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DisabledSignupRoute_UsesTheRetainedRazorFallback()
    {
        await using var fallbackFactory = factory.WithWebHostBuilder(builder => builder.UseSetting("BlazorRouting:Signup", "false"));
        using var client = fallbackFactory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        var source = await client.GetStringAsync("/account/signup?culture=en");

        Assert.Contains("data-migration-renderer=\"blazor-static-ssr\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("data-migration-route-owner=\"blazor-static-ssr\"", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ThaiDescription_PreservesSourceCopyOnActiveAndFallbackRoutes(bool rendererEnabled)
    {
        await using var routeFactory = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("BlazorRouting:Signup", rendererEnabled ? "true" : "false"));
        using var client = routeFactory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        using var response = await client.GetAsync("/account/signup?culture=th");
        var source = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<meta name=\"description\" content=\"สมัครบัญชี MALIEV เพื่อจัดการใบเสนอราคา คำสั่งซื้อ ไฟล์งาน และโปรเจ็คผลิตของคุณออนไลน์อย่างปลอดภัย\"", source, StringComparison.Ordinal);
        if (rendererEnabled)
        {
            Assert.Contains("data-migration-route-owner=\"blazor-static-ssr\"", source, StringComparison.Ordinal);
        }
        else
        {
            Assert.DoesNotContain("data-migration-route-owner=\"blazor-static-ssr\"", source, StringComparison.Ordinal);
        }
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
