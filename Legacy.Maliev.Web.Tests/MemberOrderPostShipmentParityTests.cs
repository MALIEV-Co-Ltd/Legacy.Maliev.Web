using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Components.Pages.Member;

namespace Legacy.Maliev.Web.Tests;

public sealed class MemberOrderPostShipmentParityTests
{
    [Fact]
    public void DetailLoader_EnablesFollowUpOnlyAfterShippedStatus()
    {
        var shipped = MemberDetailLoaders.CreateOrderDisplayModel(Details("Shipped"), null, []);
        var reviewing = MemberDetailLoaders.CreateOrderDisplayModel(Details("Reviewing"), null, []);

        Assert.True(shipped.HasShippedStatus);
        Assert.False(reviewing.HasShippedStatus);
    }

    [Fact]
    public void DetailContent_PreservesReviewAndSupportAnalyticsContracts()
    {
        var source = File.ReadAllText(Path.Combine(
            SolutionRoot(),
            "Legacy.Maliev.Web",
            "Components",
            "Pages",
            "Member",
            "MemberOrderDetailContent.razor"));

        Assert.Contains("data-maliev-review-platform=\"google_business_profile\"", source, StringComparison.Ordinal);
        Assert.Contains("data-contact-placement=\"member_order_line\"", source, StringComparison.Ordinal);
        Assert.Contains("data-contact-placement=\"member_order_whatsapp\"", source, StringComparison.Ordinal);
        Assert.Contains("SocialNetworks.GoogleMaps", source, StringComparison.Ordinal);
        Assert.Contains("SocialNetworks.Line", source, StringComparison.Ordinal);
        Assert.Contains("SocialNetworks.WhatsApp", source, StringComparison.Ordinal);
    }

    private static CustomerOrderDetails Details(string status) => new(
        new CustomerOrder(7, 42, "Part", null, 3, 1, 1, 0, null, null, null, null, null, null, null, false, false, null, null, null),
        null,
        [new CustomerOrderStatus(1, 7, 1, status, null, DateTime.UtcNow, DateTime.UtcNow)],
        []);

    private static string SolutionRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Web.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
