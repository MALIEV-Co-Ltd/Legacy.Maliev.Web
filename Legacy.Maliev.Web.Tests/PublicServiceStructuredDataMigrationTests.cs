using System.Net;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Legacy.Maliev.Web.Tests;

public sealed partial class PublicServiceStructuredDataMigrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public PublicServiceStructuredDataMigrationTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory.WithWebHostBuilder(builder => builder.UseSetting("environment", "Testing"));
    }

    [Fact]
    public void ServiceRoutes_UseDisplayOnlyStaticStructuredDataComponent()
    {
        var root = FindRepositoryRoot();
        var web = Path.Combine(root, "Legacy.Maliev.Web");
        var routes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["CNC-Machining.cshtml"] = "CNC Machining",
            ["3D-Printing.cshtml"] = "3D Printing",
            ["3D-Scanning.cshtml"] = "3D Scanning",
            ["Custom-Manufacturing.cshtml"] = "Custom Manufacturing"
        };

        foreach (var route in routes)
        {
            var page = File.ReadAllText(Path.Combine(web, "Pages", "Services", route.Key));
            Assert.Contains("type=\"typeof(PublicServiceStructuredData)\"", page, StringComparison.Ordinal);
            Assert.Contains("render-mode=\"Static\"", page, StringComparison.Ordinal);
            Assert.Contains($"PublicServiceStructuredDataDisplayModel.Create(\"{route.Value}\")", page, StringComparison.Ordinal);
            Assert.DoesNotContain("_SchemaService", page, StringComparison.Ordinal);
        }

        Assert.True(File.Exists(Path.Combine(web, "Components", "Metadata", "PublicServiceStructuredData.razor")));
        Assert.True(File.Exists(Path.Combine(web, "Components", "Metadata", "PublicServiceStructuredDataDisplayModel.cs")));
        Assert.False(File.Exists(Path.Combine(web, "Pages", "Shared", "_SchemaService.cshtml")));
    }

    [Theory]
    [InlineData("/services/cnc-machining", "en", "CNC Machining", "CNC Machining Services", "CNC machining for engineering metal and plastic parts from CAD or dimensioned drawings, with material, quantity, critical features, and intended use reviewed before scope is confirmed.", "https://www.maliev.com/services/cnc-machining", "https://www.maliev.com/src/images/services/cnc/cnc-hero.webp")]
    [InlineData("/services/cnc-machining", "th", "CNC Machining", "บริการรับงาน CNC ตามแบบ", "ผลิตชิ้นส่วนโลหะและพลาสติกวิศวกรรมจากไฟล์ CAD หรือแบบระบุขนาด โดยตรวจสอบวัสดุ จำนวน จุดสำคัญ และการใช้งานก่อนยืนยันขอบเขตงาน", "https://www.maliev.com/services/cnc-machining", "https://www.maliev.com/src/images/services/cnc/cnc-hero.webp")]
    [InlineData("/services/3d-printing", "en", "3D Printing", "3D Printing Services", "FDM and resin 3D printing for prototypes and functional parts, with process and material selected from the file, intended use, quantity, and finish requirements.", "https://www.maliev.com/services/3d-printing", "https://www.maliev.com/src/images/services/printing/printing-hero.webp")]
    [InlineData("/services/3d-printing", "th", "3D Printing", "บริการรับพิมพ์ 3D", "ผลิตต้นแบบและชิ้นงานใช้งานด้วยกระบวนการ FDM หรือ Resin โดยเลือกกระบวนการและวัสดุจากไฟล์ การใช้งาน จำนวน และผิวที่ต้องการ", "https://www.maliev.com/services/3d-printing", "https://www.maliev.com/src/images/services/printing/printing-hero.webp")]
    [InlineData("/services/3d-scanning", "en", "3D Scanning", "3D Scanning Services", "3D geometry capture for raw scan data, reverse-engineering input, and deviation analysis, with feasibility and deliverables confirmed for each project.", "https://www.maliev.com/services/3d-scanning", "https://www.maliev.com/src/images/services/scanning/scanning-hero.webp")]
    [InlineData("/services/3d-scanning", "th", "3D Scanning", "บริการรับสแกน 3D", "เก็บข้อมูลรูปทรงสำหรับไฟล์สแกนดิบ Reverse Engineering และการเปรียบเทียบความคลาดเคลื่อน โดยยืนยันความเป็นไปได้และสิ่งส่งมอบตามแต่ละโครงการ", "https://www.maliev.com/services/3d-scanning", "https://www.maliev.com/src/images/services/scanning/scanning-hero.webp")]
    [InlineData("/services/custom-manufacturing", "en", "Custom Manufacturing", "Custom Part Manufacturing", "Manufacturing process selection for projects that may need CNC machining, 3D printing, or 3D scanning, based on the drawing or sample, material, quantity, critical features, and intended use.", "https://www.maliev.com/services/custom-manufacturing", null)]
    [InlineData("/services/custom-manufacturing", "th", "Custom Manufacturing", "รับผลิตชิ้นงานตามแบบ", "ช่วยกำหนดเส้นทางประเมินระหว่าง CNC, 3D Printing และ 3D Scanning จากแบบหรือตัวอย่าง วัสดุ จำนวน จุดสำคัญ และการใช้งาน ก่อนส่งต่อไปยังบริการเฉพาะทาง", "https://www.maliev.com/services/custom-manufacturing", null)]
    public async Task ServiceRoute_RendersLocalizedValidServiceJsonLd(
        string route,
        string culture,
        string serviceType,
        string name,
        string description,
        string url,
        string? image)
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });
        using var response = await client.GetAsync($"{route}?culture={culture}");
        var source = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("data-migration-component=\"public-service-structured-data\"", source, StringComparison.Ordinal);

        using var schema = FindServiceSchema(source);
        var root = schema.RootElement;
        Assert.Equal("https://schema.org", root.GetProperty("@context").GetString());
        Assert.Equal("Service", root.GetProperty("@type").GetString());
        Assert.Equal(serviceType, root.GetProperty("serviceType").GetString());
        Assert.Equal(name, root.GetProperty("name").GetString());
        Assert.Equal(description, root.GetProperty("description").GetString());
        Assert.Equal(url, root.GetProperty("url").GetString());

        var provider = root.GetProperty("provider");
        Assert.Equal("LocalBusiness", provider.GetProperty("@type").GetString());
        Assert.Equal("https://www.maliev.com/#organization", provider.GetProperty("@id").GetString());
        Assert.Equal("Maliev Co., Ltd.", provider.GetProperty("name").GetString());

        var areaServed = root.GetProperty("areaServed");
        Assert.Equal("Country", areaServed.GetProperty("@type").GetString());
        Assert.Equal("Thailand", areaServed.GetProperty("name").GetString());

        if (image is null)
        {
            Assert.False(root.TryGetProperty("image", out _));
        }
        else
        {
            Assert.Equal(image, root.GetProperty("image").GetString());
        }
    }

    [Theory]
    [InlineData(null, "CNC Machining")]
    [InlineData("Unknown Service", "Unknown Service")]
    [InlineData(" ", " ")]
    public void DisplayModel_PreservesLegacyCncFallback(string? requestedService, string expectedServiceType)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en");
        try
        {
            var model = Legacy.Maliev.Web.Components.Metadata.PublicServiceStructuredDataDisplayModel.Create(requestedService);

            using var schema = JsonDocument.Parse(model.ServiceJson);
            var root = schema.RootElement;
            Assert.Equal(expectedServiceType, root.GetProperty("serviceType").GetString());
            Assert.Equal("CNC Machining Services", root.GetProperty("name").GetString());
            Assert.Equal("https://www.maliev.com/services/cnc-machining", root.GetProperty("url").GetString());
            Assert.Equal("https://www.maliev.com/src/images/services/cnc/cnc-hero.webp", root.GetProperty("image").GetString());
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    private static JsonDocument FindServiceSchema(string source)
    {
        foreach (Match match in JsonLdScriptRegex().Matches(source))
        {
            var document = JsonDocument.Parse(match.Groups["json"].Value);
            if (document.RootElement.TryGetProperty("@type", out var type) && type.GetString() == "Service")
            {
                return document;
            }

            document.Dispose();
        }

        throw new Xunit.Sdk.XunitException("Rendered Service JSON-LD was not found.");
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

    [GeneratedRegex("<script[^>]*type=\\\"application/ld\\+json\\\"[^>]*>(?<json>.*?)</script>", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex JsonLdScriptRegex();
}
