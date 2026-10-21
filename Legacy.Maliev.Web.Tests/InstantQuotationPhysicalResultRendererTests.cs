using System.Globalization;
using System.Net;
using Legacy.Maliev.Web.Components.Pages.InstantQuotation;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Actual status-component markup from view state; not upload admission or physical-analysis proof.</summary>
public sealed class InstantQuotationPhysicalResultRendererTests
{
    public static TheoryData<string, bool, string, string> Messages => new()
    {
        { "en", false, "GeometryReview", "This mesh has open or non-manifold surfaces. Contact our engineers for a file review." },
        { "th", false, "GeometryReview", "ไฟล์เมชมีพื้นผิวเปิดหรือโครงสร้างไม่สมบูรณ์ กรุณาติดต่อวิศวกรเพื่อช่วยตรวจสอบไฟล์" },
        { "en", false, "ComplexityExceeded", "This file exceeds the analysis limits. Simplify the mesh or contact our engineers for a quotation." },
        { "th", false, "ComplexityExceeded", "ไฟล์นี้เกินขีดจำกัดของการวิเคราะห์ กรุณาลดความซับซ้อนของเมชหรือติดต่อวิศวกรเพื่อขอใบเสนอราคา" },
        { "en", false, "SimulationUnavailable", "Print simulation is unavailable for this file. Contact our engineers for a quotation." },
        { "th", false, "SimulationUnavailable", "ไม่สามารถจำลองการพิมพ์สำหรับไฟล์นี้ได้ กรุณาติดต่อวิศวกรเพื่อขอใบเสนอราคา" },
        { "en", false, "UploadUnavailable", "Physical build analysis is unavailable. Engineering review is required." },
        { "th", false, "UploadUnavailable", "ไม่สามารถวิเคราะห์การพิมพ์จริงได้ ต้องให้วิศวกรตรวจสอบ" },
        { "en", false, "unknown-future-failure", "Physical build analysis is unavailable. Engineering review is required." },
        { "th", false, "unknown-future-failure", "ไม่สามารถวิเคราะห์การพิมพ์จริงได้ ต้องให้วิศวกรตรวจสอบ" },
        { "en", true, "None", "Physical build evidence is ready. Verified analysis is used for the current price." },
        { "th", true, "None", "ข้อมูลการพิมพ์จริงพร้อมแล้ว ราคาปัจจุบันคำนวณจากผลการวิเคราะห์ที่ตรวจสอบแล้ว" },
    };

    [Theory]
    [MemberData(nameof(Messages))]
    public async Task TypedResultStatusPreservesLocalizedActionAndAccessibleState(
        string culture, bool ready, string failure, string expectedMessage)
    {
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            using var services = new ServiceCollection().AddLogging()
                .AddLocalization(options => options.ResourcesPath = "Resources")
                .AddSingleton<IWebHostEnvironment>(new ComponentEnvironment())
                .BuildServiceProvider();
            await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
            var html = await renderer.Dispatcher.InvokeAsync(async () =>
            {
                var parameters = ParameterView.FromDictionary(new Dictionary<string, object?>
                {
                    [nameof(InstantQuotationPhysicalResult.IsReady)] = ready,
                    [nameof(InstantQuotationPhysicalResult.Failure)] = failure,
                });
                return (await renderer.RenderComponentAsync<InstantQuotationPhysicalResult>(parameters)).ToHtmlString();
            });
            Assert.Contains(expectedMessage, WebUtility.HtmlDecode(html), StringComparison.Ordinal);
            Assert.Contains("role=\"status\"", html, StringComparison.Ordinal);
            Assert.Contains("data-physical-analysis-result=\"" + (ready ? "ready" : "unavailable") + "\"", html, StringComparison.Ordinal);
            Assert.Contains("data-physical-analysis-failure=\"" + failure + "\"", html, StringComparison.Ordinal);
            Assert.DoesNotContain("<button", html, StringComparison.Ordinal);
            Assert.DoesNotContain("data-workflow-price", html, StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    [Theory]
    [InlineData("en", true, "voxel-majority-v1", 0.2, true)]
    [InlineData("th", true, "voxel-majority-v1", 0.5, true)]
    [InlineData("en", false, "voxel-majority-v1", 0.2, false)]
    [InlineData("th", false, "voxel-majority-v1", 0.2, false)]
    [InlineData("en", true, "untrusted-browser-claim", 0.2, false)]
    [InlineData("th", true, "voxel-majority-v1", 0.1, false)]
    [InlineData("en", true, "voxel-majority-v1", double.NaN, false)]
    [InlineData("th", true, "voxel-majority-v1", double.PositiveInfinity, false)]
    public async Task ReconstructionWarning_RequiresReadyValidReceiptProvenance(
        string culture, bool ready, string method, double pitch, bool visible)
    {
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            using var services = new ServiceCollection().AddLogging()
                .AddLocalization(options => options.ResourcesPath = "Resources")
                .AddSingleton<IWebHostEnvironment>(new ComponentEnvironment())
                .BuildServiceProvider();
            await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
            var html = await renderer.Dispatcher.InvokeAsync(async () =>
            {
                var parameters = ParameterView.FromDictionary(new Dictionary<string, object?>
                {
                    [nameof(InstantQuotationPhysicalResult.IsReady)] = ready,
                    [nameof(InstantQuotationPhysicalResult.Failure)] = ready ? "None" : "GeometryReview",
                    [nameof(InstantQuotationPhysicalResult.Reconstruction)] =
                        new Legacy.Maliev.Web.Application.Pricing.Simulation.VoxelReconstructionInfo(method, pitch),
                });
                return (await renderer.RenderComponentAsync<InstantQuotationPhysicalResult>(parameters)).ToHtmlString();
            });
            var decoded = WebUtility.HtmlDecode(html);
            Assert.Equal(visible, html.Contains("data-geometry-reconstruction", StringComparison.Ordinal));
            Assert.Contains("data-physical-analysis-result=\"" + (ready ? "ready" : "unavailable") + "\"", html, StringComparison.Ordinal);
            if (visible)
            {
                Assert.Contains(culture == "th" ? "ต้องให้วิศวกรตรวจสอบ" : "engineering review required", decoded, StringComparison.Ordinal);
                Assert.Contains(pitch.ToString("0.0", CultureInfo.CurrentCulture), decoded, StringComparison.Ordinal);
                Assert.Contains(culture == "th" ? "ต้องตรวจสอบไฟล์ต้นฉบับก่อนผลิต" : "the original file must be reviewed before manufacture", decoded, StringComparison.Ordinal);
                Assert.Contains("alert alert-warning", html, StringComparison.Ordinal);
                Assert.Contains("role=\"status\"", html, StringComparison.Ordinal);
            }
            else
            {
                Assert.DoesNotContain("Pricing uses reconstructed geometry.", decoded, StringComparison.Ordinal);
                Assert.DoesNotContain("ใช้แบบจำลองชั่วคราวเพื่อคำนวณราคา", decoded, StringComparison.Ordinal);
            }
            Assert.DoesNotContain("<button", html, StringComparison.Ordinal);
            Assert.DoesNotContain("data-workflow-price", html, StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("th")]
    public async Task ReviewWarning_FollowsCurrentMatchingQuoteThroughRepriceReplacementAndRemoval(string culture)
    {
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            var partId = Guid.NewGuid();
            // Presentation-only records: the actual browser test obtains these fields from real server analysis.
            var receipt = new Legacy.Maliev.Web.Application.InstantQuotationPhysicalAnalysisReceipt(
                "presentation", null, partId, Guid.NewGuid(), new string('a', 64), "PLA", "PLA",
                Legacy.Maliev.Web.Application.Pricing.BuildPreference.Standard, 1,
                "profile", "profile-sha", "analysis", "physical-sha", 100, 0, 10, 1)
            {
                GeometryReconstruction = new("voxel-majority-v1", 0.2),
            };
            var line = new Legacy.Maliev.Web.Application.InstantQuotationPartQuote(
                partId, "PLA", "Black", 1, Legacy.Maliev.Web.Application.Pricing.PrintProcess.Fdm,
                10, 1, 1, 1, 1, 10, 10, false, 0, 0, [],
                Legacy.Maliev.Web.Application.Pricing.BuildPreference.Standard, [], PhysicalReceipt: receipt);
            var quote = new Legacy.Maliev.Web.Application.InstantQuotationOrderQuote(
                [line], 10, 10, 0, 0, 0, 10, 0.7, 10.7, 1, 2);
            var part = new InstantQuotationWorkflowPartViewModel(partId, Guid.NewGuid(), "original.stl",
                Legacy.Maliev.Web.Application.AuthoritativeInstantQuotationGeometry.RestoreFromProtectedSession(
                    10, 1000, 600, [100], [40], 12, 1, true),
                new("PLA", "Black", 1), line);
            var activator = new ReviewActivator();
            using var services = new ServiceCollection().AddLogging()
                .AddLocalization(options => options.ResourcesPath = "Resources")
                .AddSingleton<IComponentActivator>(activator)
                .AddSingleton<Microsoft.JSInterop.IJSRuntime>(new NoJavaScript())
                .AddSingleton<IWebHostEnvironment>(new ComponentEnvironment())
                .BuildServiceProvider();
            await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
            await renderer.Dispatcher.InvokeAsync(async () =>
            {
                var root = await renderer.RenderComponentAsync<InstantQuotationReview>(Parameters(quote, false, [part]));
                var warning = culture == "th"
                    ? "ราคาประมาณจากแบบจำลองว็อกเซล (0.2 มม.) — ต้องให้วิศวกรตรวจสอบ"
                    : "Voxel-based estimate (0.2 mm) — engineering review required";
                Assert.Contains(warning, WebUtility.HtmlDecode(root.ToHtmlString()), StringComparison.Ordinal);
                await activator.Review!.SetParametersAsync(Parameters(quote, true, [part]));
                Assert.DoesNotContain(warning, WebUtility.HtmlDecode(root.ToHtmlString()), StringComparison.Ordinal);
                Assert.Contains("10.70", WebUtility.HtmlDecode(root.ToHtmlString()), StringComparison.Ordinal);
                await activator.Review.SetParametersAsync(Parameters(quote with
                { Parts = [line with { PhysicalReceipt = null }] }, false, [part]));
                Assert.DoesNotContain(warning, WebUtility.HtmlDecode(root.ToHtmlString()), StringComparison.Ordinal);
                await activator.Review.SetParametersAsync(Parameters(quote with
                { Parts = [line with { PartId = Guid.NewGuid() }] }, false, [part]));
                Assert.DoesNotContain(warning, WebUtility.HtmlDecode(root.ToHtmlString()), StringComparison.Ordinal);
                await activator.Review.SetParametersAsync(Parameters(quote, false, [part]));
                Assert.Contains(warning, WebUtility.HtmlDecode(root.ToHtmlString()), StringComparison.Ordinal);
                await activator.Review.SetParametersAsync(Parameters(quote, false, []));
                Assert.DoesNotContain(warning, WebUtility.HtmlDecode(root.ToHtmlString()), StringComparison.Ordinal);
                Assert.DoesNotContain("data-workflow-review-part ", root.ToHtmlString(), StringComparison.Ordinal);
            });

            static ParameterView Parameters(Legacy.Maliev.Web.Application.InstantQuotationOrderQuote current,
                bool pending, IReadOnlyList<InstantQuotationWorkflowPartViewModel> parts) =>
                ParameterView.FromDictionary(new Dictionary<string, object?>
                {
                    [nameof(InstantQuotationReview.Parts)] = parts,
                    [nameof(InstantQuotationReview.Quote)] = current,
                    [nameof(InstantQuotationReview.IsRepricing)] = pending,
                });
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    private sealed class ReviewActivator : IComponentActivator
    {
        internal InstantQuotationReview? Review { get; private set; }
        public IComponent CreateInstance(Type componentType)
        {
            var component = (IComponent)Activator.CreateInstance(componentType)!;
            if (component is InstantQuotationReview review) Review = review;
            return component;
        }
    }

    private sealed class NoJavaScript : Microsoft.JSInterop.IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            ValueTask.FromException<TValue>(new InvalidOperationException("No browser interop expected in static receipt presentation."));
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            InvokeAsync<TValue>(identifier, args);
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
