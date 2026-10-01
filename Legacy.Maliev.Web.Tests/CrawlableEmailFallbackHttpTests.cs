using System.Net;
using System.Text.RegularExpressions;
using Legacy.Maliev.Web.Application;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Legacy.Maliev.Web.Tests;

public sealed class CrawlableEmailFallbackHttpTests(TestingWebApplicationFactory factory)
    : IClassFixture<TestingWebApplicationFactory>
{
    [Theory]
    [InlineData("en", true)]
    [InlineData("th", true)]
    [InlineData("en", false)]
    [InlineData("th", false)]
    public async Task CareerDocument_PreservesRecipientAndFallbackAcrossRenderers(string culture, bool useSsr)
    {
        var career = new ControlledCareerClient();
        await using var host = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("BlazorRouting:CareerDetail", useSsr.ToString());
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ICareerClient>();
                services.AddSingleton<ICareerClient>(career);
            });
        });
        using var client = CreateClient(host);
        using var response = await client.GetAsync($"/career/view/7?culture={culture}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new[] { 7 }, career.Requested);
        AssertContact(await response.Content.ReadAsStringAsync(), "career@maliev.com");
    }

    [Theory]
    [InlineData("en", true)]
    [InlineData("th", true)]
    [InlineData("en", false)]
    [InlineData("th", false)]
    public async Task QuotationDocument_PreservesManufacturingRecipientAcrossRenderers(string culture, bool useSsr)
    {
        await using var host = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("BlazorRouting:Quotation", useSsr.ToString());
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ICountryClient>();
                services.AddSingleton<ICountryClient, ControlledCountryClient>();
            });
        });
        using var client = CreateClient(host);
        using var response = await client.GetAsync($"/quotation?culture={culture}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertContact(await response.Content.ReadAsStringAsync(), "manufacturing@maliev.com");
    }

    [Theory]
    [InlineData("en", false)]
    [InlineData("th", false)]
    public async Task RetainedContactDocument_PreservesKnownRecipientFallback(string culture, bool useSsr)
    {
        await using var host = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("BlazorRouting:Contact", useSsr.ToString());
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ICountryClient>();
                services.AddSingleton<ICountryClient, ControlledCountryClient>();
            });
        });
        using var client = CreateClient(host);
        using var response = await client.GetAsync($"/contact?culture={culture}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        AssertContact(html, "info@maliev.com");
        Assert.DoesNotContain("href=\"mailto:info@maliev.com\"", html, StringComparison.Ordinal);
    }

    private static void AssertContact(string html, string recipient)
    {
        var anchors = Regex.Matches(WebUtility.HtmlDecode(html), "<a\\b(?<attributes>[^>]*)>(?<body>[\\s\\S]*?)</a>");
        var relevant = anchors.Where(anchor => anchor.Groups["body"].Value == recipient).ToArray();
        Assert.NotEmpty(relevant);
        foreach (var anchor in relevant)
        {
            Assert.Contains("href=\"/contact#contact-us\"", anchor.Groups["attributes"].Value, StringComparison.Ordinal);
            Assert.Contains($"data-contact-email=\"{recipient}\"", anchor.Groups["attributes"].Value, StringComparison.Ordinal);
        }
    }

    private static HttpClient CreateClient(WebApplicationFactory<Program> host) => host.CreateClient(new()
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false
    });

    private sealed class ControlledCareerClient : ICareerClient
    {
        public List<int> Requested { get; } = [];

        public Task<CareerListing> GetListingAsync(CareerSort sort, string? search, int pageIndex, int pageSize,
            CancellationToken cancellationToken) =>
            Task.FromResult(new CareerListing([], CareerOfferPage.Empty(pageIndex), true));

        public Task<ServiceResponse<CareerOffer>> GetOfferAsync(int offerId, CancellationToken cancellationToken)
        {
            Requested.Add(offerId);
            return Task.FromResult(new ServiceResponse<CareerOffer>(
                new CareerOffer(7, 1, "Manufacturing Engineer", null, "Engineering responsibilities.",
                    "Engineering experience.", "A practical team.", "Nonthaburi", false, null, null,
                    new CareerLevel(1, "Engineer", null, null, null)), true));
        }
    }

    private sealed class ControlledCountryClient : ICountryClient
    {
        public Task<ServiceResponse<IReadOnlyList<Country>>> GetCountriesAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new ServiceResponse<IReadOnlyList<Country>>(
                [new Country(764, "Thailand", "Asia", "66", "TH", "THA", null, null)], true));
    }
}
