using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml.Linq;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.DataProtection.StackExchangeRedis;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
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
    [InlineData(null)]
    [InlineData("false")]
    [InlineData("True")]
    [InlineData("1")]
    [InlineData("true")]
    public Task Production_ExactStandbyPreservesEncryptedExistingCookie(string? standbyValue) =>
        VerifyStandbyAsync("Production", "Legacy.Maliev.Web", standbyValue);

    [Theory]
    [InlineData(null)]
    [InlineData("false")]
    [InlineData("True")]
    [InlineData("1")]
    [InlineData("true")]
    public Task Development_ExactStandbyPreservesEncryptedExistingCookie(string? standbyValue) =>
        VerifyStandbyAsync("Development", "Legacy.Maliev.Web.Development", standbyValue);

    private async Task VerifyStandbyAsync(string environment, string discriminator, string? standbyValue)
    {
        RequireAbsentStandbyEnvironment(standbyValue);
        string cookie;
        (int Count, string Sha256) fingerprint;
        await using (var seed = fixture.CreateFactory(environment,
            builder => builder.UseSetting("MALIEV_OBSERVABILITY_STANDBY", "false")))
        {
            var format = CookieFormat(seed.Services);
            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "synthetic-standby-member")],
                CookieAuthenticationDefaults.AuthenticationScheme);
            cookie = format.Protect(new AuthenticationTicket(new ClaimsPrincipal(identity),
                new AuthenticationProperties(), CookieAuthenticationDefaults.AuthenticationScheme));
            fingerprint = await KeyRingFingerprintAsync(seed.Services);
        }

        await using var candidate = fixture.CreateFactory(environment, builder =>
        {
            if (standbyValue is not null) builder.UseSetting("MALIEV_OBSERVABILITY_STANDBY", standbyValue);
        });
        var services = candidate.Services;
        Assert.Equal(standbyValue, services.GetRequiredService<IConfiguration>()["MALIEV_OBSERVABILITY_STANDBY"]);
        AssertRealEnvironmentAndRedis(candidate, environment, discriminator);
        var management = services.GetRequiredService<IOptions<KeyManagementOptions>>().Value;
        Assert.Equal(standbyValue != "true", management.AutoGenerateKeys);
        Assert.IsType<RedisXmlRepository>(management.XmlRepository);
        Assert.NotNull(management.XmlEncryptor);
        Assert.IsNotType<EphemeralDataProtectionProvider>(services.GetRequiredService<IDataProtectionProvider>());
        var cache = services.GetRequiredService<IOptions<RedisCacheOptions>>().Value;
        Assert.Equal("legacy:web:", cache.InstanceName);
        Assert.NotNull(cache.ConnectionMultiplexerFactory);
        Assert.Same(services.GetRequiredService<IConnectionMultiplexer>(), await cache.ConnectionMultiplexerFactory!());
        Assert.NotEqual(IntPtr.Zero, services.GetRequiredService<X509Certificate2>().Handle);
        var restored = CookieFormat(services).Unprotect(cookie);
        Assert.NotNull(restored);
        Assert.Equal(CookieAuthenticationDefaults.AuthenticationScheme, restored.AuthenticationScheme);
        Assert.Equal("synthetic-standby-member", restored.Principal.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.Equal(fingerprint, await KeyRingFingerprintAsync(services));
    }

    private static ISecureDataFormat<AuthenticationTicket> CookieFormat(IServiceProvider services) =>
        services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme).TicketDataFormat;

    private static async Task<(int Count, string Sha256)> KeyRingFingerprintAsync(IServiceProvider services)
    {
        var values = await services.GetRequiredService<IConnectionMultiplexer>().GetDatabase().ListRangeAsync(KeyRing);
        Assert.NotEmpty(values);
        Assert.True(values.All(value => value.ToString().Contains("encryptedSecret", StringComparison.Ordinal)
            && value.ToString().Contains("EncryptedKey", StringComparison.Ordinal)
            && !value.ToString().Contains("<masterKey", StringComparison.Ordinal)),
            "The disposable key ring must contain only encrypted synthetic key material.");
        var hashes = values.Select(value => Convert.ToHexString(SHA256.HashData((byte[])value!)));
        return (values.Length, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join(';', hashes)))));
    }

    internal static void RequireAbsentStandbyEnvironment(string? standbyValue)
    {
        if (standbyValue is not null) return;
        // A null UseSetting becomes an empty command-line value in minimal hosting.
        foreach (var prefix in new[] { "", "ASPNETCORE_", "DOTNET_" })
            Assert.True(Environment.GetEnvironmentVariable(prefix + "MALIEV_OBSERVABILITY_STANDBY") is null,
                "The absent standby case requires no ambient standby configuration.");
    }

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
            Assert.Contains(culture == "th" ? ">ศูนย์ความรู้</span></a>" : ">Knowledge center</span></a>", document, StringComparison.Ordinal);
        }
    }
}

public sealed class StartupDataProtectionMigrationTestsTestingControls
{
    [Theory]
    [InlineData(null)]
    [InlineData("false")]
    [InlineData("True")]
    [InlineData("1")]
    [InlineData("true")]
    public async Task Testing_StandbyDoesNotChangeMemoryCacheOrDefaultGeneration(string? standbyValue)
    {
        StartupDataProtectionMigrationTests.RequireAbsentStandbyEnvironment(standbyValue);
        var directory = Directory.CreateTempSubdirectory("maliev-web474-");
        Exception? primary = null;
        try
        {
            await using var parent = new TestingWebApplicationFactory();
            await using var factory = parent.WithWebHostBuilder(builder =>
            {
                if (standbyValue is not null) builder.UseSetting("MALIEV_OBSERVABILITY_STANDBY", standbyValue);
                // Keep the actual Program/provider; isolate only Testing's default
                // filesystem key repository from the persistent user profile.
                builder.ConfigureServices(services => services.Configure<KeyManagementOptions>(options =>
                    options.XmlRepository = new FileSystemXmlRepository(directory, NullLoggerFactory.Instance)));
            });
            var services = factory.Services;
            Assert.Equal("Testing", services.GetRequiredService<IHostEnvironment>().EnvironmentName);
            Assert.Equal(standbyValue, services.GetRequiredService<IConfiguration>()["MALIEV_OBSERVABILITY_STANDBY"]);
            Assert.IsType<MemoryDistributedCache>(services.GetRequiredService<IDistributedCache>());
            Assert.Null(services.GetService<IConnectionMultiplexer>());
            Assert.Null(services.GetService<X509Certificate2>());
            Assert.Equal("Legacy.Maliev.Web",
                services.GetRequiredService<IOptions<DataProtectionOptions>>().Value.ApplicationDiscriminator);
            var management = services.GetRequiredService<IOptions<KeyManagementOptions>>().Value;
            Assert.True(management.AutoGenerateKeys);
            Assert.IsType<FileSystemXmlRepository>(management.XmlRepository);
        }
        catch (Exception original)
        {
            primary = original;
            throw;
        }
        finally
        {
            try
            {
                var temporaryRoot = Path.GetFullPath(Path.GetTempPath());
                Assert.StartsWith(temporaryRoot, Path.GetFullPath(directory.FullName), StringComparison.OrdinalIgnoreCase);
                Assert.StartsWith("maliev-web474-", directory.Name, StringComparison.Ordinal);
                Assert.False(directory.Attributes.HasFlag(FileAttributes.ReparsePoint));
                directory.Delete(recursive: true);
                Assert.False(Directory.Exists(directory.FullName));
            }
            catch (Exception cleanup)
            {
                if (primary is null) throw;
                OwnedWebSurfaceResources.RecordFailureTypes(primary, [cleanup]);
            }
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

    public WebApplicationFactory<Program> CreateFactory(string environment, Action<IWebHostBuilder>? configure = null) =>
        new StartupWebFactory(environment, redis.GetConnectionString(), certificatePfxBase64, configure);

    private sealed class StartupWebFactory(
        string environment, string connection, string certificate, Action<IWebHostBuilder>? configure)
        : TestingWebApplicationFactory
    {
        private readonly OwnedWebSurfaceResources nativeResources = new();
        private IConnectionMultiplexer? ownedRedis;
        private X509Certificate2? ownedCertificate;

        public override async ValueTask DisposeAsync()
        {
            try { await base.DisposeAsync(); }
            catch (Exception original)
            {
                nativeResources.ReleasePreserving(original);
                throw;
            }
            nativeResources.Dispose();
            Assert.False(ownedRedis?.IsConnected ?? false);
            if (ownedCertificate is not null) Assert.Equal(IntPtr.Zero, ownedCertificate.Handle);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseEnvironment(environment);
            builder.UseSetting("ConnectionStrings:redis", connection);
            builder.UseSetting("DataProtection:CertificatePfxBase64", certificate);
            builder.UseSetting("DataProtection:CertificatePassword", CertificatePassword);
            builder.UseSetting("BlazorRouting:KnowledgesIndex", "true");
            builder.UseSetting("Logging:LogLevel:Default", "None");
            configure?.Invoke(builder);
            builder.ConfigureServices(services =>
            {
                // Program creates these exact synthetic instances, but AddSingleton(instance)
                // does not give DI their disposal ownership. Release them after host shutdown.
                ownedCertificate = nativeResources.Own(Assert.IsType<X509Certificate2>(Assert.Single(services,
                    descriptor => descriptor.ServiceType == typeof(X509Certificate2)).ImplementationInstance));
                ownedRedis = nativeResources.Own(Assert.IsAssignableFrom<IConnectionMultiplexer>(Assert.Single(services,
                    descriptor => descriptor.ServiceType == typeof(IConnectionMultiplexer)).ImplementationInstance));
            });
        }
    }
}
