using System.Security.Claims;
using System.Text.Json;
using Legacy.Maliev.Web.Pages.Shared;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Checks consent, authenticated server subjects and safe GA4 configuration shape.</summary>
public sealed class GoogleAnalyticsUserIdTests
{
    /// <summary>Only the opaque authenticated subject is serialized, without email or session credentials.</summary>
    /// <param name="subject">A supported server-issued customer or legacy opaque subject.</param>
    [Theory]
    [InlineData("customer:1")]
    [InlineData("11111111-2222-3333-4444-555555555555")]
    public void ConsentedAuthenticatedSubject_ProducesOnlyOpaqueUserId(string subject)
    {
        Assert.True(GoogleAnalyticsUserId.TryBuildConfiguration(Principal(subject), true, out var json));
        using var document = JsonDocument.Parse(json);
        Assert.Equal(subject, Assert.Single(document.RootElement.EnumerateObject()).Value.GetString());
        Assert.Equal("user_id", Assert.Single(document.RootElement.EnumerateObject()).Name);
        Assert.DoesNotContain("private@example.test", json, StringComparison.Ordinal);
    }

    /// <summary>Both granted consent and an authenticated principal are required before identity disclosure.</summary>
    /// <param name="consent">Whether optional tracking was granted.</param>
    /// <param name="authenticated">Whether the principal was authenticated by the server.</param>
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public void ConsentOrAuthenticationAbsent_DoesNotExposeSubject(bool consent, bool authenticated)
    {
        Assert.False(GoogleAnalyticsUserId.TryBuildConfiguration(Principal("customer:1", authenticated), consent, out var json));
        Assert.Empty(json);
    }

    /// <summary>Malformed, email-like, script-like and noncanonical subjects fail closed.</summary>
    /// <param name="subject">The invalid claimed subject.</param>
    [Theory]
    [InlineData("")]
    [InlineData("private@example.test")]
    [InlineData("<script>")]
    [InlineData("customer:0")]
    [InlineData("customer:-1")]
    [InlineData("customer:01")]
    [InlineData("customer:2147483648")]
    [InlineData("customer:1\n")]
    [InlineData(" 11111111-2222-3333-4444-555555555555")]
    [InlineData("11111111-2222-3333-4444-555555555555 ")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void InvalidSubject_FailsClosed(string subject)
    {
        Assert.False(GoogleAnalyticsUserId.TryBuildConfiguration(Principal(subject), true, out var json));
        Assert.Empty(json);
    }

    /// <summary>Missing principals, missing subjects and conflicting subjects cannot supply analytics identity.</summary>
    [Fact]
    public void MissingOrAmbiguousIdentity_FailsClosed()
    {
        Assert.False(GoogleAnalyticsUserId.TryBuildConfiguration(null, true, out _));
        Assert.False(GoogleAnalyticsUserId.TryBuildConfiguration(new ClaimsPrincipal(new ClaimsIdentity([], "test")), true, out _));
        var principal = Principal("customer:1");
        ((ClaimsIdentity)principal.Identity!).AddClaim(new Claim(ClaimTypes.NameIdentifier, "customer:2"));
        Assert.False(GoogleAnalyticsUserId.TryBuildConfiguration(principal, true, out _));
    }

    private static ClaimsPrincipal Principal(string subject, bool authenticated = true) => new(new ClaimsIdentity(
        [new Claim(ClaimTypes.NameIdentifier, subject), new Claim(ClaimTypes.Email, "private@example.test")],
        authenticated ? "test" : null));
}
