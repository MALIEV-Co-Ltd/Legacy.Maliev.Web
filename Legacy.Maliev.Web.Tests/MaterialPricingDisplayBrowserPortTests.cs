using System.Net;
using System.Net.Sockets;
using System.Reflection;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Guards the source server-port policy at the actual material browser fixture.</summary>
public sealed class MaterialPricingDisplayBrowserPortTests
{
    [Fact]
    public void BrowserHostPort_IsOutsideClientPoolAndAvailableForHost()
    {
        var fixture = typeof(MaterialPricingDisplayBrowserTests).GetNestedType("Fixture", BindingFlags.NonPublic);
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
