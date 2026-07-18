using System.Text.Json;
using Legacy.Maliev.Web.Pages.InstantQuotation;
using Microsoft.AspNetCore.Mvc;

namespace Legacy.Maliev.Web.Tests;

public sealed class InstantQuotationPageTests
{
    [Fact]
    public void GetEstimate_ReturnsLegacyCompatibleDeterministicShape()
    {
        var page = new ThreeDimensionalPrinting();

        var result = Assert.IsType<JsonResult>(page.OnGetGetEstimate(
            "PLA",
            30,
            20_000,
            400,
            string.Join(',', Enumerable.Repeat(20_000.0 / 30, 40)),
            string.Join(',', Enumerable.Repeat(80.0, 40)),
            "THB",
            1));
        var json = JsonSerializer.SerializeToElement(result.Value);

        Assert.True(json.GetProperty("success").GetBoolean());
        Assert.Equal("fdm", json.GetProperty("process").GetString());
        Assert.Equal("THB", json.GetProperty("currency").GetString());
        Assert.True(json.GetProperty("unitPrice").GetDouble() > 0);
        Assert.Equal(json.GetProperty("unitPrice").GetDouble(), json.GetProperty("subtotal").GetDouble());
        Assert.Equal(4, json.GetProperty("tiers").GetArrayLength());
    }

    [Fact]
    public void GetEstimate_RejectsUnsupportedMaterialAndNormalizesToThb()
    {
        var page = new ThreeDimensionalPrinting();

        var unsupported = Assert.IsType<JsonResult>(
            page.OnGetGetEstimate("unknown", 1, 1, 1, null, null, "THB", 1));
        var unsupportedJson = JsonSerializer.SerializeToElement(unsupported.Value);
        Assert.False(unsupportedJson.GetProperty("success").GetBoolean());

        var supported = Assert.IsType<JsonResult>(
            page.OnGetGetEstimate("M68", 10, 5_000, 100, null, null, "USD", 2));
        var supportedJson = JsonSerializer.SerializeToElement(supported.Value);
        Assert.Equal("THB", supportedJson.GetProperty("currency").GetString());
    }

    [Fact]
    public void GetOrderTotal_ReturnsLegacyCompatibleBreakdown()
    {
        var page = new ThreeDimensionalPrinting();

        var result = Assert.IsType<JsonResult>(page.OnGetGetOrderTotal(
            "fdm,resin",
            "1200,1800",
            500,
            2_000,
            "THB"));
        var json = JsonSerializer.SerializeToElement(result.Value);

        Assert.True(json.GetProperty("success").GetBoolean());
        Assert.Equal(3_000, json.GetProperty("printing").GetDouble(), 2);
        Assert.True(json.GetProperty("shipping").GetDouble() >= 100);
        Assert.True(json.GetProperty("vat").GetDouble() > 0);
        Assert.True(json.GetProperty("finalOrderPrice").GetDouble() > 3_000);
        Assert.Equal("THB", json.GetProperty("currency").GetString());
    }

    [Fact]
    public void PreservedRoute_IsAccessibleAndContainsNoRetiredCoupling()
    {
        var root = FindRepositoryRoot();
        var page = Path.Combine(root, "Legacy.Maliev.Web", "Pages", "InstantQuotation", "3D-Printing.cshtml");
        var model = $"{page}.cs";
        Assert.True(File.Exists(page));
        Assert.True(File.Exists(model));

        var component = Path.Combine(
            root,
            "Legacy.Maliev.Web",
            "Components",
            "Pages",
            "InstantQuotation",
            "ThreeDimensionalPrintingEstimateContent.razor");
        var browserModule = Path.Combine(
            root,
            "Legacy.Maliev.Web",
            "wwwroot",
            "src",
            "app",
            "js",
            "instant-quotation.js");
        var controllerModule = Path.Combine(
            root,
            "Legacy.Maliev.Web",
            "wwwroot",
            "src",
            "app",
            "js",
            "instant-quotation-controller.mjs");
        var calculator = Path.Combine(
            root,
            "Legacy.Maliev.Web",
            "Components",
            "Pages",
            "InstantQuotation",
            "InstantQuotationCalculator.cs");
        var source = string.Join(
            '\n',
            File.ReadAllText(page),
            File.ReadAllText(model),
            File.ReadAllText(component),
            File.ReadAllText(calculator),
            File.ReadAllText(browserModule),
            File.ReadAllText(controllerModule));
        Assert.Contains("@page", source, StringComparison.Ordinal);
        Assert.Contains("handler: 'GetEstimate'", source, StringComparison.Ordinal);
        Assert.Contains("handler: 'GetOrderTotal'", source, StringComparison.Ordinal);
        Assert.Contains("/Quotation?item=3D-Printing", source, StringComparison.Ordinal);
        Assert.Contains("aria-live", source, StringComparison.Ordinal);
        Assert.Contains("<fieldset", source, StringComparison.Ordinal);
        Assert.Contains("string.Equals(material.Key, \"PLA\"", source, StringComparison.Ordinal);
        Assert.Contains("@Localizer[", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DbContext", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Prediction", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PayPal", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Omise", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Barcode", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("access_token", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Route_UsesLocalizedStaticSsrComponentAndScopedBrowserModule()
    {
        var root = FindRepositoryRoot();
        var web = Path.Combine(root, "Legacy.Maliev.Web");
        var fallback = File.ReadAllText(Path.Combine(web, "Pages", "InstantQuotation", "3D-Printing.cshtml"));
        var page = File.ReadAllText(Path.Combine(
            web,
            "Components",
            "Pages",
            "InstantQuotation",
            "InstantQuotationPage.razor"));
        var component = File.ReadAllText(Path.Combine(
            web,
            "Components",
            "Pages",
            "InstantQuotation",
            "ThreeDimensionalPrintingEstimateContent.razor"));
        var model = File.ReadAllText(Path.Combine(web, "Pages", "InstantQuotation", "3D-Printing.cshtml.cs"));

        Assert.Contains("@page \"/InstantQuotation/3D-Printing\"", page, StringComparison.Ordinal);
        Assert.Contains("IStringLocalizer<ThreeDimensionalPrintingEstimateContent>", page, StringComparison.Ordinal);
        Assert.Contains("data-migration-route-owner=\"blazor-static-ssr\"", page, StringComparison.Ordinal);
        Assert.Contains("<ThreeDimensionalPrintingEstimateContent", page, StringComparison.Ordinal);
        Assert.Contains("type=\"typeof(ThreeDimensionalPrintingEstimateContent)\"", fallback, StringComparison.Ordinal);
        Assert.DoesNotContain("<main class=\"instant-quote\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("<style>", page, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<script>", page, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("data-migration-renderer=\"blazor-static-ssr\"", component, StringComparison.Ordinal);
        Assert.Contains("data-instant-estimate", component, StringComparison.Ordinal);
        Assert.Contains("IStringLocalizer<ThreeDimensionalPrintingEstimateContent>", component, StringComparison.Ordinal);
        Assert.DoesNotContain("@rendermode", component, StringComparison.Ordinal);
        Assert.DoesNotContain("asp-page", component, StringComparison.Ordinal);
        Assert.Contains("InstantQuotationCalculator.CreateDisplayModel", model, StringComparison.Ordinal);

        Assert.True(File.Exists(Path.Combine(
            web,
            "Resources",
            "Components",
            "Pages",
            "InstantQuotation",
            "ThreeDimensionalPrintingEstimateContent.th.resx")));
        Assert.False(File.Exists(Path.Combine(
            web,
            "Resources",
            "Pages",
            "InstantQuotation",
            "3D-Printing.th.resx")));
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
}
