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

    [Fact]
    public void ResolvedLineIncidentNoticeStaysAbsentWhilePublicLineContactRemains()
    {
        var root = FindRepositoryRoot();
        var web = Path.Combine(root, "Legacy.Maliev.Web");
        var surfaces = new[]
        {
            Path.Combine(web, "Pages", "Shared", "_Layout.cshtml"),
            Path.Combine(web, "Pages", "Shared", "_InstantQuotationLayout.cshtml"),
            Path.Combine(web, "Areas", "Member", "Pages", "Shared", "_LayoutMember.cshtml"),
            Path.Combine(web, "Areas", "Knowledges", "Pages", "Shared", "_LayoutKnowledges.cshtml"),
            Path.Combine(web, "Components", "App.razor"),
            Path.Combine(web, "Components", "Pages", "Member", "MemberWorkspaceShell.razor"),
            Path.Combine(web, "Components", "Pages", "Knowledges", "KnowledgeShell.razor"),
        };
        foreach (var surface in surfaces)
        {
            var markup = File.ReadAllText(surface);
            Assert.DoesNotContain("_AccouncementPartial", markup, StringComparison.Ordinal);
            Assert.DoesNotContain("_LineOaRecoveryAlertPartial", markup, StringComparison.Ordinal);
            Assert.DoesNotContain("maliev-service-notice", markup, StringComparison.Ordinal);
        }
        Assert.False(File.Exists(Path.Combine(web, "Pages", "Shared", "_AccouncementPartial.cshtml")));
        Assert.False(File.Exists(Path.Combine(web, "Pages", "Shared", "_LineOaRecoveryAlertPartial.cshtml")));

        var social = File.ReadAllText(Path.Combine(web, "Components", "Pages", "About", "SocialMediaContent.razor"));
        Assert.Contains("SocialNetworks.Line", social, StringComparison.Ordinal);
        Assert.Contains("Chat with our team through LINE Official Account", social, StringComparison.Ordinal);
        Assert.DoesNotContain("check the site notice", social, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("https://line.me/ti/p/@maliev", Legacy.Maliev.Web.Application.SocialNetworks.Line);
        var translations = System.Xml.Linq.XDocument.Load(Path.Combine(web, "Resources", "Components", "Pages", "About", "SocialMediaContent.th.resx"));
        var lineTranslation = translations.Root!.Elements("data")
            .Single(entry => (string?)entry.Attribute("name") == "Chat with our team through LINE Official Account")
            .Element("value")?.Value;
        Assert.Equal("แชตกับทีมงานผ่าน LINE Official Account", lineTranslation);
    }

    [Fact]
    public void ApplicationAreaShellsPreserveOriginalZoomAndInlineStyleExclusions()
    {
        var web = Path.Combine(FindRepositoryRoot(), "Legacy.Maliev.Web");
        foreach (var relativePath in new[]
        {
            "Areas/Member/Pages/Shared/_LayoutMember.cshtml",
            "Areas/Knowledges/Pages/Shared/_LayoutKnowledges.cshtml",
            "Components/App.razor"
        })
        {
            var source = File.ReadAllText(Path.Combine(web, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            Assert.DoesNotContain("user-scalable=no", source, StringComparison.Ordinal);
        }

        foreach (var relativePath in new[]
        {
            "Areas/Member/Pages/Shared/_LayoutMember.cshtml",
            "Areas/Member/Pages/Shared/_MemberNavigation.cshtml",
            "Areas/Knowledges/Pages/Shared/_LayoutKnowledges.cshtml",
            "Components/Pages/Member/MemberWorkspaceShell.razor",
            "Components/Pages/Knowledges/KnowledgeShell.razor",
            "Components/Pages/Knowledges/KnowledgeNavigation.razor"
        })
        {
            var source = File.ReadAllText(Path.Combine(web, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            Assert.DoesNotContain("style=", source, StringComparison.OrdinalIgnoreCase);
        }
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
