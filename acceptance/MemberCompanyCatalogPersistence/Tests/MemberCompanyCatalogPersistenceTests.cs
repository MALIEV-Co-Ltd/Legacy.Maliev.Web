using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.Web.Application;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Playwright;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Actual Catalog company adapter through an owned synthetic upstream and normal Customer company UPDATE.</summary>
public sealed class MemberCompanyCatalogPersistenceTests(MemberAuthorityFixture authority) : IClassFixture<MemberAuthorityFixture>
{
    [Theory]
    [InlineData("en", 1280)]
    [InlineData("th", 375)]
    public async Task CompanyUpdate_RealAdapter_NormalSave_ApiReadbackAndReload(string culture, int width)
    {
        var database = $"profile_contract_{Guid.NewGuid():N}";
        await using var resources = new BillingProofLifetime(proofCulture: null);
        try
        {
            await resources.RunAsync(async () =>
            {
                var postgres = resources.Postgres(database);
                using var startup = new CancellationTokenSource(TimeSpan.FromSeconds(120));
                await resources.OwnOperation(() => resources.StartPostgresAsync(postgres, startup.Token)).WaitAsync(startup.Token);
                var seed = Child(resources, Required("MALIEV_PROFILE_SEED_DLL"), new()
                {
                    ["MALIEV_PROFILE_DISPOSABLE_CONNECTION"] = postgres.GetConnectionString(),
                    ["MALIEV_PROFILE_DISPOSABLE_DATABASE"] = database,
                });
                await seed.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60));
                Assert.Equal(0, seed.ExitCode);
                await seed.CloseAsync();
                var rsa = resources.AcquireDependency(() => RSA.Create(2048), value => { value.Dispose(); return ValueTask.CompletedTask; });
                var customerOrigin = Origin();
                var catalogOrigin = Origin();
                var upstream = await SyntheticProvider.Start(resources);
                var environment = new Dictionary<string, string>
                {
                    ["DOTNET_ENVIRONMENT"] = "Development",
                    ["ASPNETCORE_ENVIRONMENT"] = "Development",
                    ["Cache__RedisEnabled"] = "false",
                    ["Jwt__PublicKey"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(rsa.ExportSubjectPublicKeyInfoPem())),
                    ["Jwt__Issuer"] = "https://billing-proof.example.test",
                    ["Jwt__Audience"] = "billing-proof-services",
                    ["Logging__LogLevel__Default"] = "None",
                };
                var customer = Child(resources, Required("MALIEV_PROFILE_PRODUCER_DLL"), new(environment)
                {
                    ["ASPNETCORE_URLS"] = customerOrigin.ToString(),
                    ["ConnectionStrings__CustomerDbContext"] = postgres.GetConnectionString(),
                });
                var catalog = Child(resources, Required("MALIEV_COMPANY_CATALOG_HOST_DLL"), new(environment)
                {
                    ["MALIEV_COMPANY_CATALOG_ORIGIN"] = catalogOrigin.ToString(),
                    ["MALIEV_COMPANY_UPSTREAM_ORIGIN"] = upstream.Origin.ToString(),
                    ["MALIEV_COMPANY_CATALOG_CONTENT_ROOT"] = Environment.GetEnvironmentVariable("MALIEV_COMPANY_CATALOG_CONTENT_ROOT")!,
                    ["ConnectionStrings__CatalogDbContext"] = postgres.GetConnectionString(),
                    ["ConnectionStrings__CountryDbContext"] = postgres.GetConnectionString(),
                    ["ConnectionStrings__CurrencyDbContext"] = postgres.GetConnectionString(),
                    ["InstantQuotationCatalog__ReconciliationEnabled"] = "false",
                });
                {
                    using var customerHttp = resources.AcquireHost(() => new HttpClient { BaseAddress = customerOrigin, Timeout = TimeSpan.FromSeconds(15) }, value => { value.Dispose(); return ValueTask.CompletedTask; });
                    using var catalogHttp = resources.AcquireHost(() => new HttpClient { BaseAddress = catalogOrigin, Timeout = TimeSpan.FromSeconds(15) }, value => { value.Dispose(); return ValueTask.CompletedTask; });
                    await Ready(customerHttp, customer, "customers/1");
                    await Ready(catalogHttp, catalog, "api/v1/companies/search?q=Synthetic&queryType=name&language=en");
                    var permissions = new[] { "legacy-customer.customers.read", "legacy-customer.customers.update", "legacy-customer.companies.read", "legacy-customer.companies.update", "legacy-catalog.companies.read" };
                    var serviceToken = Token(rsa, permissions);
                    catalogHttp.DefaultRequestHeaders.Authorization = new("Bearer", Token(rsa, []));
                    using var deniedLookup = await catalogHttp.GetAsync("api/v1/companies/search?q=Denied&queryType=name&language=en");
                    Assert.Equal(HttpStatusCode.Forbidden, deniedLookup.StatusCode);
                    Assert.Empty(upstream.Observations);
                    customerHttp.DefaultRequestHeaders.Authorization = new("Bearer", serviceToken);
                    catalogHttp.DefaultRequestHeaders.Authorization = new("Bearer", serviceToken);
                    // The unchanged address-only seed has no customer email; ordinary profile PUT requires one.
                    using var seededRead = await customerHttp.GetAsync("customers/1");
                    Assert.Equal(HttpStatusCode.OK, seededRead.StatusCode);
                    var seededJson = await seededRead.Content.ReadAsStringAsync();
                    using var seeded = JsonDocument.Parse(seededJson);
                    var preparation = ProfileInput(seeded.RootElement);
                    Assert.Null(preparation["Email"]);
                    using var invalidSeed = await customerHttp.PutAsJsonAsync("customers/1", preparation);
                    Assert.Equal(HttpStatusCode.BadRequest, invalidSeed.StatusCode);
                    using var unmodifiedSeed = await customerHttp.GetAsync("customers/1");
                    Assert.Equal(HttpStatusCode.OK, unmodifiedSeed.StatusCode);
                    Assert.Equal(seededJson, await unmodifiedSeed.Content.ReadAsStringAsync());
                    preparation["Email"] = "member-crawl@example.test";
                    using var preparedSeed = await customerHttp.PutAsJsonAsync("customers/1", preparation);
                    Assert.Equal(HttpStatusCode.NoContent, preparedSeed.StatusCode);
                    using var initial = await customerHttp.GetAsync("customers/1");
                    Assert.Equal(HttpStatusCode.OK, initial.StatusCode);
                    using var before = JsonDocument.Parse(await initial.Content.ReadAsStringAsync());
                    Assert.Equal("member-crawl@example.test", before.RootElement.GetProperty("Email").GetString());
                    Assert.Equal(Preserved(seeded.RootElement, includeEmail: false), Preserved(before.RootElement, includeEmail: false));
                    Assert.Equal(seeded.RootElement.GetProperty("CompanyId").GetInt32(), before.RootElement.GetProperty("CompanyId").GetInt32());
                    var companyId = before.RootElement.GetProperty("CompanyId").GetInt32();
                    var preserved = Preserved(before.RootElement);
                    using var originalCompanyRead = await customerHttp.GetAsync($"customers/companies/{companyId}");
                    Assert.Equal(HttpStatusCode.OK, originalCompanyRead.StatusCode);
                    var originalCompanyJson = await originalCompanyRead.Content.ReadAsStringAsync();
                    customerHttp.DefaultRequestHeaders.Authorization = new("Bearer", Token(rsa, permissions.Where(permission => permission != "legacy-customer.companies.update").ToArray()));
                    using var deniedWrite = await customerHttp.PutAsJsonAsync($"customers/companies/{companyId}", new { Name = "Forbidden mutation", TaxNumber = "0000000000000", Registrar = "Forbidden" });
                    Assert.Equal(HttpStatusCode.Forbidden, deniedWrite.StatusCode);
                    customerHttp.DefaultRequestHeaders.Authorization = new("Bearer", serviceToken);
                    using var unchanged = await customerHttp.GetAsync($"customers/companies/{companyId}");
                    Assert.Equal(HttpStatusCode.OK, unchanged.StatusCode);
                    var unchangedCompanyJson = await unchanged.Content.ReadAsStringAsync();
                    Assert.Equal(originalCompanyJson, unchangedCompanyJson);
                    using var unchangedCompany = JsonDocument.Parse(unchangedCompanyJson);
                    Assert.Equal("Stored company", unchangedCompany.RootElement.GetProperty("Name").GetString());
                    using var deniedCustomerRead = await customerHttp.GetAsync("customers/1");
                    using var deniedCustomer = JsonDocument.Parse(await deniedCustomerRead.Content.ReadAsStringAsync());
                    Assert.Equal(companyId, deniedCustomer.RootElement.GetProperty("CompanyId").GetInt32());
                    Assert.Equal(preserved, Preserved(deniedCustomer.RootElement));
                    var expectedCompany = culture == "th" ? "บริษัทสังเคราะห์ จำกัด" : "Synthetic Company Limited";
                    var baseWeb = resources.AcquireHost(() => authority.Web(retained: false), value => value.DisposeAsync());
                    var web = resources.AcquireHost(() => baseWeb.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
                    {
                        services.AddHttpClient("customers", client => client.BaseAddress = customerOrigin);
                        services.AddHttpClient("catalog", client => client.BaseAddress = catalogOrigin);
                        services.RemoveAll<IServiceAccessTokenProvider>();
                        services.AddSingleton<IServiceAccessTokenProvider>(new Tokens(serviceToken));
                        // Country list is a separate consumer contract; this bounded case fixes the seeded Thailand ID.
                        services.RemoveAll<ICountryClient>();
                        services.AddSingleton<ICountryClient, Countries>();
                    })), value => value.DisposeAsync(), out var webLease, startupRequired: true);
                    var webOrigin = Origin(https: true);
                    var certificateRequest = new CertificateRequest("CN=billing-loopback", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                    var alternativeNames = new SubjectAlternativeNameBuilder();
                    alternativeNames.AddIpAddress(IPAddress.Loopback);
                    certificateRequest.CertificateExtensions.Add(alternativeNames.Build());
                    var certificate = resources.AcquireDependency(() => certificateRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1)), value => { value.Dispose(); return ValueTask.CompletedTask; });
                    web.UseKestrel(options => options.Listen(IPAddress.Loopback, webOrigin.Port, listener => listener.UseHttps(certificate)));
                    resources.StartHost(webLease, () => web.StartServer());
                    // Probe an actual socket rather than CreateClient's factory transport before giving the origin to Chromium.
                    using var transport = resources.AcquireHost(() => new HttpClientHandler
                    {
                        ServerCertificateCustomValidationCallback = (request, peer, _, _) => request.RequestUri?.Host == "127.0.0.1"
                            && peer is not null && CryptographicOperations.FixedTimeEquals(peer.GetCertHash(), certificate.GetCertHash()),
                    }, value => { value.Dispose(); return ValueTask.CompletedTask; });
                    using var host = resources.AcquireHost(() => new HttpClient(transport) { BaseAddress = webOrigin, Timeout = TimeSpan.FromSeconds(15) }, value => { value.Dispose(); return ValueTask.CompletedTask; });
                    using var loginReady = await host.GetAsync("/Account/Login?culture=en");
                    Assert.Equal(HttpStatusCode.OK, loginReady.StatusCode);
                    var playwright = await resources.AcquireHostAsync(Playwright.CreateAsync, value => { value.Dispose(); return ValueTask.CompletedTask; });
                    var browser = await resources.AcquireHostAsync(() => playwright.Chromium.LaunchAsync(new() { Headless = true }), value => value.DisposeAsync());
                    var context = await resources.AcquireHostAsync(() => browser.NewContextAsync(new() { IgnoreHTTPSErrors = true, ViewportSize = new() { Width = width, Height = 850 } }), value => value.DisposeAsync());
                    var page = await resources.AcquireHostAsync(context.NewPageAsync, value => value.DisposeAsync());

                    var route = "/Member/Account/Manage/Profile?culture=" + culture;
                    await page.GotoAsync(new Uri(webOrigin, "/Account/Login?culture=en&returnUrl=" + Uri.EscapeDataString(route)).ToString());
                    await page.Locator("#cookieConsent [data-consent-action='reject']").ClickAsync();
                    await page.Locator("#Email").FillAsync("member-crawl@example.test");
                    try { await page.Locator("#Password").FillAsync(authority.Password); }
                    catch (PlaywrightException) { throw new InvalidOperationException("Synthetic credential entry failed; credential-bearing browser diagnostics suppressed."); }
                    await page.Locator("button[type=submit]").First.ClickAsync();
                    await page.Locator("#profile-company-name").WaitForAsync();
                    Assert.Equal("Stored company", await page.Locator("#profile-company-name").InputValueAsync());
                    var cookie = Assert.Single(await context.CookiesAsync(), value => value.Name == "__Host-Maliev.Legacy.Session");
                    Assert.True(cookie.HttpOnly && cookie.Secure);
                    await page.Locator("#profile-registrar").FillAsync("Manual registrar retained");
                    var widget = page.Locator("[data-thai-lookup]:has(#member-company-lookup-query)");
                    var reply = resources.OwnOperation(() => page.WaitForResponseAsync(response => response.Url.Contains("/lookups/companies/search", StringComparison.Ordinal)));
                    await widget.Locator("[data-lookup-query]").FillAsync("Synthetic " + culture);
                    var lookupResponse = await reply;
                    Assert.Equal(200, lookupResponse.Status);
                    var result = await lookupResponse.JsonAsync();
                    Assert.Equal("matches", result!.Value.GetProperty("outcome").GetString());
                    await widget.Locator("[role=option]").First.ClickAsync();
                    await widget.Locator("[data-lookup-apply]").ClickAsync();
                    Assert.Equal(expectedCompany, await page.Locator("#profile-company-name").InputValueAsync());
                    Assert.Equal("0123456789012", await page.Locator("#profile-tax-number").InputValueAsync());
                    Assert.Equal("Manual registrar retained", await page.Locator("#profile-registrar").InputValueAsync());
                    var save = resources.OwnOperation(() => page.WaitForResponseAsync(response => response.Request.Method == "POST" && response.Url.Contains("handler=UpdateProfile", StringComparison.Ordinal)));
                    await page.Locator("[data-migration-component=member-profile-content] button[type=submit]").ClickAsync();
                    Assert.Equal(302, (await save).Status);
                    await page.Locator("#profile-company-name").WaitForAsync();
                    using var readback = await customerHttp.GetAsync("customers/1");
                    Assert.Equal(HttpStatusCode.OK, readback.StatusCode);
                    using var after = JsonDocument.Parse(await readback.Content.ReadAsStringAsync());
                    Assert.Equal(companyId, after.RootElement.GetProperty("CompanyId").GetInt32());
                    Assert.Equal(preserved, Preserved(after.RootElement));
                    using var companyRead = await customerHttp.GetAsync($"customers/companies/{companyId}");
                    Assert.Equal(HttpStatusCode.OK, companyRead.StatusCode);
                    using var company = JsonDocument.Parse(await companyRead.Content.ReadAsStringAsync());
                    Assert.Equal(expectedCompany, company.RootElement.GetProperty("Name").GetString());
                    Assert.Equal("0123456789012", company.RootElement.GetProperty("TaxNumber").GetString());
                    Assert.Equal("Manual registrar retained", company.RootElement.GetProperty("Registrar").GetString());
                    await page.GotoAsync(new Uri(webOrigin, route).ToString());
                    await page.ReloadAsync();
                    Assert.Equal(expectedCompany, await page.Locator("#profile-company-name").InputValueAsync());
                    Assert.Equal("0123456789012", await page.Locator("#profile-tax-number").InputValueAsync());
                    Assert.Equal("Manual registrar retained", await page.Locator("#profile-registrar").InputValueAsync());
                    foreach (var scenario in new[] { ("Unavailable " + culture, 503), ("RateLimited " + culture, 429) })
                    {
                        var failure = resources.OwnOperation(() => page.WaitForResponseAsync(response => response.Url.Contains("/lookups/companies/search", StringComparison.Ordinal)));
                        await widget.Locator("[data-lookup-query]").FillAsync(scenario.Item1);
                        Assert.Equal(scenario.Item2, (await failure).Status);
                        Assert.Equal(expectedCompany, await page.Locator("#profile-company-name").InputValueAsync());
                        Assert.True(await page.Locator("#profile-company-name").IsEditableAsync());
                    }
                    var html = await page.ContentAsync();
                    Assert.False(html.Contains(serviceToken, StringComparison.Ordinal), "Member HTML must not disclose synthetic service credentials.");
                    var evidence = Path.Combine(AppContext.BaseDirectory, "TestResults", "member-company");
                    Directory.CreateDirectory(evidence);
                    await page.ScreenshotAsync(new() { Path = Path.Combine(evidence, culture + ".png"), FullPage = true });
                    await File.WriteAllTextAsync(Path.Combine(evidence, culture + ".json"), JsonSerializer.Serialize(new
                    {
                        surface = "member-company-update", culture, width,
                        candidateHead = Environment.GetEnvironmentVariable("MALIEV_COMPANY_CANDIDATE_HEAD"),
                        companyId, selectedCompany = expectedCompany, selectedTaxId = "0123456789012",
                        catalogStatus = 200, saveStatus = 302, readbackStatus = 200, companyReadbackStatus = 200,
                        reloadVerified = true, contactsAndAddressIdentitiesPreserved = true, manualRegistrarPreserved = true,
                        deniedLookupStatus = 403, deniedWriteStatus = 403, deniedWriteUnchanged = true,
                        fixtureNullEmailRejectedStatus = (int)invalidSeed.StatusCode, fixturePreparedStatus = (int)preparedSeed.StatusCode,
                        preparedFixtureEmailPreserved = true,
                        unavailableStatus = 503, rateLimitedStatus = 429, manualFallbackEditable = true,
                        syntheticUpstreamOnly = true, observations = upstream.Observations.ToArray(),
                    }));
                }
            });
        }
        finally
        {
            await resources.DisposeAsync();
            var cleanupDirectory = Path.Combine(AppContext.BaseDirectory, "TestResults", "member-company");
            Directory.CreateDirectory(cleanupDirectory);
            await File.WriteAllTextAsync(Path.Combine(cleanupDirectory, culture + "-cleanup.json"), resources.OwnershipReceipt());
        }
    }

    private static string Required(string name) => Environment.GetEnvironmentVariable(name) is { } value && File.Exists(value)
        ? value : throw new InvalidOperationException("Pinned hosted binary required: " + name);
    private static Dictionary<string, object?> ProfileInput(JsonElement value)
    {
        var fields = new[] { "FirstName", "LastName", "Telephone", "Mobile", "Fax", "Email", "DateOfBirth", "CompanyId", "BillingAddressId", "ShippingAddressId" };
        return fields.ToDictionary(field => field, field => value.TryGetProperty(field, out var property) && property.ValueKind != JsonValueKind.Null ? (object?)property.Clone() : null);
    }
    private static string Preserved(JsonElement value, bool includeEmail = true) => JsonSerializer.Serialize(value.EnumerateObject()
        .Where(p => p.Name is "FirstName" or "LastName" or "Telephone" or "Mobile" or "Fax" or "Email" or "DateOfBirth" or "BillingAddressId" or "ShippingAddressId")
        .Where(p => includeEmail || p.Name != "Email")
        .OrderBy(p => p.Name, StringComparer.Ordinal).ToDictionary(p => p.Name, p => p.Value.ValueKind == JsonValueKind.Null ? (object)string.Empty : p.Value.Clone()));
    private static Uri Origin(bool https = false)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return new Uri($"{(https ? "https" : "http")}://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}");
    }
    private static string Token(RSA rsa, string[] permissions) => new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
        "https://billing-proof.example.test", "billing-proof-services", [new Claim("sub", "billing-disposable-workload"), .. permissions.Select(value => new Claim("permissions", value))],
        DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(20), new SigningCredentials(new RsaSecurityKey(rsa), SecurityAlgorithms.RsaSha256)));
    private static BillingChildLease Child(BillingProofLifetime resources, string dll, Dictionary<string, string> environment)
    {
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = Path.GetDirectoryName(dll)!, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(dll);
        var essentials = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "PATH", "HOME", "TMPDIR", "TEMP", "TMP", "SystemRoot", "DOTNET_ROOT", "DOTNET_ROOT_X64" };
        var inherited = start.Environment.Where(pair => essentials.Contains(pair.Key)).ToArray();
        start.Environment.Clear();
        foreach (var pair in inherited) start.Environment[pair.Key] = pair.Value;
        foreach (var pair in environment) start.Environment[pair.Key] = pair.Value;
        start.Environment["LEGACY_DEPLOY_ENABLED"] = "false";
        start.Environment["DOTNET_GCHeapHardLimit"] = "20000000"; // 512MiB managed-heap ceiling, not an OS memory guarantee.
        return resources.StartChild(() => new BillingSystemChild(start));
    }
    private static async Task Ready(HttpClient http, BillingChildLease child, string path)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        using var ticks = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
        do
        {
            Assert.False(child.HasExited, "Disposable service startup failed; credential-bearing output suppressed.");
            try { using var response = await http.GetAsync(path, deadline.Token); if (response.StatusCode == HttpStatusCode.Unauthorized) return; }
            catch (HttpRequestException) { }
        } while (await ticks.WaitForNextTickAsync(deadline.Token));
        throw new TimeoutException("Owned service readiness timed out.");
    }
    private sealed class Tokens(string token) : IServiceAccessTokenProvider
    {
        public ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken) => ValueTask.FromResult<string?>(token);
        public void Invalidate(string value) { }
    }
    private sealed class Countries : ICountryClient
    {
        public Task<ServiceResponse<IReadOnlyList<Country>>> GetCountriesAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new ServiceResponse<IReadOnlyList<Country>>([new(66, "Thailand", null, null, "TH", "THA", null, null)], true));
    }
}
