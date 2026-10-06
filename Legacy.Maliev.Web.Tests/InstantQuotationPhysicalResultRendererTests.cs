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
