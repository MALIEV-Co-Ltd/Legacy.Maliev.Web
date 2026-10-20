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


    [Theory]
    [InlineData("en", "PETG", "Select all", "Any")]
    [InlineData("th", "PETG", "เลือกทั้งหมด", "Any")]
    [InlineData("en", "M68", "Select all", "Gray")]
    [InlineData("th", "M68", "เลือกทั้งหมด", "Gray")]
    public async Task BulkMaterial_ChangesOnlySelectedPartsThroughExistingServerConfiguration(
        string culture, string material, string selectAll, string color)
    {
        using var cultures = new CultureScope(culture);
        var active = Part(material, BuildPreference.Standard);
        var selected = Part("PLA", BuildPreference.Strength) with
        {
            Configuration = new("PLA", "Blue", 7, BuildPreference.Strength),
        };
        var untouched = Part("PLA", BuildPreference.Quality);
        var store = new SessionStore(active, selected, untouched);
        await using var coordinator = new InstantQuotationWorkflowCoordinator(
            store, new UnusedUploadClient(), new InstantQuotationPricingService(), null,
            authoritativePricingService: SyntheticAuthoritativePricingTestService.Instance);
        await coordinator.InitializeAsync("presentation-session", default);
        var activator = new CapturingActivator(coordinator);
        using var services = Services(activator, new RecordingJavaScript());
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var root = await renderer.RenderComponentAsync<InstantQuotationWorkflow>(ParameterView.Empty);
            var component = Assert.IsType<InstantQuotationWorkflow>(activator.Workflow);
            var initial = WebUtility.HtmlDecode(root.ToHtmlString());
            Assert.Contains(selectAll, initial, StringComparison.Ordinal);
            Assert.Equal(3, Regex.Matches(initial, "data-workflow-part-select").Count);
            var labels = Regex.Matches(initial, "<label class=\"instant-quote__bulk-checkbox\">[\\s\\S]*?</label>")
                .Cast<Match>().Where(label => label.Value.Contains("data-workflow-part-select", StringComparison.Ordinal)).ToArray();
            Assert.Equal(3, labels.Length);
            Assert.All(labels, label => Assert.Matches("<span class=\"instant-quote__sr-only\">[^<]+</span>", label.Value));
            Assert.Contains("data-workflow-select-all", initial, StringComparison.Ordinal);
            Assert.Contains("data-workflow-apply-material", initial, StringComparison.Ordinal);

            void InvokeSelection(string method, params object[] arguments) =>
                typeof(InstantQuotationWorkflow).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(component, arguments);

            InvokeSelection("SelectAllParts", new ChangeEventArgs { Value = true });
            Assert.Equal(4, Regex.Matches(root.ToHtmlString(), "<input\\b[^>]*data-workflow-(?:part-select|select-all)[^>]*\\schecked(?:[\\s=>])").Count);
            InvokeSelection("SelectAllParts", new ChangeEventArgs { Value = false });
            InvokeSelection("SetPartSelected", selected.PartId, new ChangeEventArgs { Value = true });
            Assert.Contains("aria-checked=\"mixed\"", root.ToHtmlString(), StringComparison.Ordinal);
            InvokeSelection("OnQuantityInput", selected.PartId, new ChangeEventArgs { Value = "9" });
            await (Task)typeof(InstantQuotationWorkflow)
                .GetMethod("ApplyMaterialToSelectedAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(component, null)!;
            Assert.Equal("PLA", coordinator.Parts.Single(part => part.PartId == selected.PartId).Configuration.MaterialKey);
            await (Task)typeof(InstantQuotationWorkflow)
                .GetMethod("ChangeQuantityAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(component, [selected.PartId, new ChangeEventArgs { Value = "7" }])!;
            await (Task)typeof(InstantQuotationWorkflow)
                .GetMethod("ApplyMaterialToSelectedAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(component, null)!;

            var result = coordinator.Parts.Single(part => part.PartId == selected.PartId);
            Assert.Equal(material, result.Configuration.MaterialKey);
            Assert.Equal(color, result.Configuration.Color);
            Assert.Equal(7, result.Configuration.Quantity);
            Assert.Equal(material == "M68" ? BuildPreference.Standard : BuildPreference.Strength,
                result.Configuration.BuildPreference);
            Assert.Equal(active.Configuration, coordinator.Parts.Single(part => part.PartId == active.PartId).Configuration);
            Assert.Equal(untouched.Configuration, coordinator.Parts.Single(part => part.PartId == untouched.PartId).Configuration);
            Assert.NotNull(coordinator.OrderQuote);
            Assert.Equal(3, coordinator.OrderQuote.Parts.Count);
            Assert.Equal(7, coordinator.OrderQuote.Parts.Single(part => part.PartId == selected.PartId).Quantity);
        });
    }


    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task BulkMaterial_OwnsConfigurationUntilTheDelayedWriteSettles(bool refusesWrite, bool disposes)
    {
        var active = Part("PETG", BuildPreference.Standard);
        var first = Part("PLA", BuildPreference.Quality);
        var next = Part("PLA", BuildPreference.Strength) with
        {
            Configuration = new("PLA", "Blue", 7, BuildPreference.Strength),
        };
        var store = new SessionStore(active, first, next);
        await using var coordinator = new InstantQuotationWorkflowCoordinator(
            store, new UnusedUploadClient(), new InstantQuotationPricingService(), null,
            authoritativePricingService: SyntheticAuthoritativePricingTestService.Instance);
        await coordinator.InitializeAsync("presentation-session", default);
        var activator = new CapturingActivator(coordinator);
        using var services = Services(activator, new RecordingJavaScript());
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        using var lease = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var root = await renderer.RenderComponentAsync<InstantQuotationWorkflow>(ParameterView.Empty);
            var component = Assert.IsType<InstantQuotationWorkflow>(activator.Workflow);
            object? Invoke(string name, params object[] arguments) =>
                typeof(InstantQuotationWorkflow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(component, arguments);
            Invoke("SelectAllParts", new ChangeEventArgs { Value = true });
            store.BeforePut = async () =>
            {
                store.BeforePut = null;
                entered.TrySetResult();
                await release.Task.WaitAsync(lease.Token);
                if (refusesWrite) throw new InvalidOperationException("Synthetic owned write refusal.");
            };
            var applying = (Task)Invoke("ApplyMaterialToSelectedAsync")!;
            Exception? primary = null;
            try
            {
                await entered.Task.WaitAsync(lease.Token);
                var fieldset = Regex.Match(root.ToHtmlString(), "<fieldset[^>]*data-workflow-material-color-quantity[^>]*>").Value;
                Assert.NotEmpty(fieldset);
                Assert.Matches("\\sdisabled(?:[\\s=>])", fieldset);
                Assert.Matches("\\sinert(?:[\\s=>])", fieldset);
                Invoke("OnQuantityInput", next.PartId, new ChangeEventArgs { Value = "9" });
                await (Task)Invoke("ChangeQuantityAsync", next.PartId, new ChangeEventArgs { Value = "9" })!;
                await (Task)Invoke("ChangeMaterialAsync", next.PartId, new ChangeEventArgs { Value = "M68" })!;
                await (Task)Invoke("ChangeColorAsync", next.PartId, new ChangeEventArgs { Value = "White" })!;
                await (Task)Invoke("ChangeBuildPreferenceAsync", next.PartId, BuildPreference.Quality)!;
                await (Task)Invoke("RemovePartAsync", next.PartId)!;
                Assert.Equal(next.Configuration, coordinator.Parts.Single(part => part.PartId == next.PartId).Configuration);
                if (disposes) await component.DisposeAsync();
                release.TrySetResult();
                if (disposes) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => applying);
                else if (refusesWrite) await Assert.ThrowsAsync<InvalidOperationException>(() => applying);
                else await applying.WaitAsync(lease.Token);
                Assert.False((bool)typeof(InstantQuotationWorkflow)
                    .GetField("bulkApplying", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(component)!);
                if (disposes) return;
                var after = Regex.Match(root.ToHtmlString(), "<fieldset[^>]*data-workflow-material-color-quantity[^>]*>").Value;
                Assert.DoesNotMatch("\\sdisabled(?:[\\s=>])", after);
                Assert.DoesNotMatch("\\sinert(?:[\\s=>])", after);
                if (!refusesWrite)
                {
                    var changed = coordinator.Parts.Single(part => part.PartId == next.PartId);
                    Assert.Equal("PETG", changed.Configuration.MaterialKey);
                    Assert.Equal(7, changed.Configuration.Quantity);
                    Assert.Equal(BuildPreference.Strength, changed.Configuration.BuildPreference);
                }
            }
            catch (Exception exception)
            {
                primary = exception;
                throw;
            }
            finally
            {
                await MaterialPricingTestCleanup.ReleaseAllAsync(primary,
                    async () => await MaterialPricingTestCleanup.DrainQuoteAsync(
                        applying, () => lease.Cancel(), () => release.TrySetResult(), lease.Token, null,
                        receipt => Assert.True(receipt.ActualTaskSettled), TimeSpan.FromSeconds(5),
                        expectedTerminalFailure: refusesWrite || disposes, taskKind: "bulk-configuration"));
            }
        });
    }

    [Fact]
    public async Task BulkAppearanceCompletion_RestoresTheRenderedSelectedPart()
    {
        using var cultures = new CultureScope("en");
        var active = Part("PETG", BuildPreference.Standard);
        var other = Part("PLA", BuildPreference.Strength);
        await using var coordinator = new InstantQuotationWorkflowCoordinator(
            new SessionStore(active, other), new UnusedUploadClient(), new InstantQuotationPricingService(), null,
            authoritativePricingService: SyntheticAuthoritativePricingTestService.Instance);
        await coordinator.InitializeAsync("presentation-session", default);
        var activator = new CapturingActivator(coordinator);
        using var services = Services(activator, new RecordingJavaScript());
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var viewerSelections = new List<string>();
        var appearances = new List<string>();
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var root = await renderer.RenderComponentAsync<InstantQuotationWorkflow>(ParameterView.Empty);
            var component = Assert.IsType<InstantQuotationWorkflow>(activator.Workflow);
            var preview = new BulkAppearancePreviewReference((name, args) =>
            {
                if (name == "select") viewerSelections.Add((string)args![0]!);
                if (name == "updatePartAppearance") appearances.Add((string)args![0]!);
                return Task.FromResult<object?>(null);
            });
            void Set(string name, object value) => typeof(InstantQuotationWorkflow)
                .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(component, value);
            object? Invoke(string name, params object[] args) => typeof(InstantQuotationWorkflow)
                .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(component, args);
            Set("previewInterop", preview);
            Set("selectedPreviewPartId", active.PartId);
            Invoke("SelectAllParts", new ChangeEventArgs { Value = true });
            await (Task)Invoke("ApplyMaterialToSelectedAsync")!;
            Assert.Equal("PETG", coordinator.Parts.Single(part => part.PartId == other.PartId).Configuration.MaterialKey);
            Assert.Equal(other.PartId.ToString("N"), Assert.Single(appearances));
            Assert.Equal(active.PartId.ToString("N"), viewerSelections.Last());
            var html = WebUtility.HtmlDecode(root.ToHtmlString());
            Assert.Matches("aria-label=\"View " + Regex.Escape(active.DisplayFileName) + "\"[^>]*aria-pressed=\"true\"", html);
        });
    }


    [Theory]
    [InlineData("current")]
    [InlineData("null")]
    [InlineData("removed")]
    [InlineData("empty")]
    [InlineData("disposed")]
    public async Task AppearanceCompletion_UsesOnlyCurrentExistingSelection(string transition)
    {
        var original = Part("PLA", BuildPreference.Quality);
        var other = Part("PETG", BuildPreference.Strength);
        await using var coordinator = new InstantQuotationWorkflowCoordinator(
            new SessionStore(original, other), new SuccessfulRemoveClient(), new InstantQuotationPricingService(), null,
            authoritativePricingService: SyntheticAuthoritativePricingTestService.Instance);
        await coordinator.InitializeAsync("presentation-session", default);
        var activator = new CapturingActivator(coordinator);
        using var services = Services(activator, new RecordingJavaScript());
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        using var lease = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var selected = new List<string>();
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<InstantQuotationWorkflow>(ParameterView.Empty);
            var component = Assert.IsType<InstantQuotationWorkflow>(activator.Workflow);
            var preview = new BulkAppearancePreviewReference(async (name, args) =>
            {
                if (name == "updatePartAppearance")
                {
                    entered.TrySetResult();
                    await release.Task.WaitAsync(lease.Token);
                }
                if (name == "select") selected.Add((string)args![0]!);
                return null;
            });
            void Set(string name, object? value) => typeof(InstantQuotationWorkflow)
                .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(component, value);
            object? Invoke(string name, params object[] args) => typeof(InstantQuotationWorkflow)
                .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(component, args);
            Set("previewInterop", preview);
            Set("selectedPreviewPartId", original.PartId);
            var appearance = (Task)Invoke("UpdatePartAppearanceAsync", original.PartId, "Blue")!;
            Exception? primary = null;
            try
            {
                await entered.Task.WaitAsync(lease.Token);
                if (transition == "current") await (Task)Invoke("SelectPreviewAsync", other.PartId)!;
                if (transition == "null") Set("selectedPreviewPartId", null);
                if (transition is "removed" or "empty") await (Task)Invoke("RemovePartAsync", original.PartId)!;
                if (transition == "empty") await (Task)Invoke("RemovePartAsync", other.PartId)!;
                if (transition == "disposed") await component.DisposeAsync();
                selected.Clear();
                release.TrySetResult();
                await appearance.WaitAsync(lease.Token);
                if (transition is "empty" or "disposed")
                {
                    Assert.Empty(selected);
                    if (transition == "empty")
                        Assert.Null(typeof(InstantQuotationWorkflow)
                            .GetField("selectedPreviewPartId", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(component));
                }
                else
                {
                    var expected = transition == "null" ? original.PartId : other.PartId;
                    Assert.Equal(expected.ToString("N"), Assert.Single(selected));
                    Assert.Equal(expected, (Guid?)typeof(InstantQuotationWorkflow)
                        .GetField("selectedPreviewPartId", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(component));
                    Assert.Contains(coordinator.Parts, part => part.PartId == expected);
                }
            }
            catch (Exception exception)
            {
                primary = exception;
                throw;
            }
            finally
            {
                await MaterialPricingTestCleanup.ReleaseAllAsync(primary,
                    async () => await MaterialPricingTestCleanup.DrainQuoteAsync(
                        appearance, () => lease.Cancel(), () => release.TrySetResult(), lease.Token, null,
                        receipt => Assert.True(receipt.ActualTaskSettled), TimeSpan.FromSeconds(5),
                        expectedTerminalFailure: false, taskKind: "appearance"));
            }
        });
    }

    private sealed class BulkAppearancePreviewReference(Func<string, object?[]?, Task<object?>> invoke) : IJSObjectReference
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, default, args);
        public async ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            var result = await invoke(identifier, args);
            return result is null ? default! : (TValue)result;
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class SuccessfulRemoveClient : IInstantQuotationUploadClient
    {
        public Task<InstantQuotationUploadResult> UploadAsync(string sessionId, string? ownerIdentity, Stream content,
            string fileName, string contentType, long contentLength, InstantQuotationGeometryClaim geometryClaim,
            string operationId, CancellationToken cancellationToken) => throw new InvalidOperationException("No upload expected.");
        public Task<InstantQuotationRemoveResult> RemoveAsync(string sessionId, string? ownerIdentity,
            InstantQuotationUploadReference uploadReference, string operationId, CancellationToken cancellationToken) =>
            Task.FromResult(new InstantQuotationRemoveResult(operationId, InstantQuotationServiceStatus.Available,
                InstantQuotationAuthorizationStatus.Authorized, InstantQuotationOperationStatus.Succeeded,
                InstantQuotationProblemCategory.None));
        public Task<InstantQuotationFinalizationResult> FinalizeAsync(string sessionId, string? ownerIdentity,
            int quotationRequestId, IReadOnlyList<InstantQuotationUploadReference> uploadReferences,
            string operationId, CancellationToken cancellationToken) => throw new InvalidOperationException("No finalization expected.");
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
        internal InstantQuotationWorkflow? Workflow { get; private set; }
        public IComponent CreateInstance(Type componentType)
        {
            var component = (IComponent)Activator.CreateInstance(componentType)!;
            if (component is InstantQuotationWorkflow workflow)
            {
                Workflow = workflow;
                typeof(InstantQuotationWorkflow).GetField("workflow", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .SetValue(workflow, coordinator);
            }
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

    private sealed class SessionStore(params InstantQuotationPart[] parts) : IInstantQuotationSessionStore
    {
        private InstantQuotationSessionState state = new("presentation-session", "presentation-submission", new(parts), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        public Task<InstantQuotationSessionState> CreateAsync(string? ownerIdentity, InstantQuotationOrderState requestState, CancellationToken cancellationToken) => throw new InvalidOperationException("Restore expected.");
        public Task<InstantQuotationSessionState?> GetAsync(string sessionId, string? ownerIdentity, CancellationToken cancellationToken) => Task.FromResult<InstantQuotationSessionState?>(state);
        internal Func<Task>? BeforePut { get; set; }
        public async Task<bool> PutAsync(InstantQuotationSessionState session, string? ownerIdentity, CancellationToken cancellationToken)
        {
            if (BeforePut is { } before) await before();
            state = session;
            return true;
        }
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
