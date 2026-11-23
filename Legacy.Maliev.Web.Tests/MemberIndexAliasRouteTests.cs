using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;

namespace Legacy.Maliev.Web.Tests;

public sealed class MemberIndexAliasRouteTests(TestingWebApplicationFactory factory) : IClassFixture<TestingWebApplicationFactory>
{
    [Theory]
    [InlineData("/Member", "BlazorRouting:MemberOverview", true)]
    [InlineData("/Member", "BlazorRouting:MemberOverview", false)]
    [InlineData("/Member/Index", "BlazorRouting:MemberOverview", true)]
    [InlineData("/Member/Index", "BlazorRouting:MemberOverview", false)]
    [InlineData("/Member/Account", "BlazorRouting:MemberAccountIndex", true)]
    [InlineData("/Member/Account", "BlazorRouting:MemberAccountIndex", false)]
    [InlineData("/Member/Account/Index", "BlazorRouting:MemberAccountIndex", true)]
    [InlineData("/Member/Account/Index", "BlazorRouting:MemberAccountIndex", false)]
    [InlineData("/Member/Orders", "BlazorRouting:MemberOrdersIndex", true)]
    [InlineData("/Member/Orders", "BlazorRouting:MemberOrdersIndex", false)]
    [InlineData("/Member/Orders/Index", "BlazorRouting:MemberOrdersIndex", true)]
    [InlineData("/Member/Orders/Index", "BlazorRouting:MemberOrdersIndex", false)]
    public async Task IndexAndShortRoute_PreserveLoginChallenge(string path, string feature, bool enabled)
    {
        await using var host = factory.WithWebHostBuilder(builder => builder.UseSetting(feature, enabled.ToString()));
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        var destination = new Uri(client.BaseAddress!, response.Headers.Location);
        Assert.Equal("/Account/Login", destination.AbsolutePath, ignoreCase: true);
        Assert.Equal(path, QueryHelpers.ParseQuery(destination.Query)["ReturnUrl"].ToString(), ignoreCase: true);
    }
}
