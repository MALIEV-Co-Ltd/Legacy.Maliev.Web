using System.Net;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Legacy.Maliev.Web.Tests;

public sealed class CncUploadRouteTests(TestingWebApplicationFactory factory)
    : IClassFixture<TestingWebApplicationFactory>
{
    [Fact]
    public void UploadRoute_RequiresAntiforgeryAndOnlyAcceptsPost()
    {
        using var client = CreateClient();
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>();
        var route = Assert.Single(endpoints.Endpoints.OfType<RouteEndpoint>(), endpoint =>
            endpoint.RoutePattern.RawText == "/InstantQuotation/CNC-Machining");
        Assert.True(route.Metadata.GetMetadata<IAntiforgeryMetadata>()?.RequiresValidation);
        Assert.Equal(["POST"], route.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods);
    }

    [Theory]
    [InlineData("handler=UploadFile")]
    [InlineData("handler=UploadFile&handler=UploadFile&uploadRole=model")]
    [InlineData("handler=UploadFile&uploadRole=model&uploadRole=drawing")]
    public async Task InvalidAdmission_IsRejectedByTheRealHttpPipeline(string query)
    {
        using var client = CreateClient();
        using var body = new MultipartFormDataContent();
        using var response = await client.PostAsync($"/InstantQuotation/CNC-Machining?{query}", body);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain("Set-Cookie", response.Headers.Select(header => header.Key));
    }

    private HttpClient CreateClient() => factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        BaseAddress = new Uri("https://localhost"),
    });
}
