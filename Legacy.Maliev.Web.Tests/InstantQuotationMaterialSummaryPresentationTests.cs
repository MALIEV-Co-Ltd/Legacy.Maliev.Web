using System.Collections;
using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Application.Pricing;
using Legacy.Maliev.Web.Components.Pages.InstantQuotation;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Local material summaries are presentation only, without upload or quote authority.</summary>
public sealed class InstantQuotationMaterialSummaryPresentationTests
{
    [Theory]
    [InlineData("en", "parsing")]
    [InlineData("th", "parsing")]
    [InlineData("en", "pending")]
    [InlineData("th", "pending")]
    [InlineData("en", "failed")]
    [InlineData("th", "failed")]
    [InlineData("en", "cleared")]
    [InlineData("th", "cleared")]
    public async Task UnknownLocalMaterial_RendersDashWithoutInventingConfigurationOrAuthority(string culture, string state)
    {
        await RenderAsync(culture, state, (html, component) =>
        {
            Assert.Equal("—", Summary(html));
            Assert.Contains("local-presentation.stl", WebUtility.HtmlDecode(html), StringComparison.Ordinal);
            Assert.DoesNotContain("PLA 1×", WebUtility.HtmlDecode(html), StringComparison.Ordinal);
            Assert.DoesNotMatch("<article\\b[^>]*\\sdata-workflow-part(?:\\s|=|>)", html);
            Assert.DoesNotContain("data-workflow-part-summary", html, StringComparison.Ordinal);
            Assert.DoesNotContain("data-workflow-price-tier", html, StringComparison.Ordinal);
            Assert.Null(Field("workflow").GetValue(component));
            Assert.Equal(state == "cleared" ? 1 : 0, Map(component, "pendingConfigurations").Count);
            if (state == "failed")
            {
                Assert.Contains("data-workflow-file-error-badge", html, StringComparison.Ordinal);
            }
            else
            {
                Assert.Contains("data-workflow-pending-parse", html, StringComparison.Ordinal);
                Assert.Contains("role=\"status\"", html, StringComparison.Ordinal);
            }
        });
    }

    [Theory]
    [InlineData("en")]
    [InlineData("th")]
    public async Task KnownInitialLocalMaterial_RendersPlaOneWithBlackWithoutAuthority(string culture)
    {
        await RenderAsync(culture, "default", (html, component) =>
        {
            Assert.Equal("PLA 1×", Summary(html));
            var intent = Map(component, "pendingConfigurations")["local-preview"]!;
            var configuration = Assert.IsType<InstantQuotationPartConfiguration>(
                intent.GetType().GetProperty("Configuration")!.GetValue(intent));
            Assert.Equal(new InstantQuotationPartConfiguration("PLA", "Black", 1), configuration);
            Assert.Null(Field("workflow").GetValue(component));
            Assert.DoesNotContain("data-workflow-part-summary", html, StringComparison.Ordinal);
            Assert.DoesNotContain("data-workflow-price-tier", html, StringComparison.Ordinal);
            Assert.Contains("data-workflow-pending-parse", html, StringComparison.Ordinal);
        });
    }

    [Theory]
    [InlineData("en")]
    [InlineData("th")]
    public async Task ReservedUnadmittedUpload_RendersDashWithoutCreatingLocalConfigurationOrQuote(string culture)
    {
        await using var coordinator = new InstantQuotationWorkflowCoordinator(
            new EmptySessionStore(), new UnusedUploadClient(), new InstantQuotationPricingService(), null);
        await coordinator.InitializeAsync(default);
        var localId = Assert.Single(coordinator.ReserveUploads([new InstantQuotationWorkflowUploadFile(
            "local-presentation.stl", "model/stl", 4,
            new(1, new string('a', 64), 10, 10, 10, 1_000, 600,
                Enumerable.Repeat(100.0, 64).ToArray(), Enumerable.Repeat(40.0, 64).ToArray(),
                100, 1, true, false, false, 0.8),
            _ => throw new InvalidOperationException("Reserved presentation must not open the file."))]));
        await RenderAsync(culture, "reserved", (html, component) =>
        {
            Assert.Same(coordinator, Field("workflow").GetValue(component));
            Assert.Equal(localId, Assert.Single(coordinator.Uploads).LocalId);
            Assert.Equal(InstantQuotationWorkflowUploadStatus.Pending, Assert.Single(coordinator.Uploads).Status);
            Assert.Empty(coordinator.Parts);
            Assert.Null(coordinator.OrderQuote);
            Assert.False(coordinator.HasCompleteAuthoritativeQuote);
            Assert.Empty(Map(component, "pendingConfigurations"));
            Assert.Equal("—", Summary(html));
            Assert.Contains("data-workflow-upload-item", html, StringComparison.Ordinal);
            Assert.DoesNotContain("data-workflow-pending-parse", html, StringComparison.Ordinal);
            Assert.Contains("local-presentation.stl", WebUtility.HtmlDecode(html), StringComparison.Ordinal);
            Assert.DoesNotContain("PLA 1×", WebUtility.HtmlDecode(html), StringComparison.Ordinal);
            Assert.DoesNotContain("data-workflow-part-summary", html, StringComparison.Ordinal);
            Assert.DoesNotContain("data-workflow-price-tier", html, StringComparison.Ordinal);
        }, coordinator);
    }

    private static string Summary(string html)
    {
        var match = Assert.Single(Regex.Matches(html,
            "<div\\b[^>]*data-workflow-local-configuration-summary[^>]*>([^<]*)</div>").Cast<Match>());
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private static async Task RenderAsync(string culture, string state, Action<string, InstantQuotationWorkflow> assert,
        InstantQuotationWorkflowCoordinator? coordinator = null)
    {
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            var activator = new LocalStateActivator(state, coordinator);
            using var services = new ServiceCollection().AddLogging()
                .AddLocalization(options => options.ResourcesPath = "Resources")
                .AddSingleton<IComponentActivator>(activator)
                .AddSingleton<IJSRuntime>(new UnusedJavaScript())
                .AddSingleton<NavigationManager>(new InstantQuotationTestNavigationManager())
                .BuildServiceProvider();
            await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
            await renderer.Dispatcher.InvokeAsync(async () =>
            {
                var root = await renderer.RenderComponentAsync<InstantQuotationWorkflow>(ParameterView.Empty);
                assert(root.ToHtmlString(), Assert.IsType<InstantQuotationWorkflow>(activator.Workflow));
            });
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    private static FieldInfo Field(string name) => typeof(InstantQuotationWorkflow)
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static IDictionary Map(InstantQuotationWorkflow component, string name) =>
        (IDictionary)Field(name).GetValue(component)!;

    private sealed class LocalStateActivator(string state, InstantQuotationWorkflowCoordinator? coordinator) : IComponentActivator
    {
        internal InstantQuotationWorkflow? Workflow { get; private set; }

        public IComponent CreateInstance(Type componentType)
        {
            var component = (IComponent)Activator.CreateInstance(componentType)!;
            if (component is not InstantQuotationWorkflow workflow) return component;
            Workflow = workflow;
            if (coordinator is not null)
            {
                Field("workflow").SetValue(workflow, coordinator);
                Map(workflow, "previewKeys").Add(Assert.Single(coordinator.Uploads).LocalId, "reserved-preview");
                return component;
            }
            object Local(string typeName, params object[] arguments) => Activator.CreateInstance(
                typeof(InstantQuotationWorkflow).GetNestedType(typeName, BindingFlags.NonPublic)!,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null, args: arguments, culture: CultureInfo.InvariantCulture)!;
            if (state == "failed")
            {
                Map(workflow, "failedPreviewSelections").Add("local-preview",
                    Local("LocalPreviewFailure", "local-presentation.stl", "unreadable"));
            }
            else
            {
                Map(workflow, "pendingPreviewSelections").Add("local-preview",
                    Local("PendingLocalPreview", "local-presentation.stl", 1L));
                if (state is "default" or "cleared")
                {
                    var intent = Local("PendingLocalConfiguration", "local-presentation.stl", 1L);
                    if (state == "cleared")
                    {
                        // Source's defensive null presentation, not an accepted configuration mutation.
                        intent.GetType().GetProperty("Configuration")!.SetValue(intent,
                            new InstantQuotationPartConfiguration(null!, "Black", 1));
                    }
                    Map(workflow, "pendingConfigurations").Add("local-preview", intent);
                }
                if (state == "pending") Field("selectedPendingPreviewKey").SetValue(workflow, "local-preview");
            }
            return component;
        }
    }

    private sealed class EmptySessionStore : IInstantQuotationSessionStore
    {
        public Task<InstantQuotationSessionState> CreateAsync(string? ownerIdentity, InstantQuotationOrderState requestState, CancellationToken cancellationToken) =>
            Task.FromResult(new InstantQuotationSessionState("local-presentation", "local-submission", requestState, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        public Task<InstantQuotationSessionState?> GetAsync(string sessionId, string? ownerIdentity, CancellationToken cancellationToken) => throw new InvalidOperationException("No restore expected.");
        public Task<bool> PutAsync(InstantQuotationSessionState session, string? ownerIdentity, CancellationToken cancellationToken) => throw new InvalidOperationException("No persistence expected.");
        public Task<bool> RemoveAsync(string sessionId, string? ownerIdentity, CancellationToken cancellationToken) => throw new InvalidOperationException("No removal expected.");
    }

    private sealed class UnusedUploadClient : IInstantQuotationUploadClient
    {
        public Task<InstantQuotationUploadResult> UploadAsync(string sessionId, string? ownerIdentity, Stream content, string fileName, string contentType, long contentLength, InstantQuotationGeometryClaim geometryClaim, string operationId, CancellationToken cancellationToken) => throw new InvalidOperationException("No upload expected.");
        public Task<InstantQuotationRemoveResult> RemoveAsync(string sessionId, string? ownerIdentity, InstantQuotationUploadReference uploadReference, string operationId, CancellationToken cancellationToken) => throw new InvalidOperationException("No removal expected.");
        public Task<InstantQuotationFinalizationResult> FinalizeAsync(string sessionId, string? ownerIdentity, int quotationRequestId, IReadOnlyList<InstantQuotationUploadReference> uploadReferences, string operationId, CancellationToken cancellationToken) => throw new InvalidOperationException("No finalization expected.");
    }

    private sealed class UnusedJavaScript : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            ValueTask.FromResult(default(TValue)!);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            ValueTask.FromResult(default(TValue)!);
    }
}
