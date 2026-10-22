using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Components.Layout;
using Legacy.Maliev.Web.Infrastructure;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Original shared selector fallback and descriptive country validation through owned public boundaries.</summary>
public sealed class SharedSelectorValidationOriginalParityTests(TestingWebApplicationFactory factory)
    : IClassFixture<TestingWebApplicationFactory>
{
    [Theory]
    [InlineData("en", "th", null, false, "en")]
    [InlineData("th", "en", null, false, "th")]
    [InlineData("th", "en", "en", false, "en")]
    [InlineData("en", "th", "th", false, "th")]
    [InlineData("en", "th", "en", true, "EN")]
    [InlineData("th", "en", "th", true, "TH")]
    [InlineData("en", "th", null, true, "EN")]
    [InlineData("fr", "en", null, false, null)]
    public void Selector_UsesFeatureThenCurrentUiCultureWithoutInventingDefault(
        string uiCulture, string culture, string? featureCulture, bool upperNames, string? expected)
    {
        var previousUi = CultureInfo.CurrentUICulture;
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(uiCulture);
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            using var services = new ServiceCollection()
                .AddSingleton<IAntiforgery, FixedAntiforgery>()
                .AddSingleton<IOptions<RequestLocalizationOptions>>(Options.Create(new RequestLocalizationOptions
                {
                    SupportedUICultures = upperNames
                        ? [new UpperCulture("en"), new UpperCulture("th")]
                        : [CultureInfo.GetCultureInfo("en"), CultureInfo.GetCultureInfo("th")],
                })).BuildServiceProvider();
            var context = new DefaultHttpContext { RequestServices = services };
            context.Request.Path = "/contact";
            if (featureCulture is not null)
            {
                context.Features.Set<IRequestCultureFeature>(new RequestCultureFeature(new RequestCulture(featureCulture), null));
            }
            var model = PublicNavigationDisplayModel.Create(context, false);
            if (expected is null)
            {
                Assert.DoesNotContain(model.Cultures, option => option.IsSelected);
            }
            else
            {
                var selected = Assert.Single(model.Cultures, option => option.IsSelected);
                Assert.Equal(expected, selected.Name);
                Assert.Equal(expected.Equals("th", StringComparison.OrdinalIgnoreCase) ? "ไทย" : "English", selected.Label);
            }
            Assert.Equal("__RequestVerificationToken", model.AntiforgeryFieldName);
            Assert.Contains("handler=SetLanguage", model.LanguageAction, StringComparison.Ordinal);
            Assert.Contains("returnUrl=", model.LanguageAction, StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousUi;
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData("en", true)]
    [InlineData("th", true)]
    [InlineData("en", false)]
    [InlineData("th", false)]
    public async Task Selector_ActualRenderedFormHasNoImplicitRequiredAndPostsWithRealAntiforgery(string culture, bool enabled)
    {
        var boundary = new PublicBoundary();
        await using var host = Host(boundary, enabled);
        using var client = Client(host);
        using var page = await client.GetAsync($"/contact?culture={culture}");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var html = WebUtility.HtmlDecode(await page.Content.ReadAsStringAsync());
        var form = Form(html, "<form[^>]*class=\"maliev-language-form\"[^>]*action=\"(?<action>[^\"]+)\"[\\s\\S]*?</form>");
        Assert.Contains("form-select form-select-sm maliev-language-select", form.Value, StringComparison.Ordinal);
        Assert.Contains("for=\"maliev-navbar-language\"", form.Value, StringComparison.Ordinal);
        var selector = Regex.Match(form.Value, "<select[^>]*id=\"maliev-navbar-language\"[^>]*>", RegexOptions.CultureInvariant);
        Assert.True(selector.Success);
        Assert.DoesNotContain("data-val-required", selector.Value, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch("\\srequired(?:\\s|=|>)", selector.Value);
        Assert.Matches($"<option[^>]*selected[^>]*>{(culture == "th" ? "ไทย" : "English")}</option>", form.Value);
        var target = culture == "en" ? "th" : "en";
        using var response = await client.PostAsync(form.Groups["action"].Value,
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["culture"] = target,
                ["__RequestVerificationToken"] = Token(form.Value),
            }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(target == "th" ? "/contact" : "/contact?culture=en", response.Headers.Location?.OriginalString);
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), value => value.Contains($"c%3D{target}%7Cuic%3D{target}", StringComparison.Ordinal));
        Assert.Equal(0, boundary.Submissions);
        Assert.Equal(0, boundary.AntiBotChecks);
    }

    [Theory]
    [InlineData("en", true)]
    [InlineData("th", true)]
    [InlineData("en", false)]
    [InlineData("th", false)]
    public async Task MissingCountry_ActualPostRendersDescriptiveLocalizedErrorWithoutSubmitting(string culture, bool enabled)
    {
        var boundary = new PublicBoundary();
        await using var host = Host(boundary, enabled);
        using var client = Client(host);
        using var page = await client.GetAsync($"/contact?culture={culture}");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var html = WebUtility.HtmlDecode(await page.Content.ReadAsStringAsync());
        var form = Form(html, "<form(?=[^>]*id=\"contact-us\")(?=[^>]*\\saction=\"(?<action>[^\"]+)\")[^>]*[\\s\\S]*?</form>");
        Assert.Contains("handler=SubmitRequest", form.Groups["action"].Value, StringComparison.Ordinal);
        using var response = await client.PostAsync(form.Groups["action"].Value,
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = Token(form.Value),
                ["FirstName"] = "Source",
                ["LastName"] = "Parity",
                ["Email"] = "source-parity@example.test",
                ["Message"] = "Public validation control.",
                ["Country"] = string.Empty,
            }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        var error = Regex.Match(body, "<span[^>]*id=\"Country-error\"[^>]*>(?<text>[^<]*)</span>", RegexOptions.CultureInvariant);
        Assert.True(error.Success);
        Assert.Equal(culture == "th" ? "กรุณาเลือกประเทศ" : "Please select your country", error.Groups["text"].Value.Trim());
        Assert.Equal(0, boundary.Submissions);
        Assert.Equal(0, boundary.AntiBotChecks);
    }

    [Theory]
    [InlineData("en", "th", null, false, "en")]
    [InlineData("th", "en", null, false, "th")]
    [InlineData("th", "en", "en", false, "en")]
    [InlineData("en", "th", "th", false, "th")]
    [InlineData("en", "th", "en", true, "EN")]
    [InlineData("th", "en", "th", true, "TH")]
    [InlineData("en", "th", null, true, "EN")]
    [InlineData("fr", "en", null, false, null)]
    public async Task RetainedFooter_ActualRazorPartialPreservesOriginalCultureAndPlainSelect(
        string uiCulture, string culture, string? featureCulture, bool upperNames, string? expected)
    {
        // Qualifies the retained partial only; default live-footer delivery is a separate obligation.
        await using var host = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("BlazorRouting:Home", "false");
            builder.ConfigureServices(services => services.PostConfigure<RequestLocalizationOptions>(options =>
                options.SupportedUICultures = upperNames
                    ? [new UpperCulture("en"), new UpperCulture("th")]
                    : [CultureInfo.GetCultureInfo("en"), CultureInfo.GetCultureInfo("th")]));
        });
        using var scope = host.Services.CreateScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("localhost");
        context.Request.Path = "/contact";
        if (featureCulture is not null)
            context.Features.Set<IRequestCultureFeature>(new RequestCultureFeature(new RequestCulture(featureCulture), null));
        var previousUi = CultureInfo.CurrentUICulture;
        var previous = CultureInfo.CurrentCulture;
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        var previousContext = accessor.HttpContext;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(uiCulture);
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            accessor.HttpContext = context;
            var endpoint = scope.ServiceProvider.GetRequiredService<EndpointDataSource>().Endpoints
                .OfType<RouteEndpoint>()
                .Where(endpoint => endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Mvc.RazorPages.PageActionDescriptor>()?.ViewEnginePath == "/Index")
                .OrderBy(endpoint => endpoint.RoutePattern.RawText, StringComparer.Ordinal)
                .First();
            var descriptor = Assert.IsAssignableFrom<Microsoft.AspNetCore.Mvc.RazorPages.PageActionDescriptor>(
                endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Mvc.RazorPages.PageActionDescriptor>());
            var routeData = new RouteData();
            foreach (var value in descriptor.RouteValues) routeData.Values[value.Key] = value.Value;
            context.Request.RouteValues = routeData.Values;
            context.SetEndpoint(endpoint);
            var action = new ActionContext(context, routeData, descriptor);
            var view = scope.ServiceProvider.GetRequiredService<IRazorViewEngine>()
                .GetView(null, "/Pages/Shared/_SelectLanguageFooterPartial.cshtml", false);
            Assert.True(view.Success, string.Join(";", view.SearchedLocations ?? []));
            var resolved = Assert.IsAssignableFrom<Microsoft.AspNetCore.Mvc.ViewEngines.IView>(view.View);
            using var writer = new StringWriter(CultureInfo.InvariantCulture);
            var viewContext = new ViewContext(action, resolved,
                new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary()),
                scope.ServiceProvider.GetRequiredService<ITempDataDictionaryFactory>().GetTempData(context),
                writer, new HtmlHelperOptions());
            await resolved.RenderAsync(viewContext);
            var html = WebUtility.HtmlDecode(writer.ToString());
            var selector = Regex.Match(html, "<select[^>]*name=\"culture\"[^>]*>", RegexOptions.CultureInvariant);
            Assert.True(selector.Success);
            Assert.DoesNotContain("data-val-required", selector.Value, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotMatch("\\srequired(?:\\s|=|>)", selector.Value);
            var selected = Regex.Matches(html, "<option[^>]*selected[^>]*>", RegexOptions.CultureInvariant);
            if (expected is null) Assert.Empty(selected);
            else Assert.Contains($"value=\"{expected}\"", Assert.Single(selected.Cast<Match>()).Value, StringComparison.Ordinal);
            Assert.NotEmpty(Token(html));
            Assert.Contains("handler=SetLanguage", html, StringComparison.Ordinal);
        }
        finally
        {
            accessor.HttpContext = previousContext;
            CultureInfo.CurrentUICulture = previousUi;
            CultureInfo.CurrentCulture = previous;
        }
    }

    private WebApplicationFactory<Program> Host(PublicBoundary boundary, bool enabled) => factory.WithWebHostBuilder(builder =>
    {
        builder.UseSetting("BlazorRouting:Contact", enabled.ToString());
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<ICountryClient>();
            services.RemoveAll<IContactClient>();
            services.RemoveAll<IAntiBotVerifier>();
            services.RemoveAll<IContactTrustedCustomerLoader>();
            services.AddSingleton<ICountryClient>(boundary);
            services.AddSingleton<IContactClient>(boundary);
            services.AddSingleton<IAntiBotVerifier>(boundary);
            services.AddSingleton<IContactTrustedCustomerLoader>(boundary);
        });
    });

    private static HttpClient Client(WebApplicationFactory<Program> host) => host.CreateClient(
        new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });

    private static Match Form(string html, string pattern)
    {
        var form = Regex.Match(html, pattern, RegexOptions.CultureInvariant);
        Assert.True(form.Success);
        return form;
    }

    private static string Token(string form)
    {
        var token = Regex.Match(form, "name=\"__RequestVerificationToken\"[^>]*value=\"(?<value>[^\"]+)\"", RegexOptions.CultureInvariant);
        Assert.True(token.Success);
        return token.Groups["value"].Value;
    }

    private sealed class UpperCulture(string name) : CultureInfo(name)
    {
        public override string Name => base.Name.ToUpperInvariant();
    }

    private sealed class FixedAntiforgery : IAntiforgery
    {
        public AntiforgeryTokenSet GetAndStoreTokens(HttpContext context) => new("test-request", "test-cookie", "__RequestVerificationToken", "X-CSRF-TOKEN");
        public AntiforgeryTokenSet GetTokens(HttpContext context) => GetAndStoreTokens(context);
        public Task<bool> IsRequestValidAsync(HttpContext context) => Task.FromResult(true);
        public Task ValidateRequestAsync(HttpContext context) => Task.CompletedTask;
        public void SetCookieTokenAndHeader(HttpContext context) { }
    }

    private sealed class PublicBoundary : ICountryClient, IContactClient, IAntiBotVerifier, IContactTrustedCustomerLoader
    {
        public int Submissions { get; private set; }
        public int AntiBotChecks { get; private set; }
        public Task<ServiceResponse<IReadOnlyList<Country>>> GetCountriesAsync(CancellationToken token) =>
            Task.FromResult(new ServiceResponse<IReadOnlyList<Country>>([new Country(1, "Thailand", null, null, null, null, null, null)], true));
        public Task<ContactSubmissionResult> SubmitAsync(ContactSubmission submission, CancellationToken token)
        {
            Submissions++;
            return Task.FromResult(new ContactSubmissionResult(null, false, false));
        }
        public Task<bool> VerifyAsync(string? token, string action, CancellationToken cancellation)
        {
            AntiBotChecks++;
            return Task.FromResult(true);
        }
        public Task<ContactTrustedCustomerLoadResult> LoadAsync(HttpContext context, IReadOnlyList<Country> countries, CancellationToken token) =>
            Task.FromResult(new ContactTrustedCustomerLoadResult(null, false, true));
    }
}
