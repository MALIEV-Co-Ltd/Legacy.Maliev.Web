using System.Net;
using System.Text.Json;
using Legacy.Maliev.Web.Application;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Legacy.Maliev.Web.Tests;

public sealed class InstantQuotationStaticSsrRouteTests : IClassFixture<TestingWebApplicationFactory>, IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> factory;

    public InstantQuotationStaticSsrRouteTests(TestingWebApplicationFactory factory)
    {
        this.factory = factory.WithWebHostBuilder(builder => builder
            .UseSetting("environment", "Testing")
            .UseSetting("Recaptcha:ProjectId", "test-project")
            .UseSetting("Recaptcha:SiteKey", "test-site-key"));
    }

    /// <summary>Completes setup after the per-test child factory has been created.</summary>
    /// <returns>A completed setup task.</returns>
    public Task InitializeAsync() => Task.CompletedTask;

    /// <summary>Releases this test's child host and its tracked clients without disposing the borrowed parent.</summary>
    /// <returns>A task that completes after the owned child factory has been disposed.</returns>
    public async Task DisposeAsync() => await factory.DisposeAsync();

    [Fact]
    public async Task BlazorRoute_OwnsDocumentGet_WhileLegacyEstimateQueryReturnsExactJsonContract()
    {
        using var client = CreateClient(factory);

        var page = await client.GetStringAsync("/InstantQuotation/3D-Printing?culture=en");
        using var response = await client.GetAsync(
            "/InstantQuotation/3D-Printing?handler=GetEstimate&material=PLA&dimensionZ=30&volume=20000&footprint=400&quantity=1&currency=THB");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Contains("data-migration-route-owner=\"blazor-static-ssr\"", page, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("fdm", json.RootElement.GetProperty("process").GetString());
        Assert.Equal("THB", json.RootElement.GetProperty("currency").GetString());
        Assert.True(json.RootElement.TryGetProperty("tiers", out _));
    }

    [Fact]
    public async Task LegacyOrderTotalQuery_ReturnsExactJsonContract()
    {
        using var client = CreateClient(factory);
        using var response = await client.GetAsync(
            "/InstantQuotation/3D-Printing?handler=GetOrderTotal&processes=fdm%2Cresin&subtotals=1200%2C1800&totalWeightGrams=500&totalBoundingCm3=2000&currency=THB");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(3_000, json.RootElement.GetProperty("printing").GetDouble(), 2);
        Assert.Equal(3_100, json.RootElement.GetProperty("priceBeforeVat").GetDouble(), 2);
        Assert.Equal("THB", json.RootElement.GetProperty("currency").GetString());
        Assert.True(json.RootElement.TryGetProperty("finalOrderPrice", out _));
    }

    [Fact]
    public async Task DisabledRoute_UsesRetainedRazorPageAndHandlers()
    {
        await using var fallbackFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("BlazorRouting:InstantQuotation", "false");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IInstantQuotationSubmissionService>();
                services.AddSingleton<IInstantQuotationSubmissionService, UnusedSubmissionService>();
            });
        });
        using var client = CreateClient(fallbackFactory);

        var page = await client.GetStringAsync("/InstantQuotation/3D-Printing?culture=en");
        using var response = await client.GetAsync(
            "/InstantQuotation/3D-Printing?handler=GetEstimate&material=PLA&dimensionZ=30&volume=20000&footprint=400&quantity=1&currency=THB");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.DoesNotContain("data-migration-route-owner=\"blazor-static-ssr\"", page, StringComparison.Ordinal);
        Assert.Contains("data-migration-renderer=\"blazor-static-ssr\"", page, StringComparison.Ordinal);
        Assert.True(json.RootElement.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task DisabledRoute_OrderTotalHandlerPreservesPreVatField()
    {
        await using var fallbackFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("BlazorRouting:InstantQuotation", "false");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IInstantQuotationSubmissionService>();
                services.AddSingleton<IInstantQuotationSubmissionService, UnusedSubmissionService>();
            });
        });
        using var client = CreateClient(fallbackFactory);
        using var response = await client.GetAsync(
            "/InstantQuotation/3D-Printing?handler=GetOrderTotal&processes=fdm%2Cresin&subtotals=1200%2C1800&totalWeightGrams=500&totalBoundingCm3=2000&currency=THB&culture=th");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3_100, json.RootElement.GetProperty("priceBeforeVat").GetDouble(), 2);
        Assert.Equal("THB", json.RootElement.GetProperty("currency").GetString());
    }

    [Theory]
    [InlineData(true, "en",
        "Instant 3D Printing Quote | MALIEV",
        "Upload a 3D model, choose material and quantity, and receive an online estimate for printing cost and lead time.",
        "3d print, 3d printing company, 3d printing model, 3d printing online, 3d printing service, 3d printing services, 3d printing website, 3d printing, online 3d printing, instant pricing, instant quotation")]
    [InlineData(false, "en",
        "Instant 3D Printing Quote | MALIEV",
        "Upload a 3D model, choose material and quantity, and receive an online estimate for printing cost and lead time.",
        "3d print, 3d printing company, 3d printing model, 3d printing online, 3d printing service, 3d printing services, 3d printing website, 3d printing, online 3d printing, instant pricing, instant quotation")]
    [InlineData(true, "th",
        "ประเมินราคาพิมพ์ 3D ออนไลน์ | MALIEV",
        "อัปโหลดไฟล์ 3D เลือกวัสดุและจำนวน เพื่อรับราคาประเมินงานพิมพ์ 3D และระยะเวลาผลิตออนไลน์",
        "ปริ้น 3d, ปริ้น 3 มิติ, พิมพ์ 3 มิติ, พิมพ์ 3d, รับปริ้น 3 มิติ, รับปริ้น 3d, ประเมินราคาทันที, instant pricing")]
    [InlineData(false, "th",
        "ประเมินราคาพิมพ์ 3D ออนไลน์ | MALIEV",
        "อัปโหลดไฟล์ 3D เลือกวัสดุและจำนวน เพื่อรับราคาประเมินงานพิมพ์ 3D และระยะเวลาผลิตออนไลน์",
        "ปริ้น 3d, ปริ้น 3 มิติ, พิมพ์ 3 มิติ, พิมพ์ 3d, รับปริ้น 3 มิติ, รับปริ้น 3d, ประเมินราคาทันที, instant pricing")]
    public async Task InstantQuotationDocument_PreservesOriginalSourceMetadataAcrossRenderers(
        bool useSsr, string culture, string title, string description, string keywords)
    {
        await using var host = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("BlazorRouting:InstantQuotation", useSsr.ToString());
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IInstantQuotationSubmissionService>();
                services.AddSingleton<IInstantQuotationSubmissionService, UnusedSubmissionService>();
            });
        });
        using var client = CreateClient(host);
        using var response = await client.GetAsync($"/instantquotation/3d-printing?culture={culture}");
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains($"<title>{title}</title>", html, StringComparison.Ordinal);
        Assert.Contains($"<meta name=\"description\" content=\"{description}\"", html, StringComparison.Ordinal);
        Assert.Contains($"<meta name=\"keywords\" content=\"{keywords}\"", html, StringComparison.Ordinal);
        Assert.Contains("data-migration-component=\"instant-quotation-three-dimensional-printing\"", html, StringComparison.Ordinal);
        Assert.Contains("data-migration-renderer=\"blazor-static-ssr\"", html, StringComparison.Ordinal);
        if (useSsr)
        {
            Assert.Contains("data-migration-route-owner=\"blazor-static-ssr\"", html, StringComparison.Ordinal);
        }
        else
        {
            Assert.DoesNotContain("data-migration-route-owner=\"blazor-static-ssr\"", html, StringComparison.Ordinal);
        }
    }

    private static HttpClient CreateClient(WebApplicationFactory<Program> application) =>
        application.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });

    private sealed class UnusedSubmissionService : IInstantQuotationSubmissionService
    {
        public Task<InstantQuotationSubmissionResult> SubmitAsync(
            string sessionId,
            string? ownerIdentity,
            InstantQuotationCustomerSubmission customer,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The fallback route GET must not submit an instant quotation.");
    }
}
