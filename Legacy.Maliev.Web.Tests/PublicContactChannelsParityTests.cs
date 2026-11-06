namespace Legacy.Maliev.Web.Tests;

public sealed class PublicContactChannelsParityTests
{
    [Fact]
    public void ContactAndServiceLocationExposeOwnedChannelsWithAccessibleLabels()
    {
        var root = FindRepositoryRoot();
        var web = Path.Combine(root, "Legacy.Maliev.Web");
        var contact = File.ReadAllText(Path.Combine(web, "Components", "Pages", "Contact", "ContactPage.razor"));
        var serviceLocation = File.ReadAllText(Path.Combine(web, "Components", "Shared", "ServiceLocation.razor"));
        var socialLinks = File.ReadAllText(Path.Combine(web, "Components", "Layout", "SocialLinks.razor"));
        var networks = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Web.Application", "SocialNetworks.cs"));

        Assert.Contains("https://wa.me/66898950690", networks, StringComparison.Ordinal);
        Assert.Contains("MessengerChat = Messenger", networks, StringComparison.Ordinal);
        Assert.Contains("SocialNetworks.WhatsApp", socialLinks, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"@Localizer[\"Contact MALIEV through WhatsApp\"]\"", socialLinks, StringComparison.Ordinal);
        Assert.Contains("data-maliev-contact-destination=\"whatsapp_business\"", socialLinks, StringComparison.Ordinal);
        Assert.Contains("href=\"/contact#contact-us\"", contact, StringComparison.Ordinal);
        Assert.Contains("class=\"contact-channel-actions\"", contact, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"@Localizer[\"Chat channels\"]\"", contact, StringComparison.Ordinal);
        Assert.Contains("data-contact-placement=\"contact_whatsapp\"", contact, StringComparison.Ordinal);
        Assert.Contains("data-contact-placement=\"contact_messenger\"", contact, StringComparison.Ordinal);
        Assert.Contains("fab fa-facebook-messenger", contact, StringComparison.Ordinal);
        Assert.DoesNotContain("fas fa-comment-dots", contact, StringComparison.Ordinal);
        Assert.Contains("data-contact-placement=\"service_location_whatsapp\"", serviceLocation, StringComparison.Ordinal);
        Assert.Contains("Chat on WhatsApp", serviceLocation, StringComparison.Ordinal);
    }

    [Fact]
    public void InquiryStylesProvideKeyboardVisibleChannelActionsAndResponsiveLocationGrid()
    {
        var root = FindRepositoryRoot();
        var css = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Web", "wwwroot", "src", "app", "css", "inquiry-pages.css"));
        var serviceCss = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Web", "wwwroot", "src", "app", "css", "service-pages.css"));

        Assert.Contains(".contact-channel-actions a", css, StringComparison.Ordinal);
        Assert.Contains("min-height: 3.15rem", css, StringComparison.Ordinal);
        Assert.Contains(".contact-line-card { display: grid; grid-template-columns: minmax(10rem, 13rem) minmax(0, 1fr); gap: 1.25rem; align-items: center; }", css, StringComparison.Ordinal);
        Assert.Contains(".contact-line-card > div { display: flex; min-width: 0; flex-direction: column; justify-content: center; }", css, StringComparison.Ordinal);
        Assert.Contains(".contact-channel-actions { display: grid; grid-template-columns: 1fr;", css, StringComparison.Ordinal);
        Assert.Contains(".contact-channel-actions a { display: flex; min-width: 0; min-height: 3.15rem; flex-direction: row;", css, StringComparison.Ordinal);
        Assert.Contains("@media (min-width: 48rem) and (max-width: 69.99rem)", css, StringComparison.Ordinal);
        Assert.Contains(".contact-channel-actions a { min-height: 2.8rem; gap: .55rem;", css, StringComparison.Ordinal);
        Assert.Contains(".service-location-section .service-actions { display: grid; grid-template-columns: repeat(4", serviceCss, StringComparison.Ordinal);
        Assert.Contains("@media (prefers-reduced-motion: reduce)", css, StringComparison.Ordinal);
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
