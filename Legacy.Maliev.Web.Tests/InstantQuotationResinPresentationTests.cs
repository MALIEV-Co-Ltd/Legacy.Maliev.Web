using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Application.Pricing;
using Legacy.Maliev.Web.Components.Pages.InstantQuotation;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Actual rendered controls and popup snapshot; synthetic part state is not upload or browser admission proof.</summary>
public sealed class InstantQuotationResinPresentationTests
{
    [Theory]
    [InlineData("en", "Standard resin")]
    [InlineData("th", "การพิมพ์เรซินมาตรฐาน")]
    public async Task Workflow_DisablesOnlyResinBuildPreferenceControls(string culture, string label)
    {
        using var cultures = new CultureScope(culture);
        foreach (var material in new[] { "M68", "PLA" })
        {
            var part = Part(material, BuildPreference.Standard);
            var store = new SessionStore(part);
            await using var coordinator = new InstantQuotationWorkflowCoordinator(
                store, new UnusedUploadClient(), new InstantQuotationPricingService(), null,
                authoritativePricingService: SyntheticAuthoritativePricingTestService.Instance);
            await coordinator.InitializeAsync("presentation-session", default);
            var activator = new CapturingActivator(coordinator);
            using var services = Services(activator, new RecordingJavaScript());
            await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
            var html = await renderer.Dispatcher.InvokeAsync(async () =>
                (await renderer.RenderComponentAsync<InstantQuotationWorkflow>(ParameterView.Empty)).ToHtmlString());

            var fieldsets = Regex.Matches(html, "<fieldset\\b[^>]*data-workflow-build-preference[^>]*>[\\s\\S]*?</fieldset>");
            var fieldset = Assert.Single(fieldsets.Cast<Match>()).Value;
            var opening = fieldset[..(fieldset.IndexOf('>') + 1)];
            var disabled = Regex.IsMatch(opening, "\\sdisabled(?:\\s|=|>)");
            Assert.Equal(material == "M68", disabled);
            Assert.Equal(3, Regex.Matches(fieldset, "type=\"radio\"").Count);
            if (material == "M68")
            {
                Assert.Contains("data-resin-build-preference", fieldset, StringComparison.Ordinal);
                Assert.Contains(label, WebUtility.HtmlDecode(fieldset), StringComparison.Ordinal);
            }
            else
            {
                Assert.DoesNotContain("data-resin-build-preference", fieldset, StringComparison.Ordinal);
            }

            var materialCards = Regex.Matches(html, "<div\\b[^>]*role=\"radio\"[^>]*data-material-key=\"[^\"]+\"[^>]*>");
            Assert.NotEmpty(materialCards.Cast<Match>());
            foreach (Match card in materialCards)
            {
                Assert.DoesNotMatch("\\sdisabled(?:\\s|=|>)", card.Value);
                Assert.DoesNotContain("aria-disabled=\"true\"", card.Value, StringComparison.Ordinal);
            }
            var selectedCard = Assert.Single(materialCards.Cast<Match>(),
                card => card.Value.Contains("aria-checked=\"true\"", StringComparison.Ordinal));
            Assert.Contains("data-material-key=\"" + material + "\"", selectedCard.Value, StringComparison.Ordinal);

            foreach (var field in new[] { "color" })
            {
                var control = Regex.Match(html, "<select\\b[^>]*id=\"" + field + "-" + part.PartId + "\"[^>]*>");
                Assert.True(control.Success);
                Assert.DoesNotMatch("\\sdisabled(?:\\s|=|>)", control.Value);
            }
            var quantityControl = Regex.Match(html, "<input\\b[^>]*id=\"quantity-" + part.PartId + "\"[^>]*>");
            Assert.True(quantityControl.Success);
            Assert.DoesNotMatch("\\sdisabled(?:\\s|=|>)", quantityControl.Value);
        }
    }

    [Theory]
    [InlineData("en", "M68", "Smooth, highly detailed appearance for resin-suitable parts · suitability is confirmed during review")]
    [InlineData("th", "M68", "ผิวเรียบและรายละเอียดคมชัดสำหรับงานที่เหมาะกับเรซิน · วิศวกรจะตรวจสอบความเหมาะสม")]
    [InlineData("en", "PLA", null)]
    [InlineData("th", "PLA", null)]
    public async Task Review_UsesFinalResinSuitabilityCopyWithoutTechnicalPromises(
        string culture, string material, string? expectedDetail)
    {
        using var cultures = new CultureScope(culture);
        var part = Part(material, BuildPreference.Standard);
        var viewModel = new InstantQuotationWorkflowPartViewModel(
            part.PartId, Guid.NewGuid(), part.DisplayFileName, part.Geometry, part.Configuration, null);
        using var services = Services(new CapturingActivator(), new RecordingJavaScript());
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var parameters = ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(InstantQuotationReview.Parts)] = new[] { viewModel },
                [nameof(InstantQuotationReview.Materials)] = new InstantQuotationWorkflowMaterialOption[]
                {
                    new("M68", "M68 resin"), new("PLA", "PLA"),
                },
            });
            return (await renderer.RenderComponentAsync<InstantQuotationReview>(parameters)).ToHtmlString();
        });

        var details = Regex.Matches(html,
            "<dd\\b[^>]*data-review-build-preference-detail[^>]*>([\\s\\S]*?)</dd>");
        if (expectedDetail is null)
        {
            Assert.Empty(details.Cast<Match>());
            return;
        }

        var detail = WebUtility.HtmlDecode(Assert.Single(details.Cast<Match>()).Groups[1].Value);
        Assert.Equal(expectedDetail, detail);
        foreach (var fdmTerm in new[] { "0.05", "full-layer", "post-cure", "walls", "infill", "ชั้นเรซิน", "ฉายแสง", "ผนัง", "อินฟิลล์" })
        {
            Assert.DoesNotContain(fdmTerm, detail, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Theory]
    [InlineData("en", "PLA", BuildPreference.Quality, "Finer detail and a smoother appearance · takes longer and costs more")]
    [InlineData("th", "PLA", BuildPreference.Quality, "เน้นรายละเอียดที่คมชัดและผิวที่เรียบขึ้น · ใช้เวลาผลิตและมีค่าใช้จ่ายมากขึ้น")]
    [InlineData("en", "PLA", BuildPreference.Standard, "Balanced finish, durability, lead time, and price for everyday use")]
    [InlineData("th", "PLA", BuildPreference.Standard, "สมดุลด้านผิวชิ้นงาน ความทนทาน ระยะเวลา และราคา สำหรับการใช้งานทั่วไป")]
    [InlineData("en", "PLA", BuildPreference.Strength, "Greater structural strength · material and load direction still require engineering review")]
    [InlineData("th", "PLA", BuildPreference.Strength, "เพิ่มความแข็งแรงของโครงสร้าง · วัสดุและทิศทางรับแรงยังต้องให้วิศวกรตรวจสอบ")]
    [InlineData("en", "M68", BuildPreference.Standard, null)]
    [InlineData("th", "M68", BuildPreference.Standard, null)]
    public async Task Review_UsesFinalFdmPreferenceCopyWithoutTechnicalPromises(
        string culture, string material, BuildPreference preference, string? expectedDetail)
    {
        using var cultures = new CultureScope(culture);
        var part = Part(material, preference);
        var viewModel = new InstantQuotationWorkflowPartViewModel(
            part.PartId, Guid.NewGuid(), part.DisplayFileName, part.Geometry, part.Configuration, null);
        using var services = Services(new CapturingActivator(), new RecordingJavaScript());
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var parameters = ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(InstantQuotationReview.Parts)] = new[] { viewModel },
                [nameof(InstantQuotationReview.Materials)] = new InstantQuotationWorkflowMaterialOption[]
                {
                    new("M68", "M68 resin"), new("PLA", "PLA"),
                },
            });
            return (await renderer.RenderComponentAsync<InstantQuotationReview>(parameters)).ToHtmlString();
        });

        var details = Regex.Matches(html,
            "<dd\\b[^>]*data-review-fdm-preference-detail[^>]*>([\\s\\S]*?)</dd>");
        var resinDetails = Regex.Matches(html,
            "<dd\\b[^>]*data-review-build-preference-detail[^>]*>([\\s\\S]*?)</dd>");
        if (expectedDetail is null)
        {
            Assert.Empty(details.Cast<Match>());
            Assert.Single(resinDetails.Cast<Match>());
            return;
        }

        Assert.Empty(resinDetails.Cast<Match>());
        var detail = WebUtility.HtmlDecode(Assert.Single(details.Cast<Match>()).Groups[1].Value);
        Assert.Equal(expectedDetail, detail);
        foreach (var obsoletePromise in new[]
        {
            "0.12 mm layers, reduced speed and acceleration, Gyroid sparse infill",
            "0.20", "3 walls", "6 walls", "15%", "25%", "0.05", "full-layer", "post-cure",
            "ผนัง", "อินฟิลล์", "ฉายแสง",
        })
        {
            Assert.DoesNotContain(obsoletePromise, detail, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Theory]
    [InlineData("en", "Standard resin", "Quality")]
    [InlineData("th", "การพิมพ์เรซินมาตรฐาน", "เน้นคุณภาพผิว")]
    public async Task PreliminaryQuotation_UsesQuotedProcessAndPreferenceDespiteStaleConfiguration(
        string culture, string resinLabel, string fdmLabel)
    {
        using var cultures = new CultureScope(culture);
        var resin = Part("M68", BuildPreference.Standard);
        var fdm = Part("PLA", BuildPreference.Quality);
        var quote = SyntheticPhysicalPricingTestData.Quote(new([resin, fdm]));
        var quotedParts = new[] { resin, fdm }.Select((part, index) => new InstantQuotationWorkflowPartViewModel(
            part.PartId, Guid.NewGuid(), part.DisplayFileName, part.Geometry,
            part.Configuration with { BuildPreference = BuildPreference.Strength }, quote.Parts[index])).ToArray();
        var javascript = new RecordingJavaScript();
        var activator = new CapturingActivator();
        using var services = Services(activator, javascript);
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var parameters = ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(InstantQuotationPreliminaryQuotation.Parts)] = quotedParts,
                [nameof(InstantQuotationPreliminaryQuotation.Quote)] = quote,
                [nameof(InstantQuotationPreliminaryQuotation.Materials)] = new InstantQuotationWorkflowMaterialOption[]
                {
                    new("M68", "M68 resin"), new("PLA", "PLA"),
                },
                [nameof(InstantQuotationPreliminaryQuotation.DfmWarnings)] =
                    (Func<InstantQuotationWorkflowPartViewModel, IReadOnlyList<string>>)(_ => []),
            });
            var output = await renderer.RenderComponentAsync<InstantQuotationPreliminaryQuotation>(parameters);
            var button = Regex.Match(output.ToHtmlString(), "<button\\b[^>]*>");
            Assert.True(button.Success);
            Assert.DoesNotMatch("\\sdisabled(?:\\s|=|>)", button.Value);
            Assert.Contains("aria-disabled=\"false\"", button.Value, StringComparison.Ordinal);
            var component = Assert.IsType<InstantQuotationPreliminaryQuotation>(activator.Preliminary);
            var open = typeof(InstantQuotationPreliminaryQuotation).GetMethod(
                "OpenPreliminaryQuotationAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            await (Task)open.Invoke(component, [])!;
        });

        var snapshot = Assert.Single(javascript.Snapshots);
        Assert.Equal(culture, snapshot.GetProperty("locale").GetString());
        var parts = snapshot.GetProperty("parts").EnumerateArray().ToArray();
        Assert.Equal(2, parts.Length);
        Assert.Equal(resin.PartId.ToString("D"), parts[0].GetProperty("partId").GetString());
        Assert.Equal(resinLabel, parts[0].GetProperty("buildPreference").GetString());
        Assert.Equal(fdmLabel, parts[1].GetProperty("buildPreference").GetString());
        for (var index = 0; index < parts.Length; index++)
        {
            Assert.Equal(BuildPreference.Strength, quotedParts[index].Configuration.BuildPreference);
            Assert.Equal(quote.Parts[index].UnitPrice, parts[index].GetProperty("unitPrice").GetDouble());
            Assert.Equal(quote.Parts[index].Subtotal, parts[index].GetProperty("subtotal").GetDouble());
        }
    }

    [Fact]
    public async Task PreliminaryQuotation_NotReadyDisablesButtonAndDoesNotOpenSnapshot()
    {
        var javascript = new RecordingJavaScript();
        var activator = new CapturingActivator();
        using var services = Services(activator, javascript);
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<InstantQuotationPreliminaryQuotation>(ParameterView.Empty);
            var button = Regex.Match(output.ToHtmlString(), "<button\\b[^>]*>");
            Assert.True(button.Success);
            Assert.Matches("\\sdisabled(?:\\s|=|>)", button.Value);
            Assert.Contains("aria-disabled=\"true\"", button.Value, StringComparison.Ordinal);
            var component = Assert.IsType<InstantQuotationPreliminaryQuotation>(activator.Preliminary);
            var open = typeof(InstantQuotationPreliminaryQuotation).GetMethod(
                "OpenPreliminaryQuotationAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            await (Task)open.Invoke(component, [])!;
        });
        Assert.Empty(javascript.Snapshots);
    }

    private static InstantQuotationPart Part(string material, BuildPreference preference) => new(
        Guid.NewGuid(), material + ".stl", new InstantQuotationUploadReference("synthetic-presentation"),
        AuthoritativeInstantQuotationGeometry.RestoreFromProtectedSession(10, 1_000, 100, [10], [10], 12, 1, true),
        new(material, material == "M68" ? "Gray" : "Black", 1, preference));

    private static ServiceProvider Services(CapturingActivator activator, RecordingJavaScript javascript) =>
        new ServiceCollection().AddLogging().AddLocalization(options => options.ResourcesPath = "Resources")
            .AddSingleton<IComponentActivator>(activator).AddSingleton<IJSRuntime>(javascript)
            .AddSingleton(FdmRuntimeProfileCatalog.LoadEmbedded())
            .AddSingleton<IWebHostEnvironment>(new ComponentEnvironment()).BuildServiceProvider();

    private sealed class CapturingActivator(InstantQuotationWorkflowCoordinator? coordinator = null) : IComponentActivator
    {
        internal InstantQuotationPreliminaryQuotation? Preliminary { get; private set; }
        public IComponent CreateInstance(Type componentType)
        {
            var component = (IComponent)Activator.CreateInstance(componentType)!;
            if (component is InstantQuotationWorkflow workflow)
                typeof(InstantQuotationWorkflow).GetField("workflow", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .SetValue(workflow, coordinator);
            if (component is InstantQuotationPreliminaryQuotation preliminary) Preliminary = preliminary;
            return component;
        }
    }

    private sealed class RecordingJavaScript : IJSRuntime
    {
        internal List<JsonElement> Snapshots { get; } = [];
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, default, args);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            if (identifier == "malievPreliminaryQuotation.open")
            {
                var snapshot = Assert.Single(args!);
                Assert.NotNull(snapshot);
                Snapshots.Add(JsonSerializer.SerializeToElement(snapshot, snapshot.GetType(),
                    new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            }
            return ValueTask.FromResult(default(TValue)!);
        }
    }

    private sealed class SessionStore(InstantQuotationPart part) : IInstantQuotationSessionStore
    {
        private InstantQuotationSessionState state = new("presentation-session", "presentation-submission", new([part]), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        public Task<InstantQuotationSessionState> CreateAsync(string? ownerIdentity, InstantQuotationOrderState requestState, CancellationToken cancellationToken) => throw new InvalidOperationException("Restore expected.");
        public Task<InstantQuotationSessionState?> GetAsync(string sessionId, string? ownerIdentity, CancellationToken cancellationToken) => Task.FromResult<InstantQuotationSessionState?>(state);
        public Task<bool> PutAsync(InstantQuotationSessionState session, string? ownerIdentity, CancellationToken cancellationToken) { state = session; return Task.FromResult(true); }
        public Task<bool> RemoveAsync(string sessionId, string? ownerIdentity, CancellationToken cancellationToken) => throw new InvalidOperationException("No removal expected.");
    }

    private sealed class UnusedUploadClient : IInstantQuotationUploadClient
    {
        public Task<InstantQuotationUploadResult> UploadAsync(string sessionId, string? ownerIdentity, Stream content, string fileName, string contentType, long contentLength, InstantQuotationGeometryClaim geometryClaim, string operationId, CancellationToken cancellationToken) => throw new InvalidOperationException("No upload expected.");
        public Task<InstantQuotationRemoveResult> RemoveAsync(string sessionId, string? ownerIdentity, InstantQuotationUploadReference uploadReference, string operationId, CancellationToken cancellationToken) => throw new InvalidOperationException("No removal expected.");
        public Task<InstantQuotationFinalizationResult> FinalizeAsync(string sessionId, string? ownerIdentity, int quotationRequestId, IReadOnlyList<InstantQuotationUploadReference> uploadReferences, string operationId, CancellationToken cancellationToken) => throw new InvalidOperationException("No finalization expected.");
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo culture = CultureInfo.CurrentCulture;
        private readonly CultureInfo uiCulture = CultureInfo.CurrentUICulture;
        internal CultureScope(string name) { CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name); CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(name); }
        public void Dispose() { CultureInfo.CurrentCulture = culture; CultureInfo.CurrentUICulture = uiCulture; }
    }

    private sealed class ComponentEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = typeof(Program).Assembly.GetName().Name!;
        public string EnvironmentName { get; set; } = "Testing";
        public string ContentRootPath { get; set; } = string.Empty;
        public string WebRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
