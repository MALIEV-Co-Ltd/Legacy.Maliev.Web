using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Legacy.Maliev.Web.Tests;

public sealed class InitialPasswordMigrationTests : IClassFixture<TestingWebApplicationFactory>
{
    private readonly WebApplicationFactory<Program> factory;

    public InitialPasswordMigrationTests(TestingWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Theory]
    [InlineData("en", "Set your new password", "New password", "Confirm password")]
    [InlineData("th", "ตั้งรหัสผ่านใหม่", "รหัสผ่านใหม่", "ยืนยันรหัสผ่าน")]
    public async Task SetInitialPasswordGet_RendersLocalizedStaticSsrWithoutEchoingPasswords(
        string culture,
        string heading,
        string passwordLabel,
        string confirmLabel)
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });
        var token = new string('t', 43);
        using var response = await client.GetAsync(
            $"/Account/SetInitialPassword?culture={culture}&email=customer%40example.com&token={token}&returnUrl=%2FAccount");
        var source = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains($">{heading}<", source, StringComparison.Ordinal);
        Assert.Contains($">{passwordLabel}<", source, StringComparison.Ordinal);
        Assert.Contains($">{confirmLabel}<", source, StringComparison.Ordinal);
        Assert.Contains("name=\"__RequestVerificationToken\"", source, StringComparison.Ordinal);
        Assert.Contains("autocomplete=\"new-password\"", source, StringComparison.Ordinal);
        Assert.Contains("name=\"robots\" content=\"noindex, nofollow\"", source, StringComparison.Ordinal);
        Assert.Equal("no-referrer", Assert.Single(response.Headers.GetValues("Referrer-Policy")));
        Assert.DoesNotContain("value=\"customer-owned-password\"", source, StringComparison.Ordinal);
    }
}
