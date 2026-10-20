using System.Net;
using System.Text.RegularExpressions;
using Legacy.Maliev.Web.Application;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Legacy.Maliev.Web.Tests;

public sealed class QuotationStaticSsrRouteTests : IClassFixture<TestingWebApplicationFactory>
{
    private readonly WebApplicationFactory<Program> factory;

    public QuotationStaticSsrRouteTests(TestingWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public void Host_DeclaresQuotationGetAndRetainsSensitiveServerWriteDependencies()
    {
        var root = FindRepositoryRoot();
        var web = Path.Combine(root, "Legacy.Maliev.Web");
        var program = File.ReadAllText(Path.Combine(web, "Program.cs"));
        var route = File.ReadAllText(Path.Combine(web, "Components", "Pages", "Quotation", "QuotationPage.razor"));
        var fallback = File.ReadAllText(Path.Combine(web, "Pages", "Quotation", "Index.cshtml"));

        Assert.Contains("BlazorRouting:Quotation", program, StringComparison.Ordinal);
        Assert.Contains("\"/Quotation/Index\"", program, StringComparison.Ordinal);
        Assert.Contains("type=\"typeof(QuotationFormFields)\"", fallback, StringComparison.Ordinal);
        Assert.Contains("ICountryClient", route, StringComparison.Ordinal);
        Assert.Contains("IAccountSessionManager", route, StringComparison.Ordinal);
        Assert.Contains("ICustomerAccountClient", route, StringComparison.Ordinal);
        Assert.DoesNotContain("RouteItem", route, StringComparison.Ordinal);
        Assert.Contains("<meta name=\"robots\" content=\"noindex,follow\"", route, StringComparison.Ordinal);
        Assert.Contains("ViewData[\"Robots\"] = \"noindex,follow\"", fallback, StringComparison.Ordinal);
        Assert.DoesNotContain("IQuotationClient", route, StringComparison.Ordinal);
        Assert.DoesNotContain("IQuotationFileClient", route, StringComparison.Ordinal);
        Assert.DoesNotContain("INotificationClient", route, StringComparison.Ordinal);
        Assert.DoesNotContain("IAntiBotVerifier", route, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DisabledQuotationRoute_UsesTheRetainedMultipartRazorFallback()
    {
        await using var fallbackFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("BlazorRouting:Quotation", "false");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ICountryClient>();
                services.AddSingleton<ICountryClient, StubCountryClient>();
            });
        });
        using var client = fallbackFactory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        var source = await client.GetStringAsync("/quotation?culture=en&item=3d-printing");

        Assert.Contains("data-migration-renderer=\"blazor-static-ssr\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("data-migration-route-owner=\"blazor-static-ssr\"", source, StringComparison.Ordinal);
        Assert.Contains("enctype=\"multipart/form-data\"", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CountryLookupUnavailable_KeepsQuotationPageAvailable(bool useBlazorRoute)
    {
        await using var unavailableFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("BlazorRouting:Quotation", useBlazorRoute.ToString());
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ICountryClient>();
                services.AddSingleton<ICountryClient, UnavailableCountryClient>();
            });
        });
        using var client = unavailableFactory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });

        using var response = await client.GetAsync("/quotation?culture=en&item=3d-printing");
        var source = await response.Content.ReadAsStringAsync();

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Could not retrieve countries from the server", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, "en")]
    [InlineData(true, "th")]
    [InlineData(false, "en")]
    [InlineData(false, "th")]
    public async Task QuotationRequiredLabels_RetainVisibleSourceMarkers(bool useBlazorRoute, string culture)
    {
        await using var markerFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("BlazorRouting:Quotation", useBlazorRoute ? "true" : "false");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ICountryClient>();
                services.AddSingleton<ICountryClient, StubCountryClient>();
            });
        });
        using var client = markerFactory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        using var response = await client.GetAsync($"/quotation?culture={culture}&item=3d-printing");
        var source = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("data-migration-renderer=\"blazor-static-ssr\"", source, StringComparison.Ordinal);
        Assert.Equal(useBlazorRoute, source.Contains("data-migration-route-owner=\"blazor-static-ssr\"", StringComparison.Ordinal));
        var form = Assert.Single(Regex.Matches(source,
            "<form\\b[^>]*\\bid=\"quotation-form\"[^>]*>[\\s\\S]*?</form>",
            RegexOptions.CultureInvariant).Cast<Match>());
        Assert.Contains("<div class=\"inquiry-page\"", source, StringComparison.Ordinal);
        Assert.Contains("<header class=\"inquiry-form-header\">", form.Value, StringComparison.Ordinal);
        Assert.Contains("id=\"quotation-form-title\"", form.Value, StringComparison.Ordinal);
        Assert.Contains("class=\"inquiry-required-note\"", form.Value, StringComparison.Ordinal);
        Assert.Contains("class=\"inquiry-field-grid\"", form.Value, StringComparison.Ordinal);
        Assert.Contains("class=\"inquiry-field inquiry-field--full\"", form.Value, StringComparison.Ordinal);
        Assert.Contains("class=\"inquiry-submit\"", form.Value, StringComparison.Ordinal);
        var decodedForm = WebUtility.HtmlDecode(form.Value);
        Assert.Contains(culture == "th" ? "ขอใบเสนอราคาจากทีมงาน" : "Request a manual quotation", decodedForm, StringComparison.Ordinal);
        Assert.Contains(culture == "th" ? "กรอกแบบฟอร์มด้านล่าง ช่องที่จำเป็นจะแสดงด้วยเครื่องหมายดอกจัน" : "Complete the form below. Required fields are marked with an asterisk.", decodedForm, StringComparison.Ordinal);
        var projectGuidance = culture == "th"
            ? "ระบุบริการ จำนวน วัสดุ ผิวสำเร็จ ค่าความคลาดเคลื่อน และวันที่ต้องการเมื่อทราบ"
            : "Include service, quantity, material, finish, tolerances, and target date when known.";
        var fileGuidance = culture == "th"
            ? "เลือกได้หลายไฟล์ ควร ZIP ไฟล์ที่เกี่ยวข้องเพื่อรักษาการอ้างอิงและโครงสร้างโฟลเดอร์"
            : "You may select multiple files. ZIP related files together to preserve references and folder structure.";
        var projectHelp = Assert.Single(Regex.Matches(source,
            "<span\\b[^>]*\\bid=\"quotation-message-help\"[^>]*>[\\s\\S]*?</span>",
            RegexOptions.CultureInvariant).Cast<Match>());
        Assert.Contains(projectGuidance, WebUtility.HtmlDecode(projectHelp.Value), StringComparison.Ordinal);
        var fileHelp = Assert.Single(Regex.Matches(source,
            "<div\\b[^>]*\\bid=\"quotation-files-help\"[^>]*>[\\s\\S]*?</div>",
            RegexOptions.CultureInvariant).Cast<Match>());
        Assert.Contains(fileGuidance, WebUtility.HtmlDecode(fileHelp.Value), StringComparison.Ordinal);
        Assert.Matches("<textarea\\b[^>]*\\bid=\"Message\"[^>]*\\baria-describedby=\"Message-error quotation-message-help\"", source);
        Assert.Contains("aria-describedby=\"quotation-files-help Files-error\"", source, StringComparison.Ordinal);
        foreach (var field in new[] { "FirstName", "LastName", "Email", "Country", "Message" })
        {
            var label = Assert.Single(Regex.Matches(
                source,
                $"<label\\b[^>]*\\bfor=\"{field}\"[^>]*>[\\s\\S]*?</label>",
                RegexOptions.CultureInvariant).Cast<Match>());
            Assert.Contains("<span class=\"inquiry-required\">*</span>", label.Value, StringComparison.Ordinal);
        }
        foreach (var field in new[] { "Phone", "Company", "TaxNumber" })
        {
            var label = Assert.Single(Regex.Matches(
                source,
                $"<label\\b[^>]*\\bfor=\"{field}\"[^>]*>[\\s\\S]*?</label>",
                RegexOptions.CultureInvariant).Cast<Match>());
            Assert.DoesNotContain("inquiry-required", label.Value, StringComparison.Ordinal);
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

    private sealed class StubCountryClient : ICountryClient
    {
        public Task<ServiceResponse<IReadOnlyList<Country>>> GetCountriesAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new ServiceResponse<IReadOnlyList<Country>>(
                [new Country(764, "Thailand", "Asia", "66", "TH", "THA", null, null)],
                true));
    }

    private sealed class UnavailableCountryClient : ICountryClient
    {
        public Task<ServiceResponse<IReadOnlyList<Country>>> GetCountriesAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new ServiceResponse<IReadOnlyList<Country>>([], false));
    }
}
