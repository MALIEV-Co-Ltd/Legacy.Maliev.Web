namespace Legacy.Maliev.Web.Tests;

public sealed class AnalyticsSurfaceContractTests
{
    [Fact]
    public void AnalyticsPartials_EmitOnlyThroughConsentGateWithCanonicalNames()
    {
        var root = FindRepositoryRoot();
        var shared = Path.Combine(root, "Legacy.Maliev.Web", "Pages", "Shared");
        var googlePath = Path.Combine(
            root,
            "Legacy.Maliev.Web",
            "Components",
            "Analytics",
            "PublicGoogleTagManagerHead.razor");
        Assert.True(File.Exists(googlePath));
        var google = File.ReadAllText(googlePath) + File.ReadAllText(Path.Combine(
            root,
            "Legacy.Maliev.Web",
            "Components",
            "Analytics",
            "PublicGoogleTagManagerDisplayModel.cs"));
        var consent = File.ReadAllText(Path.Combine(
            root,
            "Legacy.Maliev.Web",
            "Components",
            "Layout",
            "PublicCookieConsent.razor"));
        var contactPath = Path.Combine(
            root,
            "Legacy.Maliev.Web",
            "Components",
            "Analytics",
            "PublicContactChannelAnalytics.razor");
        Assert.True(File.Exists(contactPath));
        var contact = File.ReadAllText(contactPath);
        var quotation = File.ReadAllText(Path.Combine(
            root,
            "Legacy.Maliev.Web",
            "Pages",
            "Quotation",
            "Index.cshtml"));

        Assert.Contains("pendingEvents", google, StringComparison.Ordinal);
        Assert.Contains("window.malievAnalytics.emit", google, StringComparison.Ordinal);
        Assert.Contains("file_upload_complete", google, StringComparison.Ordinal);
        Assert.Contains("window.malievLoadGoogleTagManager", google, StringComparison.Ordinal);
        Assert.Contains("if (consentState === 'granted')", google, StringComparison.Ordinal);
        Assert.Contains("data-maliev-gtm-loader", google, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "})(window, document, 'script', 'dataLayer', 'GTM-KHDDLVRR');",
            google,
            StringComparison.Ordinal);
        Assert.Contains("window.malievAnalytics.setConsent(state)", consent, StringComparison.Ordinal);
        Assert.Contains("window.malievLoadGoogleTagManager();", consent, StringComparison.Ordinal);
        Assert.True(
            consent.IndexOf("updateGoogleConsent('granted')", StringComparison.Ordinal) <
            consent.IndexOf("window.malievLoadGoogleTagManager();", StringComparison.Ordinal),
            "Google consent must be granted before the GTM network loader runs.");
        Assert.Contains("window.malievAnalytics.emit(contactEvent)", contact, StringComparison.Ordinal);
        Assert.Contains("window.malievAnalytics.emit(reviewEvent)", contact, StringComparison.Ordinal);
        Assert.DoesNotContain("window.dataLayer.push(contactEvent)", contact, StringComparison.Ordinal);
        Assert.DoesNotContain("window.dataLayer.push(reviewEvent)", contact, StringComparison.Ordinal);
        Assert.Contains("file_upload_start", quotation, StringComparison.Ordinal);
        Assert.Contains("window.malievAnalytics.emit", quotation, StringComparison.Ordinal);
        Assert.Contains("event.preventDefault()", quotation, StringComparison.Ordinal);
        Assert.Contains("window.setTimeout", quotation, StringComparison.Ordinal);
        Assert.Contains("form.submit()", quotation, StringComparison.Ordinal);
    }

    [Fact]
    public void AnalyticsRunbook_DefinesPrimarySecondaryAndLocalizedValidationBoundaries()
    {
        var runbook = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "docs",
            "analytics-validation.md"));

        Assert.Contains("`request_quote`", runbook, StringComparison.Ordinal);
        Assert.Contains("Primary Ads conversion", runbook, StringComparison.Ordinal);
        Assert.Contains("Secondary only", runbook, StringComparison.Ordinal);
        Assert.Contains("GTM Preview", runbook, StringComparison.Ordinal);
        Assert.Contains("GA4 DebugView", runbook, StringComparison.Ordinal);
        Assert.Contains("Google Ads conversion diagnostics", runbook, StringComparison.Ordinal);
        Assert.Contains("Thai", runbook, StringComparison.Ordinal);
        Assert.Contains("English", runbook, StringComparison.Ordinal);
        Assert.Contains("Google-hosted lead form", runbook, StringComparison.Ordinal);
        Assert.Contains("maliev-legacy", runbook, StringComparison.Ordinal);
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
