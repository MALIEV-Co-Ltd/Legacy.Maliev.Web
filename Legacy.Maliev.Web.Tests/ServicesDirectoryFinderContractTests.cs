namespace Legacy.Maliev.Web.Tests;

public sealed class ServicesDirectoryFinderContractTests
{
    private static readonly string[] FinderStepKeys =
    [
        "files",
        "service",
        "material",
        "quantity",
        "end-use",
        "workflow-3d",
        "cnc-priority",
        "cnc-detail",
        "cnc-environment",
        "printing-priority",
        "printing-finish",
        "scanning-output",
        "scanning-accuracy",
        "molding-priority",
        "molding-detail",
        "performance",
        "environment",
    ];

    private static readonly string[] FinderServiceRoutes =
    [
        "custom|/services/custom-manufacturing",
        "cnc|/services/cnc-machining",
        "printing|/services/3d-printing",
        "scanning|/services/3d-scanning",
        "design|/services/3d-design",
        "silicone|/services/silicone-casting",
        "injection|/services/low-volume-injection-molding",
    ];

    [Fact]
    public void ServicesDirectory_RendersCurrentFinderRootAndAllSeventeenStepContracts()
    {
        var body = ReadComponent();

        Assert.Contains("class=\"maliev-page maliev-page-main services-index-page\"", body, StringComparison.Ordinal);
        Assert.Contains("data-service-finder", body, StringComparison.Ordinal);
        Assert.Contains("<ServiceBreadcrumb ServiceKey=\"Services\" />", body, StringComparison.Ordinal);
        Assert.Contains("service-finder-results", body, StringComparison.Ordinal);
        Assert.Contains("data-finder-quotation-link", body, StringComparison.Ordinal);
        Assert.Contains("data-finder-material-guidance", body, StringComparison.Ordinal);
        Assert.Contains("data-finder-path", body, StringComparison.Ordinal);

        for (var index = 0; index < FinderStepKeys.Length; index++)
        {
            Assert.Contains($"data-finder-step=\"{index}\"", body, StringComparison.Ordinal);
            Assert.Contains($"data-finder-progress-step=\"{index}\"", body, StringComparison.Ordinal);
            Assert.Contains($"data-finder-key=\"{FinderStepKeys[index]}\"", body, StringComparison.Ordinal);
        }

        foreach (var key in FinderStepKeys.Skip(5))
        {
            Assert.Contains($"data-finder-key=\"{key}\" data-finder-optional", body, StringComparison.Ordinal);
        }

        Assert.Equal(12, body.Split("data-finder-optional-progress", StringSplitOptions.None).Length - 1);
        Assert.Equal(12, body.Split("data-finder-optional hidden", StringSplitOptions.None).Length - 1);

        Assert.Contains("Answer five quick questions", body, StringComparison.Ordinal);
        Assert.Contains("ตอบคำถามสั้น ๆ 5 ข้อ", body, StringComparison.Ordinal);
        Assert.Contains("Skip this optional question", body, StringComparison.Ordinal);
        Assert.Contains("ข้ามคำถามเพิ่มเติมนี้", body, StringComparison.Ordinal);
    }

    [Fact]
    public void ServicesDirectory_RendersEveryFinderOptionAndServiceLinkWithoutUnsafePageOrAnalyticsBridges()
    {
        var body = ReadComponent();

        foreach (var answer in new[]
        {
            "files-3d", "files-2d", "files-image", "files-none", "files-real-part",
            "service-machining", "service-3d", "service-molding", "service-unsure",
            "material-metal", "material-standard-plastic", "material-resin", "material-plastic", "material-silicone", "material-unsure",
            "quantity-1-10", "quantity-11-100", "quantity-101-1000", "quantity-over-1000",
            "use-prototype", "use-industrial", "use-replacement", "use-consumer",
            "workflow-3d-printing", "workflow-3d-scanning", "workflow-3d-design", "workflow-3d-unsure",
            "cnc-priority-strength", "cnc-priority-precision", "cnc-priority-weight", "cnc-priority-wear", "cnc-priority-corrosion", "cnc-priority-chemical", "cnc-priority-appearance", "cnc-priority-unsure",
            "cnc-detail-finish", "cnc-detail-threads", "cnc-detail-inspection", "cnc-detail-thin", "cnc-detail-none", "cnc-detail-unsure",
            "cnc-environment-outdoor", "cnc-environment-chemical", "cnc-environment-clean", "cnc-environment-controlled", "cnc-environment-unsure",
            "printing-priority-strength", "printing-priority-detail", "printing-priority-flexible", "printing-priority-lightweight", "printing-priority-heat", "printing-priority-unsure",
            "printing-finish-none", "printing-finish-smooth", "printing-finish-accuracy", "printing-finish-clean", "printing-finish-unsure",
            "scanning-output-visual", "scanning-output-cad", "scanning-output-deviation", "scanning-output-raw", "scanning-output-clean", "scanning-output-unsure",
            "scanning-accuracy-reference", "scanning-accuracy-dimension", "scanning-accuracy-shape", "scanning-accuracy-color", "scanning-accuracy-detail", "scanning-accuracy-unsure",
            "molding-priority-flexible", "molding-priority-rigid", "molding-priority-wear", "molding-priority-appearance", "molding-priority-heat", "molding-priority-unsure",
            "molding-detail-consistency", "molding-detail-release", "molding-detail-surface", "molding-detail-none", "molding-detail-unsure",
            "performance-strength", "performance-appearance", "performance-flexibility", "performance-temperature", "performance-unsure",
            "environment-indoor", "environment-outdoor", "environment-wet", "environment-heat-chemical", "environment-unsure",
        })
        {
            Assert.Contains($"data-finder-answer=\"{answer}\"", body, StringComparison.Ordinal);
        }

        foreach (var serviceRoute in FinderServiceRoutes)
        {
            var parts = serviceRoute.Split('|');
            Assert.Contains($"data-finder-service=\"{parts[0]}\"", body, StringComparison.Ordinal);
            Assert.Contains($"href=\"{parts[1]}\"", body, StringComparison.Ordinal);
        }

        Assert.Contains("href=\"/services\"", body, StringComparison.Ordinal);
        Assert.Contains("href=\"/Contact\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("asp-page", body, StringComparison.Ordinal);
        Assert.DoesNotContain("dataLayer", body, StringComparison.Ordinal);
        Assert.DoesNotContain("malievPushDiagnosticEvent", body, StringComparison.Ordinal);
        Assert.DoesNotContain("localStorage", body, StringComparison.Ordinal);
        Assert.DoesNotContain("jquery", body, StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadComponent()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Web.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(
            directory!.FullName,
            "Legacy.Maliev.Web",
            "Components",
            "Pages",
            "Services",
            "ServicesContent.razor"));
    }
}
