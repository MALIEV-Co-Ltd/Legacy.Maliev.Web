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
using Npgsql;
using StackExchange.Redis;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Real pinned Auth login, durable sessions, normal encrypted Web cookies and both member renderers.</summary>
public sealed class AuthenticatedMemberCrawlPolicyHttpTests : IAsyncLifetime
{
    private readonly MemberAuthorityFixture authority = new();

    public Task InitializeAsync() => authority.InitializeAsync();

    public Task DisposeAsync() => authority.DisposeAsync();

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

    [Theory]
    [InlineData(false, "EN", "/member?tracking=excluded", "en", "/member?culture=en")]
    [InlineData(true, "EN", "/member?tracking=excluded", "en", "/member?culture=en")]
    [InlineData(false, "not-a-culture", "/member?culture=en", "th", "/member")]
    [InlineData(true, "not-a-culture", "/member?culture=en", "th", "/member")]
    [InlineData(false, null, "/member", "th", "/member")]
    [InlineData(true, null, "/member", "th", "/member")]
    [InlineData(false, "en", "https://attacker.example/path", "en", "~/?culture=en")]
    [InlineData(true, "en", "https://attacker.example/path", "en", "~/?culture=en")]
    public async Task NormalLogin_MemberLanguagePostPreservesCultureCookieAndLocalRedirect(
        bool active, string? culture, string returnUrl, string expectedCulture, string expectedReturn)
    {
        await using var parent = authority.Web(false);
        await using var host = parent.WithWebHostBuilder(builder =>
            builder.UseSetting("BlazorRouting:MemberOverview", active.ToString()));
        using var client = Client(host);
        client.Timeout = TimeSpan.FromSeconds(30);
        var lineage = await Login(host, client);
        var form = await Form(client, "/Account/Logout?culture=en");
        if (culture is not null) form["culture"] = culture;
        form["returnUrl"] = returnUrl;

        using var response = await client.PostAsync("/Member?handler=SetLanguage", new FormUrlEncodedContent(form));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var expectedLocation = expectedReturn.StartsWith("~/", StringComparison.Ordinal)
            ? expectedReturn[1..] : expectedReturn;
        Assert.Equal(expectedLocation, response.Headers.Location?.OriginalString);
        var cookies = response.Headers.GetValues("Set-Cookie").Where(value =>
            value.StartsWith(".AspNetCore.Culture=", StringComparison.Ordinal)).ToArray();
        var cookie = Assert.Single(cookies);
        Assert.Contains($"c%3D{expectedCulture}%7Cuic%3D{expectedCulture}", cookie, StringComparison.Ordinal);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(await host.Services.GetRequiredService<IAccountSessionStore>()
            .GetAsync(lineage.WebSessionId, default));
        await authority.AssertActive(lineage.Session.RefreshToken);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NormalLogin_MemberLanguagePostWithoutAntiforgeryIsRejected(bool active)
    {
        await using var parent = authority.Web(false);
        await using var host = parent.WithWebHostBuilder(builder =>
            builder.UseSetting("BlazorRouting:MemberOverview", active.ToString()));
        using var client = Client(host);
        client.Timeout = TimeSpan.FromSeconds(30);
        await Login(host, client);

        using var response = await client.PostAsync("/Member?handler=SetLanguage",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["culture"] = "en",
                ["returnUrl"] = "/member",
            }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(response.Headers.TryGetValues("Set-Cookie", out var cookies)
            && cookies.Any(value => value.StartsWith(".AspNetCore.Culture=", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnonymousMemberLanguagePostRequiresAuthorization(bool active)
    {
        await using var parent = authority.Web(false);
        await using var host = parent.WithWebHostBuilder(builder =>
            builder.UseSetting("BlazorRouting:MemberOverview", active.ToString()));
        using var client = Client(host);
        client.Timeout = TimeSpan.FromSeconds(30);
        var form = await Form(client, "/Account/Login");
        form["culture"] = "en";
        form["returnUrl"] = "/member";

        using var response = await client.PostAsync("/Member?handler=SetLanguage", new FormUrlEncodedContent(form));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Account/Login", response.Headers.Location?.AbsolutePath);
        Assert.False(response.Headers.TryGetValues("Set-Cookie", out var cookies)
            && cookies.Any(value => value.StartsWith(".AspNetCore.Culture=", StringComparison.Ordinal)));
    }

    /// <summary>Checks genuine Development serves the rendered owned stylesheet and both Font Awesome WOFF2 faces with their exact bytes.</summary>
    /// <returns>A task completing after real Development page, CSS and font HTTP assertions.</returns>
    [Fact]
    public async Task DevelopmentPrintingPage_ServesOwnedFontAwesomeCssAndBothWoff2Faces()
    {
        await using var host = authority.Web(false);
        using var client = Client(host);
        Assert.Equal("Development", host.Services.GetRequiredService<IWebHostEnvironment>().EnvironmentName);
        using var page = await client.GetAsync("/services/3d-printing?culture=en");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var html = await page.Content.ReadAsStringAsync();
        Assert.Contains("data-migration-route-owner=\"blazor-static-ssr\"", html, StringComparison.Ordinal);
        var stylesheet = Assert.Single(Regex.Matches(html,
            @"href=""(?<href>/dist/site\.min\.css\?v=[^""]+)""", RegexOptions.CultureInvariant));
        var stylesheetUri = new Uri(client.BaseAddress!, WebUtility.HtmlDecode(stylesheet.Groups["href"].Value));
        Assert.Equal(client.BaseAddress!.Authority, stylesheetUri.Authority);
        Assert.Equal(client.BaseAddress.Scheme, stylesheetUri.Scheme);
        Assert.Equal("/dist/site.min.css", stylesheetUri.AbsolutePath);
        Assert.StartsWith("?v=", stylesheetUri.Query, StringComparison.Ordinal);
        using var stylesheetResponse = await client.GetAsync(stylesheetUri.PathAndQuery);
        Assert.Equal(HttpStatusCode.OK, stylesheetResponse.StatusCode);
        Assert.Equal("text/css", stylesheetResponse.Content.Headers.ContentType?.MediaType);
        var stylesheetBytes = await stylesheetResponse.Content.ReadAsByteArrayAsync();
        var expectedStylesheet = await File.ReadAllBytesAsync(Path.Combine(Root(), "Legacy.Maliev.Web", "wwwroot", "dist", "site.min.css"));
        Assert.True(expectedStylesheet.AsSpan().SequenceEqual(stylesheetBytes), "Development must serve the owned stylesheet bytes.");
        var css = System.Text.Encoding.UTF8.GetString(stylesheetBytes);
        var fonts = Regex.Matches(css,
                @"url\(""(?<path>\./assets/fa-(?:regular-400|solid-900)-[A-Z0-9]+\.woff2)""\)", RegexOptions.CultureInvariant)
            .Select(match => match.Groups["path"].Value)
            .Distinct(StringComparer.Ordinal).ToArray();
        Assert.Equal(2, fonts.Length);
        Assert.Single(fonts, path => Path.GetFileName(path).StartsWith("fa-regular-400-", StringComparison.Ordinal));
        Assert.Single(fonts, path => Path.GetFileName(path).StartsWith("fa-solid-900-", StringComparison.Ordinal));
        foreach (var font in fonts)
        {
            var fontUri = new Uri(stylesheetUri, font);
            Assert.Equal(stylesheetUri.Authority, fontUri.Authority);
            Assert.Equal(stylesheetUri.Scheme, fontUri.Scheme);
            Assert.Equal("/dist/assets/" + Path.GetFileName(fontUri.AbsolutePath), fontUri.AbsolutePath);
            using var fontResponse = await client.GetAsync(fontUri.PathAndQuery);
            Assert.Equal(HttpStatusCode.OK, fontResponse.StatusCode);
            Assert.Equal("font/woff2", fontResponse.Content.Headers.ContentType?.MediaType);
            var fontBytes = await fontResponse.Content.ReadAsByteArrayAsync();
            Assert.True(fontBytes.Length >= 4, "The owned WOFF2 response must contain its signature.");
            Assert.True(fontBytes.AsSpan(0, 4).SequenceEqual("wOF2"u8), "The font response must be a WOFF2 payload.");
            var expectedFont = await File.ReadAllBytesAsync(Path.Combine(Root(), "Legacy.Maliev.Web", "wwwroot", "dist", "assets", Path.GetFileName(fontUri.AbsolutePath)));
            Assert.True(expectedFont.AsSpan().SequenceEqual(fontBytes), "Development must serve the owned Font Awesome bytes.");
        }
    }

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
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
    private readonly RedisContainer redis = new RedisBuilder("redis:8.4-alpine").Build();
    private readonly RSA rsa = RSA.Create(2048);
    private readonly string certificatePassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
    private readonly List<MemberAuthorityChild> children = [];
    private readonly Dictionary<string, string> connections = [];
    private string certificate = "";
    private Uri origin = null!;
    public string Password { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)) + "!a";

    public async Task InitializeAsync()
    {
        var authDll = Environment.GetEnvironmentVariable("MALIEV_MEMBER_AUTH_DLL");
        var seedDll = Environment.GetEnvironmentVariable("MALIEV_MEMBER_AUTH_SEED_DLL");
        try
        {
            Assert.True(File.Exists(authDll) && File.Exists(seedDll), "Run the mandatory private member proof preparation script before building/testing this lane.");
            await postgres.StartAsync();
            await redis.StartAsync();
            var containerConnection = new NpgsqlConnectionStringBuilder(postgres.GetConnectionString()) { Pooling = false };
            await using (var admin = new NpgsqlConnection(containerConnection.ConnectionString))
            {
                await admin.OpenAsync();
                foreach (var kind in new[] { "customer", "employee", "sessions" })
                {
                    var database = $"member_{kind}_{run}";
                    await using var command = new NpgsqlCommand($"CREATE DATABASE \"{database}\"", admin);
                    await command.ExecuteNonQueryAsync();
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
                var seed = Child(seedDll!, seedEnvironment);
                await seed.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(90));
                Assert.Equal(0, seed.ExitCode);
            }
            var certificateRequest = new CertificateRequest("CN=member-proof", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var x509 = certificateRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(2));
            certificate = Convert.ToBase64String(x509.Export(X509ContentType.Pfx, certificatePassword));
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            origin = new Uri($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}");
            listener.Stop();
            var auth = Child(authDll!, new()
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
                    using var response = await http.GetAsync("auth/v1/customer-self-service/identity");
                    if (response.StatusCode == HttpStatusCode.Unauthorized) break;
                }
                catch (HttpRequestException) { }
                catch (TaskCanceledException) { }
                Assert.True(DateTimeOffset.UtcNow < deadline, "Owned Auth producer startup did not complete.");
                await Task.Delay(100);
            }
        }
        catch (Exception primary)
        {
            await MemberAuthorityCleanup.CompleteAsync(primary, [DisposeAsync]);
            throw;
        }
    }

    public WebApplicationFactory<Program> Web(bool retained) => new MemberWebFactory(origin, redis.GetConnectionString(), certificate, certificatePassword, retained);

    public async Task<Guid> AssertActive(string refreshToken)
    {
        await using var connection = new NpgsqlConnection(connections["sessions"]);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT \"IdentityId\", \"IdentityKind\", \"SecurityStamp\", \"FamilyId\", \"ExpiresAt\", \"RevokedAt\", \"RotatedAt\", \"Id\" FROM refresh_sessions WHERE \"TokenHash\"=@hash", connection);
        command.Parameters.AddWithValue("hash", Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(refreshToken))));
        await using var row = await command.ExecuteReaderAsync();
        Assert.True(await row.ReadAsync());
        Assert.Equal("member-crawl-customer", row.GetString(0));
        Assert.Equal(0, row.GetInt32(1));
        Assert.False(string.IsNullOrWhiteSpace(row.GetString(2)));
        Assert.NotEqual(Guid.Empty, row.GetGuid(3));
        Assert.True(row.GetFieldValue<DateTimeOffset>(4) > DateTimeOffset.UtcNow);
        Assert.True(row.IsDBNull(5) && row.IsDBNull(6));
        var stamp = row.GetString(2);
        var sid = row.GetGuid(7);
        Assert.NotEqual(Guid.Empty, sid);
        Assert.False(await row.ReadAsync());
        await using var identity = new NpgsqlConnection(connections["customer"]);
        await identity.OpenAsync();
        await using var user = new NpgsqlCommand("SELECT count(*) FROM \"AspNetUsers\" WHERE \"Id\"='member-crawl-customer' AND \"DatabaseID\"=1 AND \"EmailConfirmed\" AND \"PasswordHash\" IS NOT NULL AND \"SecurityStamp\"=@stamp", identity);
        user.Parameters.AddWithValue("stamp", stamp);
        Assert.Equal(1L, await user.ExecuteScalarAsync());
        return sid;
    }

    public async Task AssertRevoked(Guid sid)
    {
        await using var connection = new NpgsqlConnection(connections["sessions"]);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT \"RevokedAt\" IS NOT NULL FROM refresh_sessions WHERE \"Id\"=@sid", connection);
        command.Parameters.AddWithValue("sid", sid);
        Assert.Equal(true, await command.ExecuteScalarAsync());
    }

    public async Task AssertSeedGuard(string mutation)
    {
        var guardRun = Guid.NewGuid().ToString("N");
        var container = new NpgsqlConnectionStringBuilder(postgres.GetConnectionString()) { Pooling = false };
        var guarded = new Dictionary<string, string>();
        await using (var admin = new NpgsqlConnection(container.ConnectionString))
        {
            await admin.OpenAsync();
            foreach (var kind in new[] { "customer", "employee", "sessions" })
            {
                var database = $"member_{kind}_{guardRun}";
                await using var create = new NpgsqlCommand($"CREATE DATABASE \"{database}\"", admin);
                await create.ExecuteNonQueryAsync();
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
        var seed = Child(Environment.GetEnvironmentVariable("MALIEV_MEMBER_AUTH_SEED_DLL")!, environment);
        await seed.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
        Assert.NotEqual(0, seed.ExitCode);
        foreach (var value in guarded.Values)
        {
            await using var target = new NpgsqlConnection(value);
            await target.OpenAsync();
            await using var unchanged = new NpgsqlCommand("SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public' AND c.relkind IN ('r','p','S')", target);
            Assert.Equal(0L, await unchanged.ExecuteScalarAsync());
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
        var probe = Child(Environment.GetEnvironmentVariable("MALIEV_MEMBER_AUTH_SEED_DLL")!,
            new() { ["MALIEV_MEMBER_ENV_PROBE"] = "1" }, hostile);
        await probe.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True(probe.ExitCode == 0, "Executable child must observe only explicitly allowed environment and deployment disabled.");
    }

    private Process Child(string dll, Dictionary<string, string> environment, Dictionary<string, string>? hostileAmbient = null)
    {
        var child = new MemberAuthorityChild();
        children.Add(child); // Register creation custody before any start or identity observation can fail.
        var process = child.Process;
        process.StartInfo = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        process.StartInfo.ArgumentList.Add(dll);
        if (hostileAmbient is not null)
            foreach (var item in hostileAmbient) process.StartInfo.Environment[item.Key] = item.Value;
        var essentials = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "PATH", "SystemRoot", "WINDIR", "TEMP", "TMP", "TMPDIR", "HOME", "USERPROFILE", "APPDATA", "LOCALAPPDATA",
            "DOTNET_ROOT", "DOTNET_ROOT_X64", "DOTNET_ROOT_X86", "DOTNET_ROOT_ARM64", "DOTNET_ROOT(x86)",
        };
        var inheritedEssentials = process.StartInfo.Environment.Where(item => essentials.Contains(item.Key)).ToArray();
        process.StartInfo.Environment.Clear();
        foreach (var item in inheritedEssentials) process.StartInfo.Environment[item.Key] = item.Value;
        foreach (var item in environment) process.StartInfo.Environment[item.Key] = item.Value;
        process.StartInfo.Environment["LEGACY_DEPLOY_ENABLED"] = "false";
        child.Start(process.StartInfo);
        return process;
    }

    public async Task DisposeAsync()
    {
        var actions = children.AsEnumerable().Reverse()
            .Select<MemberAuthorityChild, Func<Task>>(child => () => child.DisposeAsync().AsTask()).ToList();
        actions.Add(() => redis.DisposeAsync().AsTask());
        actions.Add(() => postgres.DisposeAsync().AsTask());
        actions.Add(() => { rsa.Dispose(); return Task.CompletedTask; });
        await MemberAuthorityCleanup.CompleteAsync(null, actions);
        children.Clear();
    }

    private sealed class MemberWebFactory(Uri auth, string redis, string certificate, string password, bool retained) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
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
