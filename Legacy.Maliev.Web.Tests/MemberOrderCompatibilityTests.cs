namespace Legacy.Maliev.Web.Tests;

public sealed class MemberOrderCompatibilityTests
{
    [Fact]
    public void MemberOrderRoutes_RestoreProductionFlowsWithoutRetiredCoupling()
    {
        var root = FindRepositoryRoot();
        var orders = Path.Combine(root, "Legacy.Maliev.Web", "Areas", "Member", "Pages", "Orders");
        var requiredFiles = new[]
        {
            "3D-Printing.cshtml",
            "3D-Printing.cshtml.cs",
            "3D-Scanning.cshtml",
            "3D-Scanning.cshtml.cs",
            "CNC-Machining.cshtml",
            "CNC-Machining.cshtml.cs",
        };

        foreach (var requiredFile in requiredFiles)
        {
            Assert.True(File.Exists(Path.Combine(orders, requiredFile)), $"Production order artifact is missing: {requiredFile}");
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
        Assert.True(File.Exists(Path.Combine(
            root,
            "Legacy.Maliev.Web",
            "Areas",
            "Member",
            "Pages",
            "Account",
            "Manage",
            "CreatePassword.cshtml")));
        Assert.True(File.Exists(Path.Combine(
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
        Assert.Contains("/member/orders/material-options", source, StringComparison.Ordinal);
        Assert.Contains("/member/quotations/paymentsuccess", source, StringComparison.Ordinal);
        Assert.DoesNotContain("/member/account/manage/createpassword", source, StringComparison.Ordinal);
        Assert.DoesNotContain("/Quotation?item=CNC-Machining", source, StringComparison.Ordinal);
        Assert.DoesNotContain("/Quotation?item=3D-Printing", source, StringComparison.Ordinal);
        Assert.DoesNotContain("/Quotation?item=3D-Scanning", source, StringComparison.Ordinal);
        Assert.Contains("RequireAuthorization", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DbContext", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Prediction", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Barcode", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PayPal", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("access_token", source, StringComparison.OrdinalIgnoreCase);

        var program = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Web", "Program.cs"));
        Assert.Contains("app.MapMemberCompatibilityEndpoints();", program, StringComparison.Ordinal);
        Assert.Contains("/Orders/3D-Printing", program, StringComparison.Ordinal);
        Assert.Contains("/Orders/3D-Scanning", program, StringComparison.Ordinal);
        Assert.Contains("/Orders/CNC-Machining", program, StringComparison.Ordinal);

        var component = File.ReadAllText(Path.Combine(
            root,
            "Legacy.Maliev.Web",
            "Components",
            "Pages",
            "Member",
            "MemberOrderCreationPage.razor"));
        Assert.Contains("@page \"/Member/Orders/3D-Printing\"", component, StringComparison.Ordinal);
        Assert.Contains("@page \"/Member/Orders/3D-Scanning\"", component, StringComparison.Ordinal);
        Assert.Contains("@page \"/Member/Orders/CNC-Machining\"", component, StringComparison.Ordinal);
        Assert.Contains("IAntiforgery", component, StringComparison.Ordinal);

        var memberShell = File.ReadAllText(Path.Combine(
            root,
            "Legacy.Maliev.Web",
            "Components",
            "Pages",
            "Member",
            "MemberWorkspaceShell.razor"));
        Assert.Contains("/Quotation/CNC-Machining", memberShell, StringComparison.Ordinal);
        Assert.Contains("/Quotation/3D-Printing", memberShell, StringComparison.Ordinal);
        Assert.Contains("/Quotation/3D-Scanning", memberShell, StringComparison.Ordinal);
        Assert.Contains("/Member/Account/Manage/CreatePassword", memberShell, StringComparison.Ordinal);
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
