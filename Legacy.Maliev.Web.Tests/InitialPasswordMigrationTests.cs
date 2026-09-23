using System.Net;
using System.Text.RegularExpressions;
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

    [Theory]
    [InlineData("en", "Please enter your email address", "The password setup token is required.", "Please enter a new password")]
    [InlineData("th", "โปรดกรอกอีเมลของคุณ", "ต้องมีโทเค็นสำหรับตั้งรหัสผ่าน", "โปรดกรอกรหัสผ่านใหม่")]
    public async Task SetInitialPasswordPost_LocalizesVisibleValidationErrors(
        string culture,
        string emailError,
        string tokenError,
        string passwordError)
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });
        var setupToken = new string('t', 43);
        using var get = await client.GetAsync($"/Account/SetInitialPassword?culture={culture}&email=customer%40example.com&token={setupToken}");
        var getSource = await get.Content.ReadAsStringAsync();
        var antiforgery = Regex.Match(getSource, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(antiforgery.Success);

        using var post = await client.PostAsync(
            $"/Account/SetInitialPassword?handler=Complete&culture={culture}",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = WebUtility.HtmlDecode(antiforgery.Groups[1].Value),
                ["Email"] = string.Empty,
                ["Token"] = string.Empty,
                ["Password"] = string.Empty,
                ["ConfirmPassword"] = string.Empty,
            }));
        var source = WebUtility.HtmlDecode(await post.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, post.StatusCode);
        Assert.Contains(emailError, source, StringComparison.Ordinal);
        Assert.Contains(tokenError, source, StringComparison.Ordinal);
        Assert.Contains(passwordError, source, StringComparison.Ordinal);
        Assert.DoesNotContain("The Email field is required.", source, StringComparison.Ordinal);
        Assert.DoesNotContain("The Token field is required.", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en", "Please enter a valid email address", "The password setup token is invalid.", "The new password must be between 8 and 1024 characters.", "Password confirmation does not match.")]
    [InlineData("th", "โปรดกรอกอีเมลที่ถูกต้อง", "โทเค็นสำหรับตั้งรหัสผ่านไม่ถูกต้อง", "รหัสผ่านใหม่ต้องมีความยาว 8 ถึง 1024 ตัวอักษร", "การยืนยันรหัสผ่านไม่ตรงกัน")]
    public async Task SetInitialPasswordPost_LocalizesInvalidFormatAndLengthErrors(
        string culture,
        string emailError,
        string tokenError,
        string passwordError,
        string confirmationError)
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });
        var setupToken = new string('t', 43);
        using var get = await client.GetAsync($"/Account/SetInitialPassword?culture={culture}&email=customer%40example.com&token={setupToken}");
        var getSource = await get.Content.ReadAsStringAsync();
        var antiforgery = Regex.Match(getSource, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(antiforgery.Success);

        using var post = await client.PostAsync(
            $"/Account/SetInitialPassword?handler=Complete&culture={culture}",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = WebUtility.HtmlDecode(antiforgery.Groups[1].Value),
                ["Email"] = "invalid-email",
                ["Token"] = "short",
                ["Password"] = "short",
                ["ConfirmPassword"] = "different",
            }));
        var source = WebUtility.HtmlDecode(await post.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, post.StatusCode);
        Assert.Contains(emailError, source, StringComparison.Ordinal);
        Assert.Contains(tokenError, source, StringComparison.Ordinal);
        Assert.Contains(passwordError, source, StringComparison.Ordinal);
        Assert.Contains(confirmationError, source, StringComparison.Ordinal);
        Assert.DoesNotContain("name=\"Password\" value=\"short\"", source, StringComparison.Ordinal);
    }
}
