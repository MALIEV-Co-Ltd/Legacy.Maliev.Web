using Google.Cloud.RecaptchaEnterprise.V1;
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

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t ")]
    public void HostStartup_LegacyKeyIdFillsOnlyMissingCanonicalSiteKey(string? siteKey)
    {
        using var factory = new RecaptchaStartupFactory("test-project", siteKey, keyId: "legacy-site-key");
        using var client = factory.CreateClient();
        var options = factory.Services.GetRequiredService<IOptions<RecaptchaEnterpriseOptions>>().Value;
        Assert.Equal("legacy-site-key", options.SiteKey);
        Assert.Equal("legacy-site-key", options.KeyId);
        Assert.IsType<GoogleRecaptchaAssessmentClient>(factory.Services.GetRequiredService<IRecaptchaAssessmentClient>());
    }

    [Fact]
    public void HostStartup_CanonicalSiteKeyTakesPrecedenceOverLegacyKeyId()
    {
        using var factory = new RecaptchaStartupFactory("test-project", "canonical-site-key", keyId: "legacy-site-key");
        using var client = factory.CreateClient();
        var options = factory.Services.GetRequiredService<IOptions<RecaptchaEnterpriseOptions>>().Value;
        Assert.Equal("canonical-site-key", options.SiteKey);
        Assert.Equal("legacy-site-key", options.KeyId);
    }

    [Fact]
    public void HostStartup_LegacyProjectIdCasingBindsCanonicalProjectId()
    {
        using var factory = new RecaptchaStartupFactory("legacy-project", "test-site-key", legacyProjectCasing: true);
        using var client = factory.CreateClient();
        Assert.Equal("legacy-project", factory.Services.GetRequiredService<IOptions<RecaptchaEnterpriseOptions>>().Value.ProjectId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t ")]
    public void HostStartup_OptionalBlankCredentialsPathLeavesNativeAdcSelection(string? path)
    {
        using var factory = new RecaptchaStartupFactory("test-project", "test-site-key", credentialsPath: path);
        using var client = factory.CreateClient();
        var options = factory.Services.GetRequiredService<IOptions<RecaptchaEnterpriseOptions>>().Value;
        var builder = GoogleRecaptchaAssessmentClient.CreateBuilder(options);
        Assert.IsType<RecaptchaEnterpriseServiceClientBuilder>(builder);
        Assert.Null(builder.CredentialsPath);
        Assert.IsType<GoogleRecaptchaAssessmentClient>(factory.Services.GetRequiredService<IRecaptchaAssessmentClient>());
    }

    [Theory]
    [InlineData("relative")]
    [InlineData("missing")]
    [InlineData("directory")]
    [InlineData("nul")]
    public void HostStartup_MalformedCredentialsPathFailsWithFieldOnlyDiagnostic(string kind)
    {
        var directory = Path.Combine(Path.GetTempPath(), "web-recaptcha-startup-proof", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = kind switch
            {
                "relative" => "relative-credential-proof.json",
                "missing" => Path.Combine(directory, "missing-credential-proof.json"),
                "directory" => directory,
                "nul" => directory + Path.DirectorySeparatorChar + "credential\0proof.json",
                _ => throw new ArgumentException("Unknown path scenario.", nameof(kind)),
            };
            using var factory = new RecaptchaStartupFactory("test-project", "test-site-key", credentialsPath: path);
            var error = Assert.Throws<OptionsValidationException>(() =>
            {
                using var client = factory.CreateClient();
            });
            Assert.Equal("Recaptcha:CredentialsPath must name an absolute readable file.", Assert.Single(error.Failures));
            Assert.DoesNotContain(path, error.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory);
        }
    }

    [Fact]
    public void HostStartup_ReadableSyntheticFileIsForwardedToNativeBuilderWithoutCredentialAcquisition()
    {
        var directory = Path.Combine(Path.GetTempPath(), "web-recaptcha-startup-proof", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "synthetic-noncredential.txt");
        try
        {
            // Deliberately not a credential document. Calling Build/BuildAsync
            // would parse/acquire credentials, which this startup boundary excludes.
            File.WriteAllText(path, "synthetic readable file; not Google credential material");
            using var factory = new RecaptchaStartupFactory("test-project", "test-site-key", credentialsPath: path);
            using var client = factory.CreateClient();
            var options = factory.Services.GetRequiredService<IOptions<RecaptchaEnterpriseOptions>>().Value;
            Assert.Equal(path, options.CredentialsPath);
            Assert.IsType<GoogleRecaptchaAssessmentClient>(factory.Services.GetRequiredService<IRecaptchaAssessmentClient>());
            var builder = GoogleRecaptchaAssessmentClient.CreateBuilder(options);
            Assert.IsType<RecaptchaEnterpriseServiceClientBuilder>(builder);
            Assert.Equal(path, builder.CredentialsPath);
        }
        finally
        {
            File.Delete(path);
            Directory.Delete(directory);
        }
    }

    private sealed class RecaptchaStartupFactory(string? projectId, string? siteKey,
        string? keyId = null, string? credentialsPath = null, bool legacyProjectCasing = false) : TestingWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services => services.AddDataProtection().UseEphemeralDataProtectionProvider());
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    [legacyProjectCasing ? "Recaptcha:ProjectID" : "Recaptcha:ProjectId"] = projectId,
                    ["Recaptcha:SiteKey"] = siteKey,
                    ["Recaptcha:KeyID"] = keyId,
                    ["Recaptcha:CredentialsPath"] = credentialsPath,
                }));
        }
    }
}
