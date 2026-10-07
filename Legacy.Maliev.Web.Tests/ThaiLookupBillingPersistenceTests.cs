using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.Web.Application;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Playwright;
using Testcontainers.PostgreSql;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Real Catalog geography, normal Auth login/cookie/session and existing Customer address persistence.</summary>
public sealed class ThaiLookupBillingPersistenceTests(MemberAuthorityFixture authority) : IClassFixture<MemberAuthorityFixture>
{
    [Theory]
    [InlineData("en", 1280)]
    [InlineData("th", 375)]
    public async Task BillingTuple_NormalSave_ApiReadbackAndReloadPreserveManualFields(string culture, int width)
    {
        var database = $"profile_contract_{Guid.NewGuid():N}";
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").WithDatabase(database).Build();
        await postgres.StartAsync();
        using (var seed = Child(Required("MALIEV_PROFILE_SEED_DLL"), new()
        {
            ["MALIEV_PROFILE_DISPOSABLE_CONNECTION"] = postgres.GetConnectionString(),
            ["MALIEV_PROFILE_DISPOSABLE_DATABASE"] = database,
        }))
        {
            await seed.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60));
            Assert.Equal(0, seed.ExitCode);
        }
        using var rsa = RSA.Create(2048);
        var customerOrigin = Origin();
        var catalogOrigin = Origin();
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
        using var customer = Child(Required("MALIEV_PROFILE_PRODUCER_DLL"), new(environment)
        {
            ["ASPNETCORE_URLS"] = customerOrigin.ToString(),
            ["ConnectionStrings__CustomerDbContext"] = postgres.GetConnectionString(),
        });
        using var catalog = Child(Required("MALIEV_BILLING_CATALOG_DLL"), new(environment)
        {
            ["ASPNETCORE_URLS"] = catalogOrigin.ToString(),
            ["ConnectionStrings__CatalogDbContext"] = postgres.GetConnectionString(),
            ["ConnectionStrings__CountryDbContext"] = postgres.GetConnectionString(),
            ["ConnectionStrings__CurrencyDbContext"] = postgres.GetConnectionString(),
            ["InstantQuotationCatalog__ReconciliationEnabled"] = "false",
        });
        try
        {
            using var customerHttp = new HttpClient { BaseAddress = customerOrigin, Timeout = TimeSpan.FromSeconds(15) };
            using var catalogHttp = new HttpClient { BaseAddress = catalogOrigin, Timeout = TimeSpan.FromSeconds(15) };
            await Ready(customerHttp, customer, "customers/1");
            await Ready(catalogHttp, catalog, "api/v1/thai-addresses/autocomplete?postcode=11120");
            var permissions = new[] { "legacy-customer.customers.read", "legacy-customer.addresses.update", "legacy-catalog.locations.read" };
            var serviceToken = Token(rsa, permissions);
            // Service authorization remains enforced, including write permission independently of read permission.
            customerHttp.DefaultRequestHeaders.Authorization = new("Bearer", Token(rsa, []));
            using var deniedRead = await customerHttp.GetAsync("customers/1");
            Assert.Equal(HttpStatusCode.Forbidden, deniedRead.StatusCode);
            customerHttp.DefaultRequestHeaders.Authorization = new("Bearer", Token(rsa, ["legacy-customer.customers.read"]));
            using var deniedWrite = await customerHttp.PutAsJsonAsync("customers/addresses/1", new CustomerAddressInput(null, "Forbidden mutation", null, null, null, null, 66));
            Assert.Equal(HttpStatusCode.Forbidden, deniedWrite.StatusCode);
            catalogHttp.DefaultRequestHeaders.Authorization = new("Bearer", Token(rsa, []));
            using var deniedLookup = await catalogHttp.GetAsync("api/v1/thai-addresses/autocomplete?postcode=11120");
            Assert.Equal(HttpStatusCode.Forbidden, deniedLookup.StatusCode);
            customerHttp.DefaultRequestHeaders.Authorization = new("Bearer", serviceToken);
            catalogHttp.DefaultRequestHeaders.Authorization = new("Bearer", serviceToken);
            using var initial = await customerHttp.GetAsync("customers/1");
            Assert.Equal(HttpStatusCode.OK, initial.StatusCode);
            using var before = JsonDocument.Parse(await initial.Content.ReadAsStringAsync());
            var shippingBefore = before.RootElement.GetProperty("ShippingAddress").GetRawText();
            var billingId = before.RootElement.GetProperty("BillingAddressId").GetInt32();
            using var lookup = await catalogHttp.GetAsync("api/v1/thai-addresses/autocomplete?postcode=11120&limit=8&q=");
            Assert.Equal(HttpStatusCode.OK, lookup.StatusCode);
            using var tuplePage = JsonDocument.Parse(await lookup.Content.ReadAsStringAsync());
            Assert.StartsWith("thailand-geography-json:", tuplePage.RootElement.GetProperty("datasetVersion").GetString());
            var tuple = tuplePage.RootElement.GetProperty("items")[0];
            var name = culture == "th" ? "nameTh" : "nameEn";
            var expectedState = tuple.GetProperty("province").GetProperty(name).GetString();
            var expectedCity = tuple.GetProperty("district").GetProperty(name).GetString();

            await using var baseWeb = authority.Web(retained: false);
            await using var web = baseWeb.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            {
                services.AddHttpClient("customers", client => client.BaseAddress = customerOrigin);
                services.AddHttpClient("catalog", client => client.BaseAddress = catalogOrigin);
                services.RemoveAll<IServiceAccessTokenProvider>();
                services.AddSingleton<IServiceAccessTokenProvider>(new Tokens(serviceToken));
                // Country list is a separate consumer contract; this bounded case fixes the seeded Thailand ID.
                services.RemoveAll<ICountryClient>();
                services.AddSingleton<ICountryClient, Countries>();
            }));
            var webOrigin = Origin();
            web.UseKestrel(webOrigin.Port);
            using var host = web.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = webOrigin, AllowAutoRedirect = false });
            using var playwright = await Playwright.CreateAsync();
            await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
            await using var context = await browser.NewContextAsync(new() { ViewportSize = new() { Width = width, Height = 850 } });
            await using var page = await context.NewPageAsync();
            var route = "/Member/Account/Manage/Address?culture=" + culture;
            await page.GotoAsync(new Uri(webOrigin, "/Account/Login?culture=en&returnUrl=" + Uri.EscapeDataString(route)).ToString());
            await page.Locator("#Email").FillAsync("member-crawl@example.test");
            try { await page.Locator("#Password").FillAsync(authority.Password); }
            catch (PlaywrightException) { throw new InvalidOperationException("Synthetic login credential entry failed; credential-bearing browser diagnostics suppressed."); }
            await page.Locator("button[type=submit]").First.ClickAsync();
            await page.Locator("#BillingAddress1").WaitForAsync();
            Assert.Equal("Stored billing", await page.Locator("#BillingAddress1").InputValueAsync());
            var cookie = Assert.Single(await context.CookiesAsync(), value => value.Name == "__Host-Maliev.Legacy.Session");
            Assert.True(cookie.HttpOnly && cookie.Secure);
            await page.Locator("#BillingBuilding").FillAsync("Synthetic building");
            await page.Locator("#BillingAddress1").FillAsync("36/1 synthetic road");
            await page.Locator("#BillingAddress2").FillAsync("Manual district detail retained");
            var widget = page.Locator("[data-thai-lookup]:has(#member-billing-lookup-query)");
            var reply = page.WaitForResponseAsync(response => response.Url.Contains("/lookups/addresses/search", StringComparison.Ordinal));
            await widget.Locator("[data-lookup-query]").FillAsync("11120");
            var lookupResponse = await reply;
            Assert.Equal(200, lookupResponse.Status);
            var browserPage = await lookupResponse.JsonAsync();
            Assert.Equal(tuplePage.RootElement.GetProperty("datasetVersion").GetString(), browserPage!.Value.GetProperty("datasetVersion").GetString());
            await widget.Locator("[role=option]").First.ClickAsync();
            await widget.Locator("[data-lookup-apply]").ClickAsync();
            Assert.Equal(expectedState, await page.Locator("#BillingState").InputValueAsync());
            Assert.Equal(expectedCity, await page.Locator("#BillingCity").InputValueAsync());
            Assert.Equal("11120", await page.Locator("#BillingPostalCode").InputValueAsync());
            Assert.Equal("Manual district detail retained", await page.Locator("#BillingAddress2").InputValueAsync());
            var save = page.WaitForResponseAsync(response => response.Request.Method == "POST" && response.Url.Contains("handler=UpdateAddress", StringComparison.Ordinal));
            await page.Locator("[data-migration-component=member-address-content] button[type=submit]").ClickAsync();
            Assert.Equal(302, (await save).Status);
            await page.Locator("#BillingAddress1").WaitForAsync();
            using var readback = await customerHttp.GetAsync("customers/1");
            Assert.Equal(HttpStatusCode.OK, readback.StatusCode);
            using var after = JsonDocument.Parse(await readback.Content.ReadAsStringAsync());
            var billing = after.RootElement.GetProperty("BillingAddress");
            Assert.Equal(billingId, after.RootElement.GetProperty("BillingAddressId").GetInt32());
            Assert.Equal(expectedState, billing.GetProperty("State").GetString());
            Assert.Equal(expectedCity, billing.GetProperty("City").GetString());
            Assert.Equal("11120", billing.GetProperty("PostalCode").GetString());
            Assert.Equal("Synthetic building", billing.GetProperty("Building").GetString());
            Assert.Equal("36/1 synthetic road", billing.GetProperty("AddressLine1").GetString());
            Assert.Equal("Manual district detail retained", billing.GetProperty("AddressLine2").GetString());
            Assert.Equal(66, billing.GetProperty("CountryId").GetInt32());
            Assert.Equal(shippingBefore, after.RootElement.GetProperty("ShippingAddress").GetRawText());
            await page.GotoAsync(new Uri(webOrigin, route).ToString());
            await page.ReloadAsync();
            Assert.Equal(expectedState, await page.Locator("#BillingState").InputValueAsync());
            Assert.Equal(expectedCity, await page.Locator("#BillingCity").InputValueAsync());
            Assert.Equal("11120", await page.Locator("#BillingPostalCode").InputValueAsync());
            Assert.Equal("Synthetic building", await page.Locator("#BillingBuilding").InputValueAsync());
            Assert.Equal("36/1 synthetic road", await page.Locator("#BillingAddress1").InputValueAsync());
            Assert.Equal("Manual district detail retained", await page.Locator("#BillingAddress2").InputValueAsync());
            Assert.Equal("Distinct shipping", await page.Locator("#ShippingAddress1").InputValueAsync());
            Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth"));
            var html = await page.ContentAsync();
            Assert.False(html.Contains(serviceToken, StringComparison.Ordinal), "Member HTML must not disclose the fixture workload credential.");
            var evidence = Path.Combine(AppContext.BaseDirectory, "TestResults", "billing-persistence");
            Directory.CreateDirectory(evidence);
            // No traces, cookies, request headers, raw HTML or credentials are retained.
            await page.ScreenshotAsync(new() { Path = Path.Combine(evidence, culture + ".png"), FullPage = true });
            await File.WriteAllTextAsync(Path.Combine(evidence, culture + ".json"), JsonSerializer.Serialize(new
            {
                surface = "member-billing", culture, width, candidateHead = Environment.GetEnvironmentVariable("MALIEV_BILLING_CANDIDATE_HEAD"),
                datasetVersion = tuplePage.RootElement.GetProperty("datasetVersion").GetString(),
                catalogStatus = (int)lookup.StatusCode, saveStatus = 302, readbackStatus = (int)readback.StatusCode,
                reloadVerified = true, shippingPreserved = true, manualDetailPreserved = true,
                auth = "Pinned Auth normal login, encrypted cookie, Redis session; synthetic scoped service JWT",
            }));
        }
        finally
        {
            foreach (var process in new[] { catalog, customer }) process.Dispose();
        }
    }

    private static string Required(string name) => Environment.GetEnvironmentVariable(name) is { } value && File.Exists(value)
        ? value : throw new InvalidOperationException("Pinned hosted binary required: " + name);
    private static Uri Origin()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return new Uri($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}");
    }
    private static string Token(RSA rsa, string[] permissions) => new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
        "https://billing-proof.example.test", "billing-proof-services", [new Claim("sub", "billing-disposable-workload"), .. permissions.Select(value => new Claim("permissions", value))],
        DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(20), new SigningCredentials(new RsaSecurityKey(rsa), SecurityAlgorithms.RsaSha256)));
    private static OwnedChild Child(string dll, Dictionary<string, string> environment)
    {
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = Path.GetDirectoryName(dll)!, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(dll);
        var essentials = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "PATH", "HOME", "TMPDIR", "TEMP", "TMP", "SystemRoot", "DOTNET_ROOT", "DOTNET_ROOT_X64" };
        var inherited = start.Environment.Where(pair => essentials.Contains(pair.Key)).ToArray();
        start.Environment.Clear();
        foreach (var pair in inherited) start.Environment[pair.Key] = pair.Value;
        foreach (var pair in environment) start.Environment[pair.Key] = pair.Value;
        start.Environment["LEGACY_DEPLOY_ENABLED"] = "false";
        var child = Process.Start(start) ?? throw new InvalidOperationException("Owned child failed to start.");
        child.OutputDataReceived += (_, _) => { };
        child.ErrorDataReceived += (_, _) => { };
        child.BeginOutputReadLine();
        child.BeginErrorReadLine();
        return new OwnedChild(child);
    }
    private static async Task Ready(HttpClient http, OwnedChild child, string path)
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
    // Lease acquired immediately at start; disposal also covers seed timeout and partial service startup.
    private sealed class OwnedChild(Process process) : IDisposable
    {
        private bool disposed;
        public bool HasExited => process.HasExited;
        public int ExitCode => process.ExitCode;
        public Task WaitForExitAsync() => process.WaitForExitAsync();
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            try
            {
                if (!process.HasExited)
                {
                    // Windowless dotnet children have no main window; try graceful close before exact PID termination.
                    if (!process.CloseMainWindow() || !process.WaitForExit(2000)) process.Kill();
                    if (!process.WaitForExit(10000)) throw new TimeoutException("Owned child cleanup timed out.");
                }
            }
            finally { process.Dispose(); }
        }
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
