namespace Legacy.Maliev.Web.Tests;

public sealed class PublicCopyParityTests
{
    [Fact]
    public void CareerDirectoryKeepsThePostA40ResponsiveAndCloudflareSafeMarkup()
    {
        var content = ReadWebFile("Components", "Pages", "Career", "CareerIndexContent.razor");

        Assert.Contains("career-page", content, StringComparison.Ordinal);
        Assert.Contains("id=\"career-intro\"", content, StringComparison.Ordinal);
        Assert.Contains("<!--email_off-->", content, StringComparison.Ordinal);
        Assert.Contains("We look forward to meeting you.", content, StringComparison.Ordinal);
        Assert.Contains("role=\"region\"", content, StringComparison.Ordinal);
        Assert.Contains("data-responsive", content, StringComparison.Ordinal);
        Assert.Contains("data-title='@Localizer[\"Title\"]'", content, StringComparison.Ordinal);
    }

    [Fact]
    public void LegalDocumentsExposeTheLocalizedEffectiveDateAndCompleteTocAnchors()
    {
        var privacy = ReadWebFile("Components", "Pages", "Legal", "PrivacyPolicyContent.razor");
        var privacyThai = ReadWebFile("Components", "Pages", "Legal", "PrivacyPolicyThaiContent.razor");
        var terms = ReadWebFile("Components", "Pages", "Legal", "TermsConditionsContent.razor");
        var termsThai = ReadWebFile("Components", "Pages", "Legal", "TermsConditionsThaiContent.razor");

        Assert.Contains("24 July 2026", privacy, StringComparison.Ordinal);
        Assert.Contains("24 กรกฎาคม 2569", privacyThai, StringComparison.Ordinal);
        Assert.Contains("href=\"#transfer\"", privacy, StringComparison.Ordinal);
        Assert.Contains("<!--email_off-->info@@maliev.com<!--/email_off-->", privacy, StringComparison.Ordinal);
        Assert.Contains("LegalEffectiveDate", terms, StringComparison.Ordinal);
        Assert.Contains("id=\"hyperlinking\"", terms, StringComparison.Ordinal);
        Assert.Contains("id=\"disclaimer\"", terms, StringComparison.Ordinal);
        Assert.Contains("24 กรกฎาคม 2569", termsThai, StringComparison.Ordinal);
    }

    [Fact]
    public void KnowledgeAndInquirySurfacesKeepLocalizedProjectCopyAndProgressiveEnhancementMarkers()
    {
        var knowledge = ReadWebFile("Resources", "Components", "Pages", "Knowledges", "KnowledgeIndexContent.th.resx");
        var guidelines = ReadWebFile("Resources", "Components", "Pages", "Knowledges", "GuidelinesContent.th.resx");
        var workflow = ReadWebFile("Resources", "Components", "Pages", "Knowledges", "WorkflowContent.th.resx");
        var contact = ReadWebFile("Components", "Pages", "Contact", "ContactFormFields.razor");
        var quotation = ReadWebFile("Components", "Pages", "Quotation", "QuotationFormFields.razor");
        var guidance = ReadWebFile("Components", "Pages", "Quotation", "QuotationGuidanceContent.razor");

        Assert.Contains("โปรเจ็ค", knowledge, StringComparison.Ordinal);
        Assert.Contains("โปรเจ็ค", guidelines, StringComparison.Ordinal);
        Assert.Contains("โปรเจ็ค", workflow, StringComparison.Ordinal);
        Assert.Contains("data-live-email", contact, StringComparison.Ordinal);
        Assert.Contains("data-auto-country", contact, StringComparison.Ordinal);
        Assert.Contains("data-live-email", quotation, StringComparison.Ordinal);
        Assert.Contains("data-auto-country", quotation, StringComparison.Ordinal);
        Assert.Contains("data-upload-dropzone", quotation, StringComparison.Ordinal);
        Assert.Contains("data-upload-file-list", quotation, StringComparison.Ordinal);
        Assert.Contains("Google Drive", guidance, StringComparison.Ordinal);
    }

    private static string ReadWebFile(params string[] pathSegments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, "Legacy.Maliev.Web", .. pathSegments]);
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Unable to find web source: {string.Join(Path.DirectorySeparatorChar, pathSegments)}");
    }
}
