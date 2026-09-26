using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Legacy.Maliev.Web.Tests;

public sealed class ThreeDimensionalPrintingCtrDecisionTests : IClassFixture<TestingWebApplicationFactory>
{
    private readonly WebApplicationFactory<Program> factory;

    public ThreeDimensionalPrintingCtrDecisionTests(TestingWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task DecisionPack_PreservesSourceSnapshotAndBlocksPrematureMetadataPublication()
    {
        var root = FindRepositoryRoot();
        var document = File.ReadAllText(Path.Combine(root, "docs", "seo", "2026-09-26-3d-printing-ctr-decision-pack.md"));

        Assert.Contains("https://www.maliev.com/services/3d-printing", document, StringComparison.Ordinal);
        Assert.Contains("2026-09-23", document, StringComparison.Ordinal);
        Assert.Contains("2026-10-19/20", document, StringComparison.Ordinal);
        Assert.Contains("quote_route_selected", document, StringComparison.Ordinal);
        Assert.Contains("route=instant", document, StringComparison.Ordinal);
        Assert.Contains("route=engineering", document, StringComparison.Ordinal);
        Assert.Contains("persisted", document, StringComparison.Ordinal);
        Assert.Contains("no metadata publication", document, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("does not authorize production metadata publication", document, StringComparison.Ordinal);

        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/services/3d-printing?culture=th");
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("รับปริ้น 3D รับพิมพ์ 3 มิติ | ประเมินราคาออนไลน์ | MALIEV", html, StringComparison.Ordinal);
        Assert.DoesNotContain("รับปริ้น 3D รับพิมพ์ 3 มิติ | FDM เรซิ่น เริ่ม 1 ชิ้น | MALIEV", html, StringComparison.Ordinal);
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
