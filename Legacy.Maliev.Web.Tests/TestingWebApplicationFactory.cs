using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Legacy.Maliev.Web.Tests;

public class TestingWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string? contentRoot;

    public TestingWebApplicationFactory(string? contentRoot = null)
    {
        this.contentRoot = contentRoot;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        if (contentRoot is not null)
        {
            builder.UseContentRoot(contentRoot);
        }
    }
}
