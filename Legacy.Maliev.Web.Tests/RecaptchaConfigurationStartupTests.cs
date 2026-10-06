using System.Security.Cryptography;
using System.Text.Json;
using Google.Apis.Auth.OAuth2;
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
        Assert.Null(builder.GoogleCredential);
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
            // Deliberately not a credential document: actual host startup must not
            // parse it. The later builder seam verifies path selection offline.
            File.WriteAllText(path, "synthetic readable file; not Google credential material");
            using var factory = new RecaptchaStartupFactory("test-project", "test-site-key", credentialsPath: path);
            using var client = factory.CreateClient();
            var options = factory.Services.GetRequiredService<IOptions<RecaptchaEnterpriseOptions>>().Value;
            Assert.Equal(path, options.CredentialsPath);
            Assert.IsType<GoogleRecaptchaAssessmentClient>(factory.Services.GetRequiredService<IRecaptchaAssessmentClient>());
            var syntheticCredential = GoogleCredential.FromAccessToken("synthetic-offline-access-token");
            string? observedPath = null;
            var builder = GoogleRecaptchaAssessmentClient.CreateBuilder(options, configuredPath =>
            {
                observedPath = configuredPath;
                return syntheticCredential;
            });
            Assert.IsType<RecaptchaEnterpriseServiceClientBuilder>(builder);
            Assert.Equal(path, observedPath);
            Assert.Same(syntheticCredential, builder.GoogleCredential);
        }
        finally
        {
            File.Delete(path);
            Directory.Delete(directory);
        }
    }

    [Fact]
    public void AssessmentBuilder_ExplicitServiceAccountUsesSupportedTypedCredentialWithoutNetwork()
    {
        var path = Path.Combine(Path.GetTempPath(), $"web-recaptcha-synthetic-{Guid.NewGuid():N}.json");
        using var key = RSA.Create(2048);
        try
        {
            // Generated solely for this offline fixture; never a deployed key.
            File.WriteAllText(path, JsonSerializer.Serialize(new
            {
                type = "service_account",
                project_id = "synthetic-offline-project",
                client_email = "synthetic-offline@credential-proof.invalid",
                private_key = key.ExportPkcs8PrivateKeyPem(),
                token_uri = "https://oauth2.googleapis.com/token",
            }));
            var builder = GoogleRecaptchaAssessmentClient.CreateBuilder(
                new RecaptchaEnterpriseOptions { CredentialsPath = path });
            Assert.NotNull(builder.GoogleCredential);
            var credential = Assert.IsType<ServiceAccountCredential>(builder.GoogleCredential.UnderlyingCredential);
            Assert.Equal("synthetic-offline@credential-proof.invalid", credential.Id);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("authorized_user")]
    [InlineData("external_account")]
    public void AssessmentBuilder_ExplicitMountedCredentialRejectsOtherTypes(string credentialType)
    {
        var path = Path.Combine(Path.GetTempPath(), $"web-recaptcha-synthetic-{Guid.NewGuid():N}.json");
        try
        {
            // Ambient ADC/workload identity remains available by omitting the path.
            object document = credentialType == "authorized_user"
                ? new
                {
                    type = credentialType,
                    client_id = "synthetic-offline-client",
                    client_secret = "synthetic-offline",
                    refresh_token = "synthetic-offline",
                }
                : new
                {
                    type = credentialType,
                    audience = "//iam.googleapis.com/projects/1/locations/global/workloadIdentityPools/synthetic/providers/synthetic",
                    subject_token_type = "urn:ietf:params:oauth:token-type:jwt",
                    token_url = "https://sts.googleapis.com/v1/token",
                    credential_source = new { file = "/synthetic-offline/subject-token" },
                };
            File.WriteAllText(path, JsonSerializer.Serialize(document));
            // First prove these are parseable as their own type, without fetching
            // any token. A generic loader would accept them; the mounted-SA loader must reject.
            if (credentialType == "authorized_user")
            {
                Assert.IsType<UserCredential>(CredentialFactory.FromFile<UserCredential>(path));
            }
            else
            {
                Assert.IsType<FileSourcedExternalAccountCredential>(
                    CredentialFactory.FromFile<FileSourcedExternalAccountCredential>(path));
            }
            Assert.Throws<InvalidOperationException>(() => GoogleRecaptchaAssessmentClient.CreateBuilder(
                new RecaptchaEnterpriseOptions { CredentialsPath = path }));
        }
        finally
        {
            File.Delete(path);
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
