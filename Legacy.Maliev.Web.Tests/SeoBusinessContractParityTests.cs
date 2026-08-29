using System.Xml.Linq;
using Legacy.Maliev.Web.Pages;
using Xunit;

namespace Legacy.Maliev.Web.Tests;

public sealed class SeoBusinessContractParityTests
{
    private static readonly IReadOnlyDictionary<string, (string Page, string Content)> PublicPages =
        new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase)
        {
            ["/"] = ("Home/HomePage.razor", "Home/HomeContent.razor"),
            ["/services"] = ("Services/ServicesPage.razor", "Services/ServicesContent.razor"),
            ["/services/custom-manufacturing"] = ("Services/CustomManufacturingPage.razor", "Services/CustomManufacturingContent.razor"),
            ["/services/3d-design"] = ("Services/ThreeDimensionalDesignPage.razor", "Services/ThreeDimensionalDesignContent.razor"),
            ["/services/silicone-casting"] = ("Services/SiliconeCastingPage.razor", "Services/SiliconeCastingContent.razor"),
            ["/services/low-volume-injection-molding"] = ("Services/LowVolumeInjectionMoldingPage.razor", "Services/LowVolumeInjectionMoldingContent.razor"),
            ["/services/cnc-machining"] = ("Services/CncMachiningPage.razor", "Services/CncMachiningContent.razor"),
            ["/services/3d-printing"] = ("Services/ThreeDimensionalPrintingPage.razor", "Services/ThreeDimensionalPrintingContent.razor"),
            ["/services/3d-scanning"] = ("Services/ThreeDimensionalScanningPage.razor", "Services/ThreeDimensionalScanningContent.razor"),
            ["/services/finishing-and-color"] = ("Services/FinishingAndColorPage.razor", "Services/FinishingAndColorPage.razor"),
            ["/about"] = ("About/AboutPage.razor", "About/AboutContent.razor"),
            ["/about/socialmedia"] = ("About/SocialMediaPage.razor", "About/SocialMediaContent.razor"),
            ["/contact"] = ("Contact/ContactPage.razor", "Contact/ContactHeroContent.razor"),
            ["/career"] = ("Career/CareerIndexPage.razor", "Career/CareerIndexContent.razor"),
            ["/quotation"] = ("Quotation/QuotationPage.razor", "Quotation/QuotationHeroContent.razor"),
            ["/instantquotation/3d-printing"] = ("InstantQuotation/InstantQuotationPage.razor", "InstantQuotation/InstantQuotationWorkflow.razor"),
            ["/knowledges"] = ("Knowledges/KnowledgeIndexPage.razor", "Knowledges/KnowledgeIndexContent.razor"),
            ["/knowledges/guidelines"] = ("Knowledges/GuidelinesPage.razor", "Knowledges/GuidelinesContent.razor"),
            ["/knowledges/workflow"] = ("Knowledges/WorkflowPage.razor", "Knowledges/WorkflowContent.razor"),
            ["/knowledges/specifications"] = ("Knowledges/Specifications/SpecificationsIndexPage.razor", "Knowledges/Specifications/SpecificationsIndexContent.razor"),
            ["/knowledges/specifications/cnc-machining"] = ("Knowledges/Specifications/CncMachiningSpecificationPage.razor", "Knowledges/Specifications/CncMachiningContent.razor"),
            ["/knowledges/specifications/3d-printing"] = ("Knowledges/Specifications/ThreeDimensionalPrintingSpecificationPage.razor", "Knowledges/Specifications/ThreeDimensionalPrintingContent.razor"),
            ["/knowledges/specifications/3d-scanning"] = ("Knowledges/Specifications/ThreeDimensionalScanningSpecificationPage.razor", "Knowledges/Specifications/ThreeDimensionalScanningContent.razor"),
            ["/legal"] = ("Legal/LegalPage.razor", "Legal/LegalContent.razor"),
            ["/legal/privacypolicy"] = ("Legal/PrivacyPolicyPage.razor", "Legal/PrivacyPolicyContent.razor"),
            ["/legal/termsconditions"] = ("Legal/TermsConditionsPage.razor", "Legal/TermsConditionsContent.razor"),
            ["/legal/nondisclosureagreement"] = ("Legal/NonDisclosureAgreementPage.razor", "Legal/NonDisclosureAgreementContent.razor"),
        };

    [Fact]
    public void EveryIndexedRouteHasAStaticSsrPageMetadataAndHeading()
    {
        foreach (var route in PublicSearchRouteCatalog.Routes)
        {
            Assert.True(PublicPages.TryGetValue(route.Path, out var files), $"Missing source mapping for {route.Path}.");
            var page = Read(files.Page);
            var content = Read(files.Content);

            Assert.Contains("@page", page, StringComparison.Ordinal);
            Assert.Contains("<PageTitle>", page, StringComparison.Ordinal);
            Assert.Contains("<HeadContent>", page, StringComparison.Ordinal);
            Assert.Contains("<h1", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("http://localhost", page + content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("127.0.0.1", page + content, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void BusinessServicePagesKeepThaiEnglishIntentAndQuotationPath()
    {
        var contracts = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["/"] = ["รับผลิตชิ้นส่วน", "Custom Manufacturing Services"],
            ["/services"] = ["บริการผลิตชิ้นส่วน", "Manufacturing services"],
            ["/services/custom-manufacturing"] = ["รับผลิตชิ้นงานตามแบบ", "Custom Part Manufacturing"],
            ["/services/3d-design"] = ["รับออกแบบ 3 มิติ", "3D Design"],
            ["/services/silicone-casting"] = ["รับหล่อซิลิโคน", "Silicone Casting"],
            ["/services/low-volume-injection-molding"] = ["รับฉีดพลาสติก", "Low-Volume Injection Molding"],
            ["/services/cnc-machining"] = ["รับงาน CNC", "CNC Machining"],
            ["/services/3d-printing"] = ["รับพิมพ์ 3D", "3D Printing"],
            ["/services/3d-scanning"] = ["รับสแกน 3D", "3D Scanning"],
            ["/services/finishing-and-color"] = ["การเก็บผิว", "Finishing"],
        };

        foreach (var contract in contracts)
        {
            Assert.True(PublicPages.TryGetValue(contract.Key, out var files));
            var source = Read(files.Page) + Environment.NewLine + Read(files.Content);
            foreach (var phrase in contract.Value)
            {
                Assert.Contains(phrase, source, StringComparison.OrdinalIgnoreCase);
            }

            if (contract.Key.StartsWith("/services/", StringComparison.Ordinal))
            {
                Assert.Contains("/quotation", source, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void ThreeDimensionalPrintingTitleRepresentsNationwideService()
    {
        var component = Read("Services/ThreeDimensionalPrintingPage.razor");
        var fallback = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "Legacy.Maliev.Web",
            "Pages",
            "Services",
            "3D-Printing.cshtml"));

        foreach (var source in new[] { component, fallback })
        {
            Assert.Contains("Custom 3D Printing in Thailand | FDM & Resin | MALIEV", source, StringComparison.Ordinal);
            Assert.Contains("รับพิมพ์ 3D ตามแบบ | FDM เรซิ่น จัดส่งทั่วไทย | MALIEV", source, StringComparison.Ordinal);
            var titleBoundary = source[..source.IndexOf("Description", StringComparison.OrdinalIgnoreCase)];
            Assert.DoesNotContain("Bangkok", titleBoundary, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("กรุงเทพ", titleBoundary, StringComparison.Ordinal);
            Assert.DoesNotContain("นนทบุรี", titleBoundary, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void RobotsAndSitemapKeepPrivateSurfacesOutOfSearch()
    {
        var robots = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Legacy.Maliev.Web", "wwwroot", "robots.txt"));
        Assert.Contains("User-agent: *", robots, StringComparison.Ordinal);
        Assert.Contains("Allow: /", robots, StringComparison.Ordinal);
        Assert.Contains("Sitemap: https://www.maliev.com/sitemap", robots, StringComparison.Ordinal);
        Assert.Contains("Disallow: /account/", robots, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Disallow: /member/", robots, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Disallow: /instantquotation/", robots, StringComparison.OrdinalIgnoreCase);

        var sitemap = XDocument.Parse(SitemapXmlRenderer.Render(PublicSearchRouteCatalog.Routes));
        XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
        var locations = sitemap.Descendants(ns + "loc").Select(element => element.Value).ToArray();
        Assert.Equal(PublicSearchRouteCatalog.Routes.Count, locations.Length);
        Assert.DoesNotContain(locations, location => location.Contains("/account/", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(locations, location => location.Contains("/member/", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(locations, location => location.Contains("?", StringComparison.Ordinal));
        Assert.Empty(sitemap.Descendants(ns + "lastmod"));
    }

    [Fact]
    public void StructuredDataAndOpenGraphStayOnTheOwnedSecureOrigin()
    {
        var root = FindRepositoryRoot();
        var source = string.Concat(
            File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Web", "Components", "Layout", "PublicOpenGraphMetadata.razor")),
            File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Web", "Components", "Layout", "PublicOpenGraphMetadataDisplayModel.cs")),
            File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Web", "Components", "Layout", "PublicBusinessStructuredData.razor")),
            File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Web", "Components", "Layout", "PublicBusinessStructuredDataDisplayModel.cs")),
            File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Web", "Components", "Layout", "PublicWebsiteStructuredData.razor")),
            File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Web", "Components", "Metadata", "PublicServiceStructuredDataDisplayModel.cs")));

        Assert.Contains(CanonicalUrlPolicy.CanonicalOrigin, source, StringComparison.Ordinal);
        Assert.DoesNotContain("http://", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("localhost", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("example.com", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("127.0.0.1", source, StringComparison.OrdinalIgnoreCase);
    }

    private static string Read(string relativePath) => File.ReadAllText(Path.Combine(
        FindRepositoryRoot(),
        "Legacy.Maliev.Web",
        "Components",
        "Pages",
        relativePath));

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
