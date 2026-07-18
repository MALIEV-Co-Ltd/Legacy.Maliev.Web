using Microsoft.AspNetCore.Mvc.Testing;

namespace Legacy.Maliev.Web.Tests;

public sealed class MemberStaticSsrRouteOwnerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public MemberStaticSsrRouteOwnerTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory.WithWebHostBuilder(builder => builder.UseSetting("environment", "Testing"));
    }

    [Theory]
    [InlineData("MemberOverview", "/Member", "member-overview-content")]
    [InlineData("MemberAccountIndex", "/Member/Account", "member-account-index-content")]
    [InlineData("MemberOrdersIndex", "/Member/Orders", "member-orders-index-content")]
    public async Task DisabledMemberRoute_UsesRetainedAuthorizedRazorFallback(
        string flag,
        string route,
        string component)
    {
        await using var fallbackFactory = factory.WithWebHostBuilder(
            builder => builder.UseSetting($"BlazorRouting:{flag}", "false"));
        using var client = fallbackFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });

        using var response = await client.GetAsync(route);

        Assert.Equal(System.Net.HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Account/Login", response.Headers.Location?.AbsolutePath);
        var root = FindRepositoryRoot();
        Assert.Contains(
            $"data-migration-component=\"{component}\"",
            string.Join('\n', Directory.GetFiles(
                Path.Combine(root, "Legacy.Maliev.Web", "Components", "Pages", "Member"),
                "*Content.razor").Select(File.ReadAllText)),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Host_DeclaresServerAuthorizedMemberOwnersAndRetainsWriteBoundaries()
    {
        var root = FindRepositoryRoot();
        var web = Path.Combine(root, "Legacy.Maliev.Web");
        var program = File.ReadAllText(Path.Combine(web, "Program.cs"));
        var pages = Directory.GetFiles(
                Path.Combine(web, "Components", "Pages", "Member"),
                "*Page.razor")
            .Select(File.ReadAllText)
            .ToArray();

        Assert.Contains("BlazorRouting:MemberOverview", program, StringComparison.Ordinal);
        Assert.Contains("BlazorRouting:MemberAccountIndex", program, StringComparison.Ordinal);
        Assert.Contains("BlazorRouting:MemberOrdersIndex", program, StringComparison.Ordinal);
        Assert.All(pages, page => Assert.Contains("[Authorize]", page, StringComparison.Ordinal));
        Assert.DoesNotContain(pages, page => page.Contains("access_token", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("app.MapRazorPages();", program, StringComparison.Ordinal);
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
