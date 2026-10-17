using Microsoft.Playwright;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Bounds recovery of the initial loopback login GET, never the authenticated journey.</summary>
internal static class InitialBillingLoginNavigation
{
    internal static async Task NavigateAsync(Uri target, Func<Uri, float, Task> navigate, TimeProvider? clock = null)
    {
        if (!target.IsAbsoluteUri || target.Scheme != Uri.UriSchemeHttps || target.Host != "127.0.0.1"
            || target.AbsolutePath != "/Account/Login" || target.UserInfo.Length != 0 || target.Fragment.Length != 0)
            throw new ArgumentException("Only the initial HTTPS loopback login GET is admitted.", nameof(target));

        clock ??= TimeProvider.System;
        var started = clock.GetTimestamp();
        // Playwright's existing default navigation budget is 30 seconds, shared by both attempts.
        float Remaining() => (float)(TimeSpan.FromSeconds(30) - clock.GetElapsedTime(started)).TotalMilliseconds;
        for (var attempt = 0; ; attempt++)
        {
            var remaining = Remaining();
            if (remaining <= 0) throw new TimeoutException("Initial login navigation budget exhausted.");
            try
            {
                // Await the original task directly: an unsettled navigation never admits another.
                await navigate(target, remaining);
                return;
            }
            catch (PlaywrightException failure) when (attempt == 0 && IsNetworkChange(failure, target))
            {
                if (Remaining() <= 0) throw;
            }
        }
    }

    private static bool IsNetworkChange(PlaywrightException failure, Uri target)
    {
        var newline = failure.Message.IndexOf('\n');
        var firstLine = (newline < 0 ? failure.Message : failure.Message[..newline]).TrimEnd('\r');
        return string.Equals(firstLine, "net::ERR_NETWORK_CHANGED at " + target.AbsoluteUri, StringComparison.Ordinal);
    }
}
