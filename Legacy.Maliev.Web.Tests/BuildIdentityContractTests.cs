using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Legacy.Maliev.Web.Tests;

public sealed class BuildIdentityContractTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public BuildIdentityContractTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("environment", "Testing");
            builder.UseSetting("BuildIdentity:Repository", "https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.Web.git");
            builder.UseSetting("BuildIdentity:Branch", "codex/issue-154-build-identity");
            builder.UseSetting("BuildIdentity:Commit", "6e00796d263c45be73080fa292929a99dbb9af1d");
        });
    }

    [Fact]
    public async Task GET_WebBuildIdentity_ReturnsConfiguredSafeIdentity()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });

        using var response = await client.GetAsync("/web/build-identity");
        var identity = await response.Content.ReadFromJsonAsync<BuildIdentityResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(identity);
        Assert.Equal("https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.Web.git", identity.Repository);
        Assert.Equal("codex/issue-154-build-identity", identity.Branch);
        Assert.Equal("6e00796d263c45be73080fa292929a99dbb9af1d", identity.Commit);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain("SourceRoot", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task GET_About_ExposesConfiguredIdentityHeaders()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });

        using var response = await client.GetAsync("/about?culture=en");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.Web.git",
            Assert.Single(response.Headers.GetValues("X-Maliev-Build-Repository")));
        Assert.Equal(
            "codex/issue-154-build-identity",
            Assert.Single(response.Headers.GetValues("X-Maliev-Build-Branch")));
        Assert.Equal(
            "6e00796d263c45be73080fa292929a99dbb9af1d",
            Assert.Single(response.Headers.GetValues("X-Maliev-Build-Commit")));
    }

    [Theory]
    [InlineData("/InstantQuotation/3D-Printing?culture=en")]
    [InlineData("/web/liveness")]
    [InlineData("/web/readiness")]
    public async Task GET_RequiredAspireSurface_RemainsHealthyAndCarriesCommitHeader(string path)
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "6e00796d263c45be73080fa292929a99dbb9af1d",
            Assert.Single(response.Headers.GetValues("X-Maliev-Build-Commit")));
    }

    private sealed record BuildIdentityResponse(string Repository, string Branch, string Commit);
}
