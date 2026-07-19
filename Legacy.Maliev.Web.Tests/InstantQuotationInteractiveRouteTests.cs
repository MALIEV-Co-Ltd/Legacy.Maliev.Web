using System.Net;
using System.Reflection;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Components.Pages.InstantQuotation;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace Legacy.Maliev.Web.Tests;

public sealed class InstantQuotationInteractiveRouteTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string WorkflowNamespace = "Legacy.Maliev.Web.Components.Pages.InstantQuotation";
    private readonly HttpClient client;

    public InstantQuotationInteractiveRouteTests(WebApplicationFactory<Program> factory)
    {
        client = factory.WithWebHostBuilder(builder =>
            {
                builder.UseSetting("environment", "Testing");
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<ICountryClient>();
                    services.AddSingleton<ICountryClient, StubCountryClient>();
                });
            })
            .CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                BaseAddress = new Uri("https://localhost"),
            });
    }

    [Fact]
    public void Route_RemainsStaticSsr_WithExactlyOneInteractiveServerWorkflowIsland()
    {
        var root = FindRepositoryRoot();
        var web = Path.Combine(root, "Legacy.Maliev.Web");
        var page = File.ReadAllText(Path.Combine(
            web,
            "Components",
            "Pages",
            "InstantQuotation",
            "InstantQuotationPage.razor"));
        var content = File.ReadAllText(Path.Combine(
            web,
            "Components",
            "Pages",
            "InstantQuotation",
            "ThreeDimensionalPrintingEstimateContent.razor"));
        var program = File.ReadAllText(Path.Combine(web, "Program.cs"));
        var renderModeOwners = Directory.EnumerateFiles(web, "*.razor", SearchOption.AllDirectories)
            .Where(path => File.ReadAllText(path).Contains("@rendermode", StringComparison.Ordinal))
            .ToArray();

        Assert.Contains("@page \"/InstantQuotation/3D-Printing\"", page, StringComparison.Ordinal);
        Assert.Contains("<PageTitle>", page, StringComparison.Ordinal);
        Assert.Contains("<HeadContent>", page, StringComparison.Ordinal);
        Assert.DoesNotContain("@rendermode", page, StringComparison.Ordinal);
        Assert.Contains("data-migration-renderer=\"blazor-static-ssr\"", content, StringComparison.Ordinal);
        Assert.Equal(1, Count(content, "<InstantQuotationWorkflow"));
        Assert.Equal(1, Count(content, "@rendermode=\"InteractiveServer\""));
        Assert.Single(renderModeOwners);
        Assert.EndsWith("ThreeDimensionalPrintingEstimateContent.razor", renderModeOwners[0], StringComparison.Ordinal);
        Assert.Contains("AddInteractiveServerComponents", program, StringComparison.Ordinal);
        Assert.Contains("AddInteractiveServerRenderMode", program, StringComparison.Ordinal);
        Assert.DoesNotContain("MapBlazorHub", program, StringComparison.Ordinal);
    }

    [Fact]
    public void PrimaryJourney_IsUploadFirst_AndExposesAllWorkflowMarkers()
    {
        var source = ReadWorkflowSource();
        var document = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "Legacy.Maliev.Web",
            "Components",
            "Pages",
            "InstantQuotation",
            "ThreeDimensionalPrintingEstimateContent.razor"));

        Assert.Contains("Start by uploading a file", source, StringComparison.Ordinal);
        Assert.DoesNotContain("instant-quote__header", document, StringComparison.Ordinal);
        Assert.Contains("data-workflow-empty-dropzone", source, StringComparison.Ordinal);
        Assert.Contains("iq-empty-dropzone__icon", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Height (mm)", document, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Solid volume", document, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Footprint", document, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("areaProfile", document, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("perimeterProfile", document, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("GetEstimate", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("GetOrderTotal", source, StringComparison.OrdinalIgnoreCase);

        foreach (var marker in new[]
        {
            "data-workflow-upload",
            "data-workflow-viewer",
            "data-workflow-parts",
            "data-workflow-configuration",
            "data-workflow-dfm-status",
            "data-workflow-material-color-quantity",
            "data-workflow-part-price",
            "data-workflow-order-summary",
            "data-workflow-lead-time",
            "data-workflow-review",
            "data-workflow-customer-details",
            "data-workflow-submitted",
        })
        {
            Assert.Contains(marker, source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Workflow_UsesNativeAccessibleUploadAndFailClosedStatusSemantics()
    {
        var source = ReadWorkflowSource();
        var markup = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "Legacy.Maliev.Web",
            "Components",
            "Pages",
            "InstantQuotation",
            "InstantQuotationWorkflow.razor"));

        Assert.Contains("<label for=\"instant-quote-files\"", source, StringComparison.Ordinal);
        Assert.Contains("<InputFile id=\"instant-quote-files\"", source, StringComparison.Ordinal);
        Assert.Contains("accept=\".stl,.obj,.3mf,.glb,.gltf,.stp,.step,.igs,.iges\"", source, StringComparison.Ordinal);
        Assert.Contains("multiple", source, StringComparison.Ordinal);
        Assert.Contains("aria-live=\"polite\"", source, StringComparison.Ordinal);
        Assert.Contains("role=\"alert\"", source, StringComparison.Ordinal);
        Assert.Contains("aria-busy=\"@IsBusy", source, StringComparison.Ordinal);
        Assert.Contains("<button type=\"button\"", source, StringComparison.Ordinal);
        Assert.Contains("disabled", source, StringComparison.Ordinal);
        Assert.Contains("@onclick", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<div role=\"button\"", source, StringComparison.OrdinalIgnoreCase);

        foreach (var forbidden in new[]
        {
            "sessionId",
            "uploadReference",
            "storagePath",
            "access_token",
            "refresh_token",
            "serviceToken",
            "credential",
        })
        {
            Assert.DoesNotContain(forbidden, markup, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void WorkflowState_HasExactValues_AndMapsToExactVisibleSections()
    {
        var assembly = typeof(Program).Assembly;
        var stateType = assembly.GetType($"{WorkflowNamespace}.InstantQuotationWorkflowState");
        Assert.NotNull(stateType);
        Assert.True(stateType.IsEnum);
        Assert.Equal(
            ["Empty", "Uploading", "Uploaded", "Error", "MultiPart", "Configured", "Review", "CustomerDetails", "Submitted"],
            Enum.GetNames(stateType));

        var workflowType = assembly.GetType($"{WorkflowNamespace}.InstantQuotationWorkflow");
        Assert.NotNull(workflowType);
        var visibilityMethod = workflowType.GetMethod(
            "GetVisibleSections",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(visibilityMethod);

        AssertVisibility("Empty", upload: true);
        AssertVisibility("Uploading", upload: true, viewer: true, parts: true, configuration: true);
        AssertVisibility("Uploaded", upload: true, viewer: true, parts: true, configuration: true);
        AssertVisibility("Error", upload: true, error: true);
        AssertVisibility("MultiPart", upload: true, viewer: true, parts: true, configuration: true);
        AssertVisibility("Configured", upload: true, viewer: true, parts: true, configuration: true);
        AssertVisibility("Review", viewer: true, review: true);
        AssertVisibility("CustomerDetails", viewer: true, customerDetails: true);
        AssertVisibility("Submitted", submitted: true);

        void AssertVisibility(
            string state,
            bool upload = false,
            bool error = false,
            bool viewer = false,
            bool parts = false,
            bool configuration = false,
            bool review = false,
            bool customerDetails = false,
            bool submitted = false)
        {
            var value = Enum.Parse(stateType, state);
            var visibility = visibilityMethod.Invoke(null, [value]);
            Assert.NotNull(visibility);
            Assert.Equal(upload, ReadBoolean(visibility, "Upload"));
            Assert.Equal(error, ReadBoolean(visibility, "Error"));
            Assert.Equal(viewer, ReadBoolean(visibility, "Viewer"));
            Assert.Equal(parts, ReadBoolean(visibility, "Parts"));
            Assert.Equal(configuration, ReadBoolean(visibility, "Configuration"));
            Assert.Equal(review, ReadBoolean(visibility, "Review"));
            Assert.Equal(customerDetails, ReadBoolean(visibility, "CustomerDetails"));
            Assert.Equal(submitted, ReadBoolean(visibility, "Submitted"));
        }
    }

    [Theory]
    [InlineData("/InstantQuotation/3D-Printing")]
    [InlineData("/InstantQuotation/3D-Printing/")]
    [InlineData("/instantquotation/3d-printing?culture=en")]
    [InlineData("/INSTANTQUOTATION/3D-PRINTING?culture=th&source=review")]
    public async Task InstantQuotationRoute_LoadsScopedBlazorBootstrapAtBodyEnd(string requestPath)
    {
        using var response = await client.GetAsync(requestPath);
        var source = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<base href=\"/\" />", source, StringComparison.Ordinal);
        Assert.DoesNotContain("data-migration-component=\"public-footer\"", source, StringComparison.Ordinal);
        Assert.Equal(1, Count(source, "_framework/blazor.web.js"));
        Assert.True(
            source.IndexOf("/dist/app.min.js", StringComparison.Ordinal) <
            source.IndexOf("_framework/blazor.web.js", StringComparison.Ordinal));
        Assert.True(
            source.IndexOf("_framework/blazor.web.js", StringComparison.Ordinal) <
            source.IndexOf("</body>", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task OtherStaticSsrRoute_DoesNotLoadBlazorBootstrap()
    {
        using var response = await client.GetAsync("/legal?culture=en");
        var source = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("<base href=", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("_framework/blazor.web.js", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-migration-component=\"public-footer\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BlazorNegotiation_PostRemainsAvailableForTheInteractiveWorkflow()
    {
        using var response = await client.PostAsync(
            "/_blazor/negotiate?negotiateVersion=1",
            content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("th")]
    public async Task InstantQuotationQueryCulture_PersistsForTheInteractiveCircuit(string culture)
    {
        using var response = await client.GetAsync($"/InstantQuotation/3D-Printing?culture={culture}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cookie = Assert.Single(
            response.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith(
                $"{CookieRequestCultureProvider.DefaultCookieName}=",
                StringComparison.Ordinal));
        Assert.Contains($"c%3D{culture}%7Cuic%3D{culture}", cookie, StringComparison.Ordinal);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InstantQuotationUnknownQueryCulture_DoesNotPersistACookie()
    {
        using var response = await client.GetAsync("/InstantQuotation/3D-Printing?culture=unknown");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cookies = response.Headers.TryGetValues("Set-Cookie", out var values) ? values : [];
        Assert.DoesNotContain(
            cookies,
            value => value.StartsWith(
                $"{CookieRequestCultureProvider.DefaultCookieName}=",
                StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(InstantQuotationWorkflowState.Empty, "data-workflow-upload")]
    [InlineData(InstantQuotationWorkflowState.Uploading, "data-workflow-upload|data-workflow-viewer|data-workflow-parts|data-workflow-configuration")]
    [InlineData(InstantQuotationWorkflowState.Uploaded, "data-workflow-upload|data-workflow-viewer|data-workflow-parts|data-workflow-configuration")]
    [InlineData(InstantQuotationWorkflowState.Error, "data-workflow-upload|role=\"alert\"")]
    [InlineData(InstantQuotationWorkflowState.MultiPart, "data-workflow-upload|data-workflow-viewer|data-workflow-parts|data-workflow-configuration")]
    [InlineData(InstantQuotationWorkflowState.Configured, "data-workflow-upload|data-workflow-viewer|data-workflow-parts|data-workflow-configuration")]
    [InlineData(InstantQuotationWorkflowState.Review, "data-workflow-viewer|data-workflow-review")]
    [InlineData(InstantQuotationWorkflowState.CustomerDetails, "data-workflow-viewer|data-workflow-customer-details")]
    [InlineData(InstantQuotationWorkflowState.Submitted, "data-workflow-submitted")]
    public async Task WorkflowState_RendersOnlyItsMappedSections(
        InstantQuotationWorkflowState state,
        string expectedMarkers)
    {
        var html = await RenderWorkflowAsync(state);
        var expected = expectedMarkers.Split('|');
        var allMarkers = new[]
        {
            "data-workflow-upload",
            "data-workflow-viewer",
            "data-workflow-parts",
            "data-workflow-configuration",
            "data-workflow-review",
            "data-workflow-customer-details",
            "data-workflow-submitted",
        };

        Assert.Contains($"data-workflow-state=\"{state.ToString().ToLowerInvariant()}\"", html, StringComparison.Ordinal);
        foreach (var marker in allMarkers)
        {
            if (expected.Contains(marker, StringComparer.Ordinal))
            {
                Assert.Contains(marker, html, StringComparison.Ordinal);
            }
            else
            {
                Assert.DoesNotContain(marker, html, StringComparison.Ordinal);
            }
        }

        Assert.Equal(state is InstantQuotationWorkflowState.Uploading, html.Contains("aria-busy=\"true\"", StringComparison.Ordinal));
        Assert.Equal(state is InstantQuotationWorkflowState.Error, html.Contains("role=\"alert\"", StringComparison.Ordinal));
        Assert.Equal(
            state is InstantQuotationWorkflowState.Empty
                or InstantQuotationWorkflowState.Uploading
                or InstantQuotationWorkflowState.Uploaded
                or InstantQuotationWorkflowState.Error
                or InstantQuotationWorkflowState.MultiPart
                or InstantQuotationWorkflowState.Configured
                ? 1
                : 0,
            Count(html, "id=\"instant-quote-files\""));
        Assert.Equal(
            state is InstantQuotationWorkflowState.Uploading
                or InstantQuotationWorkflowState.Uploaded
                or InstantQuotationWorkflowState.MultiPart
                or InstantQuotationWorkflowState.Configured,
            html.Contains("iq-compact-dropzone", StringComparison.Ordinal));
        foreach (var forbidden in new[]
        {
            "protected-session",
            "opaque-upload",
            "storagePath",
            "access_token",
            "refresh_token",
            "serviceToken",
            "credential",
        })
        {
            Assert.DoesNotContain(forbidden, html, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Theory]
    [InlineData(InstantQuotationWorkflowState.Uploaded)]
    [InlineData(InstantQuotationWorkflowState.Review)]
    [InlineData(InstantQuotationWorkflowState.CustomerDetails)]
    public async Task OnlyWiredViewerActions_RenderEnabled(InstantQuotationWorkflowState state)
    {
        var html = await RenderWorkflowAsync(state);

        Assert.Contains("<button", html, StringComparison.Ordinal);
        var enabledButtons = System.Text.RegularExpressions.Regex.Matches(
            html,
            "<button(?![^>]* disabled)[^>]*>.*?</button>",
            System.Text.RegularExpressions.RegexOptions.Singleline);
        if (state is InstantQuotationWorkflowState.CustomerDetails)
        {
            Assert.Equal(5, enabledButtons.Count);
            Assert.Contains(enabledButtons, match => match.Value.Contains("Reset view", StringComparison.Ordinal));
            Assert.Contains(enabledButtons, match => match.Value.Contains("Back", StringComparison.Ordinal));
            Assert.Contains(enabledButtons, match => match.Value.Contains("Submit", StringComparison.Ordinal));
            return;
        }

        if (state is InstantQuotationWorkflowState.Review)
        {
            Assert.Equal(5, enabledButtons.Count);
            Assert.Contains(enabledButtons, match => match.Value.Contains("Reset view", StringComparison.Ordinal));
            Assert.Contains(enabledButtons, match => match.Value.Contains("Back", StringComparison.Ordinal));
            Assert.Contains(enabledButtons, match => match.Value.Contains("Customer details", StringComparison.Ordinal));
            return;
        }

        Assert.Equal(3, enabledButtons.Count);
        Assert.Contains(enabledButtons, match => match.Value.Contains("Reset view", StringComparison.Ordinal));
        Assert.Contains(enabledButtons, match => match.Value.Contains("Fit to view", StringComparison.Ordinal));
        Assert.Contains(enabledButtons, match => match.Value.Contains("Fullscreen", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("completed", "Ready for manufacturing", "Your quotation request was submitted.")]
    [InlineData("partial", "Request saved", "File processing is pending")]
    [InlineData("rejected", "Request not submitted", "Your request was not submitted.")]
    public async Task SubmittedOutcome_RendersTruthfulHeadingAndLiveStatus(
        string status,
        string expectedHeading,
        string expectedStatus)
    {
        var model = InstantQuotationCustomerDisplayModel.Empty with
        {
            SubmissionStatus = status,
            RequestReference = status is "rejected" ? null : 720,
            ProblemCategory = status is "rejected" ? "validation" : null,
        };
        var html = await RenderWorkflowAsync(InstantQuotationWorkflowState.Submitted, model);

        Assert.Contains(expectedHeading, html, StringComparison.Ordinal);
        Assert.Contains(expectedStatus, html, StringComparison.Ordinal);
        Assert.Contains("instant-quote__submission-icon", html, StringComparison.Ordinal);
        Assert.Equal(
            status is not "completed",
            html.Contains("instant-quote__submission-icon--warning", StringComparison.Ordinal));
        if (status is not "completed")
        {
            Assert.DoesNotContain("Ready for manufacturing", html, StringComparison.Ordinal);
            Assert.DoesNotContain("Your quotation request was submitted.", html, StringComparison.Ordinal);
        }
    }

    private static bool ReadBoolean(object target, string propertyName) =>
        (bool)(target.GetType().GetProperty(propertyName)?.GetValue(target)
            ?? throw new InvalidOperationException($"Visibility property {propertyName} was not found."));

    private static string ReadWorkflowSource()
    {
        var directory = Path.Combine(
            FindRepositoryRoot(),
            "Legacy.Maliev.Web",
            "Components",
            "Pages",
            "InstantQuotation");
        return string.Join(
            Environment.NewLine,
            File.ReadAllText(Path.Combine(directory, "InstantQuotationWorkflow.razor")),
            File.ReadAllText(Path.Combine(directory, "InstantQuotationWorkflow.razor.cs")),
            File.ReadAllText(Path.Combine(directory, "InstantQuotationWorkflowState.cs")));
    }

    private static int Count(string source, string value) =>
        source.Split(value, StringSplitOptions.None).Length - 1;

    private static async Task<string> RenderWorkflowAsync(
        InstantQuotationWorkflowState state,
        InstantQuotationCustomerDisplayModel? customerModel = null)
    {
        using var services = new ServiceCollection()
            .AddLogging()
            .AddLocalization(options => options.ResourcesPath = "Resources")
            .AddSingleton<IJSRuntime, NullJsRuntime>()
            .BuildServiceProvider();
        var loggerFactory = services.GetRequiredService<ILoggerFactory>();
        await using var renderer = new HtmlRenderer(services, loggerFactory);

        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var parameters = ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                ["InitialState"] = state,
                ["CustomerModel"] = customerModel ?? InstantQuotationCustomerDisplayModel.Empty,
            });
            var output = await renderer.RenderComponentAsync<InstantQuotationWorkflow>(parameters);
            return output.ToHtmlString();
        });
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

    private sealed class NullJsRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            ValueTask.FromResult(default(TValue)!);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args) => ValueTask.FromResult(default(TValue)!);
    }

    private sealed class StubCountryClient : ICountryClient
    {
        public Task<ServiceResponse<IReadOnlyList<Country>>> GetCountriesAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new ServiceResponse<IReadOnlyList<Country>>(
                [new Country(764, "Thailand", "Asia", "TH", "TH", "THA", null, null)],
                true));
    }
}
