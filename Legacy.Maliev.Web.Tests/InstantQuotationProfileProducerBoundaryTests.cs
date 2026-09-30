using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Components.Pages.InstantQuotation;
using Legacy.Maliev.Web.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Playwright;
using Npgsql;
using StackExchange.Redis;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Actual separately pinned CustomerService Program, normal RS256 auth, disposable PG/Redis and Web HTTP.</summary>
public sealed class InstantQuotationProfileProducerBoundaryTests
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, System.Collections.Concurrent.ConcurrentQueue<string>> ChildErrors = new();
    [Fact]
    public async Task RetryEnabledProgram_ReadWriteReplayAndCopyOnWrite()
    {
        var producerDll = Environment.GetEnvironmentVariable("MALIEV_PROFILE_PRODUCER_DLL");
        var seedDll = Environment.GetEnvironmentVariable("MALIEV_PROFILE_SEED_DLL");
        Assert.True(File.Exists(producerDll) && File.Exists(seedDll), "Separately built pinned producer and disposable seed required.");
        var database = $"profile_contract_{Guid.NewGuid():N}";
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").WithDatabase(database).Build();
        await postgres.StartAsync();
        var connection = postgres.GetConnectionString();
        using (var seed = Child(seedDll!, new()
        {
            ["MALIEV_PROFILE_DISPOSABLE_CONNECTION"] = connection,
            ["MALIEV_PROFILE_DISPOSABLE_DATABASE"] = database,
        }))
        {
            await seed.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60));
            Assert.Equal(0, seed.ExitCode);
        }
        using var rsa = RSA.Create(2048);
        var origin = new Uri($"http://127.0.0.1:{FreePort()}");
        using var producer = Child(producerDll!, new()
        {
            ["DOTNET_ENVIRONMENT"] = "Development",
            ["ASPNETCORE_ENVIRONMENT"] = "Development",
            ["ASPNETCORE_URLS"] = origin.ToString(),
            ["ConnectionStrings__CustomerDbContext"] = connection,
            ["Cache__RedisEnabled"] = "false",
            ["Jwt__PublicKey"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(rsa.ExportSubjectPublicKeyInfoPem())),
            ["Jwt__Issuer"] = "https://iam.maliev.com",
            ["Jwt__Audience"] = "maliev-services",
            ["Logging__LogLevel__Default"] = "Warning",
        });
        try
        {
            using var http = new HttpClient { BaseAddress = origin };
            await WaitForProducer(http, producer);
            http.DefaultRequestHeaders.Authorization = new("Bearer", Token(rsa, 2));
            var otherBefore = await http.GetStringAsync("customers/2/instant-quotation-profile-completion");
            http.DefaultRequestHeaders.Authorization = new("Bearer", Token(rsa, 1));
            using var graph = await http.GetAsync("customers/1/instant-quotation-profile-completion");
            Assert.Equal(HttpStatusCode.OK, graph.StatusCode);
            Assert.Equal("2", graph.Headers.GetValues("X-Quotation-Profile-Completion-Contract").Single());
            var key = Guid.NewGuid();
            async Task<string> Complete()
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "customers/1/instant-quotation-profile-completion");
                request.Headers.IfMatch.Add(graph.Headers.ETag!);
                request.Headers.Add("Idempotency-Key", key.ToString("D"));
                request.Content = new StringContent("{\"TaxNumber\":\"0115562011815\",\"TaxBranch\":\"branch\",\"TaxBranchCode\":\"00003\",\"ShipToBillingAddress\":false}", Encoding.UTF8, "application/json");
                using var receipt = await http.SendAsync(request);
                var body = await receipt.Content.ReadAsStringAsync();
                Assert.True(receipt.IsSuccessStatusCode, $"Disposable retry-enabled Program POST: {(int)receipt.StatusCode} {body}");
                return body;
            }
            var receipt = await Complete();
            Assert.Equal(receipt, await Complete());
            using var receiptJson = JsonDocument.Parse(receipt);
            Assert.NotEqual(key, receiptJson.RootElement.GetProperty("CompletionId").GetGuid());
            Assert.NotEqual(Guid.Empty, receiptJson.RootElement.GetProperty("CompletionId").GetGuid());
            http.DefaultRequestHeaders.Authorization = new("Bearer", Token(rsa, 2));
            Assert.Equal(otherBefore, await http.GetStringAsync("customers/2/instant-quotation-profile-completion"));
            http.DefaultRequestHeaders.Authorization = new("Bearer", Token(rsa, 1));
            using var owner = JsonDocument.Parse(await http.GetStringAsync("customers/1/instant-quotation-profile-completion"));
            Assert.Equal("0115562011815 (สาขาที่ 00003)", owner.RootElement.GetProperty("Company").GetProperty("TaxNumber").GetString());
            Assert.NotEqual(owner.RootElement.GetProperty("BillingAddressId").GetInt32(), owner.RootElement.GetProperty("ShippingAddressId").GetInt32());
        }
        finally
        {
            if (!producer.HasExited) producer.Kill(entireProcessTree: true);
            await producer.WaitForExitAsync();
        }
    }
    [Fact]
    public async Task OwnerProfile_RealProducer_DurableQuotationSurvivesReadinessFailureAndExactReplay()
    {
        var producerDll = Environment.GetEnvironmentVariable("MALIEV_PROFILE_PRODUCER_DLL");
        var seedDll = Environment.GetEnvironmentVariable("MALIEV_PROFILE_SEED_DLL");
        var authDll = Environment.GetEnvironmentVariable("MALIEV_PROFILE_AUTH_DLL");
        var authSeedDll = Environment.GetEnvironmentVariable("MALIEV_PROFILE_AUTH_SEED_DLL");
        Assert.True(File.Exists(producerDll), "Run scripts/prepare-profile-producer-boundary.ps1 before this contract lane; producer binary is required.");
        Assert.True(File.Exists(seedDll), "The separately built disposable seed binary is required.");
        Assert.True(File.Exists(authDll) && File.Exists(authSeedDll), "Separately pinned Auth Program and disposable identity seed binaries are required.");
        var database = $"profile_contract_{Guid.NewGuid():N}";
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").WithDatabase(database).Build();
        await using var redis = new RedisBuilder("redis:8.4-alpine").Build();
        var identityDatabase = $"profile_identity_{Guid.NewGuid():N}";
        await using var identityPostgres = new PostgreSqlBuilder("postgres:18-alpine").WithDatabase(identityDatabase).Build();
        await postgres.StartAsync();
        await identityPostgres.StartAsync();
        await redis.StartAsync();
        var connection = postgres.GetConnectionString();
        using var rsa = RSA.Create(2048);
        using (var seed = Child(seedDll!, new()
        {
            ["MALIEV_PROFILE_DISPOSABLE_CONNECTION"] = connection,
            ["MALIEV_PROFILE_DISPOSABLE_DATABASE"] = database,
        }))
        {
            await seed.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60));
            Assert.Equal(0, seed.ExitCode);
        }
        var producerOrigin = new Uri($"http://127.0.0.1:{FreePort()}");
        Process? auth = null;
        using var producer = Child(producerDll!, new()
        {
            ["DOTNET_ENVIRONMENT"] = "Development", // NOT Testing: no signature/lifetime validation bypass.
            ["ASPNETCORE_ENVIRONMENT"] = "Development",
            ["ASPNETCORE_URLS"] = producerOrigin.ToString(),
            ["ConnectionStrings__CustomerDbContext"] = connection,
            ["Cache__RedisEnabled"] = "false",
            ["Jwt__PublicKey"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(rsa.ExportSubjectPublicKeyInfoPem())),
            ["Jwt__Issuer"] = "https://iam.maliev.com",
            ["Jwt__Audience"] = "maliev-services",
            ["Logging__LogLevel__Default"] = "Warning",
        });
        try
        {
            using (var seed = Child(authSeedDll!, new()
            {
                ["MALIEV_PROFILE_DISPOSABLE_CONNECTION"] = identityPostgres.GetConnectionString(),
                ["MALIEV_PROFILE_DISPOSABLE_DATABASE"] = identityDatabase,
            }))
            {
                await seed.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60));
                Assert.Equal(0, seed.ExitCode);
            }
            var authOrigin = new Uri($"http://127.0.0.1:{FreePort()}");
            auth = Child(authDll!, new()
            {
                ["DOTNET_ENVIRONMENT"] = "Development",
                ["ASPNETCORE_ENVIRONMENT"] = "Development",
                ["ASPNETCORE_URLS"] = authOrigin.ToString(),
                ["ConnectionStrings__CustomerIdentity"] = identityPostgres.GetConnectionString(),
                ["ConnectionStrings__EmployeeIdentity"] = identityPostgres.GetConnectionString(),
                ["ConnectionStrings__RefreshSessions"] = identityPostgres.GetConnectionString(),
                ["Jwt__PrivateKeyPem"] = rsa.ExportPkcs8PrivateKeyPem(),
                ["Jwt__KeyId"] = "disposable-profile",
                ["Jwt__Issuer"] = "https://iam.maliev.com",
                ["Jwt__Audience"] = "maliev-services",
                ["Logging__LogLevel__Default"] = "Warning",
            });
            using var authHttp = new HttpClient { BaseAddress = authOrigin };
            await WaitForProducer(authHttp, auth, "auth/v1/customer-self-service/identity");
            authHttp.DefaultRequestHeaders.Authorization = new("Bearer", Token(rsa, 1));
            using var identityResponse = await authHttp.GetAsync("auth/v1/customer-self-service/identity");
            Assert.Equal(HttpStatusCode.OK, identityResponse.StatusCode);
            using var identityBody = JsonDocument.Parse(await identityResponse.Content.ReadAsStringAsync());
            Assert.Equal(1, identityBody.RootElement.GetProperty("customerId").GetInt32());
            Assert.Equal("0812345678", identityBody.RootElement.GetProperty("mobile").GetString());
            using var producerHttp = new HttpClient { BaseAddress = producerOrigin };
            await WaitForProducer(producerHttp, producer);
            using var unauthorized = await producerHttp.GetAsync("customers/1/instant-quotation-profile-completion");
            Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
            producerHttp.DefaultRequestHeaders.Authorization = new("Bearer", Token(rsa, 2));
            using var crossOwner = await producerHttp.GetAsync("customers/1/instant-quotation-profile-completion");
            Assert.Equal(HttpStatusCode.Forbidden, crossOwner.StatusCode);
            using var otherResponse = await producerHttp.GetAsync("customers/2/instant-quotation-profile-completion");
            var otherBefore = await otherResponse.Content.ReadAsStringAsync();
            Assert.True(otherResponse.IsSuccessStatusCode, $"Disposable producer owner GET: {(int)otherResponse.StatusCode} {otherBefore}");
            producerHttp.DefaultRequestHeaders.Authorization = new("Bearer", Token(rsa, 1, issuer: "https://wrong.example"));
            using var wrongIssuer = await producerHttp.GetAsync("customers/1/instant-quotation-profile-completion");
            Assert.Equal(HttpStatusCode.Unauthorized, wrongIssuer.StatusCode);

            var wire = new Wire(producerOrigin, authOrigin);
            var quotation = new Quotes(connection);
            var tempData = new TempData();
            using var redisConnection = await ConnectionMultiplexer.ConnectAsync(redis.GetConnectionString());
            await using var web = new ProfileWebFactory(wire, Token(rsa, 1), quotation, tempData, redisConnection);
            var webOrigin = new Uri($"http://127.0.0.1:{FreePort()}");
            web.UseKestrel(webOrigin.Port);
            using var browserHost = web.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = webOrigin, AllowAutoRedirect = false });
            browserHost.DefaultRequestHeaders.Add("X-Profile-Test-Owner", "1");
            browserHost.DefaultRequestHeaders.Accept.Add(new("application/json"));
            tempData.ShowCustomerForm();
            using var pageResponse = await browserHost.GetAsync("/InstantQuotation/3D-Printing?culture=en");
            var source = WebUtility.HtmlDecode(await pageResponse.Content.ReadAsStringAsync());
            // The test's loopback HTTP client does not retain Secure cookies; browser
            // loopback handling is separately exercised below. Forward only fixture cookies.
            if (pageResponse.Headers.TryGetValues("Set-Cookie", out var cookies))
                browserHost.DefaultRequestHeaders.Add("Cookie", string.Join("; ", cookies.Select(value => value.Split(';')[0])));
            var token = Regex.Match(source, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
            Assert.NotEmpty(token);
            Assert.True(source.Contains("instant-quote-firstname", StringComparison.Ordinal), $"SSR form absent. Route owner={source.Contains("data-migration-route-owner", StringComparison.Ordinal)}, temp loads={tempData.Loads}, temp status={tempData.Values.GetValueOrDefault("InstantQuotationSubmissionStatus")}");
            Assert.Contains("readonly", Regex.Match(source, "<input[^>]*id=\"instant-quote-firstname\"[^>]*>").Value, StringComparison.Ordinal);
            Assert.Contains("value=\"Distinct shipping\"", source, StringComparison.Ordinal);

            foreach (var absentEmail in new[] { "NULL", "''", "' '" })
            {
                await Sql(identityPostgres.GetConnectionString(), $"UPDATE \"AspNetUsers\" SET \"Email\"={absentEmail} WHERE \"Id\"='fixture-owner-1'");
                using var absent = await browserHost.PostAsync("/InstantQuotation/3D-Printing?handler=SubmitRequest", Form(token));
                using var absentResult = JsonDocument.Parse(await absent.Content.ReadAsStringAsync());
                Assert.Equal("retry", absentResult.RootElement.GetProperty("outcome").GetString());
                Assert.Contains("Please sign in again", absentResult.RootElement.GetProperty("errors").ToString(), StringComparison.Ordinal);
                Assert.Equal(0, quotation.Count);
                Assert.Empty(wire.Posts);
            }
            await Sql(connection, "UPDATE \"Customer\" SET \"Email\"='saved@example.test' WHERE \"ID\"=1");
            await Sql(identityPostgres.GetConnectionString(), "UPDATE \"AspNetUsers\" SET \"Email\"=NULL WHERE \"Id\"='fixture-owner-1'");
            tempData.ShowCustomerForm();
            using var storedEmailPage = await browserHost.GetAsync("/InstantQuotation/3D-Printing?culture=en");
            var storedEmailSource = WebUtility.HtmlDecode(await storedEmailPage.Content.ReadAsStringAsync());
            Assert.Contains("value=\"saved@example.test\"", storedEmailSource, StringComparison.Ordinal);
            Assert.Contains("readonly", Regex.Match(storedEmailSource, "<input[^>]*id=\"instant-quote-email\"[^>]*>").Value, StringComparison.Ordinal);
            Assert.Equal(0, quotation.Count);
            await Sql(connection, "UPDATE \"Customer\" SET \"Email\"='' WHERE \"ID\"=1");

            // An eligible old signed JWT can outlive an identity email change. It must
            // not create a quote using current email while producer persists old email.
            await Sql(identityPostgres.GetConnectionString(), "UPDATE \"AspNetUsers\" SET \"Email\"='current@example.test' WHERE \"Id\"='fixture-owner-1'");
            using var staleEmail = await browserHost.PostAsync("/InstantQuotation/3D-Printing?handler=SubmitRequest", Form(token));
            Assert.Equal(HttpStatusCode.OK, staleEmail.StatusCode);
            using var staleResult = JsonDocument.Parse(await staleEmail.Content.ReadAsStringAsync());
            Assert.Equal("retry", staleResult.RootElement.GetProperty("outcome").GetString());
            Assert.Contains("Please sign in again", staleResult.RootElement.GetProperty("errors").ToString(), StringComparison.Ordinal);
            Assert.Equal(0, quotation.Count);
            Assert.Empty(wire.Posts);
            web.OwnerSessions.Token = Token(rsa, 1, email: "current@example.test");

            // Runtime capability missing BEFORE quote creation must fail closed and remain editable.
            await Sql(connection, "ALTER TABLE \"QuotationProfileCompletionOperation\" RENAME TO \"UnavailableProfileReceipt\"");
            using var notReady = await browserHost.PostAsync("/InstantQuotation/3D-Printing?handler=SubmitRequest", Form(token));
            Assert.Equal(HttpStatusCode.OK, notReady.StatusCode);
            Assert.Equal("retry", JsonDocument.Parse(await notReady.Content.ReadAsStringAsync()).RootElement.GetProperty("outcome").GetString());
            Assert.Equal(0, quotation.Count);
            await Sql(connection, "ALTER TABLE \"UnavailableProfileReceipt\" RENAME TO \"QuotationProfileCompletionOperation\"");

            // Producer loses readiness after the durable quotation response, before profile POST.
            quotation.RemoveReadinessAfterPersist = true;
            using var partial = await browserHost.PostAsync("/InstantQuotation/3D-Printing?handler=SubmitRequest", Form(token));
            Assert.Equal(HttpStatusCode.OK, partial.StatusCode);
            Assert.Equal("terminal", JsonDocument.Parse(await partial.Content.ReadAsStringAsync()).RootElement.GetProperty("outcome").GetString());
            Assert.Equal("partial", tempData.Values["InstantQuotationSubmissionStatus"]);
            Assert.Equal(417, tempData.Values["InstantQuotationRequestReference"]);
            Assert.Equal(1, quotation.Count);
            var firstOperation = Assert.Single(wire.Posts);
            await Sql(connection, "ALTER TABLE \"UnavailableProfileReceipt\" RENAME TO \"QuotationProfileCompletionOperation\"");

            using var retry = await browserHost.PostAsync("/InstantQuotation/3D-Printing?handler=SubmitRequest", Form(token, forgedRetry: true));
            Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
            Assert.Equal("completed", tempData.Values["InstantQuotationSubmissionStatus"]);
            Assert.Equal(417, tempData.Values["InstantQuotationRequestReference"]);
            Assert.Equal(1, quotation.Count);
            Assert.Equal(2, wire.Posts.Count);
            Assert.Equal(firstOperation, wire.Posts[1]); // exact key, body and If-Match after producer restart-like failure.
            Assert.Contains("\"TaxBranchCode\":\"00003\"", firstOperation.Body, StringComparison.Ordinal);
            Assert.DoesNotContain("Email", firstOperation.Body, StringComparison.Ordinal);
            await using var db = new NpgsqlConnection(connection);
            await db.OpenAsync();
            await using var receiptCount = new NpgsqlCommand("SELECT count(*) FROM \"QuotationProfileCompletionOperation\"", db);
            Assert.Equal(1L, await receiptCount.ExecuteScalarAsync());

            producerHttp.DefaultRequestHeaders.Authorization = new("Bearer", Token(rsa, 2));
            Assert.Equal(otherBefore, await producerHttp.GetStringAsync("customers/2/instant-quotation-profile-completion"));
            producerHttp.DefaultRequestHeaders.Authorization = new("Bearer", Token(rsa, 1));
            var ownerAfter = await producerHttp.GetStringAsync("customers/1/instant-quotation-profile-completion");
            Assert.Contains("current@example.test", ownerAfter, StringComparison.Ordinal);
            Assert.Equal("0812345678", JsonDocument.Parse(ownerAfter).RootElement.GetProperty("Mobile").GetString());
            Assert.Contains("0115562011815 (สาขาที่ 00003)", WebUtility.HtmlDecode(JsonDocument.Parse(ownerAfter).RootElement.GetProperty("Company").GetProperty("TaxNumber").GetString()), StringComparison.Ordinal);
            Assert.Contains("Distinct shipping", ownerAfter, StringComparison.Ordinal);
            Assert.DoesNotContain("forged", ownerAfter, StringComparison.Ordinal);

            using var playwright = await Playwright.CreateAsync();
            await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
            foreach (var variant in new[] { (320, "en", ColorScheme.Light), (390, "th", ColorScheme.Dark), (820, "th", ColorScheme.Light), (1280, "en", ColorScheme.Dark) })
            {
                tempData.ShowCustomerForm();
                await using var context = await browser.NewContextAsync(new()
                {
                    ViewportSize = new() { Width = variant.Item1, Height = 800 },
                    ColorScheme = variant.Item3,
                    ExtraHTTPHeaders = new Dictionary<string, string> { ["X-Profile-Test-Owner"] = "1" },
                });
                await context.RouteAsync("**/*", route => route.Request.Url.StartsWith(webOrigin.ToString(), StringComparison.Ordinal)
                    ? route.ContinueAsync() : route.AbortAsync());
                await using var page = await context.NewPageAsync();
                await page.GotoAsync($"{webOrigin}InstantQuotation/3D-Printing?culture={variant.Item2}");
                var rejectCookies = page.Locator("#cookieConsent [data-consent-action='reject']");
                if (await rejectCookies.IsVisibleAsync()) await rejectCookies.ClickAsync();
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
                Assert.Equal("Owner", await page.Locator("#instant-quote-firstname").InputValueAsync());
                Assert.True(await page.Locator("#instant-quote-firstname").EvaluateAsync<bool>("element => element.readOnly"));
                Assert.True(await page.Locator("#instant-quote-ship-to-billing").IsDisabledAsync());
                Assert.False(await page.Locator("#instant-quote-ship-to-billing").IsCheckedAsync());
                Assert.Equal("Distinct shipping", await page.Locator("#instant-quote-shipping-street1").InputValueAsync());
                foreach (var field in new[] { "billing-street1", "billing-street2", "shipping-street1", "shipping-street2" })
                {
                    var saved = page.Locator("#instant-quote-" + field);
                    Assert.NotEmpty(await saved.InputValueAsync());
                    Assert.True(await saved.EvaluateAsync<bool>("element => element.readOnly"));
                    Assert.Equal("true", await saved.GetAttributeAsync("aria-readonly"));
                }
                var note = variant.Item2 == "th" ? "ข้อมูลบัญชีที่บันทึกไว้ถูกล็อกไว้" : "Saved account values are locked.";
                Assert.Contains(note, await page.Locator("[data-workflow-customer-details-content]").InnerTextAsync(), StringComparison.Ordinal);
                Assert.False(await page.Locator("#instant-quote-shipping-building").EvaluateAsync<bool>("element => element.readOnly"));
                await page.Locator("#instant-quote-shipping-building").FocusAsync();
                await Assertions.Expect(page.Locator("#instant-quote-shipping-building")).ToBeFocusedAsync();
                await page.Keyboard.TypeAsync("Optional detail");
                Assert.Equal("Optional detail", await page.Locator("#instant-quote-shipping-building").InputValueAsync());
                Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"));
                await page.EvaluateAsync("() => { document.querySelector('#instant-quote-firstname').value = 'forged'; document.querySelector('#instant-quote-ship-to-billing').checked = true; }");
                var tampered = await page.EvaluateAsync<string>("async () => { const form = document.querySelector('#instant-quotation-form'); const response = await fetch(form.action, {method:'POST', headers:{Accept:'application/json'}, body:new URLSearchParams(new FormData(form))}); return await response.text(); }");
                Assert.Equal("terminal", JsonDocument.Parse(tampered).RootElement.GetProperty("outcome").GetString());
                Assert.Equal(1, quotation.Count);
            }
        }
        finally
        {
            if (auth is not null)
            {
                if (!auth.HasExited) auth.Kill(entireProcessTree: true);
                await auth.WaitForExitAsync();
                auth.Dispose();
            }
            if (!producer.HasExited) producer.Kill(entireProcessTree: true);
            await producer.WaitForExitAsync();
        }
    }

    private static FormUrlEncodedContent Form(string token, bool forgedRetry = false) => new(new Dictionary<string, string>
    {
        ["__RequestVerificationToken"] = token,
        ["FirstName"] = "forged",
        ["LastName"] = "forged",
        ["Email"] = "forged@example.test",
        ["Mobile"] = forgedRetry ? "different" : "0800000000",
        ["Telephone"] = "020000000",
        ["Company"] = "forged company",
        ["TaxNumber"] = "0115562011815",
        ["TaxBranch"] = "branch",
        ["TaxBranchCode"] = "3",
        ["Country"] = "Thailand",
        ["BillingBuilding"] = "Filled building",
        ["BillingStreet1"] = "forged billing",
        ["BillingStreet2"] = "forged billing district",
        ["BillingCity"] = "forged city",
        ["BillingProvince"] = "forged province",
        ["BillingPostalCode"] = "99999",
        ["ShipToBillingAddress"] = "true",
        ["ShippingStreet1"] = "forged shipping",
        ["ShippingStreet2"] = "forged shipping district",
        ["ShippingCountry"] = "Thailand",
        ["Description"] = forgedRetry ? "different" : "Synthetic profile boundary fixture",
    });
    private static string Token(RSA rsa, int id, string issuer = "https://iam.maliev.com", string? email = null) => new JwtSecurityTokenHandler().WriteToken(
        new JwtSecurityToken(issuer, "maliev-services", [new Claim("sub", $"fixture-owner-{id}"), new Claim("identity_kind", "customer"),
            new Claim("legacy_database_id", id.ToString(System.Globalization.CultureInfo.InvariantCulture)), new Claim("email", email ?? (id == 1 ? "owner@example.test" : "other@example.test"))],
            DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(20), new SigningCredentials(new RsaSecurityKey(rsa), SecurityAlgorithms.RsaSha256)));
    private static int FreePort() { using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); return ((IPEndPoint)listener.LocalEndpoint).Port; }
    private static Process Child(string dll, Dictionary<string, string> environment)
    {
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = Path.GetDirectoryName(dll)!, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(dll);
        // Do not inherit host connection/auth/exporter configuration into a disposable child.
        foreach (var key in start.Environment.Keys.Where(key => key.StartsWith("ConnectionStrings", StringComparison.OrdinalIgnoreCase)
            || key.StartsWith("Jwt", StringComparison.OrdinalIgnoreCase) || key.StartsWith("OTEL_", StringComparison.OrdinalIgnoreCase)).ToArray()) start.Environment.Remove(key);
        foreach (var item in environment) start.Environment[item.Key] = item.Value;
        var child = Process.Start(start) ?? throw new InvalidOperationException("Disposable child failed to start.");
        var errors = new System.Collections.Concurrent.ConcurrentQueue<string>();
        ChildErrors[child.Id] = errors;
        child.ErrorDataReceived += (_, args) => { if (args.Data is { } line && errors.Count < 30) errors.Enqueue(line[..Math.Min(line.Length, 512)]); };
        child.BeginOutputReadLine(); child.BeginErrorReadLine();
        return child;
    }
    private static async Task WaitForProducer(HttpClient http, Process process, string path = "customers/1/instant-quotation-profile-completion")
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (!budget.IsCancellationRequested)
        {
            if (process.HasExited) throw new InvalidOperationException($"Disposable producer exited with {process.ExitCode}: {string.Join("\n", ChildErrors[process.Id])}");
            try { using var response = await http.GetAsync(path, budget.Token); if (response.StatusCode == HttpStatusCode.Unauthorized) return; }
            catch (HttpRequestException) { }
            await Task.Delay(100, budget.Token);
        }
        throw new TimeoutException("Disposable producer readiness timed out.");
    }
    private static async Task Sql(string connection, string sql)
    {
        await using var db = new NpgsqlConnection(connection); await db.OpenAsync();
        await using var command = new NpgsqlCommand(sql, db); await command.ExecuteNonQueryAsync();
    }

    private sealed record Post(string Key, string EntityTag, string Body);
    private sealed class Wire(Uri origin, Uri authOrigin) : HttpMessageHandler, IHttpClientFactory
    {
        private readonly HttpMessageInvoker upstream = new(new SocketsHttpHandler());
        public List<Post> Posts { get; } = [];
        public HttpClient CreateClient(string name) => new(this, false) { BaseAddress = name == "auth" ? authOrigin : origin };
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Post) Posts.Add(new(request.Headers.GetValues("Idempotency-Key").Single(), request.Headers.IfMatch.Single().ToString(), await request.Content!.ReadAsStringAsync(cancellationToken)));
            return await upstream.SendAsync(request, cancellationToken);
        }
        protected override void Dispose(bool disposing) { if (disposing) upstream.Dispose(); base.Dispose(disposing); }
    }
    private sealed class Quotes(string connection) : IQuotationClient
    {
        public int Count { get; private set; }
        public bool RemoveReadinessAfterPersist { get; set; }
        public async Task<QuotationRequestResult> CreateRequestAsync(QuotationRequestSubmission submission, string idempotencyKey, CancellationToken cancellationToken)
        {
            Count++;
            Assert.Equal("Owner", submission.FirstName);
            Assert.Equal("current@example.test", submission.Email);
            Assert.Equal("0115562011815 (สาขาที่ 00003)", submission.TaxIdentification);
            if (RemoveReadinessAfterPersist) { RemoveReadinessAfterPersist = false; await Sql(connection, "ALTER TABLE \"QuotationProfileCompletionOperation\" RENAME TO \"UnavailableProfileReceipt\""); }
            return new(417, true, true, TransactionId: "request-417", JourneyId: submission.JourneyId);
        }
    }
    private sealed class TempData : ITempDataProvider
    {
        public int Loads { get; private set; }
        public Dictionary<string, object> Values { get; private set; } = [];
        public void ShowCustomerForm() => Values = new() { ["InstantQuotationSubmissionStatus"] = "rejected", ["InstantQuotationProblemCategory"] = "validation", ["InstantQuotationValidationFields"] = "[\"FirstName\"]" };
        public IDictionary<string, object> LoadTempData(HttpContext context) { Loads++; return new Dictionary<string, object>(Values); }
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) => Values = new(values);
    }
    private sealed class ProfileWebFactory(Wire wire, string ownerToken, Quotes quotation, TempData tempData, IConnectionMultiplexer redis)
        : TestingWebApplicationFactory(BrowserHostIdentityVerifier.SourceProjectDirectory())
    {
        public Sessions OwnerSessions { get; } = new(ownerToken);
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("BlazorRouting:InstantQuotation", "true");
            builder.ConfigureServices(services =>
            {
                services.Configure<InstantQuotationProfileCompletionOptions>(options => options.Enabled = true);
                services.RemoveAll<IHttpClientFactory>(); services.AddSingleton<IHttpClientFactory>(wire);
                services.RemoveAll<IAccountSessionManager>(); services.AddSingleton<IAccountSessionManager>(OwnerSessions);
                services.RemoveAll<ICountryClient>(); services.AddSingleton<ICountryClient, Countries>();
                services.RemoveAll<IInstantQuotationSessionStore>(); services.AddSingleton<IInstantQuotationSessionStore, QuoteSessions>();
                services.AddSingleton(redis);
                services.RemoveAll<ITempDataProvider>(); services.AddSingleton<ITempDataProvider>(tempData);
                services.RemoveAll<IInstantQuotationSubmissionService>();
                services.AddScoped<IInstantQuotationSubmissionService>(provider => new InstantQuotationSubmissionService(
                    provider.GetRequiredService<IInstantQuotationSessionStore>(), new InstantQuotationPricingService(), quotation,
                    provider.GetRequiredService<IInstantQuotationSubmissionStore>(), new Uploads(), InstantQuotationSubmissionTests.SuccessfulRequestFileClient.Instance,
                    quoteTicketService: new Tickets(), authoritativePricingService: SyntheticAuthoritativePricingTestService.Instance,
                    profileCompletionClient: provider.GetRequiredService<IInstantQuotationProfileCompletionClient>()));
                services.AddAuthentication(options => options.DefaultAuthenticateScheme = "ProfileFixture")
                    .AddScheme<AuthenticationSchemeOptions, OwnerAuthentication>("ProfileFixture", _ => { });
            });
        }
    }
    private sealed class OwnerAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(Request.Headers["X-Profile-Test-Owner"] == "1"
            ? AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, "customer:1"), new Claim("legacy_database_id", "1"), new Claim("identity_kind", "customer"),
                new Claim(ClaimTypes.Email, "owner@example.test")], "ProfileFixture")), "ProfileFixture")) : AuthenticateResult.NoResult());
    }
    private sealed class Sessions(string token) : IAccountSessionManager
    {
        public string Token { get; set; } = token;
        public Task<int?> GetCustomerDatabaseIdAsync(HttpContext context, CancellationToken cancellationToken) => Task.FromResult<int?>(1);
        public Task<string?> GetAccessTokenAsync(HttpContext context, CancellationToken cancellationToken) => Task.FromResult<string?>(Token);
        public Task<AccountSignInStatus> SignInAsync(HttpContext context, string email, string password, bool rememberMe, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SignOutAsync(HttpContext context, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
    private sealed class Countries : ICountryClient
    {
        public Task<ServiceResponse<IReadOnlyList<Country>>> GetCountriesAsync(CancellationToken cancellationToken) => Task.FromResult(new ServiceResponse<IReadOnlyList<Country>>([new(66, "Thailand", null, null, "TH", "THA", null, null)], true));
    }
    private sealed class QuoteSessions : IInstantQuotationSessionStore
    {
        private readonly string sessionId = new string('a', 64);
        public Task<InstantQuotationSessionState> CreateAsync(string? ownerIdentity, InstantQuotationOrderState state, CancellationToken cancellationToken) => Task.FromResult(InstantQuotationSubmissionTests.ProfileContractSession(sessionId));
        public Task<InstantQuotationSessionState?> GetAsync(string id, string? ownerIdentity, CancellationToken cancellationToken) => Task.FromResult<InstantQuotationSessionState?>(ownerIdentity == "customer:1" && id == sessionId ? InstantQuotationSubmissionTests.ProfileContractSession(id) : null);
        public Task<bool> PutAsync(InstantQuotationSessionState session, string? ownerIdentity, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<bool> RemoveAsync(string id, string? ownerIdentity, CancellationToken cancellationToken) => Task.FromResult(true);
    }
    private sealed class Uploads : IInstantQuotationUploadClient
    {
        public Task<InstantQuotationFinalizationResult> FinalizeAsync(string sessionId, string? ownerIdentity, int quotationRequestId, IReadOnlyList<InstantQuotationUploadReference> references, string operationId, CancellationToken cancellationToken) => Task.FromResult(InstantQuotationSubmissionTests.SuccessfulFinalization() with { OperationId = operationId });
        public Task<InstantQuotationUploadResult> UploadAsync(string sessionId, string? ownerIdentity, Stream content, string fileName, string contentType, long contentLength, InstantQuotationGeometryClaim geometryClaim, string operationId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<InstantQuotationRemoveResult> RemoveAsync(string sessionId, string? ownerIdentity, InstantQuotationUploadReference reference, string operationId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
    private sealed class Tickets : IInstantQuotationQuoteTicketService
    {
        public InstantQuotationQuoteAuthorization Issue(InstantQuotationSessionState session, InstantQuotationOrderQuote quote, DateTimeOffset now) => new(["synthetic"], "synthetic");
        public bool Validate(InstantQuotationSessionState session, InstantQuotationOrderQuote quote, InstantQuotationQuoteAuthorization authorization, DateTimeOffset now) => true;
    }
}
