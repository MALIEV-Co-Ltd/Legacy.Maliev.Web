using System.Security.Claims;
using System.Text.Json;
using Legacy.Maliev.Web.Pages.Shared;

namespace Legacy.Maliev.Web.Tests;

public sealed class GoogleAnalyticsUserIdTests
{
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

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public void ConsentOrAuthenticationAbsent_DoesNotExposeSubject(bool consent, bool authenticated)
    {
        Assert.False(GoogleAnalyticsUserId.TryBuildConfiguration(Principal("customer:1", authenticated), consent, out var json));
        Assert.Empty(json);
    }

    [Theory]
    [InlineData("")]
    [InlineData("private@example.test")]
    [InlineData("<script>")]
    [InlineData("customer:0")]
    [InlineData("customer:-1")]
    [InlineData("customer:01")]
    [InlineData("customer:2147483648")]
    [InlineData("customer:1\n")]
    public void InvalidSubject_FailsClosed(string subject)
    {
        Assert.False(GoogleAnalyticsUserId.TryBuildConfiguration(Principal(subject), true, out var json));
        Assert.Empty(json);
    }

    [Fact]
    public void MissingOrAmbiguousIdentity_FailsClosed()
    {
        Assert.False(GoogleAnalyticsUserId.TryBuildConfiguration(null, true, out _));
        var principal = Principal("customer:1");
        ((ClaimsIdentity)principal.Identity!).AddClaim(new Claim(ClaimTypes.NameIdentifier, "customer:2"));
        Assert.False(GoogleAnalyticsUserId.TryBuildConfiguration(principal, true, out _));
    }

    private static ClaimsPrincipal Principal(string subject, bool authenticated = true) => new(new ClaimsIdentity(
        [new Claim(ClaimTypes.NameIdentifier, subject), new Claim(ClaimTypes.Email, "private@example.test")],
        authenticated ? "test" : null));
}
