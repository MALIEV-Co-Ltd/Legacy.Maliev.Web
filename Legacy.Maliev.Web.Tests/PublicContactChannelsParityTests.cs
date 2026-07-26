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
        Assert.Contains("aria-label=\"Contact MALIEV through WhatsApp\"", socialLinks, StringComparison.Ordinal);
        Assert.Contains("data-maliev-contact-destination=\"whatsapp_business\"", socialLinks, StringComparison.Ordinal);
        Assert.Contains("href=\"/contact#contact-us\"", contact, StringComparison.Ordinal);
        Assert.Contains("class=\"contact-channel-actions\"", contact, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"@Localizer[\"Chat channels\"]\"", contact, StringComparison.Ordinal);
        Assert.Contains("data-contact-placement=\"contact_whatsapp\"", contact, StringComparison.Ordinal);
        Assert.Contains("data-contact-placement=\"contact_messenger\"", contact, StringComparison.Ordinal);
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
