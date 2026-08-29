using Xunit;

namespace Legacy.Maliev.Web.Tests;

public sealed class InstantQuotationPreliminaryQuotationParityTests
{
    [Fact]
    public void PreliminaryQuotation_StaysScopedToReviewAndUsesAuthoritativeQuoteData()
    {
        var root = FindRepositoryRoot();
        var review = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Web", "Components", "Pages", "InstantQuotation", "InstantQuotationReview.razor"));
        var component = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Web", "Components", "Pages", "InstantQuotation", "InstantQuotationPreliminaryQuotation.razor"));

        Assert.Contains("<InstantQuotationPreliminaryQuotation", review, StringComparison.Ordinal);
        Assert.Contains("preliminary-quotation-button", component, StringComparison.Ordinal);
        Assert.Contains("disabled=\"@(!CanBuildPreliminaryQuotation)\"", component, StringComparison.Ordinal);
        Assert.Contains("Quote.ItemsSubtotal", component, StringComparison.Ordinal);
        Assert.Contains("Quote.FinalOrderPrice", component, StringComparison.Ordinal);
        Assert.Contains("quote.PrintTimeMinutesPerUnit", component, StringComparison.Ordinal);
        Assert.DoesNotContain("PrintTimeMinutesPerUnit *", component, StringComparison.Ordinal);
        Assert.DoesNotContain("UploadReference", component, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SessionId", component, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("StoragePath", component, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("access_token", component, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("refresh_token", component, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PreliminaryQuotation_UsesCanonicalLogoLocalizedLabelsAndA4PrintActions()
    {
        var root = FindRepositoryRoot();
        var component = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Web", "Components", "Pages", "InstantQuotation", "InstantQuotationPreliminaryQuotation.razor"));
        var script = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Web", "wwwroot", "src", "app", "js", "instant-quotation-preliminary.js"));

        foreach (var marker in new[]
        {
            "navbar_logo_black.webp",
            "PrintPreliminaryQuotation",
            "DownloadPreliminaryQuotationPdf",
            "window.print()",
            "preliminary_quotation_opened",
            "file_count",
            "Estimated print time per part",
            "DFM Analysis",
            "@page { size: A4",
            "break-inside: avoid",
            "page-break-inside: avoid",
            "object-fit: contain",
            "width: 156px; height: 156px",
            "grid-template-columns: 156px minmax(0, 1fr)",
            "margin: 10px 0 0 167px",
        })
        {
            Assert.Contains(marker, component + Environment.NewLine + script, StringComparison.Ordinal);
        }

        Assert.Contains("IJSRuntime", component, StringComparison.Ordinal);
        Assert.Contains("malievPreliminaryQuotation.open", component + script, StringComparison.Ordinal);
        Assert.Contains("snapshot.currency", script, StringComparison.Ordinal);
        Assert.Contains("timeZone: 'Asia/Bangkok'", script, StringComparison.Ordinal);
    }

    [Fact]
    public void PreliminaryQuotation_ExposesOnlyTheReviewActionAndNoConfigurationAction()
    {
        var root = FindRepositoryRoot();
        var review = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Web", "Components", "Pages", "InstantQuotation", "InstantQuotationReview.razor"));
        var estimate = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Web", "Components", "Pages", "InstantQuotation", "ThreeDimensionalPrintingEstimateContent.razor"));

        var reviewStart = review.IndexOf("<InstantQuotationPreliminaryQuotation", StringComparison.Ordinal);
        Assert.True(reviewStart >= 0, "The review component must own the preliminary quotation action.");
        Assert.DoesNotContain("<InstantQuotationPreliminaryQuotation", estimate, StringComparison.Ordinal);
        Assert.True(review[(reviewStart)..].Contains("Quote=\"@Quote\"", StringComparison.Ordinal));
        Assert.DoesNotContain("preliminary-quotation-button", estimate, StringComparison.Ordinal);
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
