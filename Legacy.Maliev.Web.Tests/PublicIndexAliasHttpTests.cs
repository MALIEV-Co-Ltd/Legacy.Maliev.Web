using System.Net;
using System.Text.RegularExpressions;
using Legacy.Maliev.Web.Application;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Legacy.Maliev.Web.Tests;

public sealed class PublicIndexAliasHttpTests(TestingWebApplicationFactory factory) : IClassFixture<TestingWebApplicationFactory>
{
    [Theory]
    [InlineData("/", "/Index", "Home")]
    [InlineData("/about", "/About/Index", "About")]
    [InlineData("/legal", "/Legal/Index", "Legal")]
    [InlineData("/account", "/Account/Index", "AccountIndex")]
    [InlineData("/contact", "/Contact/Index", "Contact")]
    [InlineData("/quotation", "/Quotation/Index", "Quotation")]
    [InlineData("/career", "/Career/Index", "CareerIndex")]
    [InlineData("/services", "/Services/Index", "Services")]
    [InlineData("/knowledges", "/Knowledges/Index", "KnowledgesIndex")]
    [InlineData("/knowledges/specifications", "/Knowledges/Specifications/Index", "KnowledgesSpecifications")]
    public async Task IndexAlias_RendersSameLocalizedTitleAsShortRoute(string shortPath, string alias, string feature)
    {
        foreach (bool active in new[] { true, false })
        {
            await using var host = CreateHost(feature, active);
            using var client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
            foreach (var culture in new[] { "en", "th" })
            {
                using var shortResponse = await client.GetAsync($"{shortPath}?culture={culture}");
                using var aliasResponse = await client.GetAsync($"{alias}?culture={culture}");
                Assert.Equal(HttpStatusCode.OK, shortResponse.StatusCode);
                Assert.Equal(HttpStatusCode.OK, aliasResponse.StatusCode);
                if (feature == "Quotation")
                {
                    Assert.Equal("noindex, follow", Assert.Single(aliasResponse.Headers.GetValues("X-Robots-Tag")));
                }
                var shortHtml = WebUtility.HtmlDecode(await shortResponse.Content.ReadAsStringAsync());
                var aliasHtml = WebUtility.HtmlDecode(await aliasResponse.Content.ReadAsStringAsync());
                Assert.Contains($"<html lang=\"{culture}\"", aliasHtml, StringComparison.Ordinal);
                var title = Regex.Match(shortHtml, "<title>(.*?)</title>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                var aliasTitle = Regex.Match(aliasHtml, "<title>(.*?)</title>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                Assert.True(title.Success && aliasTitle.Success);
                Assert.Equal(title.Groups[1].Value, aliasTitle.Groups[1].Value);
                Assert.Equal(DocumentMetadata(shortHtml), DocumentMetadata(aliasHtml));
            }
        }
    }

    private static string[] DocumentMetadata(string html) => Regex.Matches(
        html,
        "<link\\s+[^>]*\\brel=\"(?:canonical|alternate)\"[^>]*>|<meta\\s+[^>]*\\bproperty=\"og:url\"[^>]*>",
        RegexOptions.IgnoreCase).Select(match => match.Value).ToArray();

    [Fact]
    public async Task NoncanonicalPublicShortRoute_RedirectsWithoutDroppingEnglishCulture()
    {
        await using var host = CreateHost("About", true);
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        using var response = await client.GetAsync("/About?culture=en");
        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal("/about?culture=en", response.Headers.Location?.OriginalString);
    }

    [Theory]
    [InlineData("Contact", "/Contact")]
    [InlineData("Contact", "/Contact/Index")]
    [InlineData("Quotation", "/Quotation")]
    [InlineData("Quotation", "/Quotation/Index")]
    public async Task InquiryPost_WithoutAntiforgeryIsRejectedInBothRouteOwners(string feature, string path)
    {
        foreach (bool active in new[] { true, false })
        {
            await using var host = CreateHost(feature, active);
            using var client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
            using var response = await client.PostAsync($"{path}?handler=SubmitRequest", new FormUrlEncodedContent([]));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }

    [Fact]
    public async Task QuotationIndex_DoesNotAllowAdditionalPathSegments()
    {
        await using var host = CreateHost("Quotation", true);
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        using var response = await client.GetAsync("/quotation/index/extra");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("noindex, follow", Assert.Single(response.Headers.GetValues("X-Robots-Tag")));
    }

    private WebApplicationFactory<Program> CreateHost(string feature, bool active) => factory.WithWebHostBuilder(builder =>
    {
        builder.UseSetting($"BlazorRouting:{feature}", active.ToString());
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<ICountryClient>();
            services.AddSingleton<ICountryClient, CountryStub>();
            services.RemoveAll<ICareerClient>();
            services.AddSingleton<ICareerClient, CareerStub>();
            services.RemoveAll<IContactClient>();
            services.AddSingleton<IContactClient, NoWrites>();
            services.RemoveAll<IQuotationClient>();
            services.AddSingleton<IQuotationClient, NoWrites>();
            services.RemoveAll<IQuotationFileClient>();
            services.AddSingleton<IQuotationFileClient, NoWrites>();
            services.RemoveAll<INotificationClient>();
            services.AddSingleton<INotificationClient, NoWrites>();
            services.RemoveAll<IAntiBotVerifier>();
            services.AddSingleton<IAntiBotVerifier, NoWrites>();
        });
    });

    internal sealed class CountryStub : ICountryClient
    {
        public Task<ServiceResponse<IReadOnlyList<Country>>> GetCountriesAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new ServiceResponse<IReadOnlyList<Country>>([], true));
    }

    private sealed class CareerStub : ICareerClient
    {
        public Task<CareerListing> GetListingAsync(CareerSort sort, string? search, int pageIndex, int pageSize, CancellationToken cancellationToken) =>
            Task.FromResult(new CareerListing([], CareerOfferPage.Empty(pageIndex), true));
        public Task<ServiceResponse<CareerOfferPage>> GetOffersAsync(CareerSort sort, string? search, int pageIndex, int pageSize, CancellationToken cancellationToken) =>
            Task.FromResult(new ServiceResponse<CareerOfferPage>(CareerOfferPage.Empty(pageIndex), true));
        public Task<ServiceResponse<CareerOffer>> GetOfferAsync(int offerId, CancellationToken cancellationToken) =>
            Task.FromResult(new ServiceResponse<CareerOffer>(null, true));
        public Task<ServiceResponse<IReadOnlyList<CareerLevel>>> GetLevelsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new ServiceResponse<IReadOnlyList<CareerLevel>>([], true));
    }

    private sealed class NoWrites : IContactClient, IQuotationClient, IQuotationFileClient, INotificationClient, IAntiBotVerifier
    {
        public Task<ContactSubmissionResult> SubmitAsync(ContactSubmission submission, CancellationToken cancellationToken) => throw new InvalidOperationException("Unexpected contact write");
        public Task<QuotationRequestResult> CreateRequestAsync(QuotationRequestSubmission submission, string idempotencyKey, CancellationToken cancellationToken) => throw new InvalidOperationException("Unexpected quotation write");
        public Task<QuotationFileResult> UploadAndLinkAsync(int requestId, Guid submissionId, IReadOnlyList<QuotationUpload> files, CancellationToken cancellationToken) => throw new InvalidOperationException("Unexpected file write");
        public Task<NotificationResult> SendAsync(NotificationChannel channel, EmailNotification notification, CancellationToken cancellationToken) => throw new InvalidOperationException("Unexpected notification");
        public Task<bool> VerifyAsync(string? token, string expectedAction, CancellationToken cancellationToken) => throw new InvalidOperationException("Unexpected verification");
    }
}
