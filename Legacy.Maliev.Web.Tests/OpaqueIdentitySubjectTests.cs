using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Infrastructure;
using Legacy.Maliev.Web.Pages.Shared;
using Microsoft.AspNetCore.WebUtilities;

namespace Legacy.Maliev.Web.Tests;

public sealed class OpaqueIdentitySubjectTests
{
    [Theory]
    [InlineData("member-crawl-customer")]
    [InlineData("customer-guid-123")]
    [InlineData("opaque</script>subject")]
    public void AuthSubjectExtractionAndConsentedProjection_PreserveOpaqueValue(string subject)
    {
        var token = "header." + WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new { sub = subject, email = "private@example.test", name = "private-name" }))) + ".signature";
        Assert.Equal(subject, CustomerAuthenticationClient.ExtractIdentitySubject(token));
        var principal = Principal(subject);
        Assert.True(GoogleAnalyticsUserId.TryBuildConfiguration(principal, true, out var json));
        using var document = JsonDocument.Parse(json);
        var property = Assert.Single(document.RootElement.EnumerateObject());
        Assert.Equal("user_id", property.Name);
        Assert.Equal(subject, property.Value.GetString());
        Assert.DoesNotContain("</script>", json, StringComparison.Ordinal);
        Assert.DoesNotContain("private@example.test", json, StringComparison.Ordinal);
        Assert.Equal("customer:42", principal.FindFirstValue(ClaimTypes.NameIdentifier));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{\"sub\":null}")]
    [InlineData("{\"sub\":42}")]
    [InlineData("{\"sub\":\" \"}")]
    [InlineData("{\"sub\":\"first\",\"sub\":\"second\"}")]
    public void MissingOrAmbiguousAuthSubject_IsNotCoerced(string payload)
    {
        var token = "header." + WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(payload)) + ".signature";
        Assert.Null(CustomerAuthenticationClient.ExtractIdentitySubject(token));
    }

    [Theory]
    [InlineData("not-a-token")]
    [InlineData("header.!.signature")]
    public void MalformedToken_HasNoIdentityMetadata(string token) =>
        Assert.Null(CustomerAuthenticationClient.ExtractIdentitySubject(token));

    [Fact]
    public void RetainedSubject_StillRequiresConsentAuthenticationAndUnambiguousMetadata()
    {
        var principal = Principal("customer-guid-123");
        Assert.False(GoogleAnalyticsUserId.TryBuildConfiguration(principal, false, out _));
        Assert.False(GoogleAnalyticsUserId.TryBuildConfiguration(Principal("customer-guid-123", false), true, out _));
        ((ClaimsIdentity)principal.Identity!).AddClaim(new Claim(CustomerIdentityClaims.AnalyticsSubject, "other"));
        Assert.False(GoogleAnalyticsUserId.TryBuildConfiguration(principal, true, out _));
    }

    private static ClaimsPrincipal Principal(string subject, bool authenticated = true) =>
        new(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, "customer:42"),
            new Claim(CustomerIdentityClaims.AnalyticsSubject, subject),
            new Claim(ClaimTypes.Email, "private@example.test")], authenticated ? "server-session" : null));
}
