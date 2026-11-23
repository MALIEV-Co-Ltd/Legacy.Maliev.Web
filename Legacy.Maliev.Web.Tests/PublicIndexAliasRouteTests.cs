using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Legacy.Maliev.Web.Tests;

public sealed class PublicIndexAliasRouteTests(TestingWebApplicationFactory factory) : IClassFixture<TestingWebApplicationFactory>
{
    [Theory]
    [InlineData("/Index")]
    [InlineData("/About/Index")]
    [InlineData("/Legal/Index")]
    [InlineData("/Account/Index")]
    [InlineData("/Contact/Index")]
    [InlineData("/Quotation/Index")]
    [InlineData("/Career/Index")]
    [InlineData("/Services/Index")]
    [InlineData("/Knowledges/Index")]
    [InlineData("/Knowledges/Specifications/Index")]
    public void ExplicitIndexAlias_HasAGetEndpoint(string path)
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>();
        Assert.Contains(endpoints.Endpoints.OfType<RouteEndpoint>(), endpoint =>
        {
            if (!string.Equals(endpoint.RoutePattern.RawText, path, StringComparison.OrdinalIgnoreCase)) return false;
            var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods;
            return methods is null || methods.Count == 0 || methods.Contains("GET", StringComparer.OrdinalIgnoreCase);
        });
    }

}
