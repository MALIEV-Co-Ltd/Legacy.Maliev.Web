using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Infrastructure;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using StackExchange.Redis;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace Legacy.Maliev.Web.Tests;

[CollectionDefinition("CNC literal producer boundary", DisableParallelization = true)]
public sealed class CncLiteralProducerBoundaryCollection;

/// <summary>Current CNC client to separately pinned real Program, disposable PostgreSQL and Redis.
/// RS256 permissions and country lookup are controlled fixtures, not genuine IAM/CountryService acceptance.</summary>
[Collection("CNC literal producer boundary")]
public sealed class CncLiteralProducerBoundaryTests
{
    [Theory]
    [InlineData("padded", false, false)]
    [InlineData("nbsp", false, false)]
    [InlineData("utf16-256", false, false)]
    [InlineData("company-258", true, false)]
    [InlineData("address-258", false, true)]
    public async Task CncClient_RealProducer_PreservesLiteralStorageCacheAndStopsRejectedWrites(
        string variant, bool rejectCompany, bool rejectAddress)
    {
        await CncOwnedResourceScope.ExecuteAsync(async scope =>
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            var cancellation = deadline.Token;
            var dll = Environment.GetEnvironmentVariable("MALIEV_PROFILE_PRODUCER_DLL");
            var seedDll = Environment.GetEnvironmentVariable("MALIEV_PROFILE_SEED_DLL");
            Assert.True(File.Exists(dll) && File.Exists(seedDll), "Exact pinned producer and disposable seed must be built first.");
            var database = $"profile_contract_{Guid.NewGuid():N}";
            var postgres = scope.OwnContainer(new PostgreSqlBuilder("postgres:18-alpine").WithDatabase(database)
                .WithLabel("maliev.cnc.run", scope.RunId).WithLabel("maliev.cnc.expires", scope.Expires.ToString("o"))
                .WithCreateParameterModifier(value => { value.HostConfig.Memory = 402653184; value.HostConfig.NanoCPUs = 1000000000; }).Build(), 402653184, 1000000000);
            var redis = scope.OwnContainer(new RedisBuilder("redis:8.4-alpine")
                .WithLabel("maliev.cnc.run", scope.RunId).WithLabel("maliev.cnc.expires", scope.Expires.ToString("o"))
                .WithCreateParameterModifier(value => { value.HostConfig.Memory = 134217728; value.HostConfig.NanoCPUs = 1000000000; }).Build(), 134217728, 1000000000);
            await scope.StartContainerAsync(postgres, cancellation);
            await scope.StartContainerAsync(redis, cancellation);
            var connection = postgres.GetConnectionString();
            var redisConnection = redis.GetConnectionString();
            var seed = scope.StartChild(seedDll!, new()
            {
                ["MALIEV_PROFILE_DISPOSABLE_CONNECTION"] = connection,
                ["MALIEV_PROFILE_DISPOSABLE_DATABASE"] = database,
            });
            await seed.CompleteAsync(TimeSpan.FromSeconds(60), cancellation);
            Assert.Equal(0, seed.Process.ExitCode);
            using var rsa = RSA.Create(2048);
            using var socket = new TcpListener(IPAddress.Loopback, 0);
            socket.Start();
            var port = ((IPEndPoint)socket.LocalEndpoint).Port;
            socket.Stop();
            var origin = new Uri($"http://127.0.0.1:{port}");
            var producer = scope.StartChild(dll!, new()
            {
                ["DOTNET_ENVIRONMENT"] = "Production",
                ["ASPNETCORE_ENVIRONMENT"] = "Production",
                ["ASPNETCORE_URLS"] = origin.ToString(),
                ["ConnectionStrings__CustomerDbContext"] = connection,
                ["ConnectionStrings__redis"] = redisConnection,
                ["Cache__RedisEnabled"] = "true",
                ["Jwt__PublicKey"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(rsa.ExportSubjectPublicKeyInfoPem())),
                ["Jwt__Issuer"] = "cnc-literal-fixture",
                ["Jwt__Audience"] = "cnc-literal-fixture",
                ["CORS__AllowedOrigins__0"] = "https://example.test",
                ["Logging__LogLevel__Default"] = "Warning",
            });
            using var http = new HttpClient { BaseAddress = origin, Timeout = TimeSpan.FromSeconds(15) };
            await Ready(http, producer.Process, cancellation);
            var jwt = Token(rsa);
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
            using var created = await http.PostAsJsonAsync("customers", new
            {
                FirstName = "Stored", LastName = "Name", Email = "literal@example.test", Fax = "fax-kept",
            }, new JsonSerializerOptions { PropertyNamingPolicy = null }, cancellation);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            using var json = JsonDocument.Parse(await created.Content.ReadAsStringAsync(cancellation));
            var id = json.RootElement.GetProperty("Id").GetInt32();
            Assert.Equal(3, id);
            await using var db = new NpgsqlConnection(connection);
            await db.OpenAsync(cancellation);
            var untouched = await Snapshot(db, true, cancellation);
            var before = await Snapshot(db, false, cancellation);
            await using var cache = await ConnectionMultiplexer.ConnectAsync(redisConnection);
            var redisDb = cache.GetDatabase();
            for (var key = 1; key <= 3; key++)
                await redisDb.StringSetAsync($"legacy:customer:customer:{key}", $"literal-fixture-{key}");
            var company = variant switch
            {
                "company-258" => string.Concat(Enumerable.Repeat("😀", 129)),
                "utf16-256" => new string('ก', 254) + "😀",
                "nbsp" => "\u00a0บริษัท😀\u00a0",
                _ => "  บริษัท ทดสอบ  ",
            };
            var line = variant switch
            {
                "address-258" => string.Concat(Enumerable.Repeat("😀", 129)),
                "utf16-256" => string.Concat(Enumerable.Repeat("😀", 128)),
                "nbsp" => "\u00a0ถนน😀\u00a0",
                _ => "  ถนน ทดสอบ  ",
            };
            using var forwarding = new ForwardingHandler();
            using var country = new CountryFixture();
            var client = new CncProfilePersistenceClient(new Factory(origin, forwarding, country), new Tokens(jwt));
            var request = new CncProfilePersistenceRequest(42, id,
                new CustomerAccountDetails(id, "Stored", "Name", "Stored Name", null, null, "fax-kept", "literal@example.test", null, null, null, null, null, null, null, null, null),
                new CncProfileCompletion("Nat", "V", "literal@example.test", "0800000000", "020000000", company, "010",
                    new CncProfileAddressFields("9", line, null, "Bangkok", "Bangkok", "10210"), "Thailand", true, null, null));
            var result = await client.CompleteAsync(request, cancellation);
            Assert.Equal(untouched, await Snapshot(db, true, cancellation));
            Assert.Equal("literal-fixture-1", (string?)await redisDb.StringGetAsync("legacy:customer:customer:1"));
            Assert.Equal("literal-fixture-2", (string?)await redisDb.StringGetAsync("legacy:customer:customer:2"));
            using var body = JsonDocument.Parse(forwarding.Calls[0].Body!);
            Assert.Equal(company, body.RootElement.GetProperty("Name").GetString());
            Assert.True(body.RootElement.TryGetProperty("Registrar", out var registrar));
            Assert.Equal(JsonValueKind.Null, registrar.ValueKind);
            if (rejectCompany)
            {
                Assert.Equal(CncProfilePersistenceOutcome.Failed, result.Outcome);
                Assert.Equal(CncProfilePersistenceStage.Company, result.Stage);
                Assert.False(result.HasConfirmedChanges);
                Assert.Single(forwarding.Calls);
                Assert.Equal(before, await Snapshot(db, false, cancellation));
            }
            else
            {
                Assert.True(result.CreatedCompanyId > 1);
                Assert.True(result.HasConfirmedChanges);
                await using var query = new NpgsqlCommand("SELECT \"Name\" FROM \"Company\" WHERE \"ID\" = @id", db);
                query.Parameters.AddWithValue("id", result.CreatedCompanyId!.Value);
                Assert.Equal(company, await query.ExecuteScalarAsync(cancellation));
                if (rejectAddress)
                {
                    Assert.Equal(CncProfilePersistenceOutcome.Failed, result.Outcome);
                    Assert.Equal(CncProfilePersistenceStage.BillingAddress, result.Stage);
                    Assert.Equal(["POST /customers/companies", "POST /customers/3/addresses"], forwarding.Calls.Select(call => call.Method + " " + call.Path));
                    await using var graph = new NpgsqlCommand("SELECT (SELECT count(*) FROM \"Address\") = 2 AND \"CompanyID\" IS NULL AND \"BillingAddressID\" IS NULL AND \"ShippingAddressID\" IS NULL FROM \"Customer\" WHERE \"ID\"=3", db);
                    Assert.Equal(true, await graph.ExecuteScalarAsync(cancellation));
                }
                else
                {
                    Assert.Equal(CncProfilePersistenceOutcome.Completed, result.Outcome);
                    Assert.Equal(["POST /customers/companies", "POST /customers/3/addresses", "PUT /customers/3"], forwarding.Calls.Select(call => call.Method + " " + call.Path));
                    Assert.True(result.CreatedBillingAddressId > 2);
                    await using var address = new NpgsqlCommand("SELECT a.\"AddressLine1\" FROM \"Address\" a JOIN \"Customer\" c ON c.\"BillingAddressID\"=a.\"ID\" AND c.\"ShippingAddressID\"=a.\"ID\" WHERE c.\"ID\"=3 AND c.\"CompanyID\"=@company AND a.\"CountryID\"=764", db);
                    address.Parameters.AddWithValue("company", result.CreatedCompanyId!.Value);
                    Assert.Equal(line, await address.ExecuteScalarAsync(cancellation));
                    Assert.False(await redisDb.KeyExistsAsync("legacy:customer:customer:3"));
                }
            }
            if (rejectCompany || rejectAddress)
                Assert.Equal("literal-fixture-3", (string?)await redisDb.StringGetAsync("legacy:customer:customer:3"));
        });
    }

    private static async Task<string> Snapshot(NpgsqlConnection connection, bool originalOnly, CancellationToken token)
    {
        var customer = originalOnly ? " WHERE c.\"ID\" <= 2" : "";
        var company = originalOnly ? " WHERE c.\"ID\" = 1" : "";
        var address = originalOnly ? " WHERE a.\"ID\" <= 2" : "";
        await using var command = new NpgsqlCommand($"SELECT jsonb_build_object('customers',(SELECT jsonb_agg(to_jsonb(c)||jsonb_build_object('xmin',c.xmin::text) ORDER BY c.\"ID\") FROM \"Customer\" c{customer}),'companies',(SELECT jsonb_agg(to_jsonb(c)||jsonb_build_object('xmin',c.xmin::text) ORDER BY c.\"ID\") FROM \"Company\" c{company}),'addresses',(SELECT jsonb_agg(to_jsonb(a)||jsonb_build_object('xmin',a.xmin::text) ORDER BY a.\"ID\") FROM \"Address\" a{address}))::text", connection);
        return (string)(await command.ExecuteScalarAsync(token))!;
    }

    private static string Token(RSA rsa)
    {
        string[] permissions = ["legacy-customer.customers.create", "legacy-customer.customers.read", "legacy-customer.customers.update", "legacy-customer.companies.create", "legacy-customer.addresses.create"];
        var now = DateTime.UtcNow;
        var jwt = new JwtSecurityToken("cnc-literal-fixture", "cnc-literal-fixture",
            new[] { new Claim("sub", "employee:cnc-literal-fixture") }.Concat(permissions.Select(value => new Claim("permission", value))),
            now.AddMinutes(-1), now.AddMinutes(5), new SigningCredentials(new RsaSecurityKey(rsa), SecurityAlgorithms.RsaSha256));
        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }

    private static async Task Ready(HttpClient http, Process process, CancellationToken token)
    {
        using var readiness = CancellationTokenSource.CreateLinkedTokenSource(token);
        readiness.CancelAfter(TimeSpan.FromSeconds(30));
        while (!readiness.IsCancellationRequested)
        {
            Assert.False(process.HasExited, "Owned producer exited before readiness.");
            try
            {
                using var response = await http.GetAsync("health", readiness.Token);
                if (response.IsSuccessStatusCode) return;
            }
            catch (HttpRequestException) { }
            await Task.Delay(100, readiness.Token);
        }
        throw new TimeoutException("Owned producer readiness deadline.");
    }

    private sealed record Call(string Method, string Path, string? Body);
    private sealed class ForwardingHandler : DelegatingHandler
    {
        internal List<Call> Calls { get; } = [];
        internal ForwardingHandler() : base(new SocketsHttpHandler()) { }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Calls.Add(new(request.Method.Method, request.RequestUri!.AbsolutePath, request.Content is null ? null : await request.Content.ReadAsStringAsync(token)));
            return await base.SendAsync(request, token);
        }
    }
    private sealed class CountryFixture : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("[{\"Id\":764,\"Name\":\"Thailand\"}]", Encoding.UTF8, "application/json"),
        });
    }
    private sealed class Factory(Uri origin, ForwardingHandler forwarding, CountryFixture country) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(name == "customers" ? forwarding : country, false)
        {
            BaseAddress = name == "customers" ? origin : new Uri("https://countries.fixture/"),
        };
    }
    private sealed class Tokens(string token) : IServiceAccessTokenProvider
    {
        public ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken) => ValueTask.FromResult<string?>(token);
        public void Invalidate(string value) { }
    }
}
