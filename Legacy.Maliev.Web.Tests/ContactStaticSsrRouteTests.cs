using System.Collections.Concurrent;
using System.Net;
using System.Text.RegularExpressions;
using Legacy.Maliev.Web.Application;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;

namespace Legacy.Maliev.Web.Tests;

public sealed class ContactStaticSsrRouteTests : IClassFixture<TestingWebApplicationFactory>
{
    private readonly WebApplicationFactory<Program> factory;

    public ContactStaticSsrRouteTests(TestingWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public void Host_DeclaresContactGetAndRetainsTheServerPostBoundary()
    {
        var root = FindRepositoryRoot();
        var web = Path.Combine(root, "Legacy.Maliev.Web");
        var program = File.ReadAllText(Path.Combine(web, "Program.cs"));
        var appsettings = File.ReadAllText(Path.Combine(web, "appsettings.json"));
        var route = File.ReadAllText(Path.Combine(web, "Components", "Pages", "Contact", "ContactPage.razor"));
        var fallback = File.ReadAllText(Path.Combine(web, "Pages", "Contact", "Index.cshtml"));

        Assert.Contains("BlazorRouting:Contact", program, StringComparison.Ordinal);
        Assert.Contains("\"/Contact/Index\"", program, StringComparison.Ordinal);
        Assert.Contains("\"Contact\": true", appsettings, StringComparison.Ordinal);
        Assert.Contains("type=\"typeof(ContactFormFields)\"", fallback, StringComparison.Ordinal);
        Assert.Contains("ICountryClient", route, StringComparison.Ordinal);
        Assert.DoesNotContain("IContactClient", route, StringComparison.Ordinal);
        Assert.DoesNotContain("INotificationClient", route, StringComparison.Ordinal);
        Assert.DoesNotContain("IAntiBotVerifier", route, StringComparison.Ordinal);
        foreach (var resourcePath in new[]
        {
            Path.Combine(web, "Resources", "Components", "Pages", "Contact", "ContactContent.th.resx"),
            Path.Combine(web, "Resources", "Components", "Layout", "PublicFooter.th.resx"),
        })
        {
            var resource = File.ReadAllText(resourcePath);
            Assert.Contains("จันทร์-ศุกร์: 09:00-18:00 น.", resource, StringComparison.Ordinal);
            Assert.Contains("เสาร์-อาทิตย์: ปิด", resource, StringComparison.Ordinal);
            Assert.DoesNotContain("10:00-19:00", resource, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task DisabledContactRoute_UsesTheRetainedRazorFallback()
    {
        await using var fallbackFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("BlazorRouting:Contact", "false");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ICountryClient>();
                services.AddSingleton<ICountryClient, StubCountryClient>();
            });
        });
        await using var activeFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("BlazorRouting:Contact", "true");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ICountryClient>();
                services.AddSingleton<ICountryClient, StubCountryClient>();
            });
        });
        using var activeClient = activeFactory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        using var client = fallbackFactory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        foreach (var culture in new[] { "en", "th" })
        {
            using var response = await client.GetAsync($"/contact?culture={culture}");
            var source = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("data-migration-renderer=\"blazor-static-ssr\"", source, StringComparison.Ordinal);
            Assert.DoesNotContain("data-migration-route-owner=\"blazor-static-ssr\"", source, StringComparison.Ordinal);
            var panel = Assert.Single(Regex.Matches(
                source,
                "<section class=\"inquiry-panel inquiry-panel--accent contact-line-card\">[\\s\\S]*?</section>",
                RegexOptions.CultureInvariant).Cast<Match>());
            var actions = Assert.Single(Regex.Matches(
                panel.Value,
                "<div class=\"contact-channel-actions\"[^>]*>[\\s\\S]*?</div>",
                RegexOptions.CultureInvariant).Cast<Match>());
            Assert.Contains($"aria-label=\"{(culture == "th" ? "ช่องทางแชต" : "Chat channels")}\"", actions.Value, StringComparison.Ordinal);
            foreach (var (destination, english, thai, placement) in new[]
            {
                ("/contact/line", "Open LINE Official Account", "เปิด LINE Official Account", "contact_line"),
                ("https://wa.me/66898950690", "Message us on WhatsApp", "ส่งข้อความผ่าน WhatsApp", "contact_whatsapp"),
                ("https://m.me/maliev.manufacturing/", "Open Messenger", "เปิด Messenger", "contact_messenger")
            })
            {
                var anchor = Assert.Single(Regex.Matches(
                    actions.Value,
                    $"<a\\b[^>]*\\bhref=\"{Regex.Escape(destination)}\"[^>]*>[\\s\\S]*?</a>",
                    RegexOptions.CultureInvariant).Cast<Match>());
                if (placement == "contact_line")
                {
                    Assert.DoesNotMatch("\\s(?:target|rel)\\s*=", anchor.Value[..(anchor.Value.IndexOf('>') + 1)]);
                }
                else
                {
                    Assert.Contains("target=\"_blank\" rel=\"noopener\"", anchor.Value, StringComparison.Ordinal);
                }
                Assert.Contains($"data-contact-placement=\"{placement}\"", anchor.Value, StringComparison.Ordinal);
                Assert.Contains($"<span>{(culture == "th" ? thai : english)}</span>", anchor.Value, StringComparison.Ordinal);
                if (destination == "https://wa.me/66898950690")
                {
                    Assert.Contains("data-maliev-contact-destination=\"whatsapp_business\"", anchor.Value, StringComparison.Ordinal);
                }
            }
            Assert.Contains("var consentState = 'denied';", source, StringComparison.Ordinal);
            Assert.DoesNotContain("data-migration-component=\"public-google-tag-manager-body\"", source, StringComparison.Ordinal);
            using var activeResponse = await activeClient.GetAsync($"/contact?culture={culture}");
            var activeSource = WebUtility.HtmlDecode(await activeResponse.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.OK, activeResponse.StatusCode);
            Assert.Contains("data-migration-route-owner=\"blazor-static-ssr\"", activeSource, StringComparison.Ordinal);
            foreach (var rendered in new[] { source, activeSource })
            {
                var visit = Assert.Single(Regex.Matches(
                    rendered,
                    "<article\\b[^>]*class=\"contact-method-card\"[^>]*>[\\s\\S]*?</article>",
                    RegexOptions.CultureInvariant).Cast<Match>(),
                    match => match.Value.Contains("<address>", StringComparison.Ordinal));
                var footer = Assert.Single(Regex.Matches(
                    rendered,
                    "<footer\\b[^>]*data-migration-component=\"public-footer\"[^>]*>[\\s\\S]*?</footer>",
                    RegexOptions.CultureInvariant).Cast<Match>());
                foreach (var surface in new[] { visit.Value, footer.Value })
                {
                    Assert.Contains(culture == "th" ? "จันทร์-ศุกร์: 09:00-18:00 น." : "Monday-Friday: 09:00-18:00",
                        surface, StringComparison.Ordinal);
                    Assert.Contains(culture == "th" ? "เสาร์-อาทิตย์: ปิด" : "Saturday-Sunday: Closed",
                        surface, StringComparison.Ordinal);
                    Assert.DoesNotContain("10:00-19:00", surface, StringComparison.Ordinal);
                }
            }
        }
    }

    [Fact]
    public async Task CountryTransportFailure_KeepsContactGetAvailableWithoutDownstreamWrites()
    {
        var requests = new ConcurrentQueue<(string Client, string Method, string Path)>();
        await using var unavailableFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Services:Country", "https://country-outage.invalid/");
            builder.ConfigureServices(services =>
            {
                foreach (var name in new[] { "countries", "contacts", "notifications", "auth" })
                {
                    services.AddHttpClient(name)
                        .ConfigurePrimaryHttpMessageHandler(() => new CountryOutageTransport(name, requests));
                }

                // Retain the real retry/deadline policy, shortening only test retry scheduling.
                services.PostConfigure<HttpStandardResilienceOptions>("countries-standard", options =>
                {
                    options.Retry.Delay = TimeSpan.Zero;
                    options.Retry.UseJitter = false;
                    options.Retry.DelayGenerator = _ => ValueTask.FromResult<TimeSpan?>(TimeSpan.Zero);
                });
            });
        });
        using var client = unavailableFactory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        using var response = await client.GetAsync("/contact?culture=en", deadline.Token);
        var source = await response.Content.ReadAsStringAsync(deadline.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("data-migration-route-owner=\"blazor-static-ssr\"", source, StringComparison.Ordinal);
        var form = Regex.Match(source, "<form\\b[^>]*\\bid=\"contact-us\"[^>]*>");
        Assert.True(form.Success);
        Assert.Contains("method=\"post\"", form.Value, StringComparison.Ordinal);
        Assert.Contains("Could not retrieve countries from the server.", source, StringComparison.Ordinal);
        Assert.DoesNotContain("controlled-country-private-failure", source, StringComparison.Ordinal);
        var select = Regex.Match(source, "<select\\b(?<attributes>[^>]*\\bid=\"Country\"[^>]*)>(?<options>.*?)</select>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
        Assert.True(select.Success);
        Assert.Matches("\\sdisabled(?:\\s|=|$)", select.Groups["attributes"].Value);
        Assert.Single(Regex.Matches(select.Groups["options"].Value, "<option\\b", RegexOptions.IgnoreCase).Cast<Match>());
        Assert.Matches("<option\\b[^>]*\\svalue(?:=\"\"|='')?(?=\\s|>)[^>]*>", select.Groups["options"].Value);
        Assert.Contains("Select a country", select.Groups["options"].Value, StringComparison.Ordinal);
        var submit = Regex.Match(source, "<button\\b[^>]*\\bid=\"submit-button\"[^>]*>",
            RegexOptions.IgnoreCase);
        Assert.True(submit.Success);
        Assert.Matches("\\sdisabled(?:\\s|=|>)", submit.Value);
        Assert.NotEmpty(requests);
        Assert.All(requests, request =>
        {
            Assert.Equal("countries", request.Client);
            Assert.Equal("GET", request.Method);
            Assert.Equal("/Countries", request.Path);
        });
    }

    [Theory]
    [InlineData(true, "en")]
    [InlineData(true, "th")]
    [InlineData(false, "en")]
    [InlineData(false, "th")]
    public async Task ContactRequiredLabels_RetainVisibleSourceMarkers(bool useBlazorRoute, string culture)
    {
        await using var markerFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("BlazorRouting:Contact", useBlazorRoute ? "true" : "false");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ICountryClient>();
                services.AddSingleton<ICountryClient, StubCountryClient>();
            });
        });
        using var client = markerFactory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        using var response = await client.GetAsync($"/contact?culture={culture}");
        var source = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("data-migration-renderer=\"blazor-static-ssr\"", source, StringComparison.Ordinal);
        Assert.Equal(useBlazorRoute, source.Contains("data-migration-route-owner=\"blazor-static-ssr\"", StringComparison.Ordinal));
        foreach (var field in new[] { "FirstName", "LastName", "Email", "Country", "Message" })
        {
            var label = Assert.Single(Regex.Matches(
                source,
                $"<label\\b[^>]*\\bfor=\"{field}\"[^>]*>[\\s\\S]*?</label>",
                RegexOptions.CultureInvariant).Cast<Match>());
            Assert.Contains("<span class=\"inquiry-required\" aria-hidden=\"true\">*</span>", label.Value, StringComparison.Ordinal);
        }
        foreach (var field in new[] { "Phone", "Company" })
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

    private sealed class CountryOutageTransport(
        string clientName,
        ConcurrentQueue<(string Client, string Method, string Path)> requests) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            requests.Enqueue((clientName, request.Method.Method, request.RequestUri!.AbsolutePath));
            return Task.FromException<HttpResponseMessage>(
                new HttpRequestException("controlled-country-private-failure"));
        }
    }
}
