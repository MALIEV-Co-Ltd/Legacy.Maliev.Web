using Legacy.Maliev.Web.Components.Pages;
using Legacy.Maliev.Web.Middleware;
using Legacy.Maliev.Web.Pages;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Legacy.Maliev.Web.Tests;

public sealed class ErrorRoutePolicyTests
{
    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(200)]
    [InlineData(399)]
    [InlineData(600)]
    public void DirectRequest_WithoutValidCodeRedirectsHome(int? code)
    {
        var context = new DefaultHttpContext();
        var model = CreateModel(context);

        var result = Assert.IsType<RedirectResult>(model.OnGet(code));

        Assert.Equal("/", result.Url);
        Assert.False(context.Response.Headers.ContainsKey(ErrorIncidentHandler.HeaderName));
        Assert.False(model.DisplayModel.ShowIncidentId);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(404)]
    [InlineData(500)]
    [InlineData(599)]
    public void DirectRequest_ValidCodeIsIncidentFreePreview(int code)
    {
        var context = new DefaultHttpContext();
        var model = CreateModel(context);

        Assert.IsType<PageResult>(model.OnGet(code));

        Assert.True(model.DisplayModel.IsPreview);
        Assert.Equal(code, model.DisplayModel.StatusCode);
        Assert.False(model.DisplayModel.ShowIncidentId);
        Assert.False(context.Response.Headers.ContainsKey(ErrorIncidentHandler.HeaderName));
    }

    [Theory]
    [InlineData(404, 500)]
    [InlineData(503, 404)]
    public void StatusReExecution_PreservesTrustedStatusWithoutIncident(int status, int queryCode)
    {
        var context = new DefaultHttpContext();
        context.Response.StatusCode = status;
        context.Features.Set<IStatusCodeReExecuteFeature>(new StatusCodeReExecuteFeature
        {
            OriginalPath = "/missing",
        });
        var model = CreateModel(context);

        Assert.IsType<PageResult>(model.OnGet(queryCode));

        Assert.False(model.DisplayModel.IsPreview);
        Assert.Equal(status, model.DisplayModel.StatusCode);
        Assert.False(model.DisplayModel.ShowIncidentId);
    }

    [Fact]
    public void ExceptionContext_ReusesOnlyAnAlreadyLoggedIncident()
    {
        var context = new DefaultHttpContext();
        context.Features.Set<IExceptionHandlerFeature>(new ExceptionHandlerFeature
        {
            Error = new InvalidOperationException("fixture"),
            Path = "/throws",
        });
        var expected = ErrorIncidentHandler.GetOrCreate(
            context,
            new DateTimeOffset(2026, 9, 3, 9, 0, 0, TimeSpan.Zero));
        var model = CreateModel(context);

        Assert.IsType<PageResult>(model.OnGet(404));

        Assert.Equal(500, model.DisplayModel.StatusCode);
        Assert.False(model.DisplayModel.IsPreview);
        Assert.Equal(expected.IncidentId, model.DisplayModel.IncidentId);
        Assert.Equal(expected.OccurredAtUtc, model.DisplayModel.OccurredAtUtc);
    }

    private static ErrorModel CreateModel(HttpContext context) => new()
    {
        PageContext = new PageContext { HttpContext = context },
    };
}
