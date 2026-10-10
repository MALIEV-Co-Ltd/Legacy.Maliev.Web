using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Sockets;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using Legacy.Maliev.Web.Infrastructure;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;
using Npgsql;
using StackExchange.Redis;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Real pinned Auth login, durable sessions, normal encrypted Web cookies and both member renderers.</summary>
public sealed class AuthenticatedMemberCrawlPolicyHttpTests(MemberAuthorityFixture authority)
    : IClassFixture<MemberAuthorityFixture>
{
    [Theory]
    [InlineData("en", false)]
    [InlineData("th", false)]
    [InlineData("en", true)]
    [InlineData("th", true)]
    public async Task NormalLogin_ActiveAndRetainedMemberAccountHaveExactlyOneDirective(string culture, bool retained)
    {
        await using var host = authority.Web(retained);
        using var client = Client(host);
        var lineage = await Login(host, client);
        using var response = await client.GetAsync($"/Member/Account?culture={culture}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.True(html.Contains($"<html lang=\"{culture}\"", StringComparison.Ordinal), "The requested member culture must render.");
        Assert.True(html.Contains("data-migration-component=\"member-account-index-content\"", StringComparison.Ordinal), "The authorized member content must render.");
        Assert.Equal(!retained, html.Contains("data-migration-route-owner=\"blazor-static-ssr\"", StringComparison.Ordinal));
        AssertHeader(response);
        var tags = Regex.Matches(html, "<meta\\b[^>]*>", RegexOptions.IgnoreCase)
            .Where(tag => Regex.IsMatch(tag.Value, "\\bname\\s*=\\s*[\"']robots[\"']", RegexOptions.IgnoreCase)).ToArray();
        Assert.True(tags.Length == 1, "Exactly one member robots metadata directive must render.");
        var tag = tags[0];
        var directive = Regex.Match(tag.Value, "\\bcontent\\s*=\\s*[\"']([^\"']*)[\"']", RegexOptions.IgnoreCase);
        Assert.True(directive.Groups[1].Value.Replace(" ", "", StringComparison.Ordinal) == "noindex,follow", "The member metadata must preserve noindex/follow.");
        Assert.False(html.Contains(lineage.Session.AccessToken, StringComparison.Ordinal), "Member HTML must not disclose an access credential.");
        Assert.False(html.Contains(lineage.Session.RefreshToken, StringComparison.Ordinal), "Member HTML must not disclose a refresh credential.");
        Assert.False(html.Contains("access_token", StringComparison.OrdinalIgnoreCase), "Member HTML must not disclose an access-token field.");
        Assert.False(html.Contains("refresh_token", StringComparison.OrdinalIgnoreCase), "Member HTML must not disclose a refresh-token field.");
    }

    [Theory]
    [InlineData("/membership")]
    [InlineData("/member-account")]
    public async Task AuthenticatedNonMemberPrefix_DoesNotAcquireMemberHeader(string path)
    {
        await using var host = authority.Web(false);
        using var client = Client(host);
        await Login(host, client);
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False(response.Headers.Contains("X-Robots-Tag"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnonymousMember_RemainsProtectedInBothRenderers(bool retained)
    {
        await using var host = authority.Web(retained);
        using var client = Client(host);
        await AssertChallenge(client);
    }

    [Fact]
    public async Task TamperedNormalCookie_CannotRenderMemberContent()
    {
        await using var host = authority.Web(false);
        using var client = Client(host);
        var lineage = await Login(host, client);
        using var tampered = host.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = false,
        });
        var cookie = lineage.Cookie;
        var index = cookie.Length / 2;
        var changed = cookie[..index] + (cookie[index] == 'A' ? 'B' : 'A') + cookie[(index + 1)..];
        tampered.DefaultRequestHeaders.Add("Cookie", $"__Host-Maliev.Legacy.Session={changed}");
        await AssertChallenge(tampered);
    }

    [Fact]
    public async Task MissingRedisSession_RejectsGenuinePreviouslyIssuedCookie()
    {
        await using var host = authority.Web(false);
        using var client = Client(host);
        var lineage = await Login(host, client);
        await host.Services.GetRequiredService<IAccountSessionStore>().RemoveAsync(lineage.WebSessionId, default);
        Assert.Null(await host.Services.GetRequiredService<IAccountSessionStore>().GetAsync(lineage.WebSessionId, default));
        await AssertChallenge(client);
    }

    [Fact]
    public async Task NormalAntiforgeryLogout_RevokesRealSessionAndRestoresChallenge()
    {
        await using var host = authority.Web(false);
        using var client = Client(host);
        var lineage = await Login(host, client);
        var form = await Form(client, "/Account/Logout?culture=en");
        using var response = await client.PostAsync("/Account/Logout", new FormUrlEncodedContent(form));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Null(await host.Services.GetRequiredService<IAccountSessionStore>().GetAsync(lineage.WebSessionId, default));
        await authority.AssertRevoked(lineage.AuthSessionId);
        await AssertChallenge(client);
    }

    [Fact]
    public void RequiredCi_PreparesDistinctPinnedProofBeforeBrowserBuild()
    {
        var yaml = File.ReadAllText(Path.Combine(Root(), ".github", "workflows", "_build-and-test.yml"));
        var step = yaml.IndexOf("run: ./scripts/prepare-member-auth-indexing-proof.ps1", StringComparison.Ordinal);
        Assert.True(step > 0);
        Assert.True(step < yaml.IndexOf("name: Build browser acceptance host", StringComparison.Ordinal));
        var previous = yaml.LastIndexOf("      - name:", step, StringComparison.Ordinal);
        Assert.DoesNotContain("if:", yaml[previous..(step + "run: ./scripts/prepare-member-auth-indexing-proof.ps1".Length)], StringComparison.Ordinal);
        Assert.Contains("run: ./scripts/prepare-profile-producer-boundary.ps1", yaml, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("database")]
    [InlineData("host")]
    [InlineData("pooling")]
    [InlineData("port")]
    public async Task SeedGuard_RejectsChangedAuthorityBeforeAnyOwnerMigration(string mutation) =>
        await authority.AssertSeedGuard(mutation);

    [Fact]
    public async Task ChildEnvironment_HostileAmbientSettingsCannotReachExecutableOrEnableDeployment() =>
        await authority.AssertChildEnvironmentGuard();

    private async Task<Lineage> Login(WebApplicationFactory<Program> host, HttpClient client)
    {
        var form = await Form(client, "/Account/Login");
        form["Email"] = "member-crawl@example.test";
        form["Password"] = authority.Password;
        form["RememberMe"] = "false";
        using var response = await client.PostAsync("/Account/Login?handler=Login", new FormUrlEncodedContent(form));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var sessionCookies = response.Headers.GetValues("Set-Cookie").Where(
            value => value.StartsWith("__Host-Maliev.Legacy.Session=", StringComparison.Ordinal)).ToArray();
        Assert.True(sessionCookies.Length == 1, "Exactly one normal session cookie must be issued.");
        var cookieHeader = sessionCookies[0];
        Assert.True(cookieHeader.Contains("secure", StringComparison.OrdinalIgnoreCase), "Normal session cookie must remain Secure.");
        Assert.True(cookieHeader.Contains("httponly", StringComparison.OrdinalIgnoreCase), "Normal session cookie must remain HttpOnly.");
        Assert.True(cookieHeader.Contains("samesite=lax", StringComparison.OrdinalIgnoreCase), "Normal session cookie must preserve SameSite=Lax.");
        var cookie = cookieHeader.Split(';')[0]["__Host-Maliev.Legacy.Session=".Length..];
        var options = host.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(CookieAuthenticationDefaults.AuthenticationScheme);
        var ticket = options.TicketDataFormat.Unprotect(cookie);
        Assert.NotNull(ticket);
        Assert.Equal("customer:1", ticket.Principal.FindFirstValue(ClaimTypes.NameIdentifier));
        var webSessionId = ticket.Principal.FindFirstValue(AccountSessionManager.SessionIdClaim);
        Assert.False(string.IsNullOrWhiteSpace(webSessionId));
        Assert.IsAssignableFrom<RedisCache>(host.Services.GetRequiredService<IDistributedCache>());
        var session = await host.Services.GetRequiredService<IAccountSessionStore>().GetAsync(webSessionId!, default);
        Assert.NotNull(session);
        Assert.Equal(1, session.CustomerDatabaseId);
        Assert.True(await host.Services.GetRequiredService<IConnectionMultiplexer>().GetDatabase()
            .KeyExistsAsync("legacy:web:" + DistributedAccountSessionStore.CacheKeyPrefix + webSessionId));
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(session.AccessToken);
        Assert.Equal("https://member-auth.example.test", jwt.Issuer);
        Assert.Equal("member-web-tests", Assert.Single(jwt.Audiences));
        Assert.Equal("member-crawl-customer", Assert.Single(jwt.Claims, claim => claim.Type == "sub").Value);
        Assert.DoesNotContain(jwt.Claims, claim => claim.Type == "sid");
        var sid = await authority.AssertActive(session.RefreshToken);
        return new(webSessionId!, sid, cookie, session);
    }

    private static async Task<Dictionary<string, string>> Form(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"", RegexOptions.CultureInvariant);
        Assert.True(match.Success);
        return new() { ["__RequestVerificationToken"] = WebUtility.HtmlDecode(match.Groups[1].Value) };
    }

    private static async Task AssertChallenge(HttpClient client)
    {
        using var response = await client.GetAsync("/Member/Account?culture=en");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Account/Login", response.Headers.Location?.AbsolutePath);
        AssertHeader(response);
        Assert.False((await response.Content.ReadAsStringAsync()).Contains("member-account-index-content", StringComparison.Ordinal), "Unauthenticated requests must not render private member content.");
    }

    private static void AssertHeader(HttpResponseMessage response) =>
        Assert.Equal("noindex, follow", Assert.Single(response.Headers.GetValues("X-Robots-Tag")));

    private static HttpClient Client(WebApplicationFactory<Program> host) => host.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
    });

    private static string Root()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Web.slnx"))) return directory.FullName;
        throw new DirectoryNotFoundException("Owned workspace not found.");
    }

    private sealed record Lineage(string WebSessionId, Guid AuthSessionId, string Cookie, AccountSession Session);
}

/// <summary>Owns only disposable PG18/Redis and a bounded exact-pinned Auth child; no authentication replacement.</summary>
public sealed class MemberAuthorityFixture : IAsyncLifetime
{
    private readonly string run = Guid.NewGuid().ToString("N");
    private readonly PostgreSqlContainer postgres;
    private readonly RedisContainer redis;
    private readonly RSA rsa;
    private readonly string certificatePassword;
    private sealed class OwnedChild(NativeMemberAuthorityProcess backend, CompanyOwnedProcess owner)
    {
        public NativeMemberAuthorityProcess Backend { get; } = backend;
        public CompanyOwnedProcess Owner { get; } = owner;
        public bool Released { get; set; }
    }
    private readonly List<OwnedChild> children = [];
    private readonly TaskCompletionSource initializationSettled = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly MemberOwnedContainers containerOwner;
    private bool postgresCreationAttempted;
    private bool redisCreationAttempted;
    private bool redisReleased;
    private bool postgresReleased;
    private bool lifetimeReleased;
    private readonly MemberHostOwnership hosts = new();
    private readonly SemaphoreSlim cleanupGate = new(1, 1);
    private readonly CancellationTokenSource lease;
    private Task expiryCleanup = Task.CompletedTask;
    private bool cleanupCompleted;
    private Exception? expiryCleanupFailure;

    public MemberAuthorityFixture()
    {
        NativeMemberAuthorityProcess.Preflight(); // Before RSA, live CTS, or container builder allocation.
        RSA? partialRsa = null;
        CancellationTokenSource? partialLease = null;
        MemberOwnedContainers? partialOwner = null;
        PostgreSqlContainer? partialPostgres = null;
        RedisContainer? partialRedis = null;
        try
        {
        rsa = partialRsa = RSA.Create(2048);
        certificatePassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        Password = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)) + "!a";
        lease = partialLease = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var expiry = DateTimeOffset.UtcNow.AddMinutes(3).ToString("O");
        containerOwner = partialOwner = new MemberOwnedContainers(run);
        postgres = partialPostgres = new PostgreSqlBuilder("postgres:18-alpine")
            .WithDockerEndpoint(MemberOwnedContainers.Endpoint)
            .WithLabel("maliev.task", "web-476-member-authority")
            .WithLabel("maliev.run", run).WithLabel("maliev.disposable", "true")
            .WithLabel("maliev.expiresUtc", expiry)
            .WithCreateParameterModifier(value =>
            {
                value.Entrypoint = ["sh", "-c"];
                value.Cmd = ["exec timeout -s TERM -k 5 180 docker-entrypoint.sh postgres"];
                MemberOwnedContainers.Cap(value, 512L * 1024 * 1024, "/var/lib/postgresql", 256 * 1024 * 1024);
            })
            .Build();
        redis = partialRedis = new RedisBuilder("redis:8.4-alpine")
            .WithDockerEndpoint(MemberOwnedContainers.Endpoint)
            .WithLabel("maliev.task", "web-476-member-authority")
            .WithLabel("maliev.run", run).WithLabel("maliev.disposable", "true")
            .WithLabel("maliev.expiresUtc", expiry)
            .WithCreateParameterModifier(value =>
            {
                value.Entrypoint = ["sh", "-c"];
                value.Cmd = ["exec timeout -s TERM -k 5 180 docker-entrypoint.sh redis-server --save '' --appendonly no"];
                MemberOwnedContainers.Cap(value, 128L * 1024 * 1024, "/data", 64 * 1024 * 1024);
            })
            .Build();
        }
        catch (Exception construction)
        {
            var failures = new List<Exception> { construction };
            try { partialRedis?.DisposeAsync().AsTask().GetAwaiter().GetResult(); } catch (Exception error) { failures.Add(error); }
            try { partialPostgres?.DisposeAsync().AsTask().GetAwaiter().GetResult(); } catch (Exception error) { failures.Add(error); }
            try { partialOwner?.Dispose(); } catch (Exception error) { failures.Add(error); }
            try { partialLease?.Dispose(); } catch (Exception error) { failures.Add(error); }
            try { partialRsa?.Dispose(); } catch (Exception error) { failures.Add(error); }
            throw new AggregateException("Member fixture partial construction failed and all exact allocations were attempted for release.", failures);
        }
    }

    private async Task ExpireAsync()
    {
        try { await Task.Delay(Timeout.InfiniteTimeSpan, lease.Token); }
        catch (OperationCanceledException) when (lease.IsCancellationRequested) { }
        try
        {
            await initializationSettled.Task.WaitAsync(TimeSpan.FromSeconds(30));
            await ReleaseOwnedAsync();
        }
        catch (Exception error)
        {
            expiryCleanupFailure = error;
            Console.WriteLine("[member-owned-expiry] Cleanup incomplete; exact ownership retained for retry.");
        }
    }

    private readonly Dictionary<string, string> connections = [];
    private string certificate = "";
    private Uri origin = null!;
    public string Password { get; }

    public async Task InitializeAsync()
    {
        var authDll = Environment.GetEnvironmentVariable("MALIEV_MEMBER_AUTH_DLL");
        var seedDll = Environment.GetEnvironmentVariable("MALIEV_MEMBER_AUTH_SEED_DLL");
        try
        {
            Assert.True(File.Exists(authDll) && File.Exists(seedDll), "Run the mandatory private member proof preparation script before building/testing this lane.");
            NativeMemberAuthorityProcess.Preflight();
            await containerOwner.PreflightAsync();
            expiryCleanup = ExpireAsync();
            lease.Token.ThrowIfCancellationRequested();
            postgresCreationAttempted = true;
            await postgres.StartAsync(lease.Token);
            await containerOwner.CaptureAsync(postgres.Id, "postgres:18-alpine", 5432);
            lease.Token.ThrowIfCancellationRequested();
            redisCreationAttempted = true;
            await redis.StartAsync(lease.Token);
            await containerOwner.CaptureAsync(redis.Id, "redis:8.4-alpine", 6379);
            Console.WriteLine("[member-owned-containers] " + System.Text.Json.JsonSerializer.Serialize(new
            {
                run, postgresId = postgres.Id, redisId = redis.Id,
                postgresPort = postgres.GetMappedPublicPort(5432), redisPort = redis.GetMappedPublicPort(6379),
                observedStartedUtc = DateTimeOffset.UtcNow, leaseSeconds = 180, persistentData = false,
            }));
            var containerConnection = new NpgsqlConnectionStringBuilder(postgres.GetConnectionString()) { Pooling = false };
            await using (var admin = new NpgsqlConnection(containerConnection.ConnectionString))
            {
                await admin.OpenAsync(lease.Token);
                foreach (var kind in new[] { "customer", "employee", "sessions" })
                {
                    var database = $"member_{kind}_{run}";
                    await using var command = new NpgsqlCommand($"CREATE DATABASE \"{database}\"", admin);
                    await command.ExecuteNonQueryAsync(lease.Token);
                    connections[kind] = new NpgsqlConnectionStringBuilder(containerConnection.ConnectionString) { Database = database, Pooling = false }.ConnectionString;
                }
            }
            // Values are built exclusively from this started Testcontainer, not ambient connection configuration.
            var seedEnvironment = new Dictionary<string, string>
            {
                ["MALIEV_MEMBER_RUN"] = run,
                ["MALIEV_MEMBER_CONTAINER"] = postgres.Id,
                ["MALIEV_MEMBER_HOST"] = containerConnection.Host ?? throw new InvalidOperationException("Missing Testcontainer host."),
                ["MALIEV_MEMBER_PORT"] = containerConnection.Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["MALIEV_MEMBER_PASSWORD"] = Password,
            };
            foreach (var kind in connections.Keys) seedEnvironment["MALIEV_MEMBER_" + kind.ToUpperInvariant() + "_CONNECTION"] = connections[kind];
            {
                var seed = await ChildAsync(seedDll!, seedEnvironment);
                await seed.WaitAsync(lease.Token).WaitAsync(TimeSpan.FromSeconds(90));
                Assert.Equal(0, seed.ExitCode);
            }
            var certificateRequest = new CertificateRequest("CN=member-proof", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var x509 = certificateRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(2));
            certificate = Convert.ToBase64String(x509.Export(X509ContentType.Pfx, certificatePassword));
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            origin = new Uri($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}");
            listener.Stop();
            var auth = await ChildAsync(authDll!, new()
            {
                ["DOTNET_ENVIRONMENT"] = "Development",
                ["ASPNETCORE_ENVIRONMENT"] = "Development",
                ["ASPNETCORE_URLS"] = origin.ToString(),
                ["ConnectionStrings__CustomerIdentity"] = connections["customer"],
                ["ConnectionStrings__EmployeeIdentity"] = connections["employee"],
                ["ConnectionStrings__RefreshSessions"] = connections["sessions"],
                ["Jwt__PrivateKeyPem"] = rsa.ExportPkcs8PrivateKeyPem(),
                ["Jwt__KeyId"] = "member-disposable",
                ["Jwt__Issuer"] = "https://member-auth.example.test",
                ["Jwt__Audience"] = "member-web-tests",
                ["Logging__LogLevel__Default"] = "None",
            });
            using var http = new HttpClient { BaseAddress = origin, Timeout = TimeSpan.FromSeconds(2) };
            var deadline = DateTimeOffset.UtcNow.AddSeconds(45);
            while (true)
            {
                Assert.False(auth.HasExited, "Owned Auth producer terminated during startup; output remains suppressed to protect credentials.");
                try
                {
                    using var response = await http.GetAsync("auth/v1/customer-self-service/identity", lease.Token);
                    if (response.StatusCode == HttpStatusCode.Unauthorized) break;
                }
                catch (HttpRequestException) { }
                catch (TaskCanceledException) { }
                Assert.True(DateTimeOffset.UtcNow < deadline, "Owned Auth producer startup did not complete.");
                await Task.Delay(100, lease.Token);
            }
        }
        catch (Exception initializationFailure)
        {
            initializationSettled.TrySetResult();
            try { await DisposeAsync(); }
            catch (Exception cleanupFailure) { throw new AggregateException("Member fixture initialization and exact-owned cleanup failed.", initializationFailure, cleanupFailure); }
            throw;
        }
        finally { initializationSettled.TrySetResult(); }
    }

    public WebApplicationFactory<Program> Web(bool retained) => hosts.RegisterFactory(
        () => new MemberWebFactory(origin, redis.GetConnectionString(), certificate, certificatePassword, retained, lease.Token, hosts), lease.Token);

    public async Task<Guid> AssertActive(string refreshToken)
    {
        await using var connection = new NpgsqlConnection(connections["sessions"]);
        await connection.OpenAsync(lease.Token);
        await using var command = new NpgsqlCommand("SELECT \"IdentityId\", \"IdentityKind\", \"SecurityStamp\", \"FamilyId\", \"ExpiresAt\", \"RevokedAt\", \"RotatedAt\", \"Id\" FROM refresh_sessions WHERE \"TokenHash\"=@hash", connection);
        command.Parameters.AddWithValue("hash", Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(refreshToken))));
        await using var row = await command.ExecuteReaderAsync(lease.Token);
        Assert.True(await row.ReadAsync(lease.Token));
        Assert.Equal("member-crawl-customer", row.GetString(0));
        Assert.Equal(0, row.GetInt32(1));
        Assert.False(string.IsNullOrWhiteSpace(row.GetString(2)));
        Assert.NotEqual(Guid.Empty, row.GetGuid(3));
        Assert.True(row.GetFieldValue<DateTimeOffset>(4) > DateTimeOffset.UtcNow);
        Assert.True(row.IsDBNull(5) && row.IsDBNull(6));
        var stamp = row.GetString(2);
        var sid = row.GetGuid(7);
        Assert.NotEqual(Guid.Empty, sid);
        Assert.False(await row.ReadAsync(lease.Token));
        await using var identity = new NpgsqlConnection(connections["customer"]);
        await identity.OpenAsync(lease.Token);
        await using var user = new NpgsqlCommand("SELECT count(*) FROM \"AspNetUsers\" WHERE \"Id\"='member-crawl-customer' AND \"DatabaseID\"=1 AND \"EmailConfirmed\" AND \"PasswordHash\" IS NOT NULL AND \"SecurityStamp\"=@stamp", identity);
        user.Parameters.AddWithValue("stamp", stamp);
        Assert.Equal(1L, await user.ExecuteScalarAsync(lease.Token));
        return sid;
    }

    public async Task AssertRevoked(Guid sid)
    {
        await using var connection = new NpgsqlConnection(connections["sessions"]);
        await connection.OpenAsync(lease.Token);
        await using var command = new NpgsqlCommand("SELECT \"RevokedAt\" IS NOT NULL FROM refresh_sessions WHERE \"Id\"=@sid", connection);
        command.Parameters.AddWithValue("sid", sid);
        Assert.Equal(true, await command.ExecuteScalarAsync(lease.Token));
    }

    public async Task AssertSeedGuard(string mutation)
    {
        var guardRun = Guid.NewGuid().ToString("N");
        var container = new NpgsqlConnectionStringBuilder(postgres.GetConnectionString()) { Pooling = false };
        var guarded = new Dictionary<string, string>();
        await using (var admin = new NpgsqlConnection(container.ConnectionString))
        {
            await admin.OpenAsync(lease.Token);
            foreach (var kind in new[] { "customer", "employee", "sessions" })
            {
                var database = $"member_{kind}_{guardRun}";
                await using var create = new NpgsqlCommand($"CREATE DATABASE \"{database}\"", admin);
                await create.ExecuteNonQueryAsync(lease.Token);
                guarded[kind] = new NpgsqlConnectionStringBuilder(container.ConnectionString) { Database = database }.ConnectionString;
            }
        }
        var changed = new NpgsqlConnectionStringBuilder(guarded["sessions"]);
        switch (mutation)
        {
            case "database": changed.Database += "\"escaped"; break;
            case "host": changed.Host = "192.0.2.1"; break;
            case "pooling": changed.Pooling = true; break;
            case "port": changed.Port = changed.Port == 65535 ? 65534 : changed.Port + 1; break;
            default: throw new InvalidOperationException("Unknown fixture mutation.");
        }
        var environment = new Dictionary<string, string>
        {
            ["MALIEV_MEMBER_RUN"] = guardRun,
            ["MALIEV_MEMBER_CONTAINER"] = postgres.Id,
            ["MALIEV_MEMBER_HOST"] = container.Host!,
            ["MALIEV_MEMBER_PORT"] = container.Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["MALIEV_MEMBER_PASSWORD"] = Password,
        };
        foreach (var kind in guarded.Keys) environment["MALIEV_MEMBER_" + kind.ToUpperInvariant() + "_CONNECTION"] = kind == "sessions" ? changed.ConnectionString : guarded[kind];
        var seed = await ChildAsync(Environment.GetEnvironmentVariable("MALIEV_MEMBER_AUTH_SEED_DLL")!, environment);
        await seed.WaitAsync(lease.Token).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.NotEqual(0, seed.ExitCode);
        foreach (var value in guarded.Values)
        {
            await using var target = new NpgsqlConnection(value);
            await target.OpenAsync(lease.Token);
            await using var unchanged = new NpgsqlCommand("SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public' AND c.relkind IN ('r','p','S')", target);
            Assert.Equal(0L, await unchanged.ExecuteScalarAsync(lease.Token));
        }
    }

    public async Task AssertChildEnvironmentGuard()
    {
        var hostile = new Dictionary<string, string>
        {
            ["ConnectionStrings__CustomerIdentity"] = "Host=192.0.2.1;Database=never-opened;Username=synthetic",
            ["Jwt__PrivateKeyPem"] = "hostile-parent-never-used",
            ["Brevo__ApiKey"] = "hostile-parent-never-used",
            ["GOOGLE_APPLICATION_CREDENTIALS"] = "never-opened-parent-path",
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "http://192.0.2.1:4318",
            ["LEGACY_DEPLOY_ENABLED"] = "true",
        };
        // Inject only into this fresh child's inherited dictionary, not global
        // process environment shared with concurrently running test classes.
        var probe = await ChildAsync(Environment.GetEnvironmentVariable("MALIEV_MEMBER_AUTH_SEED_DLL")!,
            new() { ["MALIEV_MEMBER_ENV_PROBE"] = "1" }, hostile);
        await probe.WaitAsync(lease.Token).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True(probe.ExitCode == 0, "Executable child must observe only explicitly allowed environment and deployment disabled.");
    }

    private async Task<NativeMemberAuthorityProcess> ChildAsync(string dll, Dictionary<string, string> environment, Dictionary<string, string>? hostileAmbient = null)
    {
        await cleanupGate.WaitAsync(lease.Token);
        try
        {
            lease.Token.ThrowIfCancellationRequested();
            if (cleanupCompleted) throw new InvalidOperationException("Member fixture was already released.");
            var backend = new NativeMemberAuthorityProcess(dll, environment, hostileAmbient);
            var owner = new CompanyOwnedProcess(backend, CompanyProcessBudgets.Normal);
            children.Add(new OwnedChild(backend, owner)); // Register before any spawn.
            try { backend.Start(); backend.CaptureOwnership(); backend.BeginDrains(); }
            catch (Exception startup)
            {
                try { await owner.DisposeAsync(); }
                catch (Exception cleanup) { throw new AggregateException("Owned member startup and cleanup failed.", startup, cleanup); }
                throw;
            }
            return backend;
        }
        finally { cleanupGate.Release(); }
    }

    public async Task DisposeAsync()
    {
        if (lifetimeReleased) return;
        lease.Cancel();
        await initializationSettled.Task.WaitAsync(TimeSpan.FromSeconds(30));
        var failures = new List<Exception>();
        if (expiryCleanupFailure is not null)
            Console.WriteLine("[member-owned-cleanup] Retrying the earlier failed expiry cleanup with retained exact ownership.");
        try { await ReleaseOwnedAsync(); } catch (Exception error) { failures.Add(error); }
        try { await expiryCleanup.WaitAsync(TimeSpan.FromSeconds(240)); } catch (Exception error) { failures.Add(error); }
        // Retry is not permanently poisoned by an earlier failed expiry attempt.
        if (failures.Count == 0 && cleanupCompleted)
        {
            expiryCleanupFailure = null;
            lease.Dispose();
            lifetimeReleased = true;
        }
        if (failures.Count != 0) throw new AggregateException("Member fixture cleanup incomplete; exact ownership retained.", failures);
    }

    private async Task ReleaseOwnedAsync()
    {
        await cleanupGate.WaitAsync();
        try
        {
            if (cleanupCompleted) return;
            var failures = new List<Exception>();
            // Exact factories and their actual hosts/clients must settle before child or database teardown.
            await hosts.ReleaseAsync();
            foreach (var child in children.Where(child => !child.Released))
            {
                if (child.Backend.Released) { child.Released = true; continue; }
                try { await child.Owner.DisposeAsync(); child.Released = true; }
                catch (Exception error) { child.Released = child.Backend.Released; failures.Add(error); }
            }
            bool quiescent = true;
            foreach (var child in children)
            {
                try { if (!child.Backend.HasExited) quiescent = false; }
                catch (Exception error) { failures.Add(error); quiescent = false; }
            }
            if (!quiescent)
                throw new AggregateException("Member children not quiescent; both exact-owned containers preserved.", failures.Append(new InvalidOperationException("Child exit unverified.")));
            if (!redisReleased)
            {
                try { if (redisCreationAttempted) await containerOwner.ReleaseAsync(redis.Id, "redis:8.4-alpine", 6379); await redis.DisposeAsync(); redisReleased = true; }
                catch (Exception error) { failures.Add(error); }
            }
            if (!postgresReleased)
            {
                try { if (postgresCreationAttempted) await containerOwner.ReleaseAsync(postgres.Id, "postgres:18-alpine", 5432); await postgres.DisposeAsync(); postgresReleased = true; }
                catch (Exception error) { failures.Add(error); }
            }
            cleanupCompleted = failures.Count == 0 && redisReleased && postgresReleased;
            if (cleanupCompleted) { rsa.Dispose(); containerOwner.Dispose(); }
            if (failures.Count != 0) throw new AggregateException("Member exact-owned cleanup incomplete; unreleased entries retained for retry.", failures);
        }
        finally { cleanupGate.Release(); }
    }

    private sealed class MemberWebFactory(Uri auth, string redis, string certificate, string password, bool retained,
        CancellationToken fixtureLease, MemberHostOwnership ownership) : WebApplicationFactory<Program>
    {
        protected override IHost CreateHost(IHostBuilder builder) => ownership.BuildAndStartHost(builder, fixtureLease);

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            fixtureLease.ThrowIfCancellationRequested();
            builder.UseEnvironment("Development");
            builder.UseSetting("Services:Auth", auth.ToString());
            builder.UseSetting("Recaptcha:ProjectId", "member-proof-no-provider");
            builder.UseSetting("Recaptcha:SiteKey", "member-proof-no-provider");
            builder.UseSetting("ConnectionStrings:redis", redis);
            builder.UseSetting("DataProtection:CertificatePfxBase64", certificate);
            builder.UseSetting("DataProtection:CertificatePassword", password);
            builder.UseSetting("BlazorRouting:MemberAccountIndex", (!retained).ToString());
            builder.UseSetting("Logging:LogLevel:Default", "None");
        }
    }
}
