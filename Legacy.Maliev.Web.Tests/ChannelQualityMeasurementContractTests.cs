using System.Text.RegularExpressions;

namespace Legacy.Maliev.Web.Tests;

public sealed class ChannelQualityMeasurementContractTests
{
    [Fact]
    public void ContactChannelClassifier_EmitsOnlyTheControlledConsentGatedDiagnosticShape()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "Legacy.Maliev.Web",
            "Components",
            "Analytics",
            "PublicContactChannelAnalytics.razor"));

        Assert.Contains("eventName: 'line_click', channel: 'line', destination: 'line_oa'", source, StringComparison.Ordinal);
        Assert.Contains("eventName: 'messenger_click', channel: 'messenger', destination: 'facebook_messenger'", source, StringComparison.Ordinal);
        Assert.Contains("eventName: 'whatsapp_click', channel: 'whatsapp', destination: 'whatsapp_business'", source, StringComparison.Ordinal);
        Assert.Contains("window.malievAnalytics.emit(contactEvent)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("window.dataLayer.push(contactEvent)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("eventName: 'facebook_click'", source, StringComparison.Ordinal);

        var eventStart = source.IndexOf("var contactEvent = {", StringComparison.Ordinal);
        var eventEnd = source.IndexOf("};", eventStart, StringComparison.Ordinal);
        Assert.True(eventStart >= 0 && eventEnd > eventStart);
        var eventBlock = source[eventStart..eventEnd];
        var keys = Regex.Matches(eventBlock, @"(?m)^\s*(?<key>[a-z_]+):")
            .Select(match => match.Groups["key"].Value)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["channel", "context", "destination", "event"], keys);
        foreach (var forbidden in new[]
                 {
                     "service:", "locale:", "campaign:", "utm_", "referrer:", "href:",
                     "email_address", "phone_number", "textContent", "innerText", "FormData"
                 })
        {
            Assert.DoesNotContain(forbidden, eventBlock, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void MeasurementRunbook_DefinesAggregateQualityAndOwnerDecisionGates()
    {
        var runbook = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "docs",
            "channel-quality-measurement.md"));
        var normalizedRunbook = runbook.ReplaceLineEndings(" ");

        foreach (var heading in new[]
                 {
                     "## Exact browser contract",
                     "## Consent and privacy gate",
                     "## Weekly aggregate KPI contract",
                     "## Observation-readiness gate",
                     "## Channel decision rubric",
                     "## Pending production evidence"
                 })
        {
            Assert.Contains(heading, runbook, StringComparison.Ordinal);
        }

        foreach (var value in new[]
                 {
                     "`line_click`", "`messenger_click`", "`whatsapp_click`", "`line_oa`",
                     "`facebook_messenger`", "`whatsapp_business`", "qualified_inquiry_rate",
                     "quote_request_rate", "cost_per_qualified_inquiry", "win_rate"
                 })
        {
            Assert.Contains(value, runbook, StringComparison.Ordinal);
        }

        Assert.Contains("`request_quote` remains the only primary", normalizedRunbook, StringComparison.Ordinal);
        Assert.Contains("Click volume alone never changes", normalizedRunbook, StringComparison.Ordinal);
        Assert.Contains("verified WhatsApp Business destination", normalizedRunbook, StringComparison.Ordinal);
        Assert.Contains("`/sitemap.xml`", normalizedRunbook, StringComparison.Ordinal);
        Assert.Contains("`ads_cost` is `unavailable`", normalizedRunbook, StringComparison.Ordinal);
        Assert.Contains("No comparative production result exists yet", normalizedRunbook, StringComparison.Ordinal);
        Assert.Contains("owner approval", normalizedRunbook, StringComparison.OrdinalIgnoreCase);
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
