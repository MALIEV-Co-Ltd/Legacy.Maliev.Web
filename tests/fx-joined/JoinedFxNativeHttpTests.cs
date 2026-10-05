using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Application.Pricing;
using Legacy.Maliev.Web.Components.Pages.InstantQuotation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;

namespace Legacy.Maliev.Web.FxJoined.Tests;

// Auth and Catalog are separate, exact-pinned native Program processes.
// Missing inputs are failures. No fixture token, permission or authentication handler is installed.
[CollectionDefinition("Native FX graph", DisableParallelization = true)]
public sealed class NativeFxGraphCollection { }

[Collection("Native FX graph")]
public sealed class JoinedFxNativeHttpTests
{
    private const string CurrencyRead = "legacy-catalog.currencies.read";
    public static TheoryData<bool, string> RenderersAndCultures => new()
    {
        { true, "en" }, { true, "th" }, { false, "en" }, { false, "th" },
    };
    public static IEnumerable<object[]> InvalidNativeTokens =>
        from active in new[] { true, false }
        from culture in new[] { "en", "th" }
        from variant in new[] { "WRONG_AUDIENCE", "WRONG_SIGNING_KEY", "EXPIRED" }
        select new object[] { active, culture, variant };
    public static IEnumerable<object[]> ProviderFailures =>
        from active in new[] { true, false }
        from culture in new[] { "en", "th" }
        from mode in new[] { "zero", "negative", "missing", "malformed", "invalid-json", "refusal", "timeout" }
        select new object[] { active, culture, mode };

    [Theory]
    [MemberData(nameof(RenderersAndCultures))]
    public async Task NormalWebLogin_RealCatalogPermission_ConvertsBothHandlers(bool active, string culture)
    {
        var graph = Graph.Read();
        using var normalAuth = new HttpClient { BaseAddress = graph.Auth };
        // Obtain a real token from the normal server issuer; do not construct a JWT.
        var token = await LoginAsync(normalAuth, "legacy-web", graph.WebSecret);
        using var signer = RSA.Create();
        signer.ImportFromPem(graph.PublicKey);
        var principal = new JwtSecurityTokenHandler { MapInboundClaims = false }.ValidateToken(token,
            new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = graph.Issuer,
                ValidateAudience = true,
                ValidAudience = graph.Audience,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new RsaSecurityKey(signer),
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero,
                ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            }, out _);
        Assert.Equal("service:legacy-web", principal.FindFirst("sub")?.Value);
        Assert.Equal("service", principal.FindFirst("identity_kind")?.Value);
        Assert.Equal(graph.ConfiguredPermissions.Append(CurrencyRead).Order(StringComparer.Ordinal),
            principal.FindAll("permissions").Select(claim => claim.Value).Order(StringComparer.Ordinal));

        using var catalog = new HttpClient { BaseAddress = graph.Catalog };
        using var request = new HttpRequestMessage(HttpMethod.Get,
            "currencies/exchangerates?baseCurrency=THB&targetCurrency=USD");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var authorized = await catalog.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, authorized.StatusCode);
        using var catalogJson = JsonDocument.Parse(await authorized.Content.ReadAsStringAsync());
        Assert.Equal("THB", catalogJson.RootElement.GetProperty("Base").GetString());
        Assert.Equal("0.025", catalogJson.RootElement.GetProperty("Rates").GetProperty("USD").GetString());

        await using var web = CreateWeb(graph, active, "legacy-web", graph.WebSecret);
        using var client = Browser(web);
        foreach (var handler in new[] { "GetEstimate", "GetOrderTotal" })
        {
            using var thb = await ReadSuccess(client, Route(handler, "THB", culture));
            using var usd = await ReadSuccess(client, Route(handler, "USD", culture));
            Assert.Equal("USD", usd.RootElement.GetProperty("currency").GetString());
            var fields = handler == "GetEstimate"
                ? new[] { "unitPrice", "subtotal", "technicalFilamentMinimumPrice", "technicalFilamentMinimumAdjustment" }
                : new[] { "printing", "itemsSubtotal", "minimumOrderPrice", "minimumOrderSurcharge", "shipping", "vat", "priceBeforeVat", "finalOrderPrice" };
            foreach (var field in fields)
                Assert.Equal(Math.Round(thb.RootElement.GetProperty(field).GetDecimal() * 0.025m, 2),
                    usd.RootElement.GetProperty(field).GetDecimal());
            if (handler == "GetEstimate")
                Assert.Equal(thb.RootElement.GetProperty("subtotalThb").GetDecimal(),
                    usd.RootElement.GetProperty("subtotalThb").GetDecimal());
            Assert.DoesNotContain(graph.WebSecret, usd.RootElement.GetRawText(), StringComparison.Ordinal);
            Assert.DoesNotContain(token, usd.RootElement.GetRawText(), StringComparison.Ordinal);
            Assert.DoesNotContain("accessToken", usd.RootElement.GetRawText(), StringComparison.OrdinalIgnoreCase);
        }
        using var scope = web.Services.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<IServiceAccessTokenProvider>();
        Assert.Equal("ServiceAccessTokenProvider", provider.GetType().Name);
        var serverToken = await provider.GetAccessTokenAsync(CancellationToken.None);
        Assert.False(string.IsNullOrEmpty(serverToken));
        using var page = await client.GetAsync("/InstantQuotation/3D-Printing?culture=" + culture);
        var html = await page.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.DoesNotContain(serverToken!, html, StringComparison.Ordinal);
        Assert.DoesNotContain(graph.WebSecret, html, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(RenderersAndCultures))]
    public async Task OtherNormalMachineToken_RealCatalogDenies_WebFailsClosed(bool active, string culture)
    {
        var graph = Graph.Read();
        using var auth = new HttpClient { BaseAddress = graph.Auth };
        var token = await LoginAsync(auth, "legacy-other", graph.OtherSecret);
        using var catalog = new HttpClient { BaseAddress = graph.Catalog };
        using var request = new HttpRequestMessage(HttpMethod.Get,
            "currencies/exchangerates?baseCurrency=THB&targetCurrency=USD");
        request.Headers.Authorization = new("Bearer", token);
        using var denied = await catalog.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        await using var web = CreateWeb(graph, active, "legacy-other", graph.OtherSecret);
        using var client = Browser(web);
        await Refused(client, Route("GetEstimate", "USD", culture), graph.OtherSecret, token);
        await Refused(client, Route("GetOrderTotal", "USD", culture), graph.OtherSecret, token);
    }

    [Theory]
    [MemberData(nameof(RenderersAndCultures))]
    public async Task InvalidCredential_NormalAuthRefuses_WebFailsClosed(bool active, string culture)
    {
        var graph = Graph.Read();
        var wrong = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        using var auth = new HttpClient { BaseAddress = graph.Auth };
        using var denied = await auth.PostAsJsonAsync("auth/v1/service/login",
            new { clientId = "legacy-web", clientSecret = wrong });
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        Assert.DoesNotContain("accessToken", await denied.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        await using var web = CreateWeb(graph, active, "legacy-web", wrong);
        using var client = Browser(web);
        await Refused(client, Route("GetEstimate", "USD", culture), wrong);
    }

    [Fact]
    public async Task NoBearer_ActualCatalogEndpointRequiresAuthentication()
    {
        var graph = Graph.Read();
        using var catalog = new HttpClient { BaseAddress = graph.Catalog };
        using var denied = await catalog.GetAsync("currencies/exchangerates?baseCurrency=THB&targetCurrency=USD");
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
    }

    [Theory]
    [MemberData(nameof(InvalidNativeTokens))]
    public async Task NormalIssuer_InvalidToken_ActualCatalogRefusesAndWebFailsClosed(bool active, string culture, string variant)
    {
        var graph = Graph.Read();
        // Each variant is another actual Auth Program at the same reviewed successor.
        // Change only fixture key/audience/time, never create a JWT in this consumer test.
        graph = graph with { Auth = Graph.Loopback("MALIEV_FX_AUTH_" + variant + "_ORIGIN") };
        using var auth = new HttpClient { BaseAddress = graph.Auth };
        var token = await LoginAsync(auth, "legacy-web", graph.WebSecret);
        var decoded = new JwtSecurityTokenHandler().ReadJwtToken(token);
        Assert.Equal(SecurityAlgorithms.RsaSha256, decoded.Header.Alg);
        Assert.False(string.IsNullOrEmpty(decoded.RawSignature));
        if (variant == "WRONG_AUDIENCE") Assert.DoesNotContain(graph.Audience, decoded.Audiences);
        if (variant == "EXPIRED") Assert.True(decoded.ValidTo < DateTime.UtcNow);
        using var catalog = new HttpClient { BaseAddress = graph.Catalog };
        using var request = new HttpRequestMessage(HttpMethod.Get,
            "currencies/exchangerates?baseCurrency=THB&targetCurrency=USD");
        request.Headers.Authorization = new("Bearer", token);
        using var denied = await catalog.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        await using var web = CreateWeb(graph, active, "legacy-web", graph.WebSecret);
        using var client = Browser(web);
        await Refused(client, Route("GetEstimate", "USD", culture), graph.WebSecret, token);
        await Refused(client, Route("GetOrderTotal", "USD", culture), graph.WebSecret, token);
    }

    [Theory]
    [MemberData(nameof(ProviderFailures))]
    [Trait("FxGraphLane", "provider")]
    public async Task AuthorizedNativeGraph_UnusableUpstream_FailsClosed(bool active, string culture, string mode)
    {
        var graph = Graph.Read();
        var runId = Graph.RunId();
        using var control = new HttpClient { BaseAddress = Graph.Loopback("MALIEV_FX_PROVIDER_ORIGIN") };
        control.DefaultRequestHeaders.Add("X-Fx-Fixture-Run", runId);
        async Task SetScenario(string scenario)
        {
            using var response = await control.PostAsJsonAsync("runs/" + runId + "/control/scenario",
                new { mode = scenario, resetRequests = true });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        await SetScenario(mode);
        try
        {
            await using var web = CreateWeb(graph, active, "legacy-web", graph.WebSecret);
            using var client = Browser(web);
            await Refused(client, Route("GetEstimate", "USD", culture), graph.WebSecret);
            await Refused(client, Route("GetOrderTotal", "USD", culture), graph.WebSecret);
            using var observed = await control.GetAsync("runs/" + runId + "/control/stats");
            Assert.Equal(HttpStatusCode.OK, observed.StatusCode);
            using var json = JsonDocument.Parse(await observed.Content.ReadAsStringAsync());
            var requests = json.RootElement.GetProperty("requests").EnumerateArray().ToArray();
            Assert.NotEmpty(requests);
            Assert.All(requests, request =>
            {
                Assert.Equal("GET", request.GetProperty("method").GetString());
                Assert.Equal("/runs/" + runId + "/latest", request.GetProperty("path").GetString());
                Assert.Equal("THB", request.GetProperty("query").GetProperty("from")[0].GetString());
                Assert.Equal("USD", request.GetProperty("query").GetProperty("to")[0].GetString());
            });
        }
        finally { await SetScenario("valid"); }
    }

    [Theory]
    [MemberData(nameof(RenderersAndCultures))]
    [Trait("FxGraphLane", "state")]
    public async Task ProtectedSession_NativeObservation_ReusesThenExpiresWithoutStaleFallback(bool active, string culture)
    {
        var graph = Graph.Read();
        var clock = new AdjustableClock();
        await SetProvider("valid", true);
        await using var web = CreateWeb(graph, active, "legacy-web", graph.WebSecret, clock);
        using var client = Browser(web);
        try
        {
            using var estimate = await ReadSuccess(client, Route("GetEstimate", "USD", culture));
            Assert.Equal(1, await ProviderCount());
            await SetProvider("changed", false);
            clock.Advance(TimeSpan.FromMinutes(29));
            using var cached = await ReadSuccess(client, Route("GetOrderTotal", "USD", culture));
            Assert.Equal(77.50m, cached.RootElement.GetProperty("priceBeforeVat").GetDecimal());
            Assert.Equal(1, await ProviderCount());
            clock.Advance(TimeSpan.FromMinutes(1));
            using var refreshed = await ReadSuccess(client, Route("GetOrderTotal", "USD", culture));
            Assert.Equal(93m, refreshed.RootElement.GetProperty("priceBeforeVat").GetDecimal());
            Assert.Equal(2, await ProviderCount());
            await SetProvider("refusal", false);
            clock.Advance(TimeSpan.FromMinutes(30));
            await Refused(client, Route("GetOrderTotal", "USD", culture), graph.WebSecret);
            Assert.True(await ProviderCount() > 2);
        }
        finally { await SetProvider("valid", true); }
    }

    [Theory]
    [MemberData(nameof(RenderersAndCultures))]
    [Trait("FxGraphLane", "state")]
    public async Task DifferentProtectedSessions_NativeObservationsRemainSeparate(bool active, string culture)
    {
        var graph = Graph.Read();
        await SetProvider("valid", true);
        await using var web = CreateWeb(graph, active, "legacy-web", graph.WebSecret);
        using var first = Browser(web);
        using var second = Browser(web);
        try
        {
            using var initial = await ReadSuccess(first, Route("GetOrderTotal", "USD", culture));
            Assert.Equal(77.50m, initial.RootElement.GetProperty("priceBeforeVat").GetDecimal());
            await SetProvider("changed", false);
            using var separate = await ReadSuccess(second, Route("GetOrderTotal", "USD", culture));
            Assert.Equal(93m, separate.RootElement.GetProperty("priceBeforeVat").GetDecimal());
            using var original = await ReadSuccess(first, Route("GetOrderTotal", "USD", culture));
            Assert.Equal(77.50m, original.RootElement.GetProperty("priceBeforeVat").GetDecimal());
            Assert.Equal(2, await ProviderCount());
        }
        finally { await SetProvider("valid", true); }
    }

    [Theory]
    [MemberData(nameof(RenderersAndCultures))]
    [Trait("FxGraphLane", "state")]
    public async Task RealProtectedTickets_NativeForeignDisplay_PreservesThbIdentityAndReview(bool active, string culture)
    {
        var graph = Graph.Read();
        var clock = new AdjustableClock();
        await SetProvider("valid", true);
        await using var web = CreateWeb(graph, active, "legacy-web", graph.WebSecret, clock);
        using var client = Browser(web);
        using var scope = web.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IInstantQuotationSessionStore>();
        var claim = new InstantQuotationGeometryClaim(1, new string('a', 64), 20, 20, 10, 2000, 1200,
            Enumerable.Repeat(200d, 64).ToArray(), Enumerable.Repeat(80d, 64).ToArray(), 1024, 1, true, false, false, 0.8);
        var upload = InstantQuotationUploadResult.Succeeded("operation",
            new InstantQuotationUploadReference(Guid.NewGuid().ToString("D")), claim.Sha256);
        var geometry = AuthoritativeInstantQuotationGeometry.FromCompletedLegacyUpload(upload, claim)!;
        var part = new InstantQuotationPart(Guid.NewGuid(), "part.stl", upload.UploadReference!, geometry,
            new InstantQuotationPartConfiguration("M68", "Gray", 2, BuildPreference.Strength));
        var session = await store.CreateAsync(null, new InstantQuotationOrderState([part]), CancellationToken.None);
        var quote = scope.ServiceProvider.GetRequiredService<IInstantQuotationPricingService>().Quote(session.RequestState);
        var service = Assert.IsType<AdditiveQuoteTicketService>(scope.ServiceProvider.GetRequiredService<IInstantQuotationQuoteTicketService>());
        var authorization = service.Issue(session, quote, clock.GetUtcNow());
        session = session with { QuoteAuthorization = authorization };
        Assert.True(await store.PutAsync(session, null, CancellationToken.None));
        var context = new DefaultHttpContext();
        scope.ServiceProvider.GetRequiredService<InstantQuotationSessionIdentityCookie>().Write(
            context, session.SessionId, session.CreatedAt.Add(InstantQuotationSessionIdentityCookie.Lifetime));
        client.DefaultRequestHeaders.Add("Cookie", context.Response.Headers.SetCookie.ToString().Split(';')[0]);
        var beforeLine = service.UnprotectLine(authorization.LineTickets[0], clock.GetUtcNow());
        var beforeOrder = service.UnprotectOrder(authorization.OrderTicket, clock.GetUtcNow());
        try
        {
            using var foreign = await ReadSuccess(client, Route("GetOrderTotal", "USD", culture));
            Assert.Equal("USD", foreign.RootElement.GetProperty("currency").GetString());
            Assert.Equal(1, await ProviderCount());
            var persisted = await store.GetAsync(session.SessionId, null, CancellationToken.None);
            Assert.NotNull(persisted);
            Assert.Equal(authorization.OrderTicket, persisted.QuoteAuthorization!.OrderTicket);
            Assert.Equal(authorization.LineTickets, persisted.QuoteAuthorization.LineTickets);
            var line = service.UnprotectLine(authorization.LineTickets[0], clock.GetUtcNow());
            var order = service.UnprotectOrder(authorization.OrderTicket, clock.GetUtcNow());
            Assert.Equal(JsonSerializer.Serialize(beforeLine), JsonSerializer.Serialize(line));
            Assert.Equal(JsonSerializer.Serialize(beforeOrder), JsonSerializer.Serialize(order));
            Assert.Equal("THB", line.EffectiveCurrency);
            Assert.Equal(1m, line.ExchangeRate);
            Assert.Equal("THB", order.EffectiveCurrency);
            Assert.Equal(1m, order.ExchangeRate);
            Assert.Equal(session.SessionId, line.SessionId);
            Assert.Equal(part.PartId.ToString("D"), line.PartId);
            Assert.Equal("provisional", line.Confidence);
            Assert.Equal("engineer_review_required", line.ReviewState);
            Assert.True(service.Validate(session, quote, authorization, clock.GetUtcNow()));
            Assert.False(service.Validate(session with { OwnerIdentity = "another-owner" }, quote, authorization, clock.GetUtcNow()));
            line.EffectiveCurrency = "USD";
            line.ExchangeRate = 0.025m;
            var rewritten = service.ProtectLine(line);
            Assert.Equal("quote_invalid", Assert.Throws<AdditiveQuoteTicketException>(() =>
                service.UnprotectLine(rewritten, clock.GetUtcNow())).Code);
            var bytes = authorization.OrderTicket.ToCharArray();
            bytes[bytes.Length / 2] = bytes[bytes.Length / 2] == 'A' ? 'B' : 'A';
            Assert.Throws<AdditiveQuoteTicketException>(() => service.UnprotectOrder(new string(bytes), clock.GetUtcNow()));
        }
        finally { await SetProvider("valid", true); }
    }

    private static async Task SetProvider(string mode, bool reset)
    {
        using var client = new HttpClient { BaseAddress = Graph.Loopback("MALIEV_FX_PROVIDER_ORIGIN") };
        client.DefaultRequestHeaders.Add("X-Fx-Fixture-Run", Graph.RunId());
        using var response = await client.PostAsJsonAsync("runs/" + Graph.RunId() + "/control/scenario",
            new { mode, resetRequests = reset });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
    private static async Task<int> ProviderCount()
    {
        using var client = new HttpClient { BaseAddress = Graph.Loopback("MALIEV_FX_PROVIDER_ORIGIN") };
        client.DefaultRequestHeaders.Add("X-Fx-Fixture-Run", Graph.RunId());
        using var response = await client.GetAsync("runs/" + Graph.RunId() + "/control/stats");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("requests").GetArrayLength();
    }
    private sealed class AdjustableClock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan duration) => now += duration;
    }
    private static WebApplicationFactory<Program> CreateWeb(Graph graph, bool active, string clientId, string secret, TimeProvider? clock = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Services:Auth"] = graph.Auth.ToString(),
                    ["Services:Catalog"] = graph.Catalog.ToString(),
                    ["ServiceAuthentication:ClientId"] = clientId,
                    ["ServiceAuthentication:ClientSecret"] = secret,
                    ["BlazorRouting:InstantQuotation"] = active.ToString(),
                    ["Redis:Enabled"] = "false",
                    ["Recaptcha:ProjectId"] = "fx-disposable-project",
                    ["Recaptcha:SiteKey"] = "fx-disposable-site",
                }));
            if (clock is not null) builder.ConfigureServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton(clock);
            });
        });

    private static HttpClient Browser(WebApplicationFactory<Program> web) => web.CreateClient(new()
    {
        AllowAutoRedirect = false,
        HandleCookies = true,
        BaseAddress = new Uri("https://localhost"),
    });

    private static async Task<string> LoginAsync(HttpClient auth, string clientId, string secret)
    {
        using var response = await auth.PostAsJsonAsync("auth/v1/service/login", new { clientId, clientSecret = secret });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(secret, body, StringComparison.Ordinal);
        Assert.DoesNotContain("refreshToken", body, StringComparison.OrdinalIgnoreCase);
        using var json = JsonDocument.Parse(body);
        Assert.Equal("Bearer", json.RootElement.GetProperty("tokenType").GetString());
        Assert.InRange(json.RootElement.GetProperty("expiresIn").GetInt32(), 300, 1800);
        return json.RootElement.GetProperty("accessToken").GetString()!;
    }

    private static async Task<JsonDocument> ReadSuccess(HttpClient client, string route)
    {
        using var response = await client.GetAsync(route);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.GetProperty("success").GetBoolean());
        return json;
    }

    private static async Task Refused(HttpClient client, string route, params string[] sensitive)
    {
        using var response = await client.GetAsync(route);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("pricing_unavailable", json.RootElement.GetProperty("code").GetString());
        Assert.False(json.RootElement.TryGetProperty("unitPrice", out _));
        Assert.False(json.RootElement.TryGetProperty("finalOrderPrice", out _));
        foreach (var value in sensitive) Assert.DoesNotContain(value, body, StringComparison.Ordinal);
    }

    private static string Route(string handler, string currency, string culture) => QueryHelpers.AddQueryString(
        "/InstantQuotation/3D-Printing", new Dictionary<string, string?>
        {
            ["handler"] = handler,
            ["currency"] = currency,
            ["culture"] = culture,
            ["material"] = "PLA",
            ["dimensionZ"] = "30",
            ["volume"] = "20000",
            ["footprint"] = "400",
            ["quantity"] = "1",
            ["processes"] = "fdm,resin",
            ["subtotals"] = "1200,1800",
            ["totalWeightGrams"] = "500",
            ["totalBoundingCm3"] = "2000",
        });

    private sealed record Graph(Uri Auth, Uri Catalog, string WebSecret, string OtherSecret,
        string Issuer, string Audience, string PublicKey, string[] ConfiguredPermissions)
    {
        public static Graph Read()
        {
            var manifestPath = Required("MALIEV_FX_GRAPH_MANIFEST");
            using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var root = document.RootElement;
            Assert.True(Guid.TryParse(root.GetProperty("runId").GetString(), out _));
            var authCommit = root.GetProperty("issuanceCommit").GetString();
            Assert.Matches("^[0-9a-f]{40}$", authCommit!);
            Assert.NotEqual("5a81aba3d85c2a22cf999d7f02cdcd7f7fa38198", authCommit);
            Assert.Equal("e3db3f3bfeba7e470d0ba12ed580c8c580acd9aa", root.GetProperty("catalogCommit").GetString());
            Assert.Equal("003b255f0fb0f0bce032f5b5ff15d28be0c8c391", root.GetProperty("catalogDefaultsCommit").GetString());
            Assert.Equal("78e48ffc4ee000df0510cba5e7c7a3c4c4d539d7", root.GetProperty("contractsCommit").GetString());
            // A recorded pin is only provenance input. It never substitutes for HTTP assertions above.
            var configured = root.GetProperty("configuredWebPermissions").EnumerateArray().Select(item => item.GetString()!).ToArray();
            Assert.Equal(19, configured.Length);
            Assert.Equal(19, configured.Distinct(StringComparer.Ordinal).Count());
            Assert.DoesNotContain(CurrencyRead, configured);
            Assert.All(configured, permission => Assert.DoesNotContain("*", permission, StringComparison.Ordinal));
            return new(Loopback("MALIEV_FX_AUTH_ORIGIN"), Loopback("MALIEV_FX_CATALOG_ORIGIN"),
                Required("MALIEV_FX_WEB_SECRET"), Required("MALIEV_FX_OTHER_SECRET"),
                root.GetProperty("issuer").GetString()!, root.GetProperty("audience").GetString()!,
                Required("MALIEV_FX_PUBLIC_KEY_PEM"), configured);
        }
        private static string Required(string name) => Environment.GetEnvironmentVariable(name)
            ?? throw new InvalidOperationException($"Native disposable graph input required: {name}");
        public static string RunId()
        {
            using var document = JsonDocument.Parse(File.ReadAllText(Required("MALIEV_FX_GRAPH_MANIFEST")));
            return Guid.Parse(document.RootElement.GetProperty("runId").GetString()!).ToString("D");
        }
        public static Uri Loopback(string name)
        {
            var uri = new Uri(Required(name));
            Assert.True(uri.Scheme == "http" && (uri.Host is "127.0.0.1" or "localhost") && uri.Port > 1024
                && string.IsNullOrEmpty(uri.UserInfo) && uri.AbsolutePath == "/");
            return uri;
        }
    }
}
