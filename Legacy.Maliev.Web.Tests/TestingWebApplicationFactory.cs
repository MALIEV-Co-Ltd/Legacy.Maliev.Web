using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Legacy.Maliev.Web.Tests;

public class TestingWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string? contentRoot;

    public TestingWebApplicationFactory()
    {
    }

    internal TestingWebApplicationFactory(string contentRoot)
    {
        this.contentRoot = contentRoot;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Recaptcha:ProjectId"] = "test-project",
                ["Recaptcha:SiteKey"] = "test-site-key",
            }));
        if (contentRoot is not null)
        {
            builder.UseContentRoot(contentRoot);
        }
    }
}
