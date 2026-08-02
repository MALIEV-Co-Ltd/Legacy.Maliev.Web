namespace Legacy.Maliev.Web.Tests;

public sealed class SharedUiSourceParityTests
{
    [Fact]
    public void LandingAndInquiryStylesRetainTheLatestSourceResponsiveAndMotionContracts()
    {
        var root = FindRepositoryRoot();
        var web = Path.Combine(root, "Legacy.Maliev.Web");
        var landing = File.ReadAllText(Path.Combine(web, "wwwroot", "src", "app", "css", "landing.css"));
        var inquiry = File.ReadAllText(Path.Combine(web, "wwwroot", "src", "app", "css", "inquiry-pages.css"));
        var shell = File.ReadAllText(Path.Combine(web, "wwwroot", "src", "app", "css", "application-shell.css"));
        var app = File.ReadAllText(Path.Combine(web, "wwwroot", "src", "app", "css", "app.css"));

        Assert.Contains("landing-service-card--directory", landing, StringComparison.Ordinal);
        Assert.Contains("landing-service-card:hover > img", landing, StringComparison.Ordinal);
        Assert.Contains("landing-button-light:hover", landing, StringComparison.Ordinal);
        Assert.Contains("@container (min-width: 20rem)", landing, StringComparison.Ordinal);
        Assert.Contains("@media (prefers-reduced-motion: reduce)", landing, StringComparison.Ordinal);
        Assert.Contains("inquiry-upload__dropzone-label", inquiry, StringComparison.Ordinal);
        Assert.Contains("inquiry-upload__file-list", inquiry, StringComparison.Ordinal);
        Assert.Contains("contact-method-card:has(a):hover", inquiry, StringComparison.Ordinal);
        Assert.Contains(".inquiry-validation-summary.validation-summary-errors", inquiry, StringComparison.Ordinal);
        Assert.Contains("career-table-wrapper", shell, StringComparison.Ordinal);
        Assert.Contains("data-responsive[data-stack=\"true\"]", shell, StringComparison.Ordinal);
        Assert.Contains(".btn-facebook", app, StringComparison.Ordinal);
        Assert.DoesNotContain(".collapse-header", app, StringComparison.Ordinal);
        Assert.DoesNotContain(".underline", app, StringComparison.Ordinal);
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
