using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace Legacy.Maliev.Web.Tests;

public sealed class StartupDataProtectionMigrationTests(StartupDataProtectionMigrationFixture fixture)
    : IClassFixture<StartupDataProtectionMigrationFixture>
{
    private const string Purpose = "Legacy.Maliev.Web.StartupMigrationProof.v1";
    private const string KeyRing = "legacy:web:data-protection-keys";

    [Theory]
    [InlineData("Development", "Legacy.Maliev.Web.Development", "Production")]
    [InlineData("Production", "Legacy.Maliev.Web", "Development")]
    public async Task ActualProgram_EncryptedRedisKeysSurviveRestartAndRejectOtherEnvironment(
        string environment, string discriminator, string otherEnvironment)
    {
        Assert.True(StartupDataProtectionMigrationFixture.IsOwnedHostedLane("hosted-owned", "github-hosted", "37410020727"));
        Assert.False(StartupDataProtectionMigrationFixture.IsOwnedHostedLane(null, "github-hosted", "37410020727"));
        Assert.False(StartupDataProtectionMigrationFixture.IsOwnedHostedLane("hosted-owned", "self-hosted", "37410020727"));
        Assert.False(StartupDataProtectionMigrationFixture.IsOwnedHostedLane("hosted-owned", null, "37410020727"));
        Assert.False(StartupDataProtectionMigrationFixture.IsOwnedHostedLane("hosted-owned", "github-hosted", null));
        Assert.False(StartupDataProtectionMigrationFixture.IsOwnedHostedLane("hosted-owned", "github-hosted", "0"));
        Assert.False(StartupDataProtectionMigrationFixture.IsOwnedHostedLane("hosted-owned", "github-hosted", "not-a-run"));
        const string plaintext = "synthetic-startup-migration-payload";
        string protectedPayload;
        await using (var first = fixture.CreateFactory(environment))
        {
            using var client = CreateClient(first);
            AssertRealEnvironmentAndRedis(first, environment, discriminator);
            protectedPayload = first.Services.GetRequiredService<IDataProtectionProvider>()
                .CreateProtector(Purpose).Protect(plaintext);

            var redis = first.Services.GetRequiredService<IConnectionMultiplexer>();
            var persisted = await redis.GetDatabase().ListRangeAsync(KeyRing);
            Assert.NotEmpty(persisted);
            var keys = persisted.Select(value => XElement.Parse(value.ToString()))
                .Where(element => element.Name.LocalName == "key").ToArray();
            Assert.NotEmpty(keys);
            Assert.All(keys, key =>
            {
                Assert.Contains(key.Descendants(), element => element.Name.LocalName == "encryptedSecret");
                Assert.Contains(key.Descendants(), element => element.Name.LocalName == "EncryptedKey");
                Assert.DoesNotContain(key.Descendants(), element => element.Name.LocalName == "masterKey");
            });
            await AssertCompiledKnowledgeRoutesAsync(client);
        }

        // The first host and its provider are gone. A new actual Program must
        // recover the encrypted key material from the same owned Redis instance.
        await using (var restarted = fixture.CreateFactory(environment))
        {
            using var client = CreateClient(restarted);
            AssertRealEnvironmentAndRedis(restarted, environment, discriminator);
            var protector = restarted.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector(Purpose);
            Assert.Equal(plaintext, protector.Unprotect(protectedPayload));
        }

        // Share the key ring and certificate deliberately: rejection must come
        // from the application discriminator, not an unreadable key or new store.
        await using (var other = fixture.CreateFactory(otherEnvironment))
        {
            using var client = CreateClient(other);
            AssertRealEnvironmentAndRedis(other, otherEnvironment,
                otherEnvironment == "Development" ? "Legacy.Maliev.Web.Development" : "Legacy.Maliev.Web");
            var protector = other.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector(Purpose);
            Assert.Throws<CryptographicException>(() => protector.Unprotect(protectedPayload));
            var ownPayload = protector.Protect(plaintext);
            Assert.Equal(plaintext, protector.Unprotect(ownPayload));
        }
    }

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory) => factory.CreateClient(
        new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
        });

    private static void AssertRealEnvironmentAndRedis(WebApplicationFactory<Program> factory,
        string environment, string discriminator)
    {
        Assert.Equal(environment, factory.Services.GetRequiredService<IHostEnvironment>().EnvironmentName);
        Assert.Equal(discriminator, factory.Services.GetRequiredService<IOptions<DataProtectionOptions>>().Value.ApplicationDiscriminator);
        Assert.IsAssignableFrom<RedisCache>(factory.Services.GetRequiredService<IDistributedCache>());
        Assert.True(factory.Services.GetRequiredService<IConnectionMultiplexer>().IsConnected);
    }

    private static async Task AssertCompiledKnowledgeRoutesAsync(HttpClient client)
    {
        foreach (var culture in new[] { "en", "th" })
        {
            using var response = await client.GetAsync($"/knowledges?culture={culture}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var document = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
            Assert.Contains($"<html lang=\"{culture}\"", document, StringComparison.Ordinal);
            Assert.Contains("data-migration-route-owner=\"blazor-static-ssr\"", document, StringComparison.Ordinal);
            Assert.Contains(culture == "th" ? ">ภาพรวม<" : ">Overview<", document, StringComparison.Ordinal);
        }
    }
}

public sealed class StartupDataProtectionMigrationFixture : IAsyncLifetime
{
    private const string CertificatePassword = "owned-startup-migration-proof";
    private readonly RedisContainer redis = new RedisBuilder("redis:8.4-alpine").Build();
    private string certificatePfxBase64 = string.Empty;

    public async Task InitializeAsync()
    {
        if (!IsOwnedHostedLane(Environment.GetEnvironmentVariable("MALIEV_WEB_STARTUP_PROOF"),
            Environment.GetEnvironmentVariable("RUNNER_ENVIRONMENT"), Environment.GetEnvironmentVariable("GITHUB_RUN_ID")))
            throw new InvalidOperationException("Startup migration proof requires the owned GitHub-hosted container lane.");

        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=Owned Web startup migration proof", rsa,
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
        certificatePfxBase64 = Convert.ToBase64String(certificate.Export(X509ContentType.Pfx, CertificatePassword));
        await redis.StartAsync();
    }

    public Task DisposeAsync() => redis.DisposeAsync().AsTask();

    internal static bool IsOwnedHostedLane(string? proofLane, string? runnerEnvironment, string? runId) =>
        string.Equals(proofLane, "hosted-owned", StringComparison.Ordinal)
        && string.Equals(runnerEnvironment, "github-hosted", StringComparison.Ordinal)
        && long.TryParse(runId, System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out var parsedRunId)
        && parsedRunId > 0;

    public WebApplicationFactory<Program> CreateFactory(string environment) =>
        new StartupWebFactory(environment, redis.GetConnectionString(), certificatePfxBase64);

    private sealed class StartupWebFactory(string environment, string connection, string certificate)
        : TestingWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseEnvironment(environment);
            builder.UseSetting("ConnectionStrings:redis", connection);
            builder.UseSetting("DataProtection:CertificatePfxBase64", certificate);
            builder.UseSetting("DataProtection:CertificatePassword", CertificatePassword);
            builder.UseSetting("BlazorRouting:KnowledgesIndex", "true");
            builder.UseSetting("Logging:LogLevel:Default", "None");
        }
    }
}
