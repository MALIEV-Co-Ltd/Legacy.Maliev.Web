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
}
