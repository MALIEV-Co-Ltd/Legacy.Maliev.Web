namespace Legacy.Maliev.Web.Tests;

public sealed class MemberOrderCompatibilityTests
{
    [Fact]
    public void CompatibilityRoutes_UseAuthenticatedEndpointsWithoutRazorOrRetiredCoupling()
    {
        var root = FindRepositoryRoot();
        var orders = Path.Combine(root, "Legacy.Maliev.Web", "Areas", "Member", "Pages", "Orders");
        var retiredFiles = new[]
        {
            "3D-Printing.cshtml",
            "3D-Printing.cshtml.cs",
            "3D-Scanning.cshtml",
            "3D-Scanning.cshtml.cs",
            "CNC-Machining.cshtml",
            "CNC-Machining.cshtml.cs",
            "ServiceOrderCompatibilityPage.cs",
        };

        foreach (var retiredFile in retiredFiles)
        {
            Assert.False(File.Exists(Path.Combine(orders, retiredFile)), $"Retired Razor artifact remains: {retiredFile}");
        }

        Assert.False(File.Exists(Path.Combine(
            root,
            "Legacy.Maliev.Web",
            "Areas",
            "Member",
            "Pages",
            "Quotations",
            "PaymentSuccess.cshtml")));
        Assert.False(File.Exists(Path.Combine(
            root,
            "Legacy.Maliev.Web",
            "Areas",
            "Member",
            "Pages",
            "Quotations",
            "PaymentSuccess.cshtml.cs")));
        Assert.False(File.Exists(Path.Combine(
            root,
            "Legacy.Maliev.Web",
            "Areas",
            "Member",
            "Pages",
            "Account",
            "Manage",
            "CreatePassword.cshtml")));
        Assert.False(File.Exists(Path.Combine(
            root,
            "Legacy.Maliev.Web",
            "Areas",
            "Member",
            "Pages",
            "Account",
            "Manage",
            "CreatePassword.cshtml.cs")));

        var endpointPath = Path.Combine(root, "Legacy.Maliev.Web", "MemberCompatibilityEndpointRouteBuilderExtensions.cs");
        Assert.True(File.Exists(endpointPath));
        var source = File.ReadAllText(endpointPath);
        Assert.Contains("/member/orders/cnc-machining", source, StringComparison.Ordinal);
        Assert.Contains("/member/orders/3d-printing", source, StringComparison.Ordinal);
        Assert.Contains("/member/orders/3d-scanning", source, StringComparison.Ordinal);
        Assert.Contains("/member/quotations/paymentsuccess", source, StringComparison.Ordinal);
        Assert.Contains("/member/account/manage/createpassword", source, StringComparison.Ordinal);
        Assert.Contains("/Quotation?item=CNC-Machining", source, StringComparison.Ordinal);
        Assert.Contains("/Quotation?item=3D-Printing", source, StringComparison.Ordinal);
        Assert.Contains("/Quotation?item=3D-Scanning", source, StringComparison.Ordinal);
        Assert.Contains("RequireAuthorization", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MapPost", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DbContext", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Prediction", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Barcode", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PayPal", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("access_token", source, StringComparison.OrdinalIgnoreCase);

        var program = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Web", "Program.cs"));
        Assert.Contains("app.MapMemberCompatibilityEndpoints();", program, StringComparison.Ordinal);
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
