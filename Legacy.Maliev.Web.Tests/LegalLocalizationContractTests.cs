using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Legacy.Maliev.Web.Tests;

public sealed class LegalLocalizationContractTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient client;

    public LegalLocalizationContractTests(WebApplicationFactory<Program> factory)
    {
        client = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Testing"))
            .CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                BaseAddress = new Uri("https://localhost"),
            });
    }

    [Theory]
    [MemberData(nameof(ThaiLegalDocuments))]
    public async Task ThaiLegalRoutes_RenderCompleteLocalizedDocuments(
        string route,
        string[] requiredThaiText,
        string[] forbiddenEnglishText)
    {
        using var response = await client.GetAsync($"{route}?culture=th");
        var content = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<html lang=\"th\">", content, StringComparison.Ordinal);
        Assert.All(requiredThaiText, text => Assert.Contains(text, content, StringComparison.Ordinal));
        Assert.All(forbiddenEnglishText, text => Assert.DoesNotContain(text, content, StringComparison.Ordinal));
        Assert.Contains("rel=\"canonical\"", content, StringComparison.Ordinal);
        Assert.Contains("hreflang=\"en\"", content, StringComparison.Ordinal);
        Assert.Contains("hreflang=\"th\"", content, StringComparison.Ordinal);
    }

    public static TheoryData<string, string[], string[]> ThaiLegalDocuments => new()
    {
        {
            "/legal/privacypolicy",
            [
                "วันที่มีผลบังคับใช้", "ประเภทข้อมูลที่เก็บรวบรวม", "ข้อมูลส่วนบุคคล",
                "ข้อมูลการใช้งาน", "ข้อมูลการติดตามและคุกกี้", "การใช้ข้อมูล",
                "การโอนข้อมูล", "การเปิดเผยข้อมูล", "ข้อกำหนดทางกฎหมาย",
                "ความปลอดภัยของข้อมูล", "ผู้ให้บริการ", "ลิงก์ไปยังเว็บไซต์อื่น",
                "ความเป็นส่วนตัวของเด็ก", "การเปลี่ยนแปลงนโยบายความเป็นส่วนตัว", "ติดต่อเรา",
            ],
            ["Types of Data Collected", "Transfer Of Data", "Children's Privacy"]
        },
        {
            "/legal/termsconditions",
            [
                "ข้อกำหนดและเงื่อนไข", "คุกกี้", "สิทธิการใช้งาน",
                "การเชื่อมโยงมายังเนื้อหาของเรา", "ไอเฟรม", "การสงวนสิทธิ์",
                "การนำลิงก์ออกจากเว็บไซต์ของเรา", "ความรับผิดต่อเนื้อหา", "ข้อจำกัดความรับผิด",
            ],
            ["Hyperlinking to our Content", "Reservation of Rights", "Content Liability"]
        },
        {
            "/legal/nondisclosureagreement",
            [
                "สัญญาปกปิดความลับ", "กฎหมายและการรักษาความลับ",
                "ปกป้องข้อมูลงานผลิตที่เป็นความลับ", "ดาวน์โหลดแบบฟอร์มสัญญาปกปิดความลับ",
            ],
            ["Protect confidential manufacturing information", "Download the template Confidentiality Agreement"]
        },
    };
}
