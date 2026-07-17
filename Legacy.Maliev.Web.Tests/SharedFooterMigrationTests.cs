using System.Net;
using Legacy.Maliev.Web.Application;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Legacy.Maliev.Web.Tests;

public sealed class SharedFooterMigrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient client;

    public SharedFooterMigrationTests(WebApplicationFactory<Program> factory)
    {
        client = factory.WithWebHostBuilder(builder => builder.UseSetting("environment", "Testing")).CreateClient();
    }

    [Fact]
    public void SharedLayout_UsesStaticFooterComponentWithoutLegacyPartials()
    {
        var root = FindRepositoryRoot();
        var web = Path.Combine(root, "Legacy.Maliev.Web");
        var layoutPaths = new[]
        {
            Path.Combine(web, "Pages", "Shared", "_Layout.cshtml"),
            Path.Combine(web, "Areas", "Knowledges", "Pages", "Shared", "_LayoutKnowledges.cshtml"),
            Path.Combine(web, "Areas", "Member", "Pages", "Shared", "_LayoutMember.cshtml")
        };
        var footerPath = Path.Combine(web, "Components", "Layout", "PublicFooter.razor");
        var socialPath = Path.Combine(web, "Components", "Layout", "SocialLinks.razor");

        foreach (var layoutPath in layoutPaths)
        {
            var layout = File.ReadAllText(layoutPath);
            Assert.Contains("type=\"typeof(PublicFooter)\"", layout, StringComparison.Ordinal);
            Assert.Contains("render-mode=\"Static\"", layout, StringComparison.Ordinal);
            Assert.DoesNotContain("_FooterPartial", layout, StringComparison.Ordinal);
        }
        Assert.True(File.Exists(footerPath));
        Assert.True(File.Exists(socialPath));
        Assert.False(File.Exists(Path.Combine(web, "Pages", "Shared", "_FooterPartial.cshtml")));
        Assert.False(File.Exists(Path.Combine(web, "Pages", "Shared", "_SocialNetworksPartial.cshtml")));

        var footer = File.ReadAllText(footerPath);
        Assert.Contains("data-migration-component=\"public-footer\"", footer, StringComparison.Ordinal);
        Assert.Contains("data-migration-renderer=\"blazor-static-ssr\"", footer, StringComparison.Ordinal);
        Assert.Contains("IStringLocalizer<PublicFooter>", footer, StringComparison.Ordinal);
        Assert.Contains("<SocialLinks />", footer, StringComparison.Ordinal);
        Assert.DoesNotContain("@rendermode", footer, StringComparison.Ordinal);

        var styleEntry = File.ReadAllText(Path.Combine(web, "assets", "site-entry.css"));
        Assert.Contains("public-footer.css", styleEntry, StringComparison.Ordinal);
        Assert.DoesNotContain("<style>", footer, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(Path.Combine(
            web,
            "Resources",
            "Components",
            "Layout",
            "PublicFooter.th.resx")));
        Assert.False(File.Exists(Path.Combine(
            web,
            "Resources",
            "Pages",
            "Shared",
            "_FooterPartial.th.resx")));
    }

    [Theory]
    [InlineData("en", "Your trusted partner for CNC machining, 3D printing, and 3D scanning services.", "Business hours", "All rights reserved.")]
    [InlineData("th", "พันธมิตรที่คุณไว้วางใจสำหรับงาน CNC งานพิมพ์ 3 มิติ และงานสแกน 3 มิติ", "เวลาทำการ", "สงวนลิขสิทธิ์")]
    public async Task PublicFooter_RendersLocalizedAccessibleContactAndSocialLinks(
        string culture,
        string description,
        string businessHours,
        string rights)
    {
        using var response = await client.GetAsync($"/legal?culture={culture}");
        var source = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("data-migration-component=\"public-footer\"", source, StringComparison.Ordinal);
        Assert.Contains(description, source, StringComparison.Ordinal);
        Assert.Contains(businessHours, source, StringComparison.Ordinal);
        Assert.Contains(rights, source, StringComparison.Ordinal);
        Assert.Contains("href=\"mailto:info@maliev.com\"", source, StringComparison.Ordinal);
        Assert.Contains("href=\"tel:+66818030404\"", source, StringComparison.Ordinal);
        Assert.Contains("href=\"tel:+66898950690\"", source, StringComparison.Ordinal);
        Assert.Contains($"href=\"{SocialNetworks.Line}\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("facebook", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("messenger", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("target=\"_blank\" rel=\"noopener\"", source, StringComparison.Ordinal);
        Assert.Contains("<footer class=\"landing-footer\"", source, StringComparison.Ordinal);
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
