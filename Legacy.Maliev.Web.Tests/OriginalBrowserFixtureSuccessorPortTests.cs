using System.Net;
using System.Net.Sockets;
using System.Reflection;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Guards the original shared browser fixture's port policy at its retained successors.</summary>
public sealed class OriginalBrowserFixtureSuccessorPortTests
{
    [Theory]
    [InlineData(typeof(CncNativeBrowserFixture), null)]
    [InlineData(typeof(ErrorBrowserFixture), null)]
    [InlineData(typeof(InstantQuotationSidebarLayoutBrowserTests), null)]
    [InlineData(typeof(InstantQuotationSummaryTransitionBrowserTests), "Fixture")]
    [InlineData(typeof(InstantQuotationTerminalPhysicalFailureBrowserTests), "Fixture")]
    [InlineData(typeof(InstantQuotationWallThicknessAdmittedWorkerBrowserTests), null)]
    [InlineData(typeof(InstantQuotationWallThicknessRealUploadBrowserTests), null)]
    [InlineData(typeof(MaterialPricingDisplayBrowserTests), "Fixture")]
    [InlineData(typeof(MeasurementLifecycleBrowserFixture), null)]
    [InlineData(typeof(PublicContactBrowserFixture), null)]
    [InlineData(typeof(WallThicknessParityBrowserFixture), null)]
    public void BrowserHostPort_IsOutsideClientPoolAndAvailableForHost(Type owner, string? nestedFixtureName)
    {
        var fixture = nestedFixtureName is null
            ? owner
            : owner.GetNestedType(nestedFixtureName, BindingFlags.NonPublic);
        Assert.NotNull(fixture);
        var allocator = fixture.GetMethod("ReserveFreePort", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(allocator);
        for (var attempt = 0; attempt < 16; attempt++)
        {
            var port = Assert.IsType<int>(allocator.Invoke(null, null));
            Assert.InRange(port, 20000, 29999);
            var listener = new TcpListener(IPAddress.Loopback, port);
            try
            {
                listener.Start();
            }
            finally
            {
                listener.Stop();
            }
        }
    }
}
