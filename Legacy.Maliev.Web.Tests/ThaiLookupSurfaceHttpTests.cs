using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Components.Pages.InstantQuotation;
using Legacy.Maliev.Web.Components.Pages.Member;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Proves lookup wiring in real public documents and the actual Program HTTP boundary.</summary>
public sealed class ThaiLookupSurfaceHttpTests(TestingWebApplicationFactory factory) : IClassFixture<TestingWebApplicationFactory>
{
    [Theory]
    [InlineData("/contact", "contact-company-lookup", "en")]
    [InlineData("/contact", "contact-company-lookup", "th")]
    [InlineData("/quotation", "quotation-company-lookup", "en")]
    [InlineData("/quotation", "quotation-company-lookup", "th")]
    public async Task PublicFormContainsEditableCompanyLookupAndCsrfProtectedPost(string route, string id, string culture)
    {
        var lookup = new Lookup();
        await using var configured = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<ICountryClient>();
            services.AddSingleton<ICountryClient>(new Countries());
            services.RemoveAll<IThaiLookupClient>();
            services.AddSingleton<IThaiLookupClient>(lookup);
        }));
        using var http = configured.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = true,
        });
        using var document = await http.GetAsync(route + "?culture=" + culture);
        Assert.Equal(HttpStatusCode.OK, document.StatusCode);
        var html = await document.Content.ReadAsStringAsync();
        Assert.Contains(id + "-query", html, StringComparison.Ordinal);
        Assert.Contains("data-lookup-kind=\"company\"", html, StringComparison.Ordinal);
        Assert.Contains(culture == "th" ? "data-language=\"th\"" : "data-language=\"en\"", html, StringComparison.Ordinal);
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"", RegexOptions.CultureInvariant);
        Assert.True(token.Success, "Actual rendered form must carry its existing antiforgery token.");
        using var missingToken = await http.PostAsJsonAsync("/lookups/companies/search", new CompanyLookupQuery("example"));
        Assert.Equal(HttpStatusCode.BadRequest, missingToken.StatusCode);
        Assert.Equal(0, lookup.Calls);
        http.DefaultRequestHeaders.Add("RequestVerificationToken", WebUtility.HtmlDecode(token.Groups[1].Value));
        using var reply = await http.PostAsJsonAsync("/lookups/companies/search", new CompanyLookupQuery("example", Language: culture));
        Assert.Equal(HttpStatusCode.OK, reply.StatusCode);
        Assert.Equal(1, lookup.Calls);
        Assert.Equal(culture, lookup.Language);
        Assert.Contains("no-store", reply.Headers.CacheControl!.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("accessToken", await reply.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("profile", "en")]
    [InlineData("profile", "th")]
    [InlineData("addresses", "en")]
    [InlineData("addresses", "th")]
    [InlineData("instant", "en")]
    [InlineData("instant", "th")]
    public async Task ActualMemberAndInstantComponentsRetainTheirFieldsAndRenderLookupHooks(string surface, string culture)
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            var registrations = new ServiceCollection();
            registrations.AddLogging();
            registrations.AddLocalization(options => options.ResourcesPath = "Resources");
            using var services = registrations.BuildServiceProvider();
            await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
            var html = await renderer.Dispatcher.InvokeAsync(async () =>
            {
                if (surface == "profile")
                    return (await renderer.RenderComponentAsync<MemberProfileContent>()).ToHtmlString();
                if (surface == "addresses")
                {
                    var model = MemberAddressDisplayModel.Empty with { Countries = [new(981, "Thailand")] };
                    return (await renderer.RenderComponentAsync<MemberAddressContent>(ParameterView.FromDictionary(
                        new Dictionary<string, object?> { ["Model"] = model }))).ToHtmlString();
                }
                var instant = InstantQuotationCustomerDisplayModel.Empty with
                {
                    Company = "locked company",
                    TaxNumber = "0100000000001",
                    LockedFields = new(StringComparer.Ordinal) { "Company", "TaxNumber" },
                };
                return (await renderer.RenderComponentAsync<InstantQuotationCustomerForm>(ParameterView.FromDictionary(
                    new Dictionary<string, object?> { ["Model"] = instant }))).ToHtmlString();
            });
            Assert.Contains("data-language=\"" + culture + "\"", html, StringComparison.Ordinal);
            if (surface == "profile")
            {
                Assert.Contains("member-company-lookup-query", html, StringComparison.Ordinal);
                Assert.Contains("name=\"CompanyName\"", html, StringComparison.Ordinal);
            }
            else if (surface == "addresses")
            {
                Assert.Contains("member-billing-lookup-query", html, StringComparison.Ordinal);
                Assert.Contains("member-shipping-lookup-query", html, StringComparison.Ordinal);
                Assert.Contains("data-thai-country-value=\"981\"", html, StringComparison.Ordinal);
                Assert.Contains("name=\"BillingAddress2\"", html, StringComparison.Ordinal);
            }
            else
            {
                Assert.Contains("instant-company-lookup-query", html, StringComparison.Ordinal);
                Assert.Contains("instant-billing-lookup-query", html, StringComparison.Ordinal);
                Assert.Contains("instant-shipping-lookup-query", html, StringComparison.Ordinal);
                Assert.Matches("id=\"instant-quote-company\"[^>]*readonly", html);
                Assert.Matches("id=\"instant-quote-taxnumber\"[^>]*readonly", html);
            }
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }

    private sealed class Countries : ICountryClient
    {
        public Task<ServiceResponse<IReadOnlyList<Country>>> GetCountriesAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new ServiceResponse<IReadOnlyList<Country>>([new(764, "Thailand", null, null, "TH", "THA", null, null)], true));
    }
    private sealed class Lookup : IThaiLookupClient
    {
        public int Calls { get; private set; }
        public string? Language { get; private set; }
        public Task<LookupResult<CompanyLookupPage>> SearchCompanyAsync(CompanyLookupQuery query, CancellationToken cancellationToken)
        {
            Calls++; Language = query.Language;
            return Task.FromResult(new LookupResult<CompanyLookupPage>(new("no-match", "creden", "suggestion", [], false), 200));
        }
        public Task<LookupResult<ThaiAddressPage>> SearchAddressAsync(ThaiAddressQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<LookupResult<ThaiAddressResolution>> ResolveAddressAsync(ThaiAddressResolveRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
