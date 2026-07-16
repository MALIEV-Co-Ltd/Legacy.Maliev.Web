namespace Legacy.Maliev.Web.Tests;

public sealed class BlazorMigrationContractTests
{
    [Fact]
    public void LegalRoute_UsesStaticSsrComponentWithoutInteractiveServerInfrastructure()
    {
        var root = FindRepositoryRoot();
        var program = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Web", "Program.cs"));
        var legalPage = File.ReadAllText(Path.Combine(
            root,
            "Legacy.Maliev.Web",
            "Pages",
            "Legal",
            "Index.cshtml"));
        var legalComponent = File.ReadAllText(Path.Combine(
            root,
            "Legacy.Maliev.Web",
            "Components",
            "Pages",
            "Legal",
            "LegalContent.razor"));

        Assert.Contains("AddRazorComponents()", program, StringComparison.Ordinal);
        Assert.DoesNotContain("AddInteractiveServerComponents", program, StringComparison.Ordinal);
        Assert.DoesNotContain("MapBlazorHub", program, StringComparison.Ordinal);
        Assert.Contains("type=\"typeof(LegalContent)\"", legalPage, StringComparison.Ordinal);
        Assert.Contains("render-mode=\"Static\"", legalPage, StringComparison.Ordinal);
        Assert.Contains("data-migration-renderer=\"blazor-static-ssr\"", legalComponent, StringComparison.Ordinal);
        Assert.DoesNotContain("@rendermode", legalComponent, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Services/Index.cshtml", "Services/ServicesContent.razor", "ServicesContent")]
    [InlineData("About/SocialMedia.cshtml", "About/SocialMediaContent.razor", "SocialMediaContent")]
    [InlineData("About/Index.cshtml", "About/AboutContent.razor", "AboutContent")]
    [InlineData("Legal/PrivacyPolicy.cshtml", "Legal/PrivacyPolicyContent.razor", "PrivacyPolicyContent")]
    [InlineData("Legal/TermsConditions.cshtml", "Legal/TermsConditionsContent.razor", "TermsConditionsContent")]
    public void ReadOnlyPublicRoute_UsesNonInteractiveStaticSsrComponent(
        string pagePath,
        string componentPath,
        string componentName)
    {
        var root = FindRepositoryRoot();
        var page = File.ReadAllText(Path.Combine(
            root,
            "Legacy.Maliev.Web",
            "Pages",
            pagePath.Replace('/', Path.DirectorySeparatorChar)));
        var component = File.ReadAllText(Path.Combine(
            root,
            "Legacy.Maliev.Web",
            "Components",
            "Pages",
            componentPath.Replace('/', Path.DirectorySeparatorChar)));

        Assert.Contains($"type=\"typeof({componentName})\"", page, StringComparison.Ordinal);
        Assert.Contains("render-mode=\"Static\"", page, StringComparison.Ordinal);
        Assert.Contains("data-migration-renderer=\"blazor-static-ssr\"", component, StringComparison.Ordinal);
        Assert.DoesNotContain("@rendermode", component, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Web.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the Legacy.Maliev.Web repository root.");
    }

    [Fact]
    public void CustomManufacturingRoute_UsesStaticBlazorBody()
    {
        var root = FindRepositoryRoot();
        var page = File.ReadAllText(Path.Combine(
            root,
            "Legacy.Maliev.Web",
            "Pages",
            "Services",
            "Custom-Manufacturing.cshtml"));

        Assert.Contains("type=\"typeof(CustomManufacturingContent)\"", page, StringComparison.Ordinal);
        Assert.Contains("render-mode=\"Static\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("<main class=\"service-page\">", page, StringComparison.Ordinal);
        Assert.DoesNotContain("<section class=\"service-hero\"", page, StringComparison.Ordinal);
        Assert.Contains("<partial name=\"_SchemaService\"", page, StringComparison.Ordinal);
        Assert.Contains("FAQPage", page, StringComparison.Ordinal);

        var body = File.ReadAllText(Path.Combine(
            root,
            "Legacy.Maliev.Web",
            "Components",
            "Pages",
            "Services",
            "CustomManufacturingContent.razor"));

        Assert.Contains("<ServiceBreadcrumb ServiceKey=\"Custom Manufacturing\" />", body, StringComparison.Ordinal);
        Assert.Contains("<ServiceLocation />", body, StringComparison.Ordinal);
    }

    [Fact]
    public void CncMachiningRoute_UsesStaticBlazorBody()
    {
        var root = FindRepositoryRoot();
        var page = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Web", "Pages", "Services", "CNC-Machining.cshtml"));

        Assert.Contains("type=\"typeof(CncMachiningContent)\"", page, StringComparison.Ordinal);
        Assert.Contains("render-mode=\"Static\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("<main class=\"service-page\">", page, StringComparison.Ordinal);
        Assert.Contains("<partial name=\"_SchemaService\"", page, StringComparison.Ordinal);
        Assert.Contains("FAQPage", page, StringComparison.Ordinal);

        var body = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Web", "Components", "Pages", "Services", "CncMachiningContent.razor"));
        Assert.Contains("<ServiceBreadcrumb ServiceKey=\"CNC Machining\" />", body, StringComparison.Ordinal);
        Assert.Contains("<ServiceLocation />", body, StringComparison.Ordinal);
    }

    [Fact]
    public void ThreeDimensionalPrintingRoute_UsesStaticBlazorBody()
    {
        var root = FindRepositoryRoot();
        var page = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Web", "Pages", "Services", "3D-Printing.cshtml"));
        Assert.Contains("type=\"typeof(ThreeDimensionalPrintingContent)\"", page, StringComparison.Ordinal);
        Assert.Contains("render-mode=\"Static\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("<main class=\"service-page\">", page, StringComparison.Ordinal);
        Assert.Contains("<partial name=\"_SchemaService\"", page, StringComparison.Ordinal);
        Assert.Contains("FAQPage", page, StringComparison.Ordinal);

        var body = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Web", "Components", "Pages", "Services", "ThreeDimensionalPrintingContent.razor"));
        Assert.Contains("<ServiceBreadcrumb ServiceKey=\"3D Printing\" />", body, StringComparison.Ordinal);
        Assert.Contains("<ServiceLocation />", body, StringComparison.Ordinal);
    }

    [Fact]
    public void ThreeDimensionalScanningRoute_UsesStaticBlazorBody()
    {
        var root = FindRepositoryRoot();
        var page = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Web", "Pages", "Services", "3D-Scanning.cshtml"));
        Assert.Contains("type=\"typeof(ThreeDimensionalScanningContent)\"", page, StringComparison.Ordinal);
        Assert.Contains("render-mode=\"Static\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("<main class=\"service-page\">", page, StringComparison.Ordinal);
        Assert.Contains("<partial name=\"_SchemaService\"", page, StringComparison.Ordinal);
        Assert.Contains("FAQPage", page, StringComparison.Ordinal);

        var body = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Web", "Components", "Pages", "Services", "ThreeDimensionalScanningContent.razor"));
        Assert.Contains("<ServiceBreadcrumb ServiceKey=\"3D Scanning\" />", body, StringComparison.Ordinal);
        Assert.Contains("<ServiceLocation />", body, StringComparison.Ordinal);
    }

    [Fact]
    public void HomeRoute_UsesStaticBlazorBodyAndComponentLocalization()
    {
        var root = FindRepositoryRoot();
        var page = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Web", "Pages", "Index.cshtml"));
        Assert.Contains("type=\"typeof(HomeContent)\"", page, StringComparison.Ordinal);
        Assert.Contains("render-mode=\"Static\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("landing-hero", page, StringComparison.Ordinal);

        var body = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Web", "Components", "Pages", "Home", "HomeContent.razor"));
        Assert.Contains("IStringLocalizer<HomeContent>", body, StringComparison.Ordinal);
        Assert.Contains("data-migration-component=\"home-content\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("asp-page", body, StringComparison.Ordinal);

        Assert.True(File.Exists(Path.Combine(root, "Legacy.Maliev.Web", "Resources", "Components", "Pages", "Home", "HomeContent.resx")));
        Assert.True(File.Exists(Path.Combine(root, "Legacy.Maliev.Web", "Resources", "Components", "Pages", "Home", "HomeContent.th.resx")));
        Assert.False(File.Exists(Path.Combine(root, "Legacy.Maliev.Web", "Resources", "Pages", "Index.resx")));
        Assert.False(File.Exists(Path.Combine(root, "Legacy.Maliev.Web", "Resources", "Pages", "Index.th.resx")));
    }
}
