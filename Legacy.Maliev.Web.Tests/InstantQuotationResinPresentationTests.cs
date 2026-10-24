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
using Microsoft.AspNetCore.Components.Forms;
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
    [InlineData("en", "White", "PETG", 1, "White")]
    [InlineData("th", "White", "PETG", 1, "White")]
    [InlineData("en", "Pink", "PA-CF", 1, "Black")]
    [InlineData("th", "Pink", "PA-CF", 1, "Black")]
    [InlineData("en", "Any", "PETG", 2, "Any")]
    [InlineData("th", "Any", "PETG", 2, "Any")]
    public async Task Workflow_MaterialChangePreservesCompatibleSingleBodyColor(
        string culture, string initialColor, string targetMaterial, int bodies, string expectedColor)
    {
        using var cultures = new CultureScope(culture);
        var part = Part("PLA", BuildPreference.Standard) with
        {
            Geometry = AuthoritativeInstantQuotationGeometry.RestoreFromProtectedSession(
                10, 1_000, 100, [10], [10], 12, bodies, true),
            Configuration = new("PLA", initialColor, 2),
        };
        var store = new SessionStore(part);
        await using var coordinator = new InstantQuotationWorkflowCoordinator(
            store, new UnusedUploadClient(), new InstantQuotationPricingService(), null,
            authoritativePricingService: SyntheticAuthoritativePricingTestService.Instance);
        await coordinator.InitializeAsync("presentation-session", default);
        var activator = new CapturingActivator(coordinator);
        using var services = Services(activator, new RecordingJavaScript());
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<InstantQuotationWorkflow>(ParameterView.Empty);
            var component = Assert.IsType<InstantQuotationWorkflow>(activator.Workflow);
            var change = typeof(InstantQuotationWorkflow).GetMethod(
                "ChangeMaterialAsync", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(change);
            await (Task)change.Invoke(component, [part.PartId, new ChangeEventArgs { Value = targetMaterial }])!;
        });

        var session = await store.GetAsync("presentation-session", null, default);
        Assert.NotNull(session);
        var updated = Assert.Single(session.RequestState.Parts);
        Assert.Equal(part.PartId, updated.PartId);
        Assert.Equal(targetMaterial, updated.Configuration.MaterialKey);
        Assert.Equal(expectedColor, updated.Configuration.Color);
        Assert.Equal(2, updated.Configuration.Quantity);
        Assert.Equal(BuildPreference.Standard, updated.Configuration.BuildPreference);
    }

    [Theory]
    [InlineData("en", "Standard resin", "Review all items")]
    [InlineData("th", "การพิมพ์เรซินมาตรฐาน", "ตรวจสอบรายการทั้งหมด")]
    public async Task Workflow_DisablesOnlyResinBuildPreferenceControls(string culture, string label, string reviewLabel)
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

            var reviewAction = Assert.Single(
                Regex.Matches(html, "<button\\b[^>]*>[\\s\\S]*?</button>").Cast<Match>(),
                action => WebUtility.HtmlDecode(Regex.Replace(action.Value, "<[^>]+>", "")).Trim() == reviewLabel).Value;
            Assert.Contains("type=\"button\"", reviewAction, StringComparison.Ordinal);
            Assert.DoesNotContain("type=\"submit\"", reviewAction, StringComparison.Ordinal);

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
            var applyButton = Regex.Match(initial, "<button\\b[^>]*data-workflow-apply-material[^>]*>([\\s\\S]*?)</button>");
            Assert.True(applyButton.Success);
            Assert.Equal(culture == "th" ? $"ใช้ {material} กับชิ้นงานที่เลือก" : $"Apply {material} to selected parts",
                Regex.Replace(applyButton.Groups[1].Value, "\\s+", " ").Trim());

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


    [Theory]
    [InlineData("en", "3D file processing is unavailable. Try again or contact our engineers for a quotation.", false, false)]
    [InlineData("th", "ไม่สามารถประมวลผลไฟล์ 3 มิติได้ โปรดลองอีกครั้งหรือติดต่อวิศวกรเพื่อขอใบเสนอราคา", false, false)]
    [InlineData("en", "3D file processing is unavailable. Try again or contact our engineers for a quotation.", true, false)]
    [InlineData("en", "3D file processing is unavailable. Try again or contact our engineers for a quotation.", false, true)]
    public async Task FailedLocalSelection_BlocksReviewWithoutRemovingThePricedPart(
        string culture, string message, bool disposeWhilePending, bool unavailableSelection)
    {
        using var cultures = new CultureScope(culture);
        var part = Part("PLA", BuildPreference.Strength);
        var store = new SessionStore(part);
        await using var coordinator = new InstantQuotationWorkflowCoordinator(
            store, new UnusedUploadClient(), new InstantQuotationPricingService(), null,
            authoritativePricingService: SyntheticAuthoritativePricingTestService.Instance);
        await coordinator.InitializeAsync("presentation-session", default);
        var priced = coordinator.OrderQuote;
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
            bool CanReview() => (bool)typeof(InstantQuotationWorkflow)
                .GetProperty("CanEnterReview", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(component)!;
            var preview = new ControlledPreviewReference(async (name, _) =>
            {
                if (name == "beginSelection")
                {
                    if (!unavailableSelection) return new[] { "failed-local-preview" };
                    entered.TrySetResult();
                    await release.Task.WaitAsync(lease.Token);
                    return Array.Empty<string>();
                }
                if (name == "getGeometryClaim")
                {
                    entered.TrySetResult();
                    await release.Task.WaitAsync(lease.Token);
                    throw new JSException("private decoder detail");
                }
                if (name == "dispose") release.TrySetResult();
                return null;
            });
            typeof(InstantQuotationWorkflow).GetField("previewInterop", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(component, preview);
            typeof(InstantQuotationWorkflow).GetField("fileInput", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(component, new InputFile());
            Assert.True(CanReview());
            var uploading = (Task)Invoke("UploadFilesAsync",
                new InputFileChangeEventArgs(new IBrowserFile[] { new UnreadableBrowserFile() }))!;
            Exception? primary = null;
            try
            {
                await entered.Task.WaitAsync(lease.Token);
                Assert.False(CanReview());
                Invoke("EnterReview");
                Assert.Equal(InstantQuotationWorkflowState.Configured, coordinator.State);
                coordinator.EnterReview();
                Invoke("EnterCustomerDetails");
                Assert.Equal(InstantQuotationWorkflowState.Review, coordinator.State);
                coordinator.ReturnToConfiguration();
                if (disposeWhilePending) await component.DisposeAsync();
                release.TrySetResult();
                await uploading.WaitAsync(lease.Token);
                if (disposeWhilePending)
                {
                    Assert.Empty((System.Collections.IDictionary)typeof(InstantQuotationWorkflow)
                        .GetField("failedPreviewSelections", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(component)!);
                    return;
                }
                var html = WebUtility.HtmlDecode(root.ToHtmlString());
                Assert.Contains("data-workflow-failed-preview", html, StringComparison.Ordinal);
                Assert.Contains("unreadable.stl", html, StringComparison.Ordinal);
                Assert.Contains(message, html, StringComparison.Ordinal);
                Assert.DoesNotContain("private decoder detail", html, StringComparison.Ordinal);
                Assert.False(CanReview());
                Assert.Equal(part.PartId, Assert.Single(coordinator.Parts).PartId);
                Assert.Equal(part.Configuration, Assert.Single(coordinator.Parts).Configuration);
                Assert.Same(priced, coordinator.OrderQuote);
                var failures = (System.Collections.IDictionary)typeof(InstantQuotationWorkflow)
                    .GetField("failedPreviewSelections", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(component)!;
                await (Task)Invoke("RemoveFailedPreviewAsync", Assert.Single(failures.Keys.Cast<string>()))!;
                Assert.DoesNotContain("data-workflow-failed-preview", root.ToHtmlString(), StringComparison.Ordinal);
                Assert.True(CanReview());
                Assert.Same(priced, coordinator.OrderQuote);
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
                        uploading, () => lease.Cancel(), () => release.TrySetResult(), lease.Token, null,
                        receipt => Assert.True(receipt.ActualTaskSettled), TimeSpan.FromSeconds(5),
                        expectedTerminalFailure: false, taskKind: "selection"));
            }
        });
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task LatestUploadSelection_SelectsLastAdmissionUnlessTheUserViewedAnotherPart(
        bool userViews, bool disposeWhilePending)
    {
        var first = Part("PLA", BuildPreference.Quality);
        var latest = Part("PETG", BuildPreference.Strength);
        await using var coordinator = new InstantQuotationWorkflowCoordinator(
            new SessionStore(first, latest), new UnusedUploadClient(), new InstantQuotationPricingService(), null,
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
            var preview = new ControlledPreviewReference(async (name, args) =>
            {
                if (name == "admit" && !entered.Task.IsCompleted)
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
            var keys = (Dictionary<Guid, string>)typeof(InstantQuotationWorkflow)
                .GetField("previewKeys", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(component)!;
            var parts = coordinator.Parts.ToArray();
            keys.Add(parts[0].PreviewCorrelationId, "first-preview");
            keys.Add(parts[1].PreviewCorrelationId, "latest-preview");
            Set("autoPreviewLocalId", parts[1].PreviewCorrelationId);
            var reconciling = (Task)Invoke("ReconcilePreviewsAsync", new object[] { null! })!;
            Exception? primary = null;
            try
            {
                await entered.Task.WaitAsync(lease.Token);
                if (userViews) await (Task)Invoke("SelectPreviewAsync", first.PartId)!;
                if (disposeWhilePending) await component.DisposeAsync();
                release.TrySetResult();
                await reconciling.WaitAsync(lease.Token);
                if (disposeWhilePending)
                {
                    Assert.Null(typeof(InstantQuotationWorkflow)
                        .GetField("selectedPreviewPartId", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(component));
                    Assert.Empty(selected);
                    return;
                }
                var expected = userViews ? first.PartId : latest.PartId;
                Assert.Equal(expected, (Guid?)typeof(InstantQuotationWorkflow)
                    .GetField("selectedPreviewPartId", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(component));
                Assert.Equal(expected.ToString("N"), selected.Last());
                Assert.Equal(first.Configuration, coordinator.Parts.Single(part => part.PartId == first.PartId).Configuration);
                Assert.Equal(latest.Configuration, coordinator.Parts.Single(part => part.PartId == latest.PartId).Configuration);
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
                        reconciling, () => lease.Cancel(), () => release.TrySetResult(), lease.Token, null,
                        receipt => Assert.True(receipt.ActualTaskSettled), TimeSpan.FromSeconds(5),
                        expectedTerminalFailure: false, taskKind: "preview-admission"));
            }
        });
    }


    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UploadProducer_DoesNotRestoreAutomaticSelectionAfterAnExplicitViewDuringParsing(bool userViews)
    {
        var original = Part("PLA", BuildPreference.Strength);
        await using var coordinator = new InstantQuotationWorkflowCoordinator(
            new SessionStore(original), new SyntheticUploadClient(), new InstantQuotationPricingService(), null,
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
            var preview = new ControlledPreviewReference(async (name, _) =>
            {
                if (name == "beginSelection") return new[] { "new-preview" };
                if (name == "getGeometryClaim")
                {
                    entered.TrySetResult();
                    await release.Task.WaitAsync(lease.Token);
                    return new InstantQuotationGeometryClaim(1, new string('a', 64), 10, 10, 10, 1000, 600,
                        Enumerable.Repeat(100d, 64).ToArray(), Enumerable.Repeat(40d, 64).ToArray(),
                        100, 1, true, false, false, 0.8);
                }
                if (name == "isIncompletePreview") return false;
                return null;
            });
            void Set(string name, object value) => typeof(InstantQuotationWorkflow)
                .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(component, value);
            object? Invoke(string name, params object[] args) => typeof(InstantQuotationWorkflow)
                .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(component, args);
            Set("previewInterop", preview);
            Set("fileInput", new InputFile());
            var uploading = (Task)Invoke("UploadFilesAsync",
                new InputFileChangeEventArgs(new IBrowserFile[] { new SyntheticBrowserFile() }))!;
            Exception? primary = null;
            try
            {
                await entered.Task.WaitAsync(lease.Token);
                if (userViews) await (Task)Invoke("SelectPreviewAsync", original.PartId)!;
                release.TrySetResult();
                await uploading.WaitAsync(lease.Token);
                Assert.Equal(2, coordinator.Parts.Count);
                var newest = coordinator.Parts.Single(part => part.DisplayFileName == "new.stl");
                var expected = userViews ? original.PartId : newest.PartId;
                Assert.Equal(expected, (Guid?)typeof(InstantQuotationWorkflow)
                    .GetField("selectedPreviewPartId", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(component));
                Assert.Equal(original.Configuration, coordinator.Parts.Single(part => part.PartId == original.PartId).Configuration);
                Assert.DoesNotContain("data-workflow-failed-preview", root.ToHtmlString(), StringComparison.Ordinal);
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
                        uploading, () => lease.Cancel(), () => release.TrySetResult(), lease.Token, null,
                        receipt => Assert.True(receipt.ActualTaskSettled), TimeSpan.FromSeconds(5),
                        expectedTerminalFailure: false, taskKind: "selection"));
            }
        });
    }


    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreviewAdmissionCompletion_IgnoresRemovedOrReboundPart(bool rebindsPreview)
    {
        var original = Part("PLA", BuildPreference.Quality);
        await using var coordinator = new InstantQuotationWorkflowCoordinator(
            new SessionStore(original), new SuccessfulRemoveClient(), new InstantQuotationPricingService(), null,
            authoritativePricingService: SyntheticAuthoritativePricingTestService.Instance);
        await coordinator.InitializeAsync("presentation-session", default);
        var activator = new CapturingActivator(coordinator);
        using var services = Services(activator, new RecordingJavaScript());
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        using var lease = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var selected = new List<string>();
        var sink = new AdmissionAnalyticsSink();
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<InstantQuotationWorkflow>(ParameterView.Empty);
            var component = Assert.IsType<InstantQuotationWorkflow>(activator.Workflow);
            var preview = new ControlledPreviewReference(async (name, args) =>
            {
                if (name == "admit")
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
            Set("analytics", new InstantQuotationAnalyticsTracker(sink));
            Set("selectedPreviewPartId", null);
            var localId = Assert.Single(coordinator.Parts).PreviewCorrelationId;
            var keys = (Dictionary<Guid, string>)typeof(InstantQuotationWorkflow)
                .GetField("previewKeys", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(component)!;
            keys.Add(localId, "held-preview");
            Set("autoPreviewLocalId", localId);
            var reconciling = (Task)Invoke("ReconcilePreviewsAsync", new object[] { null! })!;
            Exception? primary = null;
            try
            {
                await entered.Task.WaitAsync(lease.Token);
                if (rebindsPreview)
                {
                    keys[localId] = "replacement-preview";
                }
                else
                {
                    await (Task)Invoke("RemovePartAsync", original.PartId)!;
                    Assert.Empty(coordinator.Parts);
                    Assert.Empty(keys);
                }
                Assert.Null(typeof(InstantQuotationWorkflow)
                    .GetField("selectedPreviewPartId", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(component));
                release.TrySetResult();
                await reconciling.WaitAsync(lease.Token);
                Assert.Null(typeof(InstantQuotationWorkflow)
                    .GetField("selectedPreviewPartId", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(component));
                Assert.Null(typeof(InstantQuotationWorkflow)
                    .GetField("revealPreviewPartId", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(component));
                Assert.Empty(selected);
                Assert.Empty(sink.Payloads);
                if (rebindsPreview)
                {
                    Assert.Equal("replacement-preview", keys[localId]);
                    Assert.Equal(original.PartId, Assert.Single(coordinator.Parts).PartId);
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
                        reconciling, () => lease.Cancel(), () => release.TrySetResult(), lease.Token, null,
                        receipt => Assert.True(receipt.ActualTaskSettled), TimeSpan.FromSeconds(5),
                        expectedTerminalFailure: false, taskKind: "stale-preview-admission"));
            }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreviewAdmission_NullInteropOrDisposedCompletionDoesNotPublishSelection(bool disposes)
    {
        var original = Part("PLA", BuildPreference.Quality);
        await using var coordinator = new InstantQuotationWorkflowCoordinator(
            new SessionStore(original), new SuccessfulRemoveClient(), new InstantQuotationPricingService(), null,
            authoritativePricingService: SyntheticAuthoritativePricingTestService.Instance);
        await coordinator.InitializeAsync("presentation-session", default);
        var activator = new CapturingActivator(coordinator);
        using var services = Services(activator, new RecordingJavaScript());
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        using var lease = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var selected = new List<string>();
        var sink = new AdmissionAnalyticsSink();
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<InstantQuotationWorkflow>(ParameterView.Empty);
            var component = Assert.IsType<InstantQuotationWorkflow>(activator.Workflow);
            var preview = new ControlledPreviewReference(async (name, args) =>
            {
                if (name == "admit")
                {
                    entered.TrySetResult();
                    await release.Task.WaitAsync(lease.Token);
                }
                if (name == "select") selected.Add((string)args![0]!);
                return null;
            });
            void Set(string name, object? value) => typeof(InstantQuotationWorkflow)
                .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(component, value);
            Set("previewInterop", disposes ? preview : null);
            Set("analytics", new InstantQuotationAnalyticsTracker(sink));
            Set("selectedPreviewPartId", null);
            var localId = Assert.Single(coordinator.Parts).PreviewCorrelationId;
            var keys = (Dictionary<Guid, string>)typeof(InstantQuotationWorkflow)
                .GetField("previewKeys", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(component)!;
            keys.Add(localId, "held-preview");
            Set("autoPreviewLocalId", localId);
            var reconciling = (Task)typeof(InstantQuotationWorkflow)
                .GetMethod("ReconcilePreviewsAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(component, [null])!;
            Exception? primary = null;
            try
            {
                if (disposes)
                {
                    await entered.Task.WaitAsync(lease.Token);
                    await component.DisposeAsync();
                    release.TrySetResult();
                }
                else
                {
                    Assert.True(reconciling.IsCompletedSuccessfully);
                    Assert.False(entered.Task.IsCompleted);
                }
                await reconciling.WaitAsync(lease.Token);
                Assert.Null(typeof(InstantQuotationWorkflow)
                    .GetField("selectedPreviewPartId", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(component));
                Assert.Null(typeof(InstantQuotationWorkflow)
                    .GetField("revealPreviewPartId", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(component));
                Assert.Empty(selected);
                Assert.Empty(sink.Payloads);
            }
            catch (Exception error) { primary = error; throw; }
            finally
            {
                await MaterialPricingTestCleanup.ReleaseAllAsync(primary,
                    async () => await MaterialPricingTestCleanup.DrainQuoteAsync(
                        reconciling, () => lease.Cancel(), () => release.TrySetResult(), lease.Token, null,
                        receipt => Assert.True(receipt.ActualTaskSettled), TimeSpan.FromSeconds(5),
                        expectedTerminalFailure: false, taskKind: "nullable-preview-admission"));
            }
        });
    }


    [Theory]
    [InlineData(null)]
    [InlineData("9")]
    [InlineData("")]
    public async Task Workflow_QuantityInputReturningToAcceptedValueKeepsReviewAvailableWithoutChangeEvent(string? draft)
    {
        var part = Part("PLA", BuildPreference.Standard);
        var store = new SessionStore(part);
        await using var coordinator = new InstantQuotationWorkflowCoordinator(
            store, new UnusedUploadClient(), new InstantQuotationPricingService(), null,
            authoritativePricingService: SyntheticAuthoritativePricingTestService.Instance);
        await coordinator.InitializeAsync("presentation-session", default);
        var activator = new CapturingActivator(coordinator);
        using var services = Services(activator, new RecordingJavaScript());
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<InstantQuotationWorkflow>(ParameterView.Empty);
            var component = Assert.IsType<InstantQuotationWorkflow>(activator.Workflow);
            bool CanReview() => (bool)typeof(InstantQuotationWorkflow)
                .GetProperty("CanEnterReview", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(component)!;
            void Input(string raw) => typeof(InstantQuotationWorkflow)
                .GetMethod("OnQuantityInput", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(component, [part.PartId, new ChangeEventArgs { Value = raw }]);
            Assert.True(CanReview());
            var quote = Assert.IsType<InstantQuotationOrderQuote>(coordinator.OrderQuote);
            var writes = 0;
            store.BeforePut = () => { writes++; return Task.CompletedTask; };
            if (draft is not null)
            {
                Input(draft);
                Assert.False(CanReview());
            }

            // A native input restored to its original value need not emit change on blur.
            Input("1");
            Assert.True(CanReview());
            Assert.Same(quote, coordinator.OrderQuote);
            Assert.Equal(1, Assert.Single(coordinator.Parts).Configuration.Quantity);
            Assert.Equal(0, writes);
        });
    }


    [Fact]
    public async Task Workflow_MatchingQuantityInputDoesNotAcceptAnInFlightConfiguration()
    {
        var part = Part("PLA", BuildPreference.Standard);
        var store = new SessionStore(part);
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
            await renderer.RenderComponentAsync<InstantQuotationWorkflow>(ParameterView.Empty);
            var component = Assert.IsType<InstantQuotationWorkflow>(activator.Workflow);
            bool CanReview() => (bool)typeof(InstantQuotationWorkflow)
                .GetProperty("CanEnterReview", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(component)!;
            store.BeforePut = async () =>
            {
                store.BeforePut = null;
                entered.TrySetResult();
                await release.Task.WaitAsync(lease.Token);
            };
            var changing = (Task)typeof(InstantQuotationWorkflow)
                .GetMethod("ChangeQuantityAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(component, [part.PartId, new ChangeEventArgs { Value = "2" }])!;
            Exception? primary = null;
            try
            {
                await entered.Task.WaitAsync(lease.Token);
                Assert.False(changing.IsCompleted);
                typeof(InstantQuotationWorkflow)
                    .GetMethod("OnQuantityInput", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(component, [part.PartId, new ChangeEventArgs { Value = "2" }]);
                Assert.False(CanReview());
                release.TrySetResult();
                await changing.WaitAsync(lease.Token);
                Assert.False(CanReview());
            }
            catch (Exception error) { primary = error; throw; }
            finally
            {
                await MaterialPricingTestCleanup.ReleaseAllAsync(primary,
                    async () => await MaterialPricingTestCleanup.DrainQuoteAsync(
                        changing, () => lease.Cancel(), () => release.TrySetResult(), lease.Token, null,
                        receipt => Assert.True(receipt.ActualTaskSettled), TimeSpan.FromSeconds(5),
                        expectedTerminalFailure: false, taskKind: "unchanged-quantity-in-flight"));
            }
        });
    }


    [Theory]
    [InlineData("material-in-flight")]
    [InlineData("change-before-blur")]
    [InlineData("newer-draft")]
    [InlineData("idle-accepted")]
    public async Task Workflow_QuantityBlurAcknowledgesOnlyTheLatestUnclaimedDraft(string scenario)
    {
        var part = Part("PLA", BuildPreference.Standard);
        var store = new SessionStore(part);
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
            await renderer.RenderComponentAsync<InstantQuotationWorkflow>(ParameterView.Empty);
            var component = Assert.IsType<InstantQuotationWorkflow>(activator.Workflow);
            bool CanReview() => (bool)typeof(InstantQuotationWorkflow)
                .GetProperty("CanEnterReview", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(component)!;
            object? Invoke(string method, params object?[] args) => typeof(InstantQuotationWorkflow)
                .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(component, args);
            void Input(string raw) => Invoke("OnQuantityInput", part.PartId, new ChangeEventArgs { Value = raw });
            Task Change(string raw) => (Task)Invoke("ChangeQuantityAsync", part.PartId, new ChangeEventArgs { Value = raw })!;
            // The current component has no blur handler. Model that event as a no-op so RED
            // reports the unacknowledged draft, rather than a missing reflection target.
            Task Blur() => (Task?)typeof(InstantQuotationWorkflow)
                .GetMethod("OnQuantityBlurAsync", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.Invoke(component, [part.PartId]) ?? Task.CompletedTask;
            Assert.True(CanReview());
            var originalQuote = coordinator.OrderQuote;
            var writes = 0;
            store.BeforePut = async () =>
            {
                if (Interlocked.Increment(ref writes) == 1 && scenario != "idle-accepted")
                {
                    entered.TrySetResult();
                    await release.Task.WaitAsync(lease.Token);
                }
            };
            Task changing = Task.CompletedTask;
            Task blurring = Task.CompletedTask;
            Exception? primary = null;
            try
            {
                if (scenario == "idle-accepted")
                {
                    Input("1");
                    await Blur();
                    Assert.True(CanReview());
                    Assert.Same(originalQuote, coordinator.OrderQuote);
                    Assert.Equal(0, writes);
                    return;
                }

                if (scenario == "material-in-flight")
                    changing = (Task)Invoke("ChangeMaterialAsync", part.PartId, new ChangeEventArgs { Value = "PETG" })!;
                else
                {
                    Input(scenario == "newer-draft" ? "2" : "9");
                    changing = Change(scenario == "newer-draft" ? "2" : "9");
                }
                await entered.Task.WaitAsync(lease.Token);
                Assert.False(changing.IsCompleted);
                if (scenario == "material-in-flight") Input("1");
                if (scenario == "newer-draft") Input("9");
                Assert.False(CanReview());
                blurring = Blur();
                release.TrySetResult();
                await Task.WhenAll(changing, blurring).WaitAsync(lease.Token);
                var accepted = Assert.Single(coordinator.Parts);
                Assert.Equal(scenario == "material-in-flight" ? "PETG" : "PLA", accepted.Configuration.MaterialKey);
                Assert.Equal(scenario == "material-in-flight" ? 1 : 9, accepted.Configuration.Quantity);
                Assert.Equal(accepted.Configuration.Quantity, Assert.Single(coordinator.OrderQuote!.Parts).Quantity);
                Assert.True(CanReview());
                if (scenario == "change-before-blur") Assert.Equal(1, writes);
                var settledWrites = writes;
                await Blur();
                Assert.Equal(settledWrites, writes);
            }
            catch (Exception error) { primary = error; throw; }
            finally
            {
                await MaterialPricingTestCleanup.ReleaseAllAsync(primary,
                    async () => await MaterialPricingTestCleanup.DrainQuoteAsync(
                        Task.WhenAll(changing, blurring), () => lease.Cancel(), () => release.TrySetResult(), lease.Token, null,
                        receipt => Assert.True(receipt.ActualTaskSettled), TimeSpan.FromSeconds(5),
                        expectedTerminalFailure: false, taskKind: "quantity-blur-ownership"));
            }
        });
    }


    [Fact]
    public async Task InvalidSamePartUiEdit_DoesNotRetireAValidReachedRepricingActivity()
    {
        var part = Part("PLA", BuildPreference.Standard);
        var store = new SessionStore(part);
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
            await renderer.RenderComponentAsync<InstantQuotationWorkflow>(ParameterView.Empty);
            var component = Assert.IsType<InstantQuotationWorkflow>(activator.Workflow);
            Task Change(int quantity) => (Task)typeof(InstantQuotationWorkflow)
                .GetMethod("ChangeQuantityAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(component, [part.PartId, new ChangeEventArgs { Value = quantity.ToString(System.Globalization.CultureInfo.InvariantCulture) }])!;
            bool Active() => (bool)typeof(InstantQuotationWorkflow)
                .GetProperty("IsRepricing", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(component)!;
            store.BeforePut = async () =>
            {
                store.BeforePut = null;
                entered.TrySetResult();
                await release.Task.WaitAsync(lease.Token);
            };
            var valid = Change(2);
            Exception? primary = null;
            try
            {
                await entered.Task.WaitAsync(lease.Token);
                Assert.True(Active());
                await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Change(0));
                Assert.True(Active());
                Assert.False(valid.IsCompleted);
                release.TrySetResult();
                await valid.WaitAsync(lease.Token);
                Assert.False(Active());
                Assert.Equal(2, Assert.Single(coordinator.Parts).Configuration.Quantity);
            }
            catch (Exception exception) { primary = exception; throw; }
            finally
            {
                await MaterialPricingTestCleanup.ReleaseAllAsync(primary,
                    async () => await MaterialPricingTestCleanup.DrainQuoteAsync(
                        valid, () => lease.Cancel(), () => release.TrySetResult(), lease.Token, null,
                        receipt => Assert.True(receipt.ActualTaskSettled), TimeSpan.FromSeconds(5),
                        expectedTerminalFailure: false, taskKind: "same-part-ui-repricing"));
            }
        });
    }


    [Fact]
    public async Task CurrentMaterialReview_DoesNotWaitForOrRunTheObsoleteAppearanceContinuation()
    {
        var part = Part("PLA", BuildPreference.Standard);
        var store = new SessionStore(part);
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
        var appearances = new List<string>();
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<InstantQuotationWorkflow>(ParameterView.Empty);
            var component = Assert.IsType<InstantQuotationWorkflow>(activator.Workflow);
            var preview = new ControlledPreviewReference(async (name, args) =>
            {
                if (name == "setThicknessMaterial" && args?[1]?.ToString() == "PETG")
                {
                    entered.TrySetResult();
                    await release.Task.WaitAsync(lease.Token);
                }
                if (name == "updatePartAppearance") appearances.Add(args![0]!.ToString()!);
                return null;
            });
            typeof(InstantQuotationWorkflow).GetField("previewInterop", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(component, preview);
            Task Change(string material) => (Task)typeof(InstantQuotationWorkflow)
                .GetMethod("ChangeMaterialAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(component, [part.PartId, new ChangeEventArgs { Value = material }])!;
            bool Property(string name) => (bool)typeof(InstantQuotationWorkflow)
                .GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(component)!;
            var old = Change("PETG");
            Task newer = Task.CompletedTask;
            Exception? primary = null;
            try
            {
                await entered.Task.WaitAsync(lease.Token);
                newer = Change("PLA");
                await newer.WaitAsync(lease.Token);
                Assert.False(old.IsCompleted);
                Assert.False(Property("IsRepricing"));
                Assert.True(Property("CanEnterReview"));
                Assert.Equal(part.PartId.ToString("N"), Assert.Single(appearances));
                var quote = Assert.IsType<InstantQuotationOrderQuote>(coordinator.OrderQuote);
                release.TrySetResult();
                await old.WaitAsync(lease.Token);
                Assert.Equal(part.PartId.ToString("N"), Assert.Single(appearances));
                Assert.Same(quote, coordinator.OrderQuote);
                Assert.Equal("PLA", Assert.Single(coordinator.Parts).Configuration.MaterialKey);
                Assert.True(Property("CanEnterReview"));
            }
            catch (Exception exception) { primary = exception; throw; }
            finally
            {
                await MaterialPricingTestCleanup.ReleaseAllAsync(primary,
                    async () => await MaterialPricingTestCleanup.DrainQuoteAsync(
                        old, () => lease.Cancel(), () => release.TrySetResult(), lease.Token, null,
                        receipt => Assert.True(receipt.ActualTaskSettled), TimeSpan.FromSeconds(5),
                        expectedTerminalFailure: false, taskKind: "obsolete-material-appearance"),
                    async () => await MaterialPricingTestCleanup.DrainQuoteAsync(
                        newer, () => lease.Cancel(), () => release.TrySetResult(), lease.Token, null,
                        receipt => Assert.True(receipt.ActualTaskSettled), TimeSpan.FromSeconds(5),
                        expectedTerminalFailure: false, taskKind: "current-material-appearance"));
            }
        });
    }

    [Theory]
    [InlineData("Material", false, "none")]
    [InlineData("Color", false, "none")]
    [InlineData("Quantity", false, "none")]
    [InlineData("BuildPreference", false, "none")]
    [InlineData("Material", true, "none")]
    [InlineData("Material", false, "same")]
    [InlineData("Material", false, "back")]
    [InlineData("Material", false, "other")]
    public async Task ReviewEditSupersession_ReentersOnlyForCurrentCompletedOwnership(
        string field, bool callbackFault, string navigation)
    {
        var part = Part("PLA", BuildPreference.Standard);
        var other = Part("PLA", BuildPreference.Quality);
        var store = new SessionStore(part, other);
        var pricing = new HeldReviewPricing();
        await using var coordinator = new InstantQuotationWorkflowCoordinator(
            store, new UnusedUploadClient(), new InstantQuotationPricingService(), null,
            authoritativePricingService: pricing);
        await coordinator.InitializeAsync("presentation-session", default);
        coordinator.EnterReview();
        var activator = new CapturingActivator(coordinator);
        using var services = Services(activator, new RecordingJavaScript());
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        using var lease = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var older = pricing.Arm(callbackFault);
        var latest = pricing.Arm(false);
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<InstantQuotationWorkflow>(ParameterView.Empty);
            var component = Assert.IsType<InstantQuotationWorkflow>(activator.Workflow);
            Task Change(string value) => (Task)typeof(InstantQuotationWorkflow)
                .GetMethod("ChangeReview" + field + "Async", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(component, [(part.PartId, value)])!;
            var values = field switch
            {
                "Material" => ("PETG", "PLA"),
                "Color" => ("Black", "White"),
                "Quantity" => ("2", "3"),
                _ => ("Strength", "Quality"),
            };
            var first = Change(values.Item1);
            Task current = Task.CompletedTask;
            Exception? primary = null;
            try
            {
                await older.Entered.Task.WaitAsync(lease.Token);
                current = Change(values.Item2);
                await latest.Entered.Task.WaitAsync(lease.Token);
                await first.WaitAsync(lease.Token);
                Assert.False(older.Actual!.IsCompleted);
                Assert.False(current.IsCompleted);
                Assert.Null(coordinator.OrderQuote);
                Assert.Equal(InstantQuotationWorkflowState.Review, coordinator.State);
                if (navigation == "back")
                    typeof(InstantQuotationWorkflow)
                        .GetMethod("ReturnToConfiguration", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(component, null);
                else if (navigation != "none")
                    await (Task)typeof(InstantQuotationWorkflow)
                        .GetMethod("EditReviewPartAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(component, [navigation == "other" ? other.PartId : part.PartId])!;
                latest.Release.TrySetResult();
                await current.WaitAsync(lease.Token);
                var expectedState = navigation != "none"
                    ? InstantQuotationWorkflowState.Configured : InstantQuotationWorkflowState.Review;
                Assert.Equal(expectedState, coordinator.State);
                var quote = Assert.IsType<InstantQuotationOrderQuote>(coordinator.OrderQuote);
                var configuration = coordinator.Parts.Single(row => row.PartId == part.PartId).Configuration;
                older.Release.TrySetResult();
                await older.Actual.WaitAsync(lease.Token);
                Assert.Same(quote, coordinator.OrderQuote);
                Assert.Equal(configuration, coordinator.Parts.Single(row => row.PartId == part.PartId).Configuration);
                Assert.Equal(expectedState, coordinator.State);
                Assert.False((bool)typeof(InstantQuotationWorkflow)
                    .GetProperty("IsRepricing", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(component)!);
            }
            catch (Exception exception) { primary = exception; throw; }
            finally
            {
                await MaterialPricingTestCleanup.ReleaseAllAsync(primary,
                    async () => await MaterialPricingTestCleanup.DrainQuoteAsync(
                        first, () => lease.Cancel(), () => older.Release.TrySetResult(), lease.Token, null,
                        receipt => Assert.True(receipt.ActualTaskSettled), TimeSpan.FromSeconds(5),
                        expectedTerminalFailure: false, taskKind: "obsolete-review-edit"),
                    async () => await MaterialPricingTestCleanup.DrainQuoteAsync(
                        current, () => lease.Cancel(), () => latest.Release.TrySetResult(), lease.Token, null,
                        receipt => Assert.True(receipt.ActualTaskSettled), TimeSpan.FromSeconds(5),
                        expectedTerminalFailure: false, taskKind: "current-review-edit"),
                    async () => await pricing.DrainAsync());
            }
        });
    }

    private sealed class HeldReviewPricing : IInstantQuotationAuthoritativePricingService
    {
        internal sealed class Pending(bool callbackFault)
        {
            internal bool CallbackFault { get; } = callbackFault;
            internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            internal Task? Actual { get; set; }
        }

        private readonly Queue<Pending> queued = new();
        private readonly List<Pending> owned = [];

        internal Pending Arm(bool callbackFault)
        {
            var next = new Pending(callbackFault);
            queued.Enqueue(next);
            owned.Add(next);
            return next;
        }

        public Task<InstantQuotationOrderQuote?> QuoteAsync(InstantQuotationSessionState session,
            string? owner, bool includeComparisons, CancellationToken token)
        {
            if (!queued.TryDequeue(out var next))
                return SyntheticAuthoritativePricingTestService.Instance.QuoteAsync(
                    session, owner, includeComparisons, token);
            var task = RunAsync();
            next.Actual = task;
            return task;

            async Task<InstantQuotationOrderQuote?> RunAsync()
            {
                using var registration = next.CallbackFault
                    ? token.Register(static () => throw new InvalidOperationException("controlled callback"))
                    : default;
                var result = await SyntheticAuthoritativePricingTestService.Instance.QuoteAsync(
                    session, owner, includeComparisons, CancellationToken.None);
                next.Entered.TrySetResult();
                await next.Release.Task;
                return result;
            }
        }

        internal async Task DrainAsync()
        {
            foreach (var next in owned) next.Release.TrySetResult();
            foreach (var next in owned)
                if (next.Actual is { } actual) await actual.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PendingLocalParseRemoval_SettlesOwnershipBeforeActualCallbackReturns(bool disposes)
    {
        var healthy = Part("PLA", BuildPreference.Standard);
        await using var coordinator = new InstantQuotationWorkflowCoordinator(
            new SessionStore(healthy), new SyntheticUploadClient(), new InstantQuotationPricingService(), null,
            authoritativePricingService: SyntheticAuthoritativePricingTestService.Instance);
        await coordinator.InitializeAsync("presentation-session", default);
        var activator = new CapturingActivator(coordinator);
        using var services = Services(activator, new RecordingJavaScript());
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        using var lease = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var preview = new PendingParsePreviewReference(lease.Token);
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var root = await renderer.RenderComponentAsync<InstantQuotationWorkflow>(ParameterView.Empty);
            var component = Assert.IsType<InstantQuotationWorkflow>(activator.Workflow);
            void Set(string name, object value) => typeof(InstantQuotationWorkflow)
                .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(component, value);
            object? Invoke(string name, params object[] args) => typeof(InstantQuotationWorkflow)
                .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(component, args);
            Set("previewInterop", preview);
            Set("fileInput", new InputFile());
            Set("selectedPreviewPartId", healthy.PartId);
            var quote = coordinator.OrderQuote;
            var uploading = (Task)Invoke("UploadFilesAsync",
                new InputFileChangeEventArgs(new IBrowserFile[] { new SyntheticBrowserFile() }))!;
            Exception? primary = null;
            try
            {
                await preview.Entered.Task.WaitAsync(lease.Token);
                Assert.Contains("data-workflow-pending-parse", root.ToHtmlString(), StringComparison.Ordinal);
                Assert.Single(coordinator.Parts);
                if (disposes) await component.DisposeAsync();
                else await (Task)Invoke("RemovePendingPreviewAsync", "pending-preview")!;
                await uploading.WaitAsync(lease.Token);
                Assert.False(preview.ActualParser.IsCompleted);
                Assert.Single(coordinator.Parts);
                Assert.Equal(healthy.PartId, Assert.Single(coordinator.Parts).PartId);
                Assert.Same(quote, coordinator.OrderQuote);
                if (!disposes)
                {
                    Assert.DoesNotContain("data-workflow-pending-parse", root.ToHtmlString(), StringComparison.Ordinal);
                    Assert.DoesNotContain("data-workflow-failed-preview", root.ToHtmlString(), StringComparison.Ordinal);
                    Assert.True((bool)typeof(InstantQuotationWorkflow)
                        .GetProperty("CanEnterReview", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(component)!);
                    Assert.Equal(healthy.PartId, (Guid?)typeof(InstantQuotationWorkflow)
                        .GetField("selectedPreviewPartId", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(component));
                }
                preview.CallbackRelease.TrySetResult();
                await preview.ActualParser.WaitAsync(lease.Token);
                Assert.Single(coordinator.Parts);
                Assert.Equal(healthy.Configuration, Assert.Single(coordinator.Parts).Configuration);
                Assert.Same(quote, coordinator.OrderQuote);
            }
            catch (Exception exception) { primary = exception; throw; }
            finally
            {
                await MaterialPricingTestCleanup.ReleaseAllAsync(primary,
                    async () => await MaterialPricingTestCleanup.DrainQuoteAsync(
                        uploading, () => lease.Cancel(), () => preview.Release(), lease.Token, null,
                        receipt => Assert.True(receipt.ActualTaskSettled), TimeSpan.FromSeconds(5),
                        expectedTerminalFailure: false, taskKind: "removed-pending-parse-consumer"),
                    async () =>
                    {
                        preview.CallbackRelease.TrySetResult();
                        await preview.ActualParser.WaitAsync(TimeSpan.FromSeconds(5));
                    });
            }
        });
    }

    [Theory]
    [InlineData("en", "unreadable", "Invalid file. Repair the model and reupload it.")]
    [InlineData("th", "unreadable", "ไฟล์ไม่สมบูรณ์ กรุณาแก้ไขโมเดลแล้วอัปโหลดใหม่")]
    [InlineData("en", "complexity", "This file exceeds the analysis limits. Simplify the mesh or contact our engineers for a quotation.")]
    [InlineData("th", "complexity", "ไฟล์นี้เกินขีดจำกัดของการวิเคราะห์ กรุณาลดความซับซ้อนของเมชหรือติดต่อวิศวกรเพื่อขอใบเสนอราคา")]
    [InlineData("en", "unavailable", "3D file processing is unavailable. Try again or contact our engineers for a quotation.")]
    [InlineData("th", "unavailable", "ไม่สามารถประมวลผลไฟล์ 3 มิติได้ โปรดลองอีกครั้งหรือติดต่อวิศวกรเพื่อขอใบเสนอราคา")]
    [InlineData("en", "private decoder detail", "3D file processing is unavailable. Try again or contact our engineers for a quotation.")]
    public async Task LocalPreviewFailure_UsesOnlyFixedAdvisoryCategoryAndRetainsHealthyAuthority(
        string culture, string category, string expected)
    {
        using var cultures = new CultureScope(culture);
        var healthy = Part("PLA", BuildPreference.Strength);
        await using var coordinator = new InstantQuotationWorkflowCoordinator(
            new SessionStore(healthy), new UnusedUploadClient(), new InstantQuotationPricingService(), null,
            authoritativePricingService: SyntheticAuthoritativePricingTestService.Instance);
        await coordinator.InitializeAsync("presentation-session", default);
        var quote = coordinator.OrderQuote;
        var activator = new CapturingActivator(coordinator);
        using var services = Services(activator, new RecordingJavaScript());
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var root = await renderer.RenderComponentAsync<InstantQuotationWorkflow>(ParameterView.Empty);
            var component = Assert.IsType<InstantQuotationWorkflow>(activator.Workflow);
            var preview = new ControlledPreviewReference((name, _) =>
            {
                if (name == "beginSelection") return Task.FromResult<object?>(new[] { "failed-category" });
                if (name == "getGeometryClaim") throw new JSException("private decoder detail");
                return Task.FromResult<object?>(name == "getFailureCategory" ? category : null);
            });
            void Set(string name, object value) => typeof(InstantQuotationWorkflow)
                .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(component, value);
            object? Invoke(string name, params object[] arguments) => typeof(InstantQuotationWorkflow)
                .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(component, arguments);
            Set("previewInterop", preview);
            Set("fileInput", new InputFile());
            using var lease = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var selecting = (Task)Invoke("UploadFilesAsync", new InputFileChangeEventArgs(
                new IBrowserFile[] { new UnreadableBrowserFile() }))!;
            Exception? primary = null;
            try
            {
                await selecting.WaitAsync(lease.Token);
                await (Task)Invoke("SelectFailedPreviewAsync", "failed-category")!;
                var html = WebUtility.HtmlDecode(root.ToHtmlString());
                Assert.Contains(expected, html, StringComparison.Ordinal);
                Assert.DoesNotContain("private decoder detail", html, StringComparison.Ordinal);
                if (category != "unreadable")
                    Assert.DoesNotContain("Invalid file. Repair", html, StringComparison.Ordinal);
                Assert.Equal(healthy.PartId, Assert.Single(coordinator.Parts).PartId);
                Assert.Equal(healthy.Configuration, Assert.Single(coordinator.Parts).Configuration);
                Assert.Same(quote, coordinator.OrderQuote);
                Assert.False((bool)typeof(InstantQuotationWorkflow)
                    .GetProperty("CanEnterReview", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(component)!);
                await (Task)Invoke("RemoveFailedPreviewAsync", "failed-category")!;
                Assert.True((bool)typeof(InstantQuotationWorkflow)
                    .GetProperty("CanEnterReview", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(component)!);
                Assert.Same(quote, coordinator.OrderQuote);
            }
            catch (Exception exception) { primary = exception; throw; }
            finally
            {
                await MaterialPricingTestCleanup.ReleaseAllAsync(primary,
                    async () => await MaterialPricingTestCleanup.DrainQuoteAsync(
                        selecting, () => lease.Cancel(), () => { }, lease.Token, null,
                        receipt => Assert.True(receipt.ActualTaskSettled), TimeSpan.FromSeconds(5),
                        expectedTerminalFailure: false, taskKind: "advisory-failure-selection"));
            }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PendingFailureCategory_IgnoresRemovedOrDisposedLocalSelection(bool disposes)
    {
        var healthy = Part("PLA", BuildPreference.Standard);
        await using var coordinator = new InstantQuotationWorkflowCoordinator(
            new SessionStore(healthy), new UnusedUploadClient(), new InstantQuotationPricingService(), null,
            authoritativePricingService: SyntheticAuthoritativePricingTestService.Instance);
        await coordinator.InitializeAsync("presentation-session", default);
        var quote = coordinator.OrderQuote;
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
            var preview = new ControlledPreviewReference(async (name, _) =>
            {
                if (name == "beginSelection") return new[] { "held-category" };
                if (name == "getGeometryClaim") throw new JSException("private parser detail");
                if (name == "getFailureCategory")
                {
                    entered.TrySetResult();
                    await release.Task.WaitAsync(lease.Token);
                    return "unavailable";
                }
                return null;
            });
            void Set(string name, object value) => typeof(InstantQuotationWorkflow)
                .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(component, value);
            object? Invoke(string name, params object[] arguments) => typeof(InstantQuotationWorkflow)
                .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(component, arguments);
            Set("previewInterop", preview);
            Set("fileInput", new InputFile());
            var selecting = (Task)Invoke("UploadFilesAsync", new InputFileChangeEventArgs(
                new IBrowserFile[] { new UnreadableBrowserFile() }))!;
            Exception? primary = null;
            try
            {
                await entered.Task.WaitAsync(lease.Token);
                if (disposes) await component.DisposeAsync();
                else await (Task)Invoke("RemovePendingPreviewAsync", "held-category")!;
                release.TrySetResult();
                await selecting.WaitAsync(lease.Token);
                Assert.Empty((System.Collections.IDictionary)typeof(InstantQuotationWorkflow)
                    .GetField("failedPreviewSelections", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(component)!);
                Assert.Equal(healthy.PartId, Assert.Single(coordinator.Parts).PartId);
                Assert.Same(quote, coordinator.OrderQuote);
                if (!disposes)
                {
                    Assert.DoesNotContain("data-workflow-failed-preview", root.ToHtmlString(), StringComparison.Ordinal);
                    Assert.True((bool)typeof(InstantQuotationWorkflow)
                        .GetProperty("CanEnterReview", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(component)!);
                }
            }
            catch (Exception exception) { primary = exception; throw; }
            finally
            {
                await MaterialPricingTestCleanup.ReleaseAllAsync(primary,
                    async () => await MaterialPricingTestCleanup.DrainQuoteAsync(
                        selecting, () => lease.Cancel(), () => release.TrySetResult(), lease.Token, null,
                        receipt => Assert.True(receipt.ActualTaskSettled), TimeSpan.FromSeconds(5),
                        expectedTerminalFailure: false, taskKind: "advisory-failure-category"));
            }
        });
    }

    private sealed class PendingParsePreviewReference(CancellationToken lease) : IJSObjectReference
    {
        private readonly TaskCompletionSource<InstantQuotationGeometryClaim> completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource CallbackRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Task ActualParser { get; private set; } = Task.CompletedTask;
        internal void Release() => completion.TrySetException(new JSException("Controlled removed parse."));

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, default, args);

        public async ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            if (identifier == "beginSelection") return (TValue)(object)new[] { "pending-preview" };
            if (identifier == "getGeometryClaim")
            {
                ActualParser = ParseAsync();
                Entered.TrySetResult();
                return (TValue)(object)await completion.Task.WaitAsync(cancellationToken);
            }
            if (identifier == "release") Release();
            return default!;
        }

        private async Task ParseAsync()
        {
            await CallbackRelease.Task.WaitAsync(lease);
            completion.TrySetResult(new InstantQuotationGeometryClaim(1, new string('a', 64),
                10, 10, 10, 1000, 600, Enumerable.Repeat(100d, 64).ToArray(),
                Enumerable.Repeat(40d, 64).ToArray(), 100, 1, true, false, false, 0.8));
        }

        public ValueTask DisposeAsync() { Release(); return ValueTask.CompletedTask; }
    }

    [Fact]
    public async Task RemovedPendingPlanarContinuation_CannotResurrectAfterPreviousPreviewRemoval()
    {
        var healthy = Part("PLA", BuildPreference.Standard);
        await using var coordinator = new InstantQuotationWorkflowCoordinator(
            new SessionStore(healthy), new UnusedUploadClient(), new InstantQuotationPricingService(), null,
            authoritativePricingService: SyntheticAuthoritativePricingTestService.Instance);
        await coordinator.InitializeAsync("presentation-session", default);
        var quote = coordinator.OrderQuote;
        var activator = new CapturingActivator(coordinator);
        using var services = Services(activator, new RecordingJavaScript());
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        using var lease = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var claims = new List<string>();
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<InstantQuotationWorkflow>(ParameterView.Empty);
            var component = Assert.IsType<InstantQuotationWorkflow>(activator.Workflow);
            var preview = new ControlledPreviewReference(async (name, args) =>
            {
                if (name == "beginSelection") return new[] { "first-planar", "removed-planar", "removed-future" };
                if (name == "getGeometryClaim")
                {
                    claims.Add((string)args![0]!);
                    return new InstantQuotationGeometryClaim(1, new string('a', 64), 10, 10, 0, 0, 200,
                        Enumerable.Repeat(100d, 64).ToArray(), Enumerable.Repeat(40d, 64).ToArray(),
                        2, 1, true, true, false, 0);
                }
                if (name == "isIncompletePreview") return true;
                if (name == "removeIncompletePreview" && args?[0]?.ToString() == "first-planar")
                {
                    entered.TrySetResult();
                    await release.Task.WaitAsync(lease.Token);
                }
                return null;
            });
            void Set(string name, object value) => typeof(InstantQuotationWorkflow)
                .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(component, value);
            object? Invoke(string name, params object[] args) => typeof(InstantQuotationWorkflow)
                .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(component, args);
            Set("previewInterop", preview);
            Set("fileInput", new InputFile());
            var uploading = (Task)Invoke("UploadFilesAsync", new InputFileChangeEventArgs(new IBrowserFile[]
            {
                new SyntheticBrowserFile("first.stl"), new SyntheticBrowserFile("removed.stl"),
                new SyntheticBrowserFile("future.stl"),
            }))!;
            Exception? primary = null;
            try
            {
                await entered.Task.WaitAsync(lease.Token);
                await (Task)Invoke("RemovePendingPreviewAsync", "removed-planar")!;
                await (Task)Invoke("RemovePendingPreviewAsync", "removed-future")!;
                release.TrySetResult();
                await uploading.WaitAsync(lease.Token);
                Assert.Equal(new[] { "first-planar", "removed-planar", "removed-future" }.AsEnumerable(), claims);
                Assert.DoesNotContain(coordinator.Uploads, row =>
                    row.DisplayFileName is "removed.stl" or "future.stl");
                Assert.Null(typeof(InstantQuotationWorkflow)
                    .GetField("incompletePreviewKey", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(component));
                Assert.Equal(healthy.Configuration, Assert.Single(coordinator.Parts).Configuration);
                Assert.Same(quote, coordinator.OrderQuote);
                var failed = (System.Collections.IDictionary)typeof(InstantQuotationWorkflow)
                    .GetField("failedPreviewSelections", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(component)!;
                Assert.DoesNotContain("removed-planar", failed.Keys.Cast<string>());
                Assert.DoesNotContain("removed-future", failed.Keys.Cast<string>());
            }
            catch (Exception exception) { primary = exception; throw; }
            finally
            {
                await MaterialPricingTestCleanup.ReleaseAllAsync(primary,
                    async () => await MaterialPricingTestCleanup.DrainQuoteAsync(
                        uploading, () => lease.Cancel(), () => release.TrySetResult(), lease.Token, null,
                        receipt => Assert.True(receipt.ActualTaskSettled), TimeSpan.FromSeconds(5),
                        expectedTerminalFailure: false, taskKind: "removed-planar-continuation"));
            }
        });
    }

    [Fact]
    public async Task PendingParseCleanup_DisposalBetweenReleasesPreservesPrimaryAndSettlesGate()
    {
        var healthy = Part("PLA", BuildPreference.Standard);
        await using var coordinator = new InstantQuotationWorkflowCoordinator(
            new SessionStore(healthy), new UnusedUploadClient(), new InstantQuotationPricingService(), null,
            authoritativePricingService: SyntheticAuthoritativePricingTestService.Instance);
        await coordinator.InitializeAsync("presentation-session", default);
        var activator = new CapturingActivator(coordinator);
        using var services = Services(activator, new RecordingJavaScript());
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        using var lease = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var preview = new DisposedCleanupPreviewReference(lease.Token);
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<InstantQuotationWorkflow>(ParameterView.Empty);
            var component = Assert.IsType<InstantQuotationWorkflow>(activator.Workflow);
            void Set(string name, object value) => typeof(InstantQuotationWorkflow)
                .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(component, value);
            Set("previewInterop", preview);
            Set("fileInput", new InputFile());
            var uploading = (Task)typeof(InstantQuotationWorkflow)
                .GetMethod("UploadFilesAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(component, [new InputFileChangeEventArgs(new IBrowserFile[]
                { new SyntheticBrowserFile(), new SyntheticBrowserFile() })])!;
            Exception? primary = null;
            try
            {
                await preview.Entered.Task.WaitAsync(lease.Token);
                await component.DisposeAsync();
                await component.DisposeAsync();
                Assert.Equal(1, preview.DisposalCalls);
                preview.Release.TrySetResult();
                var failure = await Assert.ThrowsAsync<InvalidDataException>(() => uploading);
                Assert.Same(preview.Primary, failure);
                Assert.Equal(new[] { "first", "second" }.AsEnumerable(), preview.Attempts);
                Assert.False((bool)typeof(InstantQuotationWorkflow)
                    .GetField("batchInProgress", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(component)!);
                var gate = (SemaphoreSlim)typeof(InstantQuotationWorkflow)
                    .GetField("uploadBatchGate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(component)!;
                Assert.Throws<ObjectDisposedException>(() => gate.Wait(0));
                Assert.Single(coordinator.Parts);
            }
            catch (Exception exception) { primary = exception; throw; }
            finally
            {
                await MaterialPricingTestCleanup.ReleaseAllAsync(primary,
                    async () => await MaterialPricingTestCleanup.DrainQuoteAsync(
                        uploading, () => lease.Cancel(), () => preview.Release.TrySetResult(), lease.Token, null,
                        receipt => Assert.True(receipt.ActualTaskSettled), TimeSpan.FromSeconds(5),
                        expectedTerminalFailure: true, taskKind: "primary-parse-cleanup"));
            }
        });
    }

    private sealed class DisposedCleanupPreviewReference(CancellationToken lease) : IJSObjectReference
    {
        private bool disposed;
        internal int DisposalCalls { get; private set; }
        internal InvalidDataException Primary { get; } = new("Controlled parser boundary.");
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal List<string> Attempts { get; } = [];
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, default, args);
        public async ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            if (identifier == "release")
            {
                Attempts.Add((string)args![0]!);
                if (Attempts.Count == 1)
                {
                    Entered.TrySetResult();
                    await Release.Task.WaitAsync(lease);
                }
            }
            ObjectDisposedException.ThrowIf(disposed, this);
            if (identifier == "beginSelection") return (TValue)(object)new[] { "first", "second" };
            if (identifier == "getGeometryClaim") throw Primary;
            return default!;
        }
        public ValueTask DisposeAsync() { DisposalCalls++; disposed = true; return ValueTask.CompletedTask; }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AdmittedRemoveUi_RetiresGateOnlyAfterAuthorizedAcknowledgement(bool succeeds)
    {
        using var culture = new CultureScope("en");
        var part = Part("PLA", BuildPreference.Standard);
        var other = Part("PETG", BuildPreference.Quality);
        var removal = new AcknowledgedRemoveClient(succeeds);
        var pricing = new HeldReviewPricing();
        await using var coordinator = new InstantQuotationWorkflowCoordinator(
            new SessionStore(part, other), removal, new InstantQuotationPricingService(), null,
            authoritativePricingService: pricing);
        await coordinator.InitializeAsync("presentation-session", default);
        var activator = new CapturingActivator(coordinator);
        using var services = Services(activator, new RecordingJavaScript());
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        using var lease = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var older = pricing.Arm(false);
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var root = await renderer.RenderComponentAsync<InstantQuotationWorkflow>(ParameterView.Empty);
            var component = Assert.IsType<InstantQuotationWorkflow>(activator.Workflow);
            object? Invoke(string name, params object[] args) => typeof(InstantQuotationWorkflow)
                .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(component, args);
            bool Active() => (bool)typeof(InstantQuotationWorkflow)
                .GetProperty("IsRepricing", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(component)!;
            var updating = (Task)Invoke("ChangeMaterialAsync", part.PartId, new ChangeEventArgs { Value = "PETG" })!;
            Task removing = Task.CompletedTask;
            Exception? primary = null;
            try
            {
                await older.Entered.Task.WaitAsync(lease.Token);
                Assert.True(Active());
                var removeButton = Regex.Match(WebUtility.HtmlDecode(root.ToHtmlString()),
                    "<button[^>]*aria-label=\"Remove " + Regex.Escape(part.DisplayFileName) + "\"[^>]*>");
                Assert.True(removeButton.Success);
                Assert.DoesNotContain("disabled", removeButton.Value, StringComparison.Ordinal);
                removing = (Task)Invoke("RemovePartAsync", part.PartId)!;
                await removal.Entered.Task.WaitAsync(lease.Token);
                Assert.True(Active());
                Assert.False(updating.IsCompleted);
                removal.Release.TrySetResult();
                await removing.WaitAsync(lease.Token);
                if (succeeds)
                {
                    await updating.WaitAsync(lease.Token);
                    Assert.False(Active());
                    Assert.False(older.Actual!.IsCompleted);
                    Assert.Equal(other.PartId, Assert.Single(coordinator.Parts).PartId);
                    Assert.True((bool)typeof(InstantQuotationWorkflow)
                        .GetProperty("CanEnterReview", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(component)!);
                }
                else
                {
                    Assert.True(Active());
                    Assert.False(updating.IsCompleted);
                    Assert.Equal(2, coordinator.Parts.Count);
                }
                var quote = coordinator.OrderQuote;
                older.Release.TrySetResult();
                await updating.WaitAsync(lease.Token);
                await older.Actual!.WaitAsync(lease.Token);
                if (succeeds)
                {
                    Assert.Same(quote, coordinator.OrderQuote);
                    Assert.Equal(other.PartId, Assert.Single(coordinator.Parts).PartId);
                }
                else Assert.Contains(coordinator.Parts, row => row.PartId == part.PartId);
                Assert.False(Active());
            }
            catch (Exception exception) { primary = exception; throw; }
            finally
            {
                removal.Release.TrySetResult();
                older.Release.TrySetResult();
                await MaterialPricingTestCleanup.ReleaseAllAsync(primary,
                    async () => await MaterialPricingTestCleanup.DrainQuoteAsync(
                        updating, () => lease.Cancel(), () => older.Release.TrySetResult(), lease.Token, null,
                        receipt => Assert.True(receipt.ActualTaskSettled), TimeSpan.FromSeconds(5),
                        expectedTerminalFailure: false, taskKind: "acknowledged-remove-old-price"),
                    async () => await MaterialPricingTestCleanup.DrainQuoteAsync(
                        removing, () => lease.Cancel(), () => removal.Release.TrySetResult(), lease.Token, null,
                        receipt => Assert.True(receipt.ActualTaskSettled), TimeSpan.FromSeconds(5),
                        expectedTerminalFailure: false, taskKind: "acknowledged-remove-consumer"),
                    async () => await pricing.DrainAsync());
            }
        });
    }

    private sealed class AcknowledgedRemoveClient(bool succeeds) : IInstantQuotationUploadClient
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<InstantQuotationRemoveResult> RemoveAsync(string sessionId, string? owner,
            InstantQuotationUploadReference reference, string operationId, CancellationToken token)
        {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(token);
            return new(operationId, InstantQuotationServiceStatus.Available,
                InstantQuotationAuthorizationStatus.Authorized,
                succeeds ? InstantQuotationOperationStatus.Succeeded : InstantQuotationOperationStatus.Failed,
                succeeds ? InstantQuotationProblemCategory.None : InstantQuotationProblemCategory.Validation);
        }
        public Task<InstantQuotationUploadResult> UploadAsync(string sessionId, string? owner, Stream content,
            string name, string type, long size, InstantQuotationGeometryClaim claim, string operationId,
            CancellationToken token) => throw new InvalidOperationException("No upload expected.");
        public Task<InstantQuotationFinalizationResult> FinalizeAsync(string sessionId, string? owner, int requestId,
            IReadOnlyList<InstantQuotationUploadReference> references, string operationId, CancellationToken token) =>
            throw new InvalidOperationException("No finalization expected.");
    }

    private sealed class AdmissionAnalyticsSink : IInstantQuotationAnalyticsSink
    {
        internal List<InstantQuotationAnalyticsPayload> Payloads { get; } = [];
        public ValueTask EmitAsync(InstantQuotationAnalyticsPayload payload, CancellationToken cancellationToken = default)
        {
            Payloads.Add(payload);
            return ValueTask.CompletedTask;
        }
    }


    [Theory]
    [InlineData("en", "Invalid file. Repair the model and reupload it.")]
    [InlineData("th", "ไฟล์ไม่สมบูรณ์ กรุณาแก้ไขโมเดลแล้วอัปโหลดใหม่")]
    public async Task SelectedLocalUploadFailure_ClearsDuringRetryAndRemovalWithoutInventingAPart(
        string culture, string invalidMessage)
    {
        using var cultures = new CultureScope(culture);
        using var lease = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var healthy = Part("PLA", BuildPreference.Quality);
        var client = new RetryingFailureClient(entered, release, lease.Token);
        await using var coordinator = new InstantQuotationWorkflowCoordinator(
            new SessionStore(healthy), client, new InstantQuotationPricingService(), null,
            authoritativePricingService: SyntheticAuthoritativePricingTestService.Instance);
        await coordinator.InitializeAsync("presentation-session", default);
        var priced = Assert.IsType<InstantQuotationOrderQuote>(coordinator.OrderQuote);
        var claim = new InstantQuotationGeometryClaim(1, new string('a', 64), 10, 10, 10, 1000, 600,
            Enumerable.Repeat(100d, 64).ToArray(), Enumerable.Repeat(40d, 64).ToArray(),
            12, 1, true, false, false, 0.8);
        Assert.False(coordinator.DismissFailedUpload(Assert.Single(coordinator.Parts).PreviewCorrelationId));
        var localId = Assert.Single(coordinator.ReserveUploads([
            new InstantQuotationWorkflowUploadFile("bad<&>.stl", "model/stl", 3, claim,
                _ => Task.FromResult<Stream>(new MemoryStream(new byte[] { 1, 2, 3 }, writable: false))),
        ]));
        Assert.False(coordinator.DismissFailedUpload(localId));
        await coordinator.UploadReservedAsync([localId], lease.Token);
        Assert.Equal(InstantQuotationWorkflowUploadStatus.Error, coordinator.Uploads.Single(upload => upload.LocalId == localId).Status);
        Assert.Equal(healthy.PartId, Assert.Single(coordinator.Parts).PartId);
        Assert.DoesNotContain(coordinator.Parts, part => part.PreviewCorrelationId == localId);

        var activator = new CapturingActivator(coordinator);
        using var services = Services(activator, new RecordingJavaScript());
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var root = await renderer.RenderComponentAsync<InstantQuotationWorkflow>(ParameterView.Empty);
            var component = Assert.IsType<InstantQuotationWorkflow>(activator.Workflow);
            object? Invoke(string name, params object[] args) => typeof(InstantQuotationWorkflow)
                .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(component, args);
            void Refresh() => typeof(ComponentBase).GetMethod("StateHasChanged", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(component, null);
            string Html() => WebUtility.HtmlDecode(root.ToHtmlString());
            void AssertPanel(bool visible)
            {
                Refresh();
                var match = Regex.Match(Html(), "<div[^>]*data-workflow-file-error[^>]*>([\\s\\S]*?)</div>");
                Assert.True(match.Success);
                Assert.Equal(!visible, Regex.IsMatch(match.Value[..(match.Value.IndexOf('>') + 1)], @"\bhidden(?:\s|=|>)"));
                if (visible)
                {
                    Assert.Contains("bad<&>.stl", match.Value, StringComparison.Ordinal);
                    Assert.Contains(invalidMessage, match.Value, StringComparison.Ordinal);
                    Assert.Single(Regex.Matches(Html(), "role=\"alert\""));
                }
            }
            await (Task)Invoke("SelectPreviewAsync", healthy.PartId)!;
            AssertPanel(false);
            await (Task)Invoke("SelectUploadPreviewAsync", localId)!;
            AssertPanel(true);
            Assert.DoesNotContain("bad<&>.stl", root.ToHtmlString(), StringComparison.Ordinal);
            Assert.DoesNotContain("data-part-id=\"" + localId, root.ToHtmlString(), StringComparison.Ordinal);
            Assert.Same(priced, coordinator.OrderQuote);
            var retrying = (Task)Invoke("RetryUploadAsync", localId)!;
            Exception? primary = null;
            try
            {
                await entered.Task.WaitAsync(lease.Token);
                AssertPanel(false);
                release.TrySetResult();
                await retrying.WaitAsync(lease.Token);
                AssertPanel(true);
                await (Task)Invoke("SelectPreviewAsync", healthy.PartId)!;
                AssertPanel(false);
                await (Task)Invoke("SelectUploadPreviewAsync", localId)!;
                AssertPanel(true);
                await (Task)Invoke("RemoveFailedUploadAsync", localId)!;
                AssertPanel(false);
                Assert.DoesNotContain("bad&lt;&amp;&gt;.stl", root.ToHtmlString(), StringComparison.Ordinal);
                Assert.DoesNotContain(coordinator.Uploads, upload => upload.LocalId == localId);
                Assert.Equal(healthy.PartId, Assert.Single(coordinator.Parts).PartId);
                Assert.Same(priced, coordinator.OrderQuote);
                Assert.Equal(InstantQuotationWorkflowState.Configured, coordinator.State);
                Assert.True((bool)typeof(InstantQuotationWorkflow)
                    .GetProperty("CanEnterReview", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(component)!);
                await component.DisposeAsync();
                Assert.Null(typeof(InstantQuotationWorkflow)
                    .GetProperty("SelectedFileError", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(component));
            }
            catch (Exception exception) { primary = exception; throw; }
            finally
            {
                await MaterialPricingTestCleanup.ReleaseAllAsync(primary,
                    async () => await MaterialPricingTestCleanup.DrainQuoteAsync(
                        retrying, () => lease.Cancel(), () => release.TrySetResult(), lease.Token, null,
                        receipt => Assert.True(receipt.ActualTaskSettled), TimeSpan.FromSeconds(5),
                        expectedTerminalFailure: false, taskKind: "local-failure-retry"));
            }
        });
    }

    private sealed class RetryingFailureClient(
        TaskCompletionSource entered, TaskCompletionSource release, CancellationToken lease) : IInstantQuotationUploadClient
    {
        private int attempts;
        public async Task<InstantQuotationUploadResult> UploadAsync(string sessionId, string? ownerIdentity, Stream content,
            string fileName, string contentType, long contentLength, InstantQuotationGeometryClaim geometryClaim,
            string operationId, CancellationToken cancellationToken)
        {
            if (++attempts == 2)
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(lease);
            }
            return InstantQuotationUploadResult.Failed(operationId, InstantQuotationServiceStatus.Available,
                InstantQuotationAuthorizationStatus.Authorized, InstantQuotationProblemCategory.Validation);
        }
        public Task<InstantQuotationRemoveResult> RemoveAsync(string sessionId, string? ownerIdentity,
            InstantQuotationUploadReference uploadReference, string operationId, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("A failed local upload has no persisted object to remove.");
        public Task<InstantQuotationFinalizationResult> FinalizeAsync(string sessionId, string? ownerIdentity,
            int quotationRequestId, IReadOnlyList<InstantQuotationUploadReference> uploadReferences,
            string operationId, CancellationToken cancellationToken) => throw new InvalidOperationException("No finalization expected.");
    }

    private sealed class SyntheticBrowserFile(string name = "new.stl", string contentType = "model/stl") : IBrowserFile
    {
        public string Name => name;
        public DateTimeOffset LastModified => DateTimeOffset.UnixEpoch;
        public long Size => 4;
        public string ContentType => contentType;
        public Stream OpenReadStream(long maxAllowedSize = 512000, CancellationToken cancellationToken = default) =>
            new MemoryStream(new byte[] { 1, 2, 3, 4 }, writable: false);
    }

    private sealed class SyntheticUploadClient : IInstantQuotationUploadClient
    {
        public Task<InstantQuotationUploadResult> UploadAsync(string sessionId, string? ownerIdentity, Stream content,
            string fileName, string contentType, long contentLength, InstantQuotationGeometryClaim geometryClaim,
            string operationId, CancellationToken cancellationToken) => Task.FromResult(
                InstantQuotationUploadResult.Succeeded(operationId, new InstantQuotationUploadReference("synthetic-new-upload"), geometryClaim.Sha256));
        public Task<InstantQuotationRemoveResult> RemoveAsync(string sessionId, string? ownerIdentity,
            InstantQuotationUploadReference uploadReference, string operationId, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("No removal expected.");
        public Task<InstantQuotationFinalizationResult> FinalizeAsync(string sessionId, string? ownerIdentity,
            int quotationRequestId, IReadOnlyList<InstantQuotationUploadReference> uploadReferences,
            string operationId, CancellationToken cancellationToken) => throw new InvalidOperationException("No finalization expected.");
    }



    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task NinePendingClaimConsumers_RetainLocalIntentsAndNeverAdmitRemovedSelections(bool removeFifth, bool laterFileReadyFirst)
    {
        using var culture = new CultureScope("en");
        var store = new SessionStore();
        var upload = new BatchReadyUploadClient();
        var sink = new AdmissionAnalyticsSink();
        var tracker = new InstantQuotationAnalyticsTracker(sink);
        await using var coordinator = new InstantQuotationWorkflowCoordinator(
            store, upload, new InstantQuotationPricingService(), null, analytics: tracker,
            authoritativePricingService: SyntheticAuthoritativePricingTestService.Instance);
        await coordinator.InitializeAsync("presentation-session", default);
        var activator = new CapturingActivator(coordinator);
        using var services = Services(activator, new RecordingJavaScript());
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        using var lease = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var preview = new BatchClaimPreviewReference(lease.Token);
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var root = await renderer.RenderComponentAsync<InstantQuotationWorkflow>(ParameterView.Empty);
            var component = Assert.IsType<InstantQuotationWorkflow>(activator.Workflow);
            void Set(string name, object value) => typeof(InstantQuotationWorkflow)
                .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(component, value);
            object? Invoke(string name, params object[] args) => typeof(InstantQuotationWorkflow)
                .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(component, args);
            Set("analytics", tracker);
            upload.BeforeUpload = () =>
            {
                var started = sink.Payloads.OfType<InstantQuotationUploadStartPayload>().ToArray();
                Assert.Equal(upload.StartedCount + 1, started.Length);
                Assert.All(started, payload => Assert.Equal(1, payload.FileCount));
            };
            Set("previewInterop", preview);
            Set("fileInput", new InputFile());
            var uploading = (Task)Invoke("UploadFilesAsync", new InputFileChangeEventArgs(
                Enumerable.Range(0, 9).Select(index => (IBrowserFile)new SyntheticBrowserFile($"batch-{index}.glb", "model/gltf-binary")).ToArray()))!;
            Exception? primary = null;
            try
            {
                await preview.AllEntered.Task.WaitAsync(lease.Token);
                Assert.Equal(9, preview.EnteredCount);
                Assert.Empty(coordinator.Parts);
                Assert.Contains("data-workflow-pending-parse", root.ToHtmlString(), StringComparison.Ordinal);
                for (var index = 0; index < 5; index++)
                {
                    await (Task)Invoke("SelectPendingPreviewAsync", preview.Keys[index])!;
                    await (Task)Invoke("ChangePendingMaterialAsync", preview.Keys[index], "PETG")!;
                    await (Task)Invoke("ChangePendingMaterialAsync", preview.Keys[index], "PLA")!;
                }
                await (Task)Invoke("ChangePendingQuantityAsync", preview.Keys[4], new ChangeEventArgs { Value = "7" })!;
                await (Task)Invoke("SelectPendingPreviewAsync", preview.Keys[8])!;
                if (removeFifth) await (Task)Invoke("RemovePendingPreviewAsync", preview.Keys[4])!;
                if (laterFileReadyFirst)
                {
                    preview.CompleteAt(8);
                    await upload.LastFileEntered.Task.WaitAsync(lease.Token);
                    Assert.False(preview.ActualClaims[0].IsCompleted);
                }
                preview.CompleteAll();
                await uploading.WaitAsync(lease.Token);
                Assert.Equal(removeFifth ? 8 : 9, coordinator.Parts.Count);
                var started = sink.Payloads.OfType<InstantQuotationUploadStartPayload>().ToArray();
                Assert.Equal(removeFifth ? 8 : 9, started.Length);
                Assert.Equal(removeFifth ? 8 : 9, started.Sum(payload => payload.FileCount));
                Assert.All(started, payload => Assert.Equal(1, payload.FileCount));
                Assert.Equal(started.Length, upload.StartedCount);
                Assert.All(coordinator.Parts, part => Assert.Equal("PLA", part.Configuration.MaterialKey));
                var expectedFiles = Enumerable.Range(0, 9).Where(index => !removeFifth || index != 4)
                    .Select(index => $"batch-{index}.glb").ToArray();
                Assert.Equal(expectedFiles.AsEnumerable(), coordinator.Parts.Select(part => part.DisplayFileName));
                var persisted = (await store.GetAsync("presentation-session", null, default))!;
                Assert.Equal(expectedFiles.AsEnumerable(), persisted.RequestState.Parts.Select(part => part.DisplayFileName));
                Assert.Equal(persisted.Parts.Select(part => part.PartId),
                    Assert.IsType<InstantQuotationOrderQuote>(coordinator.OrderQuote).Parts.Select(part => part.PartId));
                var fifth = coordinator.Parts.SingleOrDefault(part => part.DisplayFileName == "batch-4.glb");
                if (removeFifth) Assert.Null(fifth);
                else Assert.Equal(7, Assert.IsType<InstantQuotationWorkflowPartViewModel>(fifth).Configuration.Quantity);
                var last = coordinator.Parts.Single(part => part.DisplayFileName == "batch-8.glb");
                Assert.Equal(last.PartId, (Guid?)typeof(InstantQuotationWorkflow)
                    .GetField("selectedPreviewPartId", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(component));
                Assert.DoesNotContain("data-workflow-pending-parse", root.ToHtmlString(), StringComparison.Ordinal);
                Assert.DoesNotContain(preview.Keys[4], coordinator.Parts.Select(part => part.PartId.ToString("D")));
                Assert.All(preview.ActualClaims, task => Assert.True(task.IsCompleted));
            }
            catch (Exception exception) { primary = exception; throw; }
            finally
            {
                await MaterialPricingTestCleanup.ReleaseAllAsync(primary,
                    async () => await MaterialPricingTestCleanup.DrainQuoteAsync(
                        uploading, () => lease.Cancel(), preview.CompleteAll, lease.Token, null,
                        receipt => Assert.True(receipt.ActualTaskSettled), TimeSpan.FromSeconds(5),
                        expectedTerminalFailure: false, taskKind: "nine-owned-claim-consumers"),
                    async () =>
                    {
                        try { await Task.WhenAll(preview.ActualClaims).WaitAsync(TimeSpan.FromSeconds(5)); }
                        catch (JSException) when (removeFifth && preview.ActualClaims.Count == 9
                            && preview.ActualClaims[4].IsFaulted
                            && preview.ActualClaims.Where((_, index) => index != 4).All(task => task.Status is TaskStatus.RanToCompletion))
                        { }
                    });
            }
        });
    }


    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PendingUploadStarts_ExcludeInvalidClaimsAndDoNotRefireSettledCallbacks(bool allInvalid)
    {
        var sink = new AdmissionAnalyticsSink();
        var tracker = new InstantQuotationAnalyticsTracker(sink);
        var upload = new BatchReadyUploadClient();
        await using var coordinator = new InstantQuotationWorkflowCoordinator(
            new SessionStore(), upload, new InstantQuotationPricingService(), null, analytics: tracker,
            authoritativePricingService: SyntheticAuthoritativePricingTestService.Instance);
        await coordinator.InitializeAsync("presentation-session", default);
        var activator = new CapturingActivator(coordinator);
        using var services = Services(activator, new RecordingJavaScript());
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        using var lease = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var preview = new BatchClaimPreviewReference(lease.Token);
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<InstantQuotationWorkflow>(ParameterView.Empty);
            var component = Assert.IsType<InstantQuotationWorkflow>(activator.Workflow);
            void Set(string name, object value) => typeof(InstantQuotationWorkflow)
                .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(component, value);
            Set("previewInterop", preview);
            Set("fileInput", new InputFile());
            Set("analytics", tracker);
            upload.BeforeUpload = () =>
            {
                var starts = sink.Payloads.OfType<InstantQuotationUploadStartPayload>().ToArray();
                Assert.Equal(upload.StartedCount + 1, starts.Length);
                Assert.All(starts, payload => Assert.Equal(1, payload.FileCount));
            };
            var uploading = (Task)typeof(InstantQuotationWorkflow)
                .GetMethod("UploadFilesAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(component, [new InputFileChangeEventArgs(Enumerable.Range(0, 9)
                    .Select(index => (IBrowserFile)new SyntheticBrowserFile($"batch-{index}.glb", "model/gltf-binary")).ToArray())])!;
            Exception? primary = null;
            try
            {
                await preview.AllEntered.Task.WaitAsync(lease.Token);
                for (var index = 0; index < 9; index++)
                {
                    if (allInvalid || index % 2 == 1) preview.FailAt(index);
                    else preview.CompleteAt(index);
                }
                await uploading.WaitAsync(lease.Token);
                var starts = sink.Payloads.OfType<InstantQuotationUploadStartPayload>().ToArray();
                Assert.Equal(allInvalid ? 0 : 5, starts.Length);
                Assert.Equal(starts.Length, starts.Sum(payload => payload.FileCount));
                Assert.Equal(starts.Length, upload.StartedCount);
                Assert.Equal(starts.Length, coordinator.Parts.Count);
                Assert.All(starts, payload => Assert.Equal(1, payload.FileCount));
                preview.CompleteAll();
                preview.CompleteAll();
                Assert.Equal(starts.Length, sink.Payloads.OfType<InstantQuotationUploadStartPayload>().Count());
                Assert.All(preview.ActualClaims, task => Assert.True(task.IsCompleted));
            }
            catch (Exception exception) { primary = exception; throw; }
            finally
            {
                await MaterialPricingTestCleanup.ReleaseAllAsync(primary,
                    async () => await MaterialPricingTestCleanup.DrainQuoteAsync(
                        uploading, () => lease.Cancel(), preview.CompleteAll, lease.Token, null,
                        receipt => Assert.True(receipt.ActualTaskSettled), TimeSpan.FromSeconds(5),
                        expectedTerminalFailure: false, taskKind: "invalid-batch-starts"),
                    async () =>
                    {
                        try { await Task.WhenAll(preview.ActualClaims).WaitAsync(TimeSpan.FromSeconds(5)); }
                        catch (JSException) when (preview.ActualClaims.All(task => task.IsCompleted)) { }
                    });
            }
        });
    }

    private sealed class BatchReadyUploadClient : IInstantQuotationUploadClient
    {
        private readonly SyntheticUploadClient inner = new();
        internal TaskCompletionSource LastFileEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Action? BeforeUpload { get; set; }
        internal int StartedCount { get; private set; }
        public Task<InstantQuotationUploadResult> UploadAsync(string sessionId, string? ownerIdentity, Stream content,
            string fileName, string contentType, long contentLength, InstantQuotationGeometryClaim geometryClaim,
            string operationId, CancellationToken cancellationToken)
        {
            BeforeUpload?.Invoke();
            StartedCount++;
            if (fileName == "batch-8.glb") LastFileEntered.TrySetResult();
            Assert.Equal(BatchClaimPreviewReference.SelectedUnitPolicy, geometryClaim.GltfUnitPolicy);
            Assert.Equal("model/gltf-binary", contentType);
            return Task.FromResult(InstantQuotationUploadResult.Succeeded(
                operationId, new InstantQuotationUploadReference("synthetic-new-upload"), geometryClaim.Sha256,
                new InstantQuotationPhysicalAnalysisUpload(
                    Guid.Parse(operationId), fileName, contentType, contentLength, geometryClaim.Sha256, "clean")
                {
                    GltfUnitPolicy = geometryClaim.GltfUnitPolicy,
                }));
        }
        public Task<InstantQuotationRemoveResult> RemoveAsync(string sessionId, string? ownerIdentity,
            InstantQuotationUploadReference reference, string operationId, CancellationToken cancellationToken) =>
            inner.RemoveAsync(sessionId, ownerIdentity, reference, operationId, cancellationToken);
        public Task<InstantQuotationFinalizationResult> FinalizeAsync(string sessionId, string? ownerIdentity,
            int requestId, IReadOnlyList<InstantQuotationUploadReference> references, string operationId, CancellationToken cancellationToken) =>
            inner.FinalizeAsync(sessionId, ownerIdentity, requestId, references, operationId, cancellationToken);
    }

    private sealed class BatchClaimPreviewReference(CancellationToken lease) : IJSObjectReference
    {
        // This renderer fixture models a new selection explicitly made in printing millimetres.
        internal static InstantQuotationGltfUnitPolicy SelectedUnitPolicy { get; } =
            new(1, InstantQuotationGltfLengthUnit.Millimetres);
        internal string[] Keys { get; } = Enumerable.Range(0, 9).Select(index => $"source-local-{index}").ToArray();
        private readonly TaskCompletionSource<InstantQuotationGeometryClaim>[] completions =
            Enumerable.Range(0, 9).Select(_ => new TaskCompletionSource<InstantQuotationGeometryClaim>(
                TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
        internal TaskCompletionSource AllEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal List<Task> ActualClaims { get; } = [];
        internal int EnteredCount { get; private set; }
        internal void CompleteAt(int index) => completions[index].TrySetResult(
            new InstantQuotationGeometryClaim(1, new string('a', 64), 10, 10, 10, 1000, 600,
                Enumerable.Repeat(100d, 64).ToArray(), Enumerable.Repeat(40d, 64).ToArray(),
                12, 1, true, false, false, 1)
            {
                GltfUnitPolicy = SelectedUnitPolicy,
            });
        internal void FailAt(int index) => completions[index].TrySetException(new JSException("Controlled invalid model."));
        internal void CompleteAll()
        {
            for (var index = 0; index < completions.Length; index++) CompleteAt(index);
        }
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, default, args);
        public async ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            if (identifier == "beginSelection") return (TValue)(object)Keys;
            if (identifier == "getGeometryClaim")
            {
                var index = Array.IndexOf(Keys, (string)args![0]!);
                var actual = completions[index].Task.WaitAsync(lease);
                ActualClaims.Add(actual);
                if (++EnteredCount == 9) AllEntered.TrySetResult();
                return (TValue)(object)await actual.WaitAsync(cancellationToken);
            }
            if (identifier == "release")
            {
                var index = Array.IndexOf(Keys, (string)args![0]!);
                if (index >= 0) completions[index].TrySetException(new JSException("Removed local selection."));
            }
            return default!;
        }
        public ValueTask DisposeAsync() { CompleteAll(); return ValueTask.CompletedTask; }
    }

    private sealed class ControlledPreviewReference(Func<string, object?[]?, Task<object?>> invoke) : IJSObjectReference
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

    private sealed class UnreadableBrowserFile : IBrowserFile
    {
        public string Name => "unreadable.stl";
        public DateTimeOffset LastModified => DateTimeOffset.UnixEpoch;
        public long Size => 3;
        public string ContentType => "model/stl";
        public Stream OpenReadStream(long maxAllowedSize = 512000, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("No unreadable bytes may cross the upload boundary.");
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
            var appearance = (Task)Invoke("UpdatePartAppearanceAsync", original.PartId)!;
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

    [Fact]
    public async Task BulkEligibility_RecoveryIsUnavailableDuringExternalRepricing()
    {
        var geometry = AuthoritativeInstantQuotationGeometry.RestoreFromProtectedSession(
            1, new string('A', 64), 10, 10, 10, 1_000, 600, [100], [40], 10, 1, true, true, false, 1);
        var part = Part("PLA", BuildPreference.Standard) with
        {
            Geometry = geometry,
            PhysicalAnalysisUpload = new(Guid.NewGuid(), "recovery.stl", "model/stl", 584, geometry.Sha256, "clean"),
        };
        await using var coordinator = new InstantQuotationWorkflowCoordinator(
            new SessionStore(part), new UnusedUploadClient(), new InstantQuotationPricingService(), null,
            authoritativePricingService: new RecoveryPresentationPricing("current"));
        await coordinator.InitializeAsync("presentation-session", default);
        var activator = new CapturingActivator(coordinator);
        using var services = Services(activator, new RecordingJavaScript());
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        using var lease = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<InstantQuotationWorkflow>(ParameterView.Empty);
            var component = Assert.IsType<InstantQuotationWorkflow>(activator.Workflow);
            bool Eligible() => (bool)typeof(InstantQuotationWorkflow)
                .GetMethod("IsBulkEligible", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(component, [Assert.Single(coordinator.Parts)])!;
            Assert.True(Eligible());
            var work = (Task)typeof(InstantQuotationWorkflow)
                .GetMethod("RepriceAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(component, [new Func<Task>(async () =>
                {
                    entered.TrySetResult();
                    await release.Task.WaitAsync(lease.Token);
                }), null, null])!;
            Exception? primary = null;
            try
            {
                await entered.Task.WaitAsync(lease.Token);
                Assert.False(Eligible());
                release.TrySetResult();
                await work.WaitAsync(lease.Token);
                Assert.True(Eligible());
            }
            catch (Exception exception) { primary = exception; throw; }
            finally
            {
                await MaterialPricingTestCleanup.ReleaseAllAsync(primary,
                    async () => await MaterialPricingTestCleanup.DrainQuoteAsync(
                        work, () => lease.Cancel(), () => release.TrySetResult(), lease.Token, null,
                        receipt => Assert.True(receipt.ActualTaskSettled), TimeSpan.FromSeconds(5),
                        expectedTerminalFailure: false, taskKind: "recovery-eligibility"));
            }
        });
    }

    [Theory]
    [InlineData("current", true)]
    [InlineData("missing", false)]
    [InlineData("unpriced", false)]
    [InlineData("session", false)]
    [InlineData("owner", false)]
    [InlineData("part", false)]
    [InlineData("file", false)]
    [InlineData("sha", false)]
    [InlineData("configured", false)]
    [InlineData("material", false)]
    [InlineData("quantity", false)]
    [InlineData("build", false)]
    [InlineData("profile", false)]
    [InlineData("profile-sha", false)]
    [InlineData("analysis", false)]
    [InlineData("provenance", false)]
    [InlineData("nonfinite", false)]
    [InlineData("refused", false)]
    public async Task BulkEligibility_RecoveryRequiresCurrentBoundServerQuote(string mode, bool eligible)
    {
        var geometry = AuthoritativeInstantQuotationGeometry.RestoreFromProtectedSession(
            1, new string('A', 64), 10, 10, 10, 1_000, 600, [100], [40], 10, 1, true, true, false, 1);
        var part = Part("PLA", BuildPreference.Standard) with
        {
            Geometry = geometry,
            PhysicalAnalysisUpload = new(Guid.NewGuid(), "recovery.stl", "model/stl", 584, geometry.Sha256, "clean"),
        };
        var store = new SessionStore(part);
        await using var coordinator = new InstantQuotationWorkflowCoordinator(
            store, new UnusedUploadClient(), new InstantQuotationPricingService(), null,
            authoritativePricingService: new RecoveryPresentationPricing(mode));
        await coordinator.InitializeAsync("presentation-session", default);
        var activator = new CapturingActivator(coordinator);
        using var services = Services(activator, new RecordingJavaScript());
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var root = await renderer.RenderComponentAsync<InstantQuotationWorkflow>(ParameterView.Empty);
            var component = Assert.IsType<InstantQuotationWorkflow>(activator.Workflow);
            if (mode == "refused")
            {
                var analyses = Assert.IsType<Dictionary<Guid, InstantQuotationBoundPhysicalAnalysisResult>>(
                    typeof(InstantQuotationWorkflow).GetField("physicalAnalyses", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .GetValue(component));
                analyses[part.PartId] = InstantQuotationBoundPhysicalAnalysisResult.Unavailable(
                    InstantQuotationBoundPhysicalAnalysisFailure.UploadUnavailable);
            }
            var view = Assert.Single(coordinator.Parts);
            // An unrelated cached view-model quote cannot stand in for the coordinator's current server quote.
            var stored = Assert.IsType<InstantQuotationSessionState>(
                await store.GetAsync("presentation-session", null, default));
            var staleQuote = await new RecoveryPresentationPricing("current").QuoteAsync(stored, null, true, default);
            var stale = Assert.Single(Assert.IsType<InstantQuotationOrderQuote>(staleQuote).Parts);
            view = view with { Quote = stale };
            var actual = (bool)typeof(InstantQuotationWorkflow)
                .GetMethod("IsBulkEligible", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(component, [view])!;
            Assert.Equal(eligible, actual);
            Assert.True(view.Geometry.NonWatertight);
            Assert.Equal(part.Geometry, view.Geometry);
            Assert.Equal(part.Configuration, view.Configuration);
            if (mode == "current")
            {
                var checkbox = Regex.Match(root.ToHtmlString(), "<input[^>]*data-workflow-part-select[^>]*>");
                Assert.True(checkbox.Success);
                Assert.DoesNotMatch("\\sdisabled(?:\\s|=|>)", checkbox.Value);
                Assert.NotNull(Assert.Single(coordinator.OrderQuote!.Parts).PhysicalReceipt!.GeometryReconstruction);
            }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DevelopmentPreview_RenderOwnsItsCanvasBeforeAwaitingInputBinding(bool replaceWhileBinding)
    {
        using var lease = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var activator = new CapturingActivator();
        using var services = Services(activator, new RecordingJavaScript());
        services.GetRequiredService<IWebHostEnvironment>().EnvironmentName = "Development";
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<InstantQuotationWorkflow>(ParameterView.Empty);
            var component = Assert.IsType<InstantQuotationWorkflow>(activator.Workflow);
            var type = typeof(InstantQuotationWorkflow);
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            void Set(string field, object value) => type.GetField(field, flags)!.SetValue(component, value);
            var previews = Assert.IsAssignableFrom<System.Collections.IDictionary>(
                type.GetField("developmentPreviews", flags)!.GetValue(component));
            var partType = type.GetNestedType("DevelopmentPreviewPart", BindingFlags.NonPublic)!;
            object Part(string key) => Activator.CreateInstance(partType,
                Guid.NewGuid(), key, "controlled-demo.stl", new InstantQuotationGeometryClaim(
                    1, new string('A', 64), 10, 10, 10, 1_000, 600, [100], [40], 12, 1, true, false, false, 1))!;
            void Replace(object part, long generation, string canvas)
            {
                previews.Clear();
                previews.Add(partType.GetProperty("LocalId")!.GetValue(part)!, part);
                Set("developmentPreviewGeneration", generation);
                Set("developmentPreviewCanvas", new ElementReference(canvas));
            }
            Task Render() => (Task)type.GetMethod("OnAfterRenderAsync", flags)!.Invoke(component, [false])!;
            var original = Part("original");
            Replace(original, 1, "canvas-1");
            var interop = new HeldDevelopmentBinding(lease.Token);
            Set("previewInterop", interop);
            Set("fileInput", new InputFile());
            var pending = Render();
            try
            {
                await interop.Entered.Task.WaitAsync(lease.Token);
                var current = original;
                if (replaceWhileBinding)
                {
                    current = Part("retry");
                    // Retry state exists before its new keyed canvas has rendered.
                    Replace(current, 2, "canvas-1");
                }
                interop.Release.TrySetResult();
                await pending.WaitAsync(lease.Token);
                Assert.Equal(replaceWhileBinding ? 0 : 1, interop.Attachments.Count);
                Assert.Equal(!replaceWhileBinding, partType.GetProperty("Attached")!.GetValue(current));
                if (replaceWhileBinding)
                {
                    Set("developmentPreviewCanvas", new ElementReference("canvas-2"));
                    await Render().WaitAsync(lease.Token);
                    Assert.Equal(("retry", "canvas-2", 2L), Assert.Single(interop.Attachments));
                    Assert.Equal(true, partType.GetProperty("Attached")!.GetValue(current));
                }
            }
            finally
            {
                interop.Release.TrySetResult();
                await pending.WaitAsync(TimeSpan.FromSeconds(5));
            }
        });
    }

    private sealed class HeldDevelopmentBinding(CancellationToken token) : IJSObjectReference
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal List<(string Key, string Canvas, long Generation)> Attachments { get; } = [];
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, default, args);
        public async ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            if (identifier == "bindInput")
            {
                Entered.TrySetResult();
                await Release.Task.WaitAsync(token);
            }
            if (identifier == "retainDevelopmentPreview")
            {
                var key = Assert.IsType<string>(args![0]);
                var canvas = Assert.IsType<ElementReference>(args[1]);
                var generation = Assert.IsType<long>(args[2]);
                Attachments.Add((key, canvas.Id, generation));
                return (TValue)(object)(canvas.Id == $"canvas-{generation}");
            }
            return default!;
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecoveryPresentationPricing(string mode) : IInstantQuotationAuthoritativePricingService
    {
        public Task<InstantQuotationOrderQuote?> QuoteAsync(InstantQuotationSessionState session,
            string? ownerIdentity, bool includeComparisons, CancellationToken cancellationToken)
        {
            if (mode == "missing") return Task.FromResult<InstantQuotationOrderQuote?>(null);
            var quote = SyntheticPhysicalPricingTestData.Quote(session.RequestState);
            var part = Assert.Single(session.Parts);
            var line = Assert.Single(quote.Parts);
            var catalog = FdmRuntimeProfileCatalog.LoadEmbedded();
            Assert.True(catalog.TryResolveTrustedProfile(part.Configuration.MaterialKey,
                part.Configuration.BuildPreference, out var profile));
            var receipt = line.PhysicalReceipt! with
            {
                SessionId = session.SessionId,
                OwnerIdentity = ownerIdentity,
                ProfileSha256 = profile!.ResolvedProfileSha256,
                GeometryReconstruction = new("voxel-majority-v1", 0.2),
            };
            receipt = mode switch
            {
                "session" => receipt with { SessionId = "foreign-session" },
                "owner" => receipt with { OwnerIdentity = "foreign-owner" },
                "part" => receipt with { PartId = Guid.NewGuid() },
                "file" => receipt with { FileId = Guid.NewGuid() },
                "sha" => receipt with { UploadSha256 = new string('C', 64) },
                "configured" => receipt with { ConfiguredMaterialKey = "PETG" },
                "material" => receipt with { MaterialKey = "PETG" },
                "quantity" => receipt with { Quantity = 2 },
                "build" => receipt with { BuildPreference = BuildPreference.Strength },
                "profile" => receipt with { ProfileVersion = "stale-profile" },
                "profile-sha" => receipt with { ProfileSha256 = new string('C', 64) },
                "analysis" => receipt with { AnalysisVersion = "stale-analysis" },
                "provenance" => receipt with { GeometryReconstruction = new("untrusted", 0.2) },
                "nonfinite" => receipt with { MotionSeconds = double.NaN },
                _ => receipt,
            };
            line = line with { PhysicalReceipt = receipt, UnitPrice = mode == "unpriced" ? 0 : line.UnitPrice };
            return Task.FromResult<InstantQuotationOrderQuote?>(quote with { Parts = [line] });
        }
    }

    private static InstantQuotationPart Part(string material, BuildPreference preference) => new(
        Guid.NewGuid(), material + ".stl", new InstantQuotationUploadReference("synthetic-presentation"),
        AuthoritativeInstantQuotationGeometry.RestoreFromProtectedSession(10, 1_000, 100, [10], [10], 12, 1, true),
        new(material, material == "M68" ? "Gray" : "Black", 1, preference));

    private static ServiceProvider Services(CapturingActivator activator, RecordingJavaScript javascript) =>
        new ServiceCollection().AddLogging().AddLocalization(options => options.ResourcesPath = "Resources")
            .AddSingleton<IComponentActivator>(activator).AddSingleton<IJSRuntime>(javascript)
            .AddSingleton<NavigationManager>(new InstantQuotationTestNavigationManager())
            .AddSingleton(FdmRuntimeProfileCatalog.LoadEmbedded())
            .AddSingleton<IWebHostEnvironment>(new ComponentEnvironment()).BuildServiceProvider();

    private sealed class CapturingActivator(InstantQuotationWorkflowCoordinator? coordinator = null) : IComponentActivator
    {
        internal InstantQuotationWorkflow? Workflow { get; private set; }
        internal InstantQuotationPreliminaryQuotation? Preliminary { get; private set; }
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
