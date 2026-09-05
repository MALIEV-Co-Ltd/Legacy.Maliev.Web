using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Legacy.Maliev.Web.Tests;

public sealed partial class CustomManufacturingParityTests : IClassFixture<TestingWebApplicationFactory>
{
    private readonly WebApplicationFactory<Program> factory;

    public CustomManufacturingParityTests(TestingWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Theory]
    [InlineData(
        "en",
        "This page owns the initial manufacturing review; specialist pages own the process details.",
        "Target budget or cost constraint, delivery location, and required date",
        "the quotation confirms the proposed manufacturing route, scope, assumptions, price and lead time.")]
    [InlineData(
        "th",
        "หน้าผลิตชิ้นงานตามแบบเป็นจุดเริ่มต้นของการประเมิน ส่วนหน้าบริการเฉพาะทางเป็นแหล่งรายละเอียดของแต่ละกระบวนการ",
        "งบประมาณหรือข้อจำกัดด้านต้นทุน สถานที่จัดส่ง และวันที่ต้องการใช้ชิ้นงาน",
        "ใบเสนอราคาจะยืนยันแนวทางผลิต ขอบเขต สมมติฐาน ราคาและระยะเวลา")]
    public async Task CustomManufacturingRoute_DefinesOwnershipInputsAndQuotationOutput(
        string culture,
        string ownership,
        string procurementInputs,
        string quotationOutput)
    {
        using var client = this.factory.CreateClient();
        using var response = await client.GetAsync($"/services/custom-manufacturing?culture={culture}");
        var source = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(ownership, source, StringComparison.Ordinal);
        Assert.Contains(procurementInputs, source, StringComparison.Ordinal);
        Assert.Contains(quotationOutput, source, StringComparison.Ordinal);
        Assert.Contains("href=\"/Services/CNC-Machining\"", source, StringComparison.Ordinal);
        Assert.Contains("href=\"/Services/3D-Printing\"", source, StringComparison.Ordinal);
        Assert.Contains("href=\"/Services/3D-Scanning\"", source, StringComparison.Ordinal);
        Assert.Contains("href=\"/Quotation?item=custom-manufacturing\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CustomManufacturingRoute_RendersTheCurrentEvidenceRoutingAndPricingGuidance()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/services/custom-manufacturing?culture=en");
        var source = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("custom-manufacturing-story.webp", source, StringComparison.Ordinal);
        Assert.Contains("service-hero-media", source, StringComparison.Ordinal);
        Assert.Contains("Not sure which manufacturing process fits your part?", source, StringComparison.Ordinal);
        Assert.Contains("service-page-toc", source, StringComparison.Ordinal);
        Assert.Contains("Start with low-volume injection molding", source, StringComparison.Ordinal);
        Assert.Contains("Review low-volume injection molding", source, StringComparison.Ordinal);
        Assert.Contains("id=\"service-pricing\"", source, StringComparison.Ordinal);
        Assert.Contains("Indicative starting prices", source, StringComparison.Ordinal);
        Assert.Contains("Quote after project review", source, StringComparison.Ordinal);
        Assert.Contains("id=\"custom-faq-title\"", source, StringComparison.Ordinal);
        Assert.Equal(4, FaqDetailsRegex().Matches(source).Count);
        Assert.Equal(4, FaqSchemaQuestionRegex().Matches(source).Count);
        Assert.Contains("data-migration-route-owner=\"blazor-static-ssr\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CustomManufacturingSource_PreservesTheCurrentStoryAssetAndAccessibleSections()
    {
        var root = FindRepositoryRoot();
        var web = Path.Combine(root, "Legacy.Maliev.Web");
        var component = File.ReadAllText(Path.Combine(
            web,
            "Components",
            "Pages",
            "Services",
            "CustomManufacturingContent.razor"));

        Assert.Contains("service-page-toc", component, StringComparison.Ordinal);
        Assert.Contains("service-pricing-section", component, StringComparison.Ordinal);
        Assert.Contains("ประเมินโปรเจ็ค", component, StringComparison.Ordinal);
        Assert.Contains("ประเมินราคาหลังตรวจสอบโปรเจ็ค", component, StringComparison.Ordinal);
        Assert.DoesNotContain("ประเมินโครงการ", component, StringComparison.Ordinal);
        Assert.DoesNotContain("ตรวจสอบโครงการ", component, StringComparison.Ordinal);
        Assert.Contains("aria-labelledby=\"custom-faq-title\"", component, StringComparison.Ordinal);
        Assert.Contains("decoding=\"async\"", component, StringComparison.Ordinal);

        var image = Path.Combine(
            web,
            "wwwroot",
            "src",
            "images",
            "services",
            "custom-manufacturing",
            "custom-manufacturing-story.webp");
        Assert.True(File.Exists(image), $"Expected custom-manufacturing story asset '{image}'.");
        Assert.True(new FileInfo(image).Length > 0, $"Expected custom-manufacturing story asset '{image}' to be non-empty.");
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

    [GeneratedRegex("<details>", RegexOptions.CultureInvariant)]
    private static partial Regex FaqDetailsRegex();

    [GeneratedRegex("\\\"@type\\\":\\\"Question\\\"", RegexOptions.CultureInvariant)]
    private static partial Regex FaqSchemaQuestionRegex();
}
