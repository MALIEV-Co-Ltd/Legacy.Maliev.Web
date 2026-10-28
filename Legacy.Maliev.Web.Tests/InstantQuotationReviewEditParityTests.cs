using System.Net;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Application.Pricing;
using Legacy.Maliev.Web.Components.Pages.InstantQuotation;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace Legacy.Maliev.Web.Tests;

public sealed class InstantQuotationReviewEditParityTests
{
    [Theory]
    [InlineData("en", 2, true, false)]
    [InlineData("th", 2, true, false)]
    [InlineData("en", 3, false, false)]
    [InlineData("th", 3, false, false)]
    [InlineData("en", 350, false, false)]
    [InlineData("th", 350, false, false)]
    [InlineData("en", 351, false, true)]
    [InlineData("th", 351, false, true)]
    public async Task Review_Source21ConsequencesPreserveNumericDiagnosticsAndAnyWireValue(
        string culture, double maximum, bool small, bool large)
    {
        var previous = System.Globalization.CultureInfo.CurrentUICulture;
        try
        {
            System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo(culture);
            var id = Guid.NewGuid();
            var part = Part(id, "source-copy.stl") with
            {
                Geometry = AuthoritativeInstantQuotationGeometry.RestoreFromProtectedSession(
                    1, new string('A', 64), 1, 1, maximum, 1_000, 600, [100], [40], 12, 3, true, true, true, 1),
                Configuration = new("PLA", "Any", 1),
            };
            var html = WebUtility.HtmlDecode(await RenderReviewAsync([part], id, _ => ["retained thin-wall diagnostic"]));
            using var services = new ServiceCollection().AddLogging().AddLocalization(options => options.ResourcesPath = "Resources").BuildServiceProvider();
            var localizer = services.GetRequiredService<Microsoft.Extensions.Localization.IStringLocalizer<ThreeDimensionalPrintingEstimateContent>>();
            Assert.Contains("<option value=\"Any\" selected>" + (culture == "th" ? "ไม่ระบุสี" : "No preference") + "</option>", html, StringComparison.Ordinal);
            foreach (var key in new[] { "Non-watertight mesh", "May cause printing issues", "Non-manifold edges", "Check model integrity", "Multi-body mesh" })
                Assert.Contains(localizer[key].Value, html, StringComparison.Ordinal);
            Assert.Contains(localizer["Check for unintended merged bodies ({0} bodies)", 3].Value, html, StringComparison.Ordinal);
            Assert.Contains(localizer["Multi-body mesh ({0} bodies)", 3].Value, html, StringComparison.Ordinal);
            foreach (var key in new[] { "Unusually small dimensions", "May be difficult to print or handle reliably", "A dimension is below 3 mm." })
                Assert.Equal(small, html.Contains(localizer[key].Value, StringComparison.Ordinal));
            foreach (var key in new[] { "Unusually large dimensions", "May exceed our standard build volume and require splitting", "A dimension exceeds 350 mm." })
                Assert.Equal(large, html.Contains(localizer[key].Value, StringComparison.Ordinal));
            Assert.Contains("retained thin-wall diagnostic", html, StringComparison.Ordinal);
            Assert.Equal("Any", part.Configuration.Color);
        }
        finally { System.Globalization.CultureInfo.CurrentUICulture = previous; }
    }

    [Fact]
    public async Task Review_RendersNativePartSelectionAndEditControlsWithOneActivePart()
    {
        var firstId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var secondId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var html = await RenderReviewAsync([Part(firstId, "first.stl"), Part(secondId, "second.stl")], secondId);

        Assert.Equal(2, Count(html, "data-review-select-part"));
        Assert.Equal(2, Count(html, "data-review-edit-part"));
        Assert.Equal(1, Count(html, "aria-pressed=\"true\""));
        Assert.Contains($"data-review-part-id=\"{secondId}\" data-selected=\"true\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"View first.stl\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Edit second.stl\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("role=\"button\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Review_ExposesLabeledPerPartSettingsWithoutLosingTheEditEscape()
    {
        var id = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var html = await RenderReviewAsync([Part(id, "part.stl")], id);

        Assert.Contains($"id=\"review-material-{id}\"", html, StringComparison.Ordinal);
        Assert.Contains($"id=\"review-preference-{id}\"", html, StringComparison.Ordinal);
        Assert.Contains($"id=\"review-color-{id}\"", html, StringComparison.Ordinal);
        Assert.Contains($"id=\"review-quantity-{id}\"", html, StringComparison.Ordinal);
        Assert.Contains("data-review-edit-part", html, StringComparison.Ordinal);
        Assert.Contains("data-workflow-review-total", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en", "Standard resin", "Standard")]
    [InlineData("th", "การพิมพ์เรซินมาตรฐาน", "มาตรฐาน")]
    public async Task Review_ResinDisablesFdmChoicesWithoutDisablingOtherPartSettings(string culture, string label, string fdmLabel)
    {
        var previous = System.Globalization.CultureInfo.CurrentUICulture;
        try
        {
            System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo(culture);
            var id = Guid.Parse("11111111-1111-1111-1111-111111111111");
            var resin = Part(id, "resin.stl") with { Configuration = new("M68", "Gray", 1) };
            var html = await RenderReviewAsync([resin], id);
            var preference = System.Text.RegularExpressions.Regex.Match(html, $"<select[^>]*id=\"review-preference-{id}\"[^>]*>");
            Assert.True(preference.Success);
            Assert.Contains("disabled", preference.Value, StringComparison.Ordinal);
            Assert.Contains(label, WebUtility.HtmlDecode(html), StringComparison.Ordinal);
            foreach (var field in new[] { "material", "color" })
            {
                var control = System.Text.RegularExpressions.Regex.Match(html, $"<select[^>]*id=\"review-{field}-{id}\"[^>]*>");
                Assert.True(control.Success);
                Assert.DoesNotContain("disabled", control.Value, StringComparison.Ordinal);
            }
            var fdm = await RenderReviewAsync([Part(id, "fdm.stl")], id);
            var fdmPreference = System.Text.RegularExpressions.Regex.Match(fdm, $"<select[^>]*id=\"review-preference-{id}\"[^>]*>");
            Assert.True(fdmPreference.Success);
            Assert.DoesNotContain("disabled", fdmPreference.Value, StringComparison.Ordinal);
            var fdmStandard = System.Text.RegularExpressions.Regex.Match(
                WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Match(
                    fdm, $"<select[^>]*id=\"review-preference-{id}\"[^>]*>(.*?)</select>",
                    System.Text.RegularExpressions.RegexOptions.Singleline).Groups[1].Value),
                "<option\\b[^>]*\\bvalue=\"standard\"[^>]*>([^<]*)</option>");
            Assert.True(fdmStandard.Success);
            Assert.Equal(fdmLabel, fdmStandard.Groups[1].Value);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentUICulture = previous;
        }
    }

    [Fact]
    public async Task Review_CollapsesMeasurementsButAnnouncesEachPartsDfmVerdict()
    {
        var clearId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var warningId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var html = await RenderReviewAsync(
            [Part(clearId, "clear.stl"), Part(warningId, "warning.stl", isManifold: false)],
            clearId);

        Assert.Equal(2, Count(html, "data-review-part-details"));
        Assert.Contains("data-review-dfm-status=\"clear\"", html, StringComparison.Ordinal);
        Assert.Contains("data-review-dfm-status=\"warning\"", html, StringComparison.Ordinal);
        Assert.Contains("Non-watertight mesh", html, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Manufacturing warning: Non-watertight mesh", html, StringComparison.Ordinal);
        Assert.Contains("No automatic DFM warnings were found", html, StringComparison.Ordinal);
        Assert.Contains("<summary", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Review_KeepsAsyncWallThicknessWarningOnItsPart()
    {
        var id = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var html = await RenderReviewAsync(
            [Part(id, "thin.stl")], id,
            _ => ["Thin walls may print with defects"]);

        Assert.Contains("data-review-dfm-status=\"warning\"", html, StringComparison.Ordinal);
        Assert.Contains("Thin walls may print with defects", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Review_PlacesPreliminaryDocumentBesideForwardActionAfterTheTotal()
    {
        var id = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var html = await RenderReviewAsync([Part(id, "part.stl")], id);
        var total = html.IndexOf("data-workflow-review-total", StringComparison.Ordinal);
        var actions = html.IndexOf("data-review-forward-actions", StringComparison.Ordinal);
        var document = html.IndexOf("id=\"preliminary-quotation-button\"", StringComparison.Ordinal);
        var continueAction = html.IndexOf("data-review-continue", StringComparison.Ordinal);

        Assert.True(total >= 0 && actions > total && document > actions && continueAction > document);
        Assert.Contains("data-review-back", html, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Print preliminary quotation\"", html, StringComparison.Ordinal);
        Assert.Matches(@">\s*PDF\s*</button>", html);
    }

    [Fact]
    public async Task Review_KeepsManufacturingUnitPriceSeparateFromAllocatedOrderGross()
    {
        var id = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var part = new InstantQuotationPartQuote(
            id, "PLA", "Black", 2, PrintProcess.Fdm, 30, 1, 1, 1, 1,
            120, 240, false, 0, 0, [], BuildPreference.Standard, [],
            AllocatedOrderTotal: 577.80);
        var quote = new InstantQuotationOrderQuote(
            [part], 240, 500, 500, 260, 40, 540, 37.80, 577.80, 1, 3,
            AllocatedLineTotals: [577.80]);

        var html = await RenderReviewAsync([Part(id, "part.stl")], id, quote: quote);
        var readableHtml = WebUtility.HtmlDecode(html);

        Assert.Matches(@"<dt>Unit price</dt>\s*<dd>฿120\.00</dd>", readableHtml);
        Assert.Matches(@"<dt>Subtotal</dt>\s*<dd>฿240\.00</dd>", readableHtml);
        Assert.Matches(@"<dt>Total</dt>\s*<dd>฿577\.80</dd>", readableHtml);
        Assert.DoesNotContain("฿288.90", readableHtml, StringComparison.Ordinal);
    }

    [Fact]
    public void Workflow_ReviewSelectionAndEditNavigationKeepTheRequestedPartActive()
    {
        var markup = ReadComponent("InstantQuotationWorkflow.razor");
        var code = ReadComponent("InstantQuotationWorkflow.razor.cs");

        Assert.Contains("SelectedPartId=\"@selectedPreviewPartId\"", markup, StringComparison.Ordinal);
        Assert.Contains("OnSelectPart=\"SelectReviewPartAsync\"", markup, StringComparison.Ordinal);
        Assert.Contains("OnEditPart=\"EditReviewPartAsync\"", markup, StringComparison.Ordinal);
        Assert.Contains("await SelectPreviewAsync(partId);", code, StringComparison.Ordinal);
        Assert.Contains("workflow?.ReturnToConfiguration();", code, StringComparison.Ordinal);
        Assert.Contains("pendingFocus = PendingWorkflowFocus.Configuration;", code, StringComparison.Ordinal);
    }

    [Fact]
    public void ReviewControlsRemainTouchSafeResponsiveAndKeyboardVisible()
    {
        var css = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "Legacy.Maliev.Web",
            "wwwroot",
            "src",
            "app",
            "css",
            "instant-quotation.css"));

        Assert.Contains(".instant-quote__review-thumbnail-button", css, StringComparison.Ordinal);
        Assert.Contains("min-width: 44px", css, StringComparison.Ordinal);
        Assert.Contains("min-height: 44px", css, StringComparison.Ordinal);
        Assert.Contains("[aria-pressed=\"true\"]", css, StringComparison.Ordinal);
        Assert.Contains(".instant-quote__review-edit", css, StringComparison.Ordinal);
        Assert.Contains("@media (min-width: 48rem)", css, StringComparison.Ordinal);
    }

    [Fact]
    public void ReviewEditActionsHaveThaiLabelsWithoutChangingFileNames()
    {
        var resources = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "Legacy.Maliev.Web",
            "Resources",
            "Components",
            "Pages",
            "InstantQuotation",
            "ThreeDimensionalPrintingEstimateContent.th.resx"));

        Assert.Contains("<data name=\"Edit\" xml:space=\"preserve\"><value>แก้ไข</value></data>", resources, StringComparison.Ordinal);
        Assert.Contains("<data name=\"Edit {0}\" xml:space=\"preserve\"><value>แก้ไข {0}</value></data>", resources, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en", false, false, "Manufacturing warnings may affect the final result of this part.")]
    [InlineData("th", false, false, "มีข้อควรระวังด้านการผลิต อาจส่งผลต่อผลลัพธ์สุดท้ายของชิ้นงาน")]
    [InlineData("en", true, false, "Manufacturing warnings may affect the final result of this part.")]
    [InlineData("th", true, false, "มีข้อควรระวังด้านการผลิต อาจส่งผลต่อผลลัพธ์สุดท้ายของชิ้นงาน")]
    [InlineData("en", false, true, "Manufacturing warnings may affect the final result of this part.")]
    [InlineData("th", false, true, "มีข้อควรระวังด้านการผลิต อาจส่งผลต่อผลลัพธ์สุดท้ายของชิ้นงาน")]
    public async Task Review_ShowsLocalizedConsequenceInIdentityOnlyWhenExistingWarningsExist(
        string culture, bool geometryWarning, bool thicknessWarning, string expected)
    {
        var previous = System.Globalization.CultureInfo.CurrentUICulture;
        try
        {
            System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo(culture);
            var id = Guid.Parse("11111111-1111-1111-1111-111111111111");
            Func<Guid, IReadOnlyList<string>>? thicknessWarnings = thicknessWarning
                ? _ => ["Thin walls may print with defects"] : null;
            var html = WebUtility.HtmlDecode(await RenderReviewAsync(
                [Part(id, "part.stl", isManifold: !geometryWarning)], id, thicknessWarnings));
            var identity = System.Text.RegularExpressions.Regex.Match(
                html, "<div class=\"instant-quote__review-identity\">(.*?)</div>",
                System.Text.RegularExpressions.RegexOptions.Singleline);
            Assert.True(identity.Success);
            Assert.Contains(">part.stl</h4>", identity.Value, StringComparison.Ordinal);
            Assert.DoesNotContain("<details", identity.Value, StringComparison.Ordinal);
            if (geometryWarning || thicknessWarning)
            {
                Assert.Equal(1, Count(identity.Value, expected));
                var consequence = System.Text.RegularExpressions.Regex.Match(
                    identity.Value, "<p\\b[^>]*data-review-warning-consequence[^>]*>.*?</p>",
                    System.Text.RegularExpressions.RegexOptions.Singleline);
                Assert.True(consequence.Success);
                Assert.Contains(expected, consequence.Value, StringComparison.Ordinal);
                Assert.DoesNotContain("hidden", consequence.Value, StringComparison.Ordinal);
                Assert.DoesNotContain("sr-only", consequence.Value, StringComparison.Ordinal);
                Assert.Contains("data-review-dfm-status=\"warning\"", html, StringComparison.Ordinal);
                Assert.Contains("data-review-dfm-warnings", html, StringComparison.Ordinal);
            }
            else
            {
                Assert.DoesNotContain(expected, html, StringComparison.Ordinal);
                Assert.DoesNotContain("data-review-warning-consequence", html, StringComparison.Ordinal);
                Assert.Contains("data-review-dfm-status=\"clear\"", html, StringComparison.Ordinal);
            }
            Assert.Contains("data-review-part-details", html, StringComparison.Ordinal);
            Assert.Contains("data-review-edit-part", html, StringComparison.Ordinal);
            Assert.Contains("class=\"instant-quote__review-measurements\"", html, StringComparison.Ordinal);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentUICulture = previous;
        }
    }

    [Theory]
    [InlineData("en", "Manufacturing warnings may affect the final result of this part.")]
    [InlineData("th", "มีข้อควรระวังด้านการผลิต อาจส่งผลต่อผลลัพธ์สุดท้ายของชิ้นงาน")]
    public async Task Review_KeepsConsequenceOnTheWarnedPartInEitherOrder(string culture, string expected)
    {
        var previous = System.Globalization.CultureInfo.CurrentUICulture;
        try
        {
            System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo(culture);
            var clearId = Guid.Parse("11111111-1111-1111-1111-111111111111");
            var warningId = Guid.Parse("22222222-2222-2222-2222-222222222222");
            var clear = Part(clearId, "clear.stl");
            var warning = Part(warningId, "warning.stl", isManifold: false);
            foreach (var parts in new[] { new[] { clear, warning }, new[] { warning, clear } })
            {
                var html = WebUtility.HtmlDecode(await RenderReviewAsync(parts, clearId));
                var clearArticle = System.Text.RegularExpressions.Regex.Match(
                    html, $"<article\\b[^>]*data-review-part-id=\"{clearId}\"[^>]*>.*?</article>",
                    System.Text.RegularExpressions.RegexOptions.Singleline);
                var warningArticle = System.Text.RegularExpressions.Regex.Match(
                    html, $"<article\\b[^>]*data-review-part-id=\"{warningId}\"[^>]*>.*?</article>",
                    System.Text.RegularExpressions.RegexOptions.Singleline);
                Assert.True(clearArticle.Success);
                Assert.True(warningArticle.Success);
                Assert.Equal(2, Count(html, "data-workflow-review-part"));
                Assert.Equal(1, Count(html, expected));
                Assert.DoesNotContain(expected, clearArticle.Value, StringComparison.Ordinal);
                Assert.DoesNotContain("data-review-warning-consequence", clearArticle.Value, StringComparison.Ordinal);
                var warningIdentity = System.Text.RegularExpressions.Regex.Match(
                    warningArticle.Value, "<div class=\"instant-quote__review-identity\">(.*?)</div>",
                    System.Text.RegularExpressions.RegexOptions.Singleline);
                Assert.True(warningIdentity.Success);
                Assert.Contains(expected, warningIdentity.Value, StringComparison.Ordinal);
                Assert.Contains("data-review-dfm-status=\"clear\"", clearArticle.Value, StringComparison.Ordinal);
                Assert.Contains("data-review-dfm-status=\"warning\"", warningArticle.Value, StringComparison.Ordinal);
                Assert.Equal(2, Count(html, "data-review-part-details"));
                Assert.Equal(2, Count(html, "data-review-edit-part"));
            }
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentUICulture = previous;
        }
    }

    [Theory]
    [InlineData("en", "PLA", 1, true)]
    [InlineData("th", "PLA", 1, true)]
    [InlineData("en", "PETG", 1, false)]
    [InlineData("th", "PETG", 1, false)]
    [InlineData("en", "M68", 1, false)]
    [InlineData("th", "M68", 1, false)]
    [InlineData("en", "PLA", 2, false)]
    [InlineData("th", "M68", 2, false)]
    public async Task Review_CustomPickerIsPlaOnlyAndMultipartColorIsDisabled(
        string culture, string material, int bodies, bool pickerExpected)
    {
        var previous = System.Globalization.CultureInfo.CurrentUICulture;
        try
        {
            System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo(culture);
            var id = Guid.Parse("11111111-1111-1111-1111-111111111111");
            var color = bodies > 1 ? "Any" : material == "M68" ? "Gray" : "Black";
            var part = Part(id, "color.stl") with
            {
                Geometry = AuthoritativeInstantQuotationGeometry.RestoreFromProtectedSession(10, 1_000, 100, [10], [10], 12, bodies, true),
                Configuration = new(material, color, 1),
            };

            var html = WebUtility.HtmlDecode(await RenderReviewAsync(
                [part], id, colorsForMaterial: materialKey => PricingCatalog.MaterialColors[materialKey]));

            var picker = System.Text.RegularExpressions.Regex.Matches(html, "<input\\b[^>]*\\btype=\"color\"[^>]*>");
            Assert.Equal(pickerExpected ? 1 : 0, picker.Count);
            var select = System.Text.RegularExpressions.Regex.Match(html, $"<select\\b[^>]*id=\"review-color-{id}\"[^>]*>");
            Assert.True(select.Success);
            Assert.Equal(bodies > 1, System.Text.RegularExpressions.Regex.IsMatch(select.Value, "\\bdisabled(?:[\\s=>])"));
            Assert.Contains($"value=\"{color}\"", html, StringComparison.Ordinal);
            Assert.Equal(1, Count(html, "data-review-part-details"));
            Assert.Equal(1, Count(html, "data-review-edit-part"));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentUICulture = previous;
        }
    }

    [Theory]
    [InlineData("en", "PLA")]
    [InlineData("th", "PLA")]
    [InlineData("en", "PETG")]
    [InlineData("th", "PETG")]
    public async Task Review_RestoredHexRemainsVisibleAndIsolatedFromMultipartResin(string culture, string material)
    {
        var previous = System.Globalization.CultureInfo.CurrentUICulture;
        try
        {
            System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo(culture);
            var singleId = Guid.Parse("11111111-1111-1111-1111-111111111111");
            var multiId = Guid.Parse("22222222-2222-2222-2222-222222222222");
            var single = Part(singleId, "custom.stl") with { Configuration = new(material, "#00ff7F", 2) };
            var multi = Part(multiId, "resin.stl") with
            {
                Geometry = AuthoritativeInstantQuotationGeometry.RestoreFromProtectedSession(10, 1_000, 100, [10], [10], 12, 2, true),
                Configuration = new("M68", "Any", 1),
            };
            foreach (var parts in new[] { new[] { single, multi }, new[] { multi, single } })
            {
                var html = WebUtility.HtmlDecode(await RenderReviewAsync(
                    parts, singleId, colorsForMaterial: materialKey => PricingCatalog.MaterialColors[materialKey]));
                var singleArticle = System.Text.RegularExpressions.Regex.Match(
                    html, $"<article\\b[^>]*data-review-part-id=\"{singleId}\"[^>]*>.*?</article>",
                    System.Text.RegularExpressions.RegexOptions.Singleline);
                var multiArticle = System.Text.RegularExpressions.Regex.Match(
                    html, $"<article\\b[^>]*data-review-part-id=\"{multiId}\"[^>]*>.*?</article>",
                    System.Text.RegularExpressions.RegexOptions.Singleline);
                Assert.True(singleArticle.Success);
                Assert.True(multiArticle.Success);
                Assert.Contains("<option value=\"#00ff7F\"", singleArticle.Value, StringComparison.Ordinal);
                Assert.DoesNotContain("#00ff7F", multiArticle.Value, StringComparison.Ordinal);
                Assert.Contains("<option value=\"Any\"", multiArticle.Value, StringComparison.Ordinal);
                Assert.Equal(material == "PLA" ? 1 : 0, System.Text.RegularExpressions.Regex.Matches(
                    singleArticle.Value, "<input\\b[^>]*\\btype=\"color\"[^>]*>").Count);
                Assert.DoesNotContain("type=\"color\"", multiArticle.Value, StringComparison.Ordinal);
                Assert.Equal(2, Count(html, "data-review-part-details"));
                Assert.Equal(2, Count(html, "data-review-edit-part"));
            }
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentUICulture = previous;
        }
    }

    private static async Task<string> RenderReviewAsync(
        IReadOnlyList<InstantQuotationWorkflowPartViewModel> parts,
        Guid selectedPartId,
        Func<Guid, IReadOnlyList<string>>? thicknessWarnings = null,
        InstantQuotationOrderQuote? quote = null,
        Func<string, IReadOnlyList<string>>? colorsForMaterial = null)
    {
        using var services = new ServiceCollection()
            .AddLogging()
            .AddLocalization(options => options.ResourcesPath = "Resources")
            .AddSingleton<IJSRuntime, NullJsRuntime>()
            .AddSingleton<IWebHostEnvironment>(_ => new TestWebHostEnvironment
            {
                ApplicationName = typeof(Program).Assembly.GetName().Name ?? "Legacy.Maliev.Web",
                ContentRootPath = FindRepositoryRoot(),
                WebRootPath = Path.Combine(FindRepositoryRoot(), "Legacy.Maliev.Web", "wwwroot"),
            })
            .BuildServiceProvider();
        var loggerFactory = services.GetRequiredService<ILoggerFactory>();
        await using var renderer = new HtmlRenderer(services, loggerFactory);

        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var parameters = ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                ["Parts"] = parts,
                ["Quote"] = quote,
                ["ColorsForMaterial"] = colorsForMaterial ?? (_ => Array.Empty<string>()),
                ["SelectedPartId"] = selectedPartId,
                ["ThicknessWarnings"] = thicknessWarnings ?? ((Guid _) => Array.Empty<string>()),
            });
            var output = await renderer.RenderComponentAsync<InstantQuotationReview>(parameters);
            return output.ToHtmlString();
        });
    }

    private static InstantQuotationWorkflowPartViewModel Part(Guid id, string fileName, bool isManifold = true) => new(
        id,
        Guid.NewGuid(),
        fileName,
        AuthoritativeInstantQuotationGeometry.RestoreFromProtectedSession(
            10,
            1_000,
            100,
            [10],
            [10],
            12,
            1,
            isManifold),
        new InstantQuotationPartConfiguration("PLA", "Black", 1),
        null);

    private static int Count(string source, string value) =>
        source.Split(value, StringSplitOptions.None).Length - 1;

    private static string ReadComponent(string name) => File.ReadAllText(Path.Combine(
        FindRepositoryRoot(),
        "Legacy.Maliev.Web",
        "Components",
        "Pages",
        "InstantQuotation",
        name));

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

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = string.Empty;

        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();

        public string WebRootPath { get; set; } = string.Empty;

        public string EnvironmentName { get; set; } = "Testing";

        public string ContentRootPath { get; set; } = string.Empty;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
