using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
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

    [Theory]
    [InlineData("Development", false, true)]
    [InlineData("development", false, true)]
    [InlineData("Development", true, true)]
    [InlineData("Staging", false, false)]
    [InlineData("Production", false, false)]
    public async Task ActualProgram_ExplicitContentRoot_LoadsSourceEnvironmentJsonFromTheSelectedApplicationRoot(
        string environment, bool startsAtProject, bool expectsProject)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null
            && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Web", "Legacy.Maliev.Web.csproj")))
        {
            directory = directory.Parent;
        }
        Assert.NotNull(directory);
        var repositoryRoot = directory.FullName;
        var projectRoot = Path.Combine(repositoryRoot, "Legacy.Maliev.Web");
        var suppliedRoot = startsAtProject ? projectRoot : repositoryRoot;
        var expectedRoot = expectsProject ? projectRoot : repositoryRoot;
        Assert.True(File.Exists(Path.Combine(projectRoot, "appsettings.Development.json")));
        Assert.True(Directory.Exists(Path.Combine(projectRoot, "wwwroot")));
        Assert.False(File.Exists(Path.Combine(repositoryRoot, "appsettings.Development.json")));

        await using var factory = fixture.CreateFactory(environment, suppliedRoot);
        using var client = CreateClient(factory);
        var configuration = Assert.IsAssignableFrom<IConfigurationRoot>(
            factory.Services.GetRequiredService<IConfiguration>());
        // WebApplicationFactory applies its own later ContentRoot. Inspect the JSON provider
        // created by the actual entry point so that override cannot hide a missed early fallback.
        var json = Assert.Single(configuration.Providers.OfType<JsonConfigurationProvider>(),
            provider => provider.Source.Path == "appsettings.json");
        var files = Assert.IsType<PhysicalFileProvider>(json.Source.FileProvider);
        Assert.Equal(Path.GetFullPath(expectedRoot), Path.TrimEndingDirectorySeparator(files.Root));

        var hostEnvironment = factory.Services.GetRequiredService<IWebHostEnvironment>();
        Assert.Equal(environment, hostEnvironment.EnvironmentName, StringComparer.OrdinalIgnoreCase);
        if (expectsProject)
        {
            var development = Assert.Single(configuration.Providers.OfType<JsonConfigurationProvider>(),
                provider => provider.Source.Path == $"appsettings.{environment}.json");
            Assert.Equal(Path.GetFullPath(projectRoot),
                Path.TrimEndingDirectorySeparator(Assert.IsType<PhysicalFileProvider>(development.Source.FileProvider).Root));
            if (environment == "Development")
            {
                Assert.True(development.TryGet("Career:HideLocalAspireFixture", out var hideLocalAspireFixture));
                Assert.Equal(bool.TrueString, hideLocalAspireFixture, StringComparer.OrdinalIgnoreCase);
            }
            Assert.Equal(Path.GetFullPath(projectRoot), Path.TrimEndingDirectorySeparator(hostEnvironment.ContentRootPath));
            Assert.Equal(Path.GetFullPath(Path.Combine(projectRoot, "wwwroot")),
                Path.TrimEndingDirectorySeparator(hostEnvironment.WebRootPath));
        }
        else
        {
            Assert.Equal(Path.GetFullPath(repositoryRoot), Path.TrimEndingDirectorySeparator(hostEnvironment.ContentRootPath));
        }
        // Host startup must retain the real owned encrypted-session provider, not a Testing bypass.
        Assert.Equal(environment.Equals("Development", StringComparison.OrdinalIgnoreCase)
            ? "Legacy.Maliev.Web.Development" : "Legacy.Maliev.Web",
            factory.Services.GetRequiredService<IOptions<DataProtectionOptions>>().Value.ApplicationDiscriminator);
        Assert.IsAssignableFrom<RedisCache>(factory.Services.GetRequiredService<IDistributedCache>());
        Assert.True(factory.Services.GetRequiredService<IConnectionMultiplexer>().IsConnected);
    }

    [Theory]
    [InlineData(new string[] { "--environment=Development" }, null, null, true)]
    [InlineData(new string[] { "--environment", "Development" }, null, null, true)]
    [InlineData(new string[] { "--ENVIRONMENT=development" }, null, null, true)]
    [InlineData(new string[] { "--environment=Development", "--environment=Production" }, null, null, false)]
    [InlineData(new string[] { "--environment=", "--environment=Development" }, "Development", "Development", true)]
    [InlineData(new string[] { "--environment=Staging" }, "Development", "Development", false)]
    [InlineData(new string[] { "--environment" }, "Development", null, true)]
    [InlineData(new string[] { "--environment", "--unrelated" }, "Development", null, false)]
    [InlineData(new string[] { }, "Development", "Production", false)]
    [InlineData(new string[] { }, "Production", "Development", true)]
    [InlineData(new string[] { }, "", "Development", true)]
    [InlineData(new string[] { }, null, "Development", true)]
    [InlineData(new string[] { }, null, null, false)]
    public void ContentRoot_UsesEffectiveFrameworkEnvironmentAndOnlyOverridesForDevelopmentFallback(
        string[] args, string? aspNetCore, string? dotnet, bool expectsProject)
    {
        var root = Path.Combine(Path.GetTempPath(), $"source659-{Guid.NewGuid():N}");
        var project = Path.Combine(root, "Legacy.Maliev.Web");
        try
        {
            Directory.CreateDirectory(Path.Combine(project, "wwwroot"));
            File.WriteAllText(Path.Combine(project, "appsettings.Development.json"), "{}");
            var actual = ResolveContentRoot(args, () => root, name => name switch
            {
                "ASPNETCORE_ENVIRONMENT" => aspNetCore,
                "DOTNET_ENVIRONMENT" => dotnet,
                "ASPNETCORE_CONTENTROOT" or "DOTNET_CONTENTROOT" => null,
                _ => throw new InvalidOperationException("Unexpected environment lookup."),
            });
            Assert.Equal(expectsProject ? project : null, actual);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(false, true, true, true)]
    [InlineData(true, true, true, false)]
    [InlineData(false, false, true, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, false, false)]
    public void SourceContentRoot_DevelopmentRequiresBothChildAssetsAndPreservesOwnSettings(
        bool ownSettings, bool childSettings, bool childWebRoot, bool expectsProject)
    {
        var root = Path.Combine(Path.GetTempPath(), $"source659-{Guid.NewGuid():N}");
        var project = Path.Combine(root, "Legacy.Maliev.Web");
        try
        {
            Directory.CreateDirectory(project);
            if (ownSettings) File.WriteAllText(Path.Combine(root, "appsettings.Development.json"), "{}");
            if (childSettings) File.WriteAllText(Path.Combine(project, "appsettings.Development.json"), "{}");
            if (childWebRoot) Directory.CreateDirectory(Path.Combine(project, "wwwroot"));
            var actual = ResolveContentRoot(["--environment=Development"], () => root,
                _ => null);
            Assert.Equal(expectsProject ? project : null, actual);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ContentRoot_ExplicitLastRootUsesBothArgumentFormsWithoutReadingTheCurrentDirectory(bool separated)
    {
        var root = Path.Combine(Path.GetTempPath(), $"source659-{Guid.NewGuid():N}");
        var project = Path.Combine(root, "Legacy.Maliev.Web");
        try
        {
            Directory.CreateDirectory(Path.Combine(project, "wwwroot"));
            File.WriteAllText(Path.Combine(project, "appsettings.Development.json"), "{}");
            string[] args = separated
                ? ["--CONTENTROOT", Path.Combine(root, "ignored"), "--contentRoot", root, "--environment=Development"]
                : [$"--CONTENTROOT={Path.Combine(root, "ignored")}", $"--contentRoot={root}", "--environment=Development"];
            Assert.Equal(project, ResolveContentRoot(args,
                () => throw new InvalidOperationException("Explicit root must not read the current directory."),
                _ => null));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ContentRoot_NonDevelopmentLeavesDanglingAndEmptyArgumentsToTheFramework()
    {
        Assert.Null(ResolveContentRoot(
            ["--environment=Production", "--contentRoot"],
            () => throw new InvalidOperationException("Non-Development must not resolve the working directory."),
            _ => null));
        Assert.Null(ResolveContentRoot(
            ["--contentRoot=", "--contentRoot=ignored", "--environment=Production"],
            () => throw new InvalidOperationException("Non-Development must not resolve the working directory."),
            _ => null));
    }

    [Theory]
    [InlineData("aspnet", false)]
    [InlineData("aspnet", true)]
    [InlineData("dotnet", false)]
    [InlineData("dotnet", true)]
    [InlineData("argument", false)]
    [InlineData("argument", true)]
    public void ContentRoot_EnvironmentRootsFollowFrameworkPrecedenceAndRelativeBase(string winner, bool relative)
    {
        var root = Path.Combine(Path.GetTempPath(), $"source659-{Guid.NewGuid():N}");
        var project = Path.Combine(root, "Legacy.Maliev.Web");
        try
        {
            Directory.CreateDirectory(Path.Combine(project, "wwwroot"));
            File.WriteAllText(Path.Combine(project, "appsettings.Development.json"), "{}");
            string selected = relative ? Path.GetRelativePath(AppContext.BaseDirectory, root) : root;
            string ignored = Path.Combine(root, "ignored");
            string[] args = winner == "argument"
                ? ["--environment=Development", $"--contentRoot={selected}"]
                : ["--environment=Development"];
            Assert.Equal(project, ResolveContentRoot(args,
                () => throw new InvalidOperationException("A configured root must not read the working directory."),
                name => name switch
                {
                    "ASPNETCORE_CONTENTROOT" => winner == "aspnet" ? selected : ignored,
                    "DOTNET_CONTENTROOT" => winner == "aspnet" ? null : winner == "dotnet" ? selected : ignored,
                    _ => null,
                }));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("Development", "Production", false)]
    [InlineData("Production", "Development", true)]
    public async Task ActualProgramBuilder_DuplicateEnvironmentsCannotActivateFallbackUnderProduction(
        string firstEnvironment, string effectiveEnvironment, bool expectsProject)
    {
        var root = Path.Combine(Path.GetTempPath(), $"source659-{Guid.NewGuid():N}");
        var project = Path.Combine(root, "Legacy.Maliev.Web");
        try
        {
            Directory.CreateDirectory(Path.Combine(project, "wwwroot"));
            File.WriteAllText(Path.Combine(project, "appsettings.Development.json"), "{}");
            string[] args = [$"--environment={firstEnvironment}", "--environment", effectiveEnvironment, $"--contentRoot={root}"];
            await using var app = Program.CreateApplicationBuilder(args).Build();
            Assert.Equal(effectiveEnvironment, app.Environment.EnvironmentName);
            var json = Assert.Single(((IConfigurationRoot)app.Configuration).Providers.OfType<JsonConfigurationProvider>(),
                provider => provider.Source.Path == "appsettings.json");
            var files = Assert.IsType<PhysicalFileProvider>(json.Source.FileProvider);
            Assert.Equal(expectsProject ? project : root, Path.TrimEndingDirectorySeparator(files.Root));
            Assert.Equal(expectsProject ? project : root, Path.TrimEndingDirectorySeparator(app.Environment.ContentRootPath));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActualProgramBuilder_DuplicateRootsUseTheLastArgumentWithoutReordering(bool separated)
    {
        var root = Path.Combine(Path.GetTempPath(), $"source659-{Guid.NewGuid():N}");
        var first = Path.Combine(root, "first");
        var selected = Path.Combine(root, "selected");
        var project = Path.Combine(selected, "Legacy.Maliev.Web");
        try
        {
            Directory.CreateDirectory(Path.Combine(first, "Legacy.Maliev.Web", "wwwroot"));
            File.WriteAllText(Path.Combine(first, "Legacy.Maliev.Web", "appsettings.Development.json"), "{}");
            Directory.CreateDirectory(Path.Combine(project, "wwwroot"));
            File.WriteAllText(Path.Combine(project, "appsettings.Development.json"), "{}");
            string[] args = separated
                ? ["--environment=Development", "--contentRoot", first, "--contentRoot", selected]
                : ["--environment=Development", $"--contentRoot={first}", $"--contentRoot={selected}"];
            string[] original = args.ToArray();
            await using var app = Program.CreateApplicationBuilder(args).Build();
            Assert.Equal(original, args);
            Assert.Equal("Development", app.Environment.EnvironmentName);
            var json = Assert.Single(((IConfigurationRoot)app.Configuration).Providers.OfType<JsonConfigurationProvider>(),
                provider => provider.Source.Path == "appsettings.json");
            Assert.Equal(project, Path.TrimEndingDirectorySeparator(
                Assert.IsType<PhysicalFileProvider>(json.Source.FileProvider).Root));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static string? ResolveContentRoot(string[] args, Func<string> getCurrentDirectory,
        Func<string, string?> getEnvironmentVariable)
    {
        using var configuration = new ConfigurationManager();
        foreach (var prefix in new[] { "ASPNETCORE_", "DOTNET_" })
        {
            configuration.AddInMemoryCollection(new[]
            {
                new KeyValuePair<string, string?>("environment", getEnvironmentVariable(prefix + "ENVIRONMENT")),
                new KeyValuePair<string, string?>("contentRoot", getEnvironmentVariable(prefix + "CONTENTROOT")),
            }.Where(pair => pair.Value is not null));
        }
        configuration.AddCommandLine(args);
        return Program.ResolveContentRoot(configuration, getCurrentDirectory);
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

    public WebApplicationFactory<Program> CreateFactory(string environment, string? contentRoot = null) =>
        new StartupWebFactory(environment, redis.GetConnectionString(), certificatePfxBase64, contentRoot);

    private sealed class StartupWebFactory(string environment, string connection, string certificate, string? contentRoot)
        : TestingWebApplicationFactory
    {
        protected override IHost CreateHost(IHostBuilder builder)
        {
            if (contentRoot is not null)
            {
                // The pinned MVC DeferredHostBuilder turns these into actual entry-point
                // --environment= and --contentRoot= arguments before Program executes.
                builder.ConfigureHostConfiguration(configuration => configuration.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        [HostDefaults.EnvironmentKey] = environment,
                        [HostDefaults.ContentRootKey] = contentRoot,
                    }));
            }
            return base.CreateHost(builder);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseEnvironment(environment);
            if (contentRoot is not null)
            {
                // Preserve the explicit non-Development root after MVC's own test-root
                // override. The independent JSON-provider assertion above remains primary.
                if (!environment.Equals("Development", StringComparison.OrdinalIgnoreCase))
                    builder.UseContentRoot(contentRoot);
                builder.UseSetting("CORS:AllowedOrigins:0", "https://source659.invalid");
                builder.UseSetting("Services:File", "https://file-source659.invalid");
            }
            builder.UseSetting("ConnectionStrings:redis", connection);
            builder.UseSetting("DataProtection:CertificatePfxBase64", certificate);
            builder.UseSetting("DataProtection:CertificatePassword", CertificatePassword);
            builder.UseSetting("BlazorRouting:KnowledgesIndex", "true");
            builder.UseSetting("Logging:LogLevel:Default", "None");
        }
    }
}
