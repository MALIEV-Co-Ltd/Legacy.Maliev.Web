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

    [Theory]
    [InlineData("Contact", "en", true)]
    [InlineData("Contact", "th", true)]
    [InlineData("Contact", "en", false)]
    [InlineData("Contact", "th", false)]
    [InlineData("Quotation", "en", true)]
    [InlineData("Quotation", "th", true)]
    [InlineData("Quotation", "en", false)]
    [InlineData("Quotation", "th", false)]
    public async Task InquiryRedesign_RendersRequiredMarkersGuidanceAndStyledFieldsWithoutChangingPostBoundary(
        string page, string culture, bool useBlazorRoute)
    {
        await using var inquiryFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting($"BlazorRouting:{page}", useBlazorRoute.ToString());
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ICountryClient>();
                services.AddSingleton<ICountryClient, StubCountryClient>();
            });
        });
        using var client = inquiryFactory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        using var response = await client.GetAsync($"/{page.ToLowerInvariant()}?culture={culture}&item=3d-printing");
        var source = await response.Content.ReadAsStringAsync();
        var decoded = WebUtility.HtmlDecode(source);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(culture == "th" ? "จำเป็น" : "Required", decoded, StringComparison.Ordinal);
        Assert.Contains("class=\"inquiry-field-grid\"", source, StringComparison.Ordinal);
        Assert.Matches("""class="[^"]*\binquiry-page\b""", source);
        foreach (var field in new[] { "FirstName", "LastName", "Email", "Country", "Message" })
        {
            var label = Regex.Match(source,
                $"""<label\b(?=[^>]*\bfor="{field}")[^>]*>(?<body>[\s\S]*?)</label>""",
                RegexOptions.CultureInvariant);
            Assert.True(label.Success, $"Missing associated label for {field}.");
            Assert.Contains("class=\"inquiry-required\"", label.Groups["body"].Value, StringComparison.Ordinal);
            Assert.Contains("aria-hidden=\"true\"", label.Groups["body"].Value, StringComparison.Ordinal);
            var wrapperStart = source.LastIndexOf("<div", label.Index, StringComparison.Ordinal);
            Assert.True(wrapperStart >= 0);
            var wrapper = source[wrapperStart..source.IndexOf('>', wrapperStart)];
            Assert.Contains("inquiry-field", wrapper, StringComparison.Ordinal);
            var control = Regex.Match(source,
                $"""<(?:input|select|textarea)\b(?=[^>]*\bid="{field}")[^>]*>""",
                RegexOptions.CultureInvariant);
            Assert.True(control.Success);
            Assert.Matches("""(?:^|\s)required(?:\s|=|>)""", control.Value);
        }

        var formId = page == "Contact" ? "contact-us" : "quotation-form";
        var form = Regex.Match(source, $"""<form\b(?=[^>]*\bid="{formId}")[^>]*>""",
            RegexOptions.CultureInvariant);
        Assert.True(form.Success);
        Assert.Contains("method=\"post\"", form.Value, StringComparison.Ordinal);
        var action = Regex.Match(form.Value, "\\baction=\"([^\"]+)\"", RegexOptions.CultureInvariant);
        Assert.True(action.Success);
        var actionUri = new Uri(new Uri("https://localhost"), WebUtility.HtmlDecode(action.Groups[1].Value));
        Assert.Equal(page.ToLowerInvariant(), actionUri.AbsolutePath.Trim('/').ToLowerInvariant());
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(actionUri.Query);
        Assert.Equal("SubmitRequest", query["handler"].ToString());
        Assert.Equal(culture, query["culture"].ToString());
        Assert.Contains("name=\"__RequestVerificationToken\"", source, StringComparison.Ordinal);
        Assert.Contains("name=\"g-recaptcha-response\"", source, StringComparison.Ordinal);

        if (page == "Quotation")
        {
            Assert.Contains("enctype=\"multipart/form-data\"", form.Value, StringComparison.Ordinal);
            Assert.Contains("name=\"SubmissionId\"", source, StringComparison.Ordinal);
            Assert.Contains("name=\"ServiceContext\"", source, StringComparison.Ordinal);
            Assert.Contains("aria-describedby=\"quotation-files-help Files-error\"", source, StringComparison.Ordinal);
            var guidance = culture == "th"
                ? new[]
                {
                    "กรอกแบบฟอร์มด้านล่าง ช่องที่จำเป็นจะแสดงด้วยเครื่องหมายดอกจัน",
                    "ระบุบริการ จำนวน วัสดุ ผิวสำเร็จ ค่าความคลาดเคลื่อน และวันที่ต้องการเมื่อทราบ",
                    "เลือกได้หลายไฟล์ ควร ZIP ไฟล์ที่เกี่ยวข้องเพื่อรักษาการอ้างอิงและโครงสร้างโฟลเดอร์",
                    "เราจะไม่เปิดเผยอีเมลของคุณให้บุคคลอื่น",
                    "ช่วยให้เราติดต่อคุณได้เร็วขึ้น",
                }
                : new[]
                {
                    "Complete the form below. Required fields are marked with an asterisk.",
                    "Include service, quantity, material, finish, tolerances, and target date when known.",
                    "You may select multiple files. ZIP related files together to preserve references and folder structure.",
                    "We'll never share your email with anyone",
                    "We can get in touch with you faster",
                };
            foreach (var text in guidance)
            {
                Assert.Contains(text, decoded, StringComparison.Ordinal);
            }
        }

        var css = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Legacy.Maliev.Web", "wwwroot",
            "src", "app", "css", "inquiry-pages.css"));
        Assert.Contains(".inquiry-field .form-control", css, StringComparison.Ordinal);
        Assert.Contains("min-height: 44px", css, StringComparison.Ordinal);
        Assert.Contains("@media (min-width: 48rem)", css, StringComparison.Ordinal);
        Assert.Contains(".inquiry-page *", css, StringComparison.Ordinal);
        Assert.Contains("@media (prefers-reduced-motion: reduce)", css, StringComparison.Ordinal);
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
        Assert.Contains("<fieldset class=\"border-0 p-0 m-0\" data-upload-size-gate", form.Value, StringComparison.Ordinal);
        Assert.Contains("id=\"quotation-file-too-large\" class=\"inquiry-file-error\" role=\"alert\" data-upload-size-error", form.Value, StringComparison.Ordinal);
        Assert.Contains("data-upload-total-size", form.Value, StringComparison.Ordinal);
        Assert.Contains(culture == "th"
            ? "ไฟล์ที่คุณเลือกมีขนาดใหญ่เกินไป กรุณาอัปโหลดไฟล์ผ่านช่องทางอื่นแล้วส่งลิงก์มาให้เรา"
            : "The selected files size are too large. Please use external upload services.", WebUtility.HtmlDecode(form.Value), StringComparison.Ordinal);
        Assert.Contains(culture == "th" ? "ไฟล์ที่คุณเลือกมีขนาดรวมทั้งหมด" : "You have selected", WebUtility.HtmlDecode(form.Value), StringComparison.Ordinal);
        Assert.Contains("aria-describedby=\"quotation-files-help Files-error quotation-file-too-large\"", form.Value, StringComparison.Ordinal);
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
        Assert.Contains("aria-describedby=\"quotation-files-help Files-error quotation-file-too-large\"", source, StringComparison.Ordinal);
        foreach (var field in new[] { "FirstName", "LastName", "Email", "Country", "Message" })
        {
            var label = Assert.Single(Regex.Matches(
                source,
                $"<label\\b[^>]*\\bfor=\"{field}\"[^>]*>[\\s\\S]*?</label>",
                RegexOptions.CultureInvariant).Cast<Match>());
            Assert.Contains("<span class=\"inquiry-required\" aria-hidden=\"true\">*</span>", label.Value, StringComparison.Ordinal);
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
