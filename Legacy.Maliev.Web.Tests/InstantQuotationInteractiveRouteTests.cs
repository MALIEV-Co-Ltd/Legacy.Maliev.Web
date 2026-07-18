using System.Net;
using System.Reflection;
using Legacy.Maliev.Web.Components.Pages.InstantQuotation;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace Legacy.Maliev.Web.Tests;

public sealed class InstantQuotationInteractiveRouteTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string WorkflowNamespace = "Legacy.Maliev.Web.Components.Pages.InstantQuotation";
    private readonly HttpClient client;

    public InstantQuotationInteractiveRouteTests(WebApplicationFactory<Program> factory)
    {
        client = factory.WithWebHostBuilder(builder => builder.UseSetting("environment", "Testing"))
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

        Assert.Contains("Start by uploading a file", document, StringComparison.Ordinal);
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
        AssertVisibility("Uploading", upload: true);
        AssertVisibility("Uploaded", viewer: true, parts: true, configuration: true);
        AssertVisibility("Error", upload: true, error: true);
        AssertVisibility("MultiPart", viewer: true, parts: true, configuration: true);
        AssertVisibility("Configured", viewer: true, parts: true, configuration: true);
        AssertVisibility("Review", viewer: true, parts: true, configuration: true, review: true);
        AssertVisibility("CustomerDetails", customerDetails: true);
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
    [InlineData("/instantquotation/3d-printing?culture=en")]
    [InlineData("/INSTANTQUOTATION/3D-PRINTING?culture=th&source=review")]
    public async Task InstantQuotationRoute_LoadsScopedBlazorBootstrapAtBodyEnd(string requestPath)
    {
        using var response = await client.GetAsync(requestPath);
        var source = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
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
        Assert.DoesNotContain("_framework/blazor.web.js", source, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(InstantQuotationWorkflowState.Empty, "data-workflow-upload")]
    [InlineData(InstantQuotationWorkflowState.Uploading, "data-workflow-upload")]
    [InlineData(InstantQuotationWorkflowState.Uploaded, "data-workflow-viewer|data-workflow-parts|data-workflow-configuration")]
    [InlineData(InstantQuotationWorkflowState.Error, "data-workflow-upload|role=\"alert\"")]
    [InlineData(InstantQuotationWorkflowState.MultiPart, "data-workflow-viewer|data-workflow-parts|data-workflow-configuration")]
    [InlineData(InstantQuotationWorkflowState.Configured, "data-workflow-viewer|data-workflow-parts|data-workflow-configuration")]
    [InlineData(InstantQuotationWorkflowState.Review, "data-workflow-viewer|data-workflow-parts|data-workflow-configuration|data-workflow-review")]
    [InlineData(InstantQuotationWorkflowState.CustomerDetails, "data-workflow-customer-details")]
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
            Assert.Empty(enabledButtons);
            return;
        }

        Assert.Equal(3, enabledButtons.Count);
        Assert.Contains(enabledButtons, match => match.Value.Contains("Reset view", StringComparison.Ordinal));
        Assert.Contains(enabledButtons, match => match.Value.Contains("Fit to view", StringComparison.Ordinal));
        Assert.Contains(enabledButtons, match => match.Value.Contains("Fullscreen", StringComparison.Ordinal));
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

    private static async Task<string> RenderWorkflowAsync(InstantQuotationWorkflowState state)
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
}
