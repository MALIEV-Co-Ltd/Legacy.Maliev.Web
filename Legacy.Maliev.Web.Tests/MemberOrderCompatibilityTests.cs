using Legacy.Maliev.Web.Areas.Member.Pages.Orders;
using Microsoft.AspNetCore.Mvc;

namespace Legacy.Maliev.Web.Tests;

public sealed class MemberOrderCompatibilityTests
{
    [Theory]
    [MemberData(nameof(CompatibilityPages))]
    public void OnGet_RedirectsRetiredDirectOrderEntryToQuotationRequest(
        ServiceOrderCompatibilityPage page,
        string expectedItem)
    {
        var result = page.OnGet();

        Assert.Equal("/Quotation/Index", result.PageName);
        Assert.NotNull(result.RouteValues);
        Assert.Equal(string.Empty, result.RouteValues["area"]);
        Assert.Equal(expectedItem, result.RouteValues["item"]);
        Assert.False(result.Permanent);
        Assert.False(result.PreserveMethod);
    }

    [Fact]
    public void CompatibilityRoutes_AreAuthenticatedGetOnlyAndContainNoRetiredCoupling()
    {
        var root = FindRepositoryRoot();
        var orders = Path.Combine(root, "Legacy.Maliev.Web", "Areas", "Member", "Pages", "Orders");
        var routeFiles = new[]
        {
            "3D-Printing.cshtml",
            "3D-Scanning.cshtml",
            "CNC-Machining.cshtml",
        };
        var compatibilitySource = File.ReadAllText(Path.Combine(orders, "ServiceOrderCompatibilityPage.cs"));
        Assert.Contains("OnGet", compatibilitySource, StringComparison.Ordinal);
        Assert.DoesNotContain("OnPost", compatibilitySource, StringComparison.Ordinal);

        foreach (var routeFile in routeFiles)
        {
            var pagePath = Path.Combine(orders, routeFile);
            var modelPath = $"{pagePath}.cs";
            Assert.True(File.Exists(pagePath), $"Missing preserved route: {routeFile}");
            Assert.True(File.Exists(modelPath), $"Missing page model: {Path.GetFileName(modelPath)}");

            var routeSource = string.Join('\n', File.ReadAllText(pagePath), File.ReadAllText(modelPath));
            Assert.Contains("[Authorize]", routeSource, StringComparison.Ordinal);

            var source = string.Join('\n', routeSource, compatibilitySource);
            Assert.DoesNotContain("DbContext", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Prediction", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Barcode", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("PayPal", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("access_token", source, StringComparison.OrdinalIgnoreCase);
        }
    }

    public static TheoryData<ServiceOrderCompatibilityPage, string> CompatibilityPages => new()
    {
        { new ThreeDimensionalPrinting(), "3D-Printing" },
        { new ThreeDimensionalScanning(), "3D-Scanning" },
        { new CncMachining(), "CNC-Machining" },
    };

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
