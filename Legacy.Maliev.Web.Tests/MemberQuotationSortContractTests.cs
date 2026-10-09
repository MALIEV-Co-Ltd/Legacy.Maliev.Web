using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Components.Pages.Member;
using Legacy.Maliev.Web.Infrastructure;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Legacy.Maliev.Web.Tests;

public sealed class MemberQuotationSortContractTests
{
    [Theory]
    [InlineData("en", "QuotationId_Ascending", "Lowest quotation ID", "Highest quotation ID")]
    [InlineData("en", "QuotationId_Descending", "Lowest quotation ID", "Highest quotation ID")]
    [InlineData("th", "QuotationId_Ascending", "เลขที่ใบเสนอราคาจากน้อยไปมาก", "เลขที่ใบเสนอราคาจากมากไปน้อย")]
    [InlineData("th", "QuotationId_Descending", "เลขที่ใบเสนอราคาจากน้อยไปมาก", "เลขที่ใบเสนอราคาจากมากไปน้อย")]
    public async Task RenderedList_UsesSupportedIdChoicesAndPreservesQueryAndPagination(
        string culture, string sort, string ascendingLabel, string descendingLabel)
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            var quotation = new CustomerQuotation(9, 42, null, 30, new DateTime(2026, 12, 31),
                100, 7, 107, null, 107, 764, null, null, null, null, null, null, null);
            var model = MemberListLoaders.CreateQuotationDisplayModel(
                new([quotation], 2, 3, 101), "fixture & plate", sort, 50, []);
            using var services = new ServiceCollection().AddLogging()
                .AddLocalization(options => options.ResourcesPath = "Resources").BuildServiceProvider();
            await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
            var html = await renderer.Dispatcher.InvokeAsync(async () =>
                (await renderer.RenderComponentAsync<MemberQuotationsIndexContent>(
                    ParameterView.FromDictionary(new Dictionary<string, object?> { ["Model"] = model }))).ToHtmlString());
            var readable = WebUtility.HtmlDecode(html);
            Assert.Contains(ascendingLabel, readable, StringComparison.Ordinal);
            Assert.Contains(descendingLabel, readable, StringComparison.Ordinal);
            Assert.DoesNotContain("QuotationExpirationDate_Ascending", html, StringComparison.Ordinal);
            Assert.DoesNotContain("QuotationQuotedAmount_Descending", html, StringComparison.Ordinal);
            var sortControl = Regex.Match(html,
                "<select\\b[^>]*id=\"member-quotation-sort\"[^>]*>(.*?)</select>", RegexOptions.Singleline);
            Assert.True(sortControl.Success);
            Assert.Contains("name=\"sort\"", sortControl.Value, StringComparison.Ordinal);
            Assert.Contains("<label for=\"member-quotation-sort\">", html, StringComparison.Ordinal);
            Assert.Equal(4, Regex.Matches(sortControl.Groups[1].Value, "<option\\b").Count);
            Assert.Contains("value=\"QuotationCreatedDate_Ascending\"", sortControl.Value, StringComparison.Ordinal);
            Assert.Contains("value=\"QuotationCreatedDate_Descending\"", sortControl.Value, StringComparison.Ordinal);
            var selected = Regex.Match(sortControl.Groups[1].Value, "<option value=\"" + sort + "\"[^>]*>");
            Assert.True(selected.Success);
            Assert.Matches("\\sselected(?:\\s|=|>)", selected.Value);
            Assert.Contains("name=\"search\" value=\"fixture & plate\"", readable, StringComparison.Ordinal);
            Assert.Contains("<option value=\"50\" selected", html, StringComparison.Ordinal);
            Assert.Contains($"href=\"{model.NextHref}\"", readable, StringComparison.Ordinal);
            Assert.Contains($"href=\"{model.PreviousHref}\"", readable, StringComparison.Ordinal);
            Assert.Contains($"sort={sort}", model.NextHref!, StringComparison.Ordinal);
            Assert.Contains("search=fixture%20%26%20plate", model.NextHref!, StringComparison.Ordinal);
            Assert.Contains("size=50", model.NextHref!, StringComparison.Ordinal);
            Assert.Contains("<form method=\"get\" action=\"/member/quotations\"", html, StringComparison.Ordinal);
            Assert.Contains("name=\"index\" value=\"1\"", html, StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Theory]
    [InlineData("QuotationId_Ascending")]
    [InlineData("QuotationId_Descending")]
    public async Task TypedClient_ForwardsExactIdSortOnTheOwnedCustomerRoute(string sort)
    {
        using var handler = new RecordingHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://quotation.test/") };
        var client = new CustomerQuotationClient(new ClientFactory(http), new TokenProvider(),
            NullLogger<CustomerQuotationClient>.Instance);
        var result = await client.ListAsync(42, sort, "fixture & plate", 2, 50, default);
        Assert.True(result.Authorized);
        Assert.True(result.ServiceAvailable);
        Assert.NotNull(result.Page);
        Assert.Equal($"/quotations/customers/42?sort={sort}&search=fixture%20%26%20plate&index=2&size=50", handler.Path);
        Assert.Equal("Bearer controlled-service-token", handler.Authorization);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        internal string? Path { get; private set; }
        internal string? Authorization { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Null(Path);
            Path = request.RequestUri!.PathAndQuery;
            Authorization = request.Headers.Authorization!.ToString();
            Assert.Equal(HttpMethod.Get, request.Method);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"items\":[],\"pageIndex\":2,\"totalPages\":0,\"totalRecords\":0}", Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class ClientFactory(HttpClient http) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            Assert.Equal("quotations", name);
            return http;
        }
    }

    private sealed class TokenProvider : IServiceAccessTokenProvider
    {
        public ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<string?>("controlled-service-token");
        public void Invalidate(string token) => throw new InvalidOperationException("No invalidation expected.");
    }
}
