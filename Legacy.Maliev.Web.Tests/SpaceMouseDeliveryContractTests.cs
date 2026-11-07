using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Legacy.Maliev.Web.Tests;

public sealed class SpaceMouseDeliveryContractTests(TestingWebApplicationFactory factory)
    : IClassFixture<TestingWebApplicationFactory>
{
    private const string SdkPath = "/src/vendor/@3dconnexion/3dconnexionjs/3dconnexion.module.min.js";
    private const string LicensePath = "/src/vendor/@3dconnexion/3dconnexionjs/LicenseAgreementSDK.txt";

    [Theory]
    [InlineData(SdkPath, 44277, "9cf425c09ba529a8394ce3b68d4b0dcd804c8e9c325eb4c0edba9bb928ffccce")]
    [InlineData(LicensePath, 6390, "b6da433315842f01292b3b00b3caad9678fb23ece1b2b45fe89566340bf28287")]
    public async Task OriginalSdkAndLicense_AreServedWithTheirExactOriginalBytes(string path, int length, string sha256)
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(path);
        response.EnsureSuccessStatusCode();
        byte[] bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(length, bytes.Length);
        Assert.Equal(sha256, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
    }

    [Fact]
    public async Task Help_RendersBilingualDeviceGuidanceAndThePrescribedVisibleAttribution()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/help/spacemouse.html");
        response.EnsureSuccessStatusCode();
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        string html = await response.Content.ReadAsStringAsync();
        Assert.Contains("<section lang=\"en\"", html, StringComparison.Ordinal);
        Assert.Contains("<section lang=\"th\"", html, StringComparison.Ordinal);
        Assert.Contains("Technology acknowledgements", html, StringComparison.Ordinal);
        Assert.Contains(
            "3D input device development tools and related technology are provided under license from 3Dconnexion. © 3Dconnexion 1992 - 2025. All rights reserved.",
            html,
            StringComparison.Ordinal);
        Assert.Contains($"href=\"{LicensePath}\"", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("th")]
    public async Task ActualCncDocument_DeliversOptionalModulesAndAdmitsOnlyTheOriginalDriverOrigins(string culture)
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var response = await client.GetAsync($"/instantquotation/cnc-machining?culture={culture}&tracking=excluded");
        response.EnsureSuccessStatusCode();
        string html = await response.Content.ReadAsStringAsync();
        Assert.Contains("/src/app/js/model-viewer/spacemouse-navigation.js", html, StringComparison.Ordinal);
        Assert.Contains("/src/app/js/model-viewer/spacemouse-bootstrap.module.js", html, StringComparison.Ordinal);
        string policy = Assert.Single(response.Headers.GetValues("Content-Security-Policy"));
        string directive = Assert.Single(policy.Split(';'), value => value.TrimStart().StartsWith("connect-src ", StringComparison.Ordinal));
        string[] origins = directive.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains("https://127.51.68.120:8181", origins);
        Assert.Contains("wss://127.51.68.120:*", origins);
        Assert.DoesNotContain("*", origins);
        Assert.DoesNotContain("https:", origins);
        Assert.DoesNotContain("wss:", origins);
        Assert.DoesNotContain("http://localhost:*", origins);
    }
}
