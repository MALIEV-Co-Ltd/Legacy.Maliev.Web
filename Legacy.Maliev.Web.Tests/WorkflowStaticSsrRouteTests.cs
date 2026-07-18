using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Legacy.Maliev.Web.Tests;

public sealed partial class WorkflowStaticSsrRouteTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public WorkflowStaticSsrRouteTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory.WithWebHostBuilder(builder => builder.UseSetting("environment", "Testing"));
    }

    [Fact]
    public void Host_DeclaresTheWorkflowStaticSsrPageAndKeepsTheRazorRollbackSource()
    {
        var root = FindRepositoryRoot();
        var web = Path.Combine(root, "Legacy.Maliev.Web");
        var routePath = Path.Combine(web, "Components", "Pages", "Knowledges", "WorkflowPage.razor");

        Assert.True(File.Exists(routePath), $"Expected routed component '{routePath}'.");

        var program = File.ReadAllText(Path.Combine(web, "Program.cs"));
        var route = File.ReadAllText(routePath);
        var content = File.ReadAllText(Path.Combine(web, "Components", "Pages", "Knowledges", "WorkflowContent.razor"));
        var razorFallback = File.ReadAllText(Path.Combine(web, "Areas", "Knowledges", "Pages", "Workflow.cshtml"));

        Assert.Contains("BlazorRouting:KnowledgesWorkflow", program, StringComparison.Ordinal);
        Assert.Contains("AddAreaPageRouteModelConvention", program, StringComparison.Ordinal);
        Assert.Contains("\"Knowledges\"", program, StringComparison.Ordinal);
        Assert.Contains("\"/Workflow\"", program, StringComparison.Ordinal);
        Assert.Contains("model.Selectors.Clear()", program, StringComparison.Ordinal);
        Assert.Contains("@page \"/Knowledges/Workflow\"", route, StringComparison.Ordinal);
        Assert.Contains("RouteOwner=\"blazor-static-ssr\"", route, StringComparison.Ordinal);
        Assert.Contains("data-migration-route-owner=\"@RouteOwner\"", content, StringComparison.Ordinal);
        Assert.Contains("@page", razorFallback, StringComparison.Ordinal);
        Assert.Contains("type=\"typeof(WorkflowContent)\"", razorFallback, StringComparison.Ordinal);

        var routedPages = Directory.EnumerateFiles(
                Path.Combine(web, "Components"),
                "*.razor",
                SearchOption.AllDirectories)
            .Where(path => File.ReadLines(path).Any(line => line.TrimStart().StartsWith("@page ", StringComparison.Ordinal)))
            .Select(path => Path.GetFileName(path)!)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                "AboutPage.razor",
                "AccessDeniedPage.razor",
                "AccountIndexPage.razor",
                "CareerDetailPage.razor",
                "CareerIndexPage.razor",
                "ChangeEmailConfirmationPage.razor",
                "CncMachiningPage.razor",
                "CncMachiningSpecificationPage.razor",
                "ContactPage.razor",
                "CustomManufacturingPage.razor",
                "EmailConfirmationPage.razor",
                "ErrorPage.razor",
                "ForgotPasswordPage.razor",
                "GuidelinesPage.razor",
                "HomePage.razor",
                "InstantQuotationPage.razor",
                "KnowledgeIndexPage.razor",
                "LegalPage.razor",
                "LoginPage.razor",
                "LogoutPage.razor",
                "MemberAccountIndexPage.razor",
                "MemberOrdersIndexPage.razor",
                "MemberOverviewPage.razor",
                "NonDisclosureAgreementPage.razor",
                "PrivacyPolicyPage.razor",
                "QuotationPage.razor",
                "ResetPasswordPage.razor",
                "ServicesPage.razor",
                "SignupPage.razor",
                "SocialMediaPage.razor",
                "SpecificationsIndexPage.razor",
                "TermsConditionsPage.razor",
                "ThreeDimensionalPrintingPage.razor",
                "ThreeDimensionalPrintingSpecificationPage.razor",
                "ThreeDimensionalScanningPage.razor",
                "ThreeDimensionalScanningSpecificationPage.razor",
                "WorkflowPage.razor"
            ],
            routedPages);
    }

    [Theory]
    [InlineData(
        "en",
        "Manufacturing workflow | MALIEV",
        "Understand the MALIEV workflow from file review and quotation to production and delivery.",
        "What happens after you request a quote",
        "Project workflow",
        "Engineering review",
        "Delivery")]
    [InlineData(
        "th",
        "ขั้นตอนงานผลิต | MALIEV",
        "เข้าใจขั้นตอนของ MALIEV ตั้งแต่ตรวจไฟล์และเสนอราคา ไปจนถึงผลิตและส่งมอบ",
        "เกิดอะไรขึ้นหลังขอใบเสนอราคา",
        "ขั้นตอนโครงการ",
        "วิศวกรรมตรวจสอบ",
        "ส่งมอบ")]
    public async Task WorkflowRoute_RendersCompleteLocalizedStaticDocument(
        string culture,
        string title,
        string description,
        string heading,
        string eyebrow,
        string firstStep,
        string finalStep)
    {
        using var client = CreateClient(factory);
        using var response = await client.GetAsync($"/knowledges/workflow?culture={culture}&tracking=excluded");
        var source = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("<!DOCTYPE html>", source.TrimStart(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"<html lang=\"{culture}\"", source, StringComparison.Ordinal);
        Assert.Contains($"<title>{title}</title>", source, StringComparison.Ordinal);
        Assert.Contains($"<meta name=\"description\" content=\"{description}\"", source, StringComparison.Ordinal);
        Assert.Contains($"<meta property=\"og:title\" content=\"{title}\"", source, StringComparison.Ordinal);
        Assert.Contains($"<meta property=\"og:description\" content=\"{description}\"", source, StringComparison.Ordinal);
        Assert.Contains($">{heading}<", source, StringComparison.Ordinal);
        Assert.Contains($">{eyebrow}<", source, StringComparison.Ordinal);
        Assert.Contains($">{firstStep}<", source, StringComparison.Ordinal);
        Assert.Contains($">{finalStep}<", source, StringComparison.Ordinal);
        Assert.Equal(5, Regex.Matches(source, "<section[^>]*data-workflow-step", RegexOptions.CultureInvariant).Count);
        Assert.Contains("data-migration-route-owner=\"blazor-static-ssr\"", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-renderer=\"blazor-static-ssr\"", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-component=\"public-navigation\"", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-component=\"public-footer\"", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-component=\"public-cookie-consent\"", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-component=\"public-google-tag-manager-head\"", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-component=\"public-business-structured-data\"", source, StringComparison.Ordinal);
        Assert.Contains("var consentState = 'denied';", source, StringComparison.Ordinal);
        Assert.DoesNotContain("data-migration-component=\"public-google-tag-manager-body\"", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-component=\"public-contact-channel-analytics\"", source, StringComparison.Ordinal);
        Assert.Contains("name=\"google-site-verification\"", source, StringComparison.Ordinal);
        Assert.Contains("id=\"knowledge-navigation\"", source, StringComparison.Ordinal);
        Assert.Contains("aria-controls=\"knowledge-navigation\"", source, StringComparison.Ordinal);
        Assert.Contains("aria-expanded=\"false\"", source, StringComparison.Ordinal);
        Assert.Contains("data-workspace-open", source, StringComparison.Ordinal);
        Assert.Contains("data-workspace-close", source, StringComparison.Ordinal);
        Assert.Contains("href=\"/knowledges/guidelines\"", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("href=\"/knowledges/workflow\"", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("href=\"/knowledges/specifications/cnc-machining\"", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tracking=excluded", ExtractDocumentLinks(source), StringComparison.Ordinal);
        Assert.DoesNotContain("jquery", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("wow.js", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("class=\"wow", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("animate__", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fonts.googleapis.com", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("blazor.web.js", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("_framework/", source, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("en", "https://www.maliev.com/knowledges/workflow?culture=en", "https://www.maliev.com/knowledges/workflow?culture=en", "https://www.maliev.com/knowledges/workflow")]
    [InlineData("th", "https://www.maliev.com/knowledges/workflow", "https://www.maliev.com/knowledges/workflow?culture=en", "https://www.maliev.com/knowledges/workflow")]
    public async Task WorkflowRoute_PreservesCanonicalAndLocalizedAlternates(
        string culture,
        string canonical,
        string english,
        string thai)
    {
        using var client = CreateClient(factory);
        using var response = await client.GetAsync($"/knowledges/workflow?culture={culture}&tracking=excluded");
        var source = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, CountLink(source, "canonical", canonical));
        Assert.Equal(1, CountAlternate(source, "en", english));
        Assert.Equal(1, CountAlternate(source, "th", thai));
        Assert.Equal(1, CountAlternate(source, "x-default", thai));
        Assert.Contains($"<meta property=\"og:url\" content=\"{canonical}\"", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en", "Manufacturing workflow | MALIEV", "Knowledge center", "Manufacturing workflow")]
    [InlineData("th", "ขั้นตอนงานผลิต | MALIEV", "ศูนย์ความรู้", "ขั้นตอนงานผลิต")]
    public async Task WorkflowRoute_EmitsWebPageAndBreadcrumbStructuredData(
        string culture,
        string pageName,
        string knowledgeCenterName,
        string workflowName)
    {
        using var client = CreateClient(factory);
        using var response = await client.GetAsync($"/knowledges/workflow?culture={culture}");
        var source = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        var documents = StructuredDataRegex().Matches(source)
            .Select(match => JsonDocument.Parse(match.Groups["json"].Value))
            .ToArray();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var webPage = documents.Single(document => document.RootElement.GetProperty("@type").GetString() == "WebPage");
        using var breadcrumb = documents.Single(document => document.RootElement.GetProperty("@type").GetString() == "BreadcrumbList");
        Assert.Equal(pageName, webPage.RootElement.GetProperty("name").GetString());
        Assert.Equal(culture, webPage.RootElement.GetProperty("inLanguage").GetString());
        Assert.Equal(knowledgeCenterName, breadcrumb.RootElement.GetProperty("itemListElement")[1].GetProperty("name").GetString());
        Assert.Equal(workflowName, breadcrumb.RootElement.GetProperty("itemListElement")[2].GetProperty("name").GetString());
    }

    [Fact]
    public async Task AcceptedConsent_PreservesTheGtmBodyContainerOnTheWorkflowRoute()
    {
        using var client = CreateClient(factory);
        var initial = WebUtility.HtmlDecode(await client.GetStringAsync("/knowledges/workflow?culture=en"));
        var consentCookie = ConsentCookieRegex().Match(initial).Groups["cookie"].Value;
        Assert.False(string.IsNullOrWhiteSpace(consentCookie));

        using var request = new HttpRequestMessage(HttpMethod.Get, "/knowledges/workflow?culture=en");
        request.Headers.Add("Cookie", consentCookie.Split(';', 2)[0]);
        using var response = await client.SendAsync(request);
        var source = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("data-migration-route-owner=\"blazor-static-ssr\"", source, StringComparison.Ordinal);
        Assert.Contains("var consentState = 'granted';", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-component=\"public-google-tag-manager-body\"", source, StringComparison.Ordinal);
        Assert.Contains("https://www.googletagmanager.com/ns.html?id=GTM-KHDDLVRR", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DisabledWorkflowRoute_UsesTheRetainedRazorFallbackAtTheCanonicalUrl()
    {
        var fallbackFactory = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("BlazorRouting:KnowledgesWorkflow", "false"));
        using var client = CreateClient(fallbackFactory);
        using var response = await client.GetAsync("/knowledges/workflow?culture=en");
        var source = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<title>Manufacturing workflow | MALIEV</title>", source, StringComparison.Ordinal);
        Assert.Contains("data-migration-renderer=\"blazor-static-ssr\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("data-migration-route-owner=\"blazor-static-ssr\"", source, StringComparison.Ordinal);
        Assert.Contains("GTM-KHDDLVRR", source, StringComparison.Ordinal);
        Assert.Contains("rel=\"canonical\"", source, StringComparison.Ordinal);
    }

    private static HttpClient CreateClient(WebApplicationFactory<Program> sourceFactory) =>
        sourceFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });

    private static int CountLink(string source, string relation, string url) =>
        Regex.Matches(
            source,
            $"<link(?=[^>]*rel=\"{Regex.Escape(relation)}\")(?=[^>]*href=\"{Regex.Escape(url)}\")[^>]*>",
            RegexOptions.CultureInvariant).Count;

    private static int CountAlternate(string source, string language, string url) =>
        Regex.Matches(
            source,
            $"<link(?=[^>]*rel=\"alternate\")(?=[^>]*href=\"{Regex.Escape(url)}\")(?=[^>]*hreflang=\"{Regex.Escape(language)}\")[^>]*>",
            RegexOptions.CultureInvariant).Count;

    private static string ExtractDocumentLinks(string source) => string.Join(
        Environment.NewLine,
        Regex.Matches(source, "<link[^>]+(?:rel=\"canonical\"|hreflang=\"(?:en|th|x-default)\")[^>]*>", RegexOptions.CultureInvariant)
            .Select(match => match.Value));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Web.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }

    [GeneratedRegex("<script[^>]*type=\"application/ld\\+json\"[^>]*>(?<json>.*?)</script>", RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex StructuredDataRegex();

    [GeneratedRegex("data-cookie-string=\"(?<cookie>[^\"]+)\"", RegexOptions.CultureInvariant)]
    private static partial Regex ConsentCookieRegex();
}
