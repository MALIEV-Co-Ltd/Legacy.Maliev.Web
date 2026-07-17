using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Legacy.Maliev.Web.Tests;

public sealed class BlazorHostFoundationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient client;

    public BlazorHostFoundationTests(WebApplicationFactory<Program> factory)
    {
        client = factory.WithWebHostBuilder(builder => builder.UseSetting("environment", "Testing"))
            .CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                BaseAddress = new Uri("https://localhost")
            });
    }

    [Fact]
    public void Program_RegistersStaticSsrHostWithoutInteractiveInfrastructureAndOnlyTheApprovedRoute()
    {
        var root = FindRepositoryRoot();
        var web = Path.Combine(root, "Legacy.Maliev.Web");
        var program = File.ReadAllText(Path.Combine(web, "Program.cs"));
        var app = File.ReadAllText(Path.Combine(web, "Components", "App.razor"));
        var routes = File.ReadAllText(Path.Combine(web, "Components", "Routes.razor"));

        Assert.Contains("builder.Services.AddRazorComponents()", program, StringComparison.Ordinal);
        Assert.Contains("app.MapRazorComponents<App>()", program, StringComparison.Ordinal);
        Assert.DoesNotContain("AddInteractiveServerComponents", program, StringComparison.Ordinal);
        Assert.DoesNotContain("AddInteractiveWebAssemblyComponents", program, StringComparison.Ordinal);
        Assert.DoesNotContain("MapBlazorHub", program, StringComparison.Ordinal);
        Assert.Contains("<!DOCTYPE html>", app, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<HeadOutlet", app, StringComparison.Ordinal);
        Assert.Contains("<Routes />", app, StringComparison.Ordinal);
        Assert.Contains("<Router", routes, StringComparison.Ordinal);
        Assert.Contains("AppAssembly=\"typeof(Program).Assembly\"", routes, StringComparison.Ordinal);
        Assert.Contains("<RouteView", routes, StringComparison.Ordinal);

        var routedPages = Directory.EnumerateFiles(
                Path.Combine(web, "Components"),
                "*.razor",
                SearchOption.AllDirectories)
            .Where(path => File.ReadLines(path).Any(line => line.TrimStart().StartsWith("@page ", StringComparison.Ordinal)))
            .ToArray();
        Assert.Equal(8, routedPages.Length);
        Assert.Equal(
            ["CncMachiningPage.razor", "CustomManufacturingPage.razor", "GuidelinesPage.razor", "KnowledgeIndexPage.razor", "ServicesPage.razor", "ThreeDimensionalPrintingPage.razor", "ThreeDimensionalScanningPage.razor", "WorkflowPage.razor"],
            routedPages.Select(path => Path.GetFileName(path)!).Order(StringComparer.Ordinal).ToArray());
    }

    [Theory]
    [InlineData("en", "Legal information | MALIEV")]
    [InlineData("th", "ข้อมูลทางกฎหมาย | MALIEV")]
    public async Task ExistingLegalRoute_RemainsRazorOwnedWithCompleteStaticDocument(
        string culture,
        string title)
    {
        using var response = await client.GetAsync($"/legal?culture={culture}");
        var source = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains($"<title>{title}</title>", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-renderer=\"blazor-static-ssr\"", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-component=\"public-navigation\"", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-component=\"public-footer\"", source, StringComparison.Ordinal);
        Assert.Contains("GTM-KHDDLVRR", source, StringComparison.Ordinal);
        Assert.Contains("google-site-verification", source, StringComparison.Ordinal);
        Assert.Contains("rel=\"canonical\"", source, StringComparison.Ordinal);
        Assert.Contains("hreflang=\"th\"", source, StringComparison.Ordinal);
        Assert.Contains("hreflang=\"en\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("blazor.web.js", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MissingRoute_RemainsOwnedByExistingSafeErrorPipeline()
    {
        using var response = await client.GetAsync("/not-a-blazor-route?culture=en");
        var source = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("data-migration-component=\"error-content\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("blazor.web.js", source, StringComparison.OrdinalIgnoreCase);
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
