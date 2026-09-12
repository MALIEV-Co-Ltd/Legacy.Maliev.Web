using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Legacy.Maliev.Web.Tests;

public sealed partial class CncInstantQuotationPageTests : IClassFixture<TestingWebApplicationFactory>
{
    private readonly TestingWebApplicationFactory factory;

    public CncInstantQuotationPageTests(TestingWebApplicationFactory factory) => this.factory = factory;

    [Fact]
    public async Task Get_RendersTheCompleteProtectedCncWorkspace()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });

        using var response = await client.GetAsync("/instantquotation/cnc-machining?culture=en");
        string source = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.True(response.StatusCode == HttpStatusCode.OK, $"Expected OK but received {response.StatusCode}. Body: {source}");
        Assert.StartsWith("<!DOCTYPE html>", source.TrimStart(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<title>Instant CNC Machining Quote | MALIEV</title>", source, StringComparison.Ordinal);
        Assert.Contains("id=\"instant-quotation-component\"", source, StringComparison.Ordinal);
        Assert.Contains("id=\"model-viewer-canvas\"", source, StringComparison.Ordinal);
        Assert.Contains("id=\"quotation-config-panel\"", source, StringComparison.Ordinal);
        Assert.Contains("id=\"instant-quotation-form\"", source, StringComparison.Ordinal);
        Assert.Contains("id=\"quotation-form-token\"", source, StringComparison.Ordinal);
        Assert.Contains("/instantquotation/cnc-machining?Handler=UploadFile", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/instantquotation/cnc-machining?Handler=SubmitRequest", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/dist/cnc-quotation.min.js", source, StringComparison.Ordinal);
        Assert.Contains("new Worker(window.malievCncQuotationWorkerUrl", source, StringComparison.Ordinal);
        Assert.Matches(
            "window\\.malievModelWorkerUrl = '/src/app/js/model-viewer/model-viewer\\.worker\\.js\\?v=[0-9a-f]{16}';",
            source);
        Assert.Matches(
            "window\\.malievCncQuotationWorkerUrl = '/src/app/js/cnc-quotation/cnc-quotation\\.worker\\.js\\?v=[0-9a-f]{16}';",
            source);
        Assert.DoesNotContain("ServiceAuthentication__ClientSecret", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Authorization: Bearer", source, StringComparison.OrdinalIgnoreCase);

        Match formToken = FormTokenRegex().Match(source);
        Assert.True(formToken.Success);
        Assert.True(formToken.Groups["token"].Value.Length > 64);
    }

    [Fact]
    public void SourceDocument_RetainsResponsiveAccessibilityAndPlannerContracts()
    {
        string root = FindRepositoryRoot();
        string page = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Web", "Pages", "InstantQuotation", "CNC-Machining.cshtml"));

        Assert.Contains("@page \"/instantquotation/cnc-machining\"", page, StringComparison.Ordinal);
        Assert.Contains("@@media (max-width: 575.98px)", page, StringComparison.Ordinal);
        Assert.Contains("@@container iq-review-pane", page, StringComparison.Ordinal);
        Assert.Contains("prefers-reduced-motion: reduce", page, StringComparison.Ordinal);
        Assert.Contains("aria-live=\"polite\"", page, StringComparison.Ordinal);
        Assert.Contains("aria-describedby=\"cnc-detected-thread-hint\"", page, StringComparison.Ordinal);
        Assert.Contains("/cnc-quotation/cnc-plan-contracts.js", page, StringComparison.Ordinal);
        Assert.Contains("/cnc-quotation/cnc-material-catalog.js", page, StringComparison.Ordinal);
        Assert.Contains("/cnc-quotation/cnc-setup-planner.js", page, StringComparison.Ordinal);
        Assert.Contains("/cnc-quotation/cnc-engine.js", page, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/dist/cnc-quotation.min.js")]
    [InlineData("/src/vendor/three/OrbitControls.js")]
    [InlineData("/lib/three/3MFLoader.js")]
    [InlineData("/lib/three/GLTFLoader.js")]
    [InlineData("/src/app/js/model-viewer/model-viewer.js")]
    [InlineData("/src/app/js/model-viewer/model-viewer.worker.js")]
    [InlineData("/src/app/js/cnc-quotation/cnc-quotation.worker.js")]
    public async Task RequiredRuntimeAsset_IsDeliveredFromTheSameOrigin(string path)
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });

        using var response = await client.GetAsync(path);
        var content = await response.Content.ReadAsByteArrayAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEmpty(content);
    }

    [Fact]
    public async Task ModelWorker_ResponseReceivesItsIsolatedEvalPolicy()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });

        using var documentResponse = await client.GetAsync("/instantquotation/cnc-machining?culture=en");
        using var workerResponse = await client.GetAsync("/src/app/js/model-viewer/model-viewer.worker.js?v=test");
        using var otherScriptResponse = await client.GetAsync("/src/app/js/model-viewer/model-viewer.js?v=test");

        string documentPolicy = Assert.Single(documentResponse.Headers.GetValues("Content-Security-Policy"));
        string workerPolicy = Assert.Single(workerResponse.Headers.GetValues("Content-Security-Policy"));
        string otherScriptPolicy = Assert.Single(otherScriptResponse.Headers.GetValues("Content-Security-Policy"));

        Assert.DoesNotContain(" 'unsafe-eval'", documentPolicy, StringComparison.Ordinal);
        Assert.Contains(" 'unsafe-eval' 'wasm-unsafe-eval'", workerPolicy, StringComparison.Ordinal);
        Assert.DoesNotContain(" 'unsafe-eval'", otherScriptPolicy, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ModelWorker_PropagatesItsBuildQueryToNativeCadImports()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });

        using var response = await client.GetAsync("/src/app/js/model-viewer/model-viewer.worker.js?v=test");
        string source = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("typeof self.location.search === 'string'", source, StringComparison.Ordinal);
        Assert.Contains("cnc-native-dispatch.js' + query", source, StringComparison.Ordinal);
        Assert.Contains("cnc-native-topology.worker.js' + query", source, StringComparison.Ordinal);
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

    [GeneratedRegex("id=\"quotation-form-token\"[^>]*value=\"(?<token>[^\"]+)\"", RegexOptions.CultureInvariant)]
    private static partial Regex FormTokenRegex();
}
