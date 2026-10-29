using Microsoft.AspNetCore.Mvc.Testing;

namespace Legacy.Maliev.Web.Tests;

public sealed class PrintingSpaceMouseDeliveryContractTests(TestingWebApplicationFactory factory)
    : IClassFixture<TestingWebApplicationFactory>
{
    [Theory]
    [InlineData("en", "SpaceMouse help")]
    [InlineData("th", "การใช้งาน SpaceMouse")]
    public async Task PrintingDocument_ExposesLicensedOptionalHelpWithoutCncNavigation(string culture, string label)
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var response = await client.GetAsync($"/instantquotation/3d-printing?culture={culture}&tracking=excluded");
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        string html = await response.Content.ReadAsStringAsync();
        Assert.Contains("data-printing-spacemouse-help", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/help/spacemouse.html\"", html, StringComparison.Ordinal);
        Assert.Contains(label, System.Net.WebUtility.HtmlDecode(html), StringComparison.Ordinal);
        Assert.DoesNotContain("/src/app/js/model-viewer/spacemouse-bootstrap.module.js", html, StringComparison.Ordinal);
        string policy = Assert.Single(response.Headers.GetValues("Content-Security-Policy"));
        string directive = Assert.Single(policy.Split(';'), value => value.TrimStart().StartsWith("connect-src ", StringComparison.Ordinal));
        string[] origins = directive.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains("https://127.51.68.120:8181", origins);
        Assert.Contains("wss://127.51.68.120:*", origins);
        Assert.DoesNotContain("*", origins);
        Assert.DoesNotContain("https:", origins);
        Assert.DoesNotContain("wss:", origins);
    }
}
