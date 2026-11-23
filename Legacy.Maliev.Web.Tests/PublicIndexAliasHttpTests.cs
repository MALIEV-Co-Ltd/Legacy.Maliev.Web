using System.Net;
using System.Text.RegularExpressions;
using Legacy.Maliev.Web.Application;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;

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
    public async Task IndexAlias_RendersSameLocalizedPageAsShortRoute(string shortPath, string alias, string feature)
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
                Assert.Contains($"<html lang=\"{culture}\"", shortHtml, StringComparison.Ordinal);
                Assert.Equal(PageHeading(shortHtml), PageHeading(aliasHtml));
                var title = Regex.Match(shortHtml, "<title>(.*?)</title>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                var aliasTitle = Regex.Match(aliasHtml, "<title>(.*?)</title>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                Assert.True(title.Success && aliasTitle.Success);
                Assert.Equal(title.Groups[1].Value, aliasTitle.Groups[1].Value);
                Assert.Equal(DocumentMetadata(shortHtml), DocumentMetadata(aliasHtml));
            }
        }
    }

    private static string PageHeading(string html)
    {
        var heading = Regex.Match(html, "<h1\\b[^>]*>(.*?)</h1>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        Assert.True(heading.Success);
        var text = Regex.Replace(heading.Groups[1].Value, "<[^>]+>", "").Trim();
        Assert.NotEmpty(text);
        return text;
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

    [Fact]
    public async Task QuotationIndex_DoesNotAllowAdditionalPathSegments()
    {
        await using var host = CreateHost("Quotation", true);
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        using var response = await client.GetAsync("/quotation/index/extra");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("noindex, follow", Assert.Single(response.Headers.GetValues("X-Robots-Tag")));
    }

    [Theory]
    [InlineData("/Contact/Index", "Contact", true)]
    [InlineData("/Contact/Index", "Contact", false)]
    [InlineData("/Quotation/Index", "Quotation", true)]
    [InlineData("/Quotation/Index", "Quotation", false)]
    public async Task IndexAlias_PostSelectsRazorPageAndRejectsMissingAntiforgeryToken(string alias, string feature, bool active)
    {
        await using var host = CreateHost(feature, active);
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        var endpoints = host.Services.GetRequiredService<EndpointDataSource>();
        var endpoint = Assert.Single(endpoints.Endpoints.OfType<RouteEndpoint>(), candidate =>
            string.Equals(candidate.RoutePattern.RawText?.Trim('/'), alias.Trim('/'), StringComparison.OrdinalIgnoreCase)
            && candidate.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains("POST", StringComparer.OrdinalIgnoreCase) == true);
        Assert.Equal(alias, endpoint.Metadata.GetMetadata<PageActionDescriptor>()?.ViewEnginePath);

        using var content = new FormUrlEncodedContent([]);
        using var response = await client.PostAsync($"{alias}?handler=SubmitRequest&culture=en", content);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    private WebApplicationFactory<Program> CreateHost(string feature, bool active) => factory.WithWebHostBuilder(builder =>
    {
        builder.UseSetting($"BlazorRouting:{feature}", active.ToString());
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<IHttpMessageHandlerBuilderFilter, RejectOutboundHttpFilter>();
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

    private sealed class RejectOutboundHttpFilter : IHttpMessageHandlerBuilderFilter
    {
        public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next) => builder =>
        {
            next(builder);
            builder.AdditionalHandlers.Insert(0, new RejectOutboundHttpHandler());
        };
    }

    private sealed class RejectOutboundHttpHandler : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Unexpected outbound HTTP in the public Index GET fixture");
    }

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
