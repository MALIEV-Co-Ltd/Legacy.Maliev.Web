using Legacy.Maliev.Web.Middleware;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Legacy.Maliev.Web.Tests;

public sealed class ErrorResponseContractMiddlewareTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExceptionRendering_RestoresRequestStateEvenWhenRenderingFails(bool renderFails)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/Error";
        context.Features.Set<IExceptionHandlerFeature>(new ExceptionHandlerFeature
        {
            Error = new InvalidOperationException("Original failure"),
        });
        var endpoint = new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(), "Previous selection");
        var routes = new RouteValueDictionary { ["handler"] = "SubmitRequest" };
        context.SetEndpoint(endpoint);
        context.Request.RouteValues = routes;
        var renderFailure = new InvalidOperationException("Rendering failure");
        var calls = 0;
        var middleware = new ErrorResponseContractMiddleware(current =>
        {
            calls++;
            Assert.Equal(HttpMethods.Get, current.Request.Method);
            Assert.Null(current.GetEndpoint());
            Assert.Empty(current.Request.RouteValues);
            return renderFails ? Task.FromException(renderFailure) : Task.CompletedTask;
        });

        if (renderFails)
            Assert.Same(renderFailure, await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(context)));
        else
            await middleware.InvokeAsync(context);

        Assert.Equal(1, calls);
        Assert.Equal(HttpMethods.Post, context.Request.Method);
        Assert.Same(endpoint, context.GetEndpoint());
        Assert.Same(routes, context.Request.RouteValues);
        Assert.Equal("SubmitRequest", context.Request.RouteValues["handler"]);
    }

    [Fact]
    public async Task DirectErrorPost_WithoutExceptionProvenanceRetainsItsMethodAndEndpoint()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/Error";
        context.Request.QueryString = new QueryString("?code=500");
        var endpoint = new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(), "External POST selection");
        context.SetEndpoint(endpoint);
        var calls = 0;
        var middleware = new ErrorResponseContractMiddleware(current =>
        {
            calls++;
            Assert.Equal(HttpMethods.Post, current.Request.Method);
            Assert.Same(endpoint, current.GetEndpoint());
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);

        Assert.Equal(1, calls);
    }
}
