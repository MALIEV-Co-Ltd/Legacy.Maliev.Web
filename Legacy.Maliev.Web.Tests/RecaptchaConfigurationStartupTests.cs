using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Legacy.Maliev.Web.Tests;

public sealed class RecaptchaConfigurationStartupTests
{
    [Theory]
    [InlineData(null, "test-site-key", "ProjectId")]
    [InlineData("", "test-site-key", "ProjectId")]
    [InlineData(" \t ", "test-site-key", "ProjectId")]
    [InlineData("test-project", null, "SiteKey")]
    [InlineData("test-project", "", "SiteKey")]
    [InlineData("test-project", " \t ", "SiteKey")]
    public void HostStartup_MissingOrBlankRequiredIdentifier_FailsBeforeServingRequests(
        string? projectId,
        string? siteKey,
        string missingIdentifier)
    {
        using var factory = new RecaptchaStartupFactory(projectId, siteKey);

        var error = Assert.Throws<OptionsValidationException>(() =>
        {
            using var client = factory.CreateClient();
        });

        Assert.Contains(error.Failures, failure => failure.Contains($"Recaptcha:{missingIdentifier}", StringComparison.Ordinal));
    }

    [Fact]
    public void HostStartup_ConfiguredIdentifiers_StartsWithoutAcquiringGoogleCredentials()
    {
        using var factory = new RecaptchaStartupFactory("test-project", "test-site-key");
        using var client = factory.CreateClient();
        var options = factory.Services.GetRequiredService<IOptions<RecaptchaEnterpriseOptions>>().Value;

        Assert.Equal("test-project", options.ProjectId);
        Assert.Equal("test-site-key", options.SiteKey);
        Assert.IsType<GoogleRecaptchaAssessmentClient>(factory.Services.GetRequiredService<IRecaptchaAssessmentClient>());
        using var scope = factory.Services.CreateScope();
        Assert.IsType<RecaptchaEnterpriseVerifier>(scope.ServiceProvider.GetRequiredService<IAntiBotVerifier>());
    }

    private sealed class RecaptchaStartupFactory(string? projectId, string? siteKey) : TestingWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services => services.AddDataProtection().UseEphemeralDataProtectionProvider());
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Recaptcha:ProjectId"] = projectId,
                    ["Recaptcha:SiteKey"] = siteKey,
                }));
        }
    }
}
