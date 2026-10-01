using System.Diagnostics;
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Sockets;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Components.Pages.Member;
using Legacy.Maliev.Web.Infrastructure;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using Testcontainers.PostgreSql;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Characterizes the unmodified Accounting binary; it does not supply replacement authentication or IAM registration.</summary>
public sealed class ScbPaymentAccountingFixture : IAsyncLifetime
{
    private const string Issuer = "https://scb-boundary.example.test";
    private const string Audience = "scb-boundary-services";
    private const string Database = "scb_boundary";
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:18-alpine")
        .WithDatabase(Database).Build();
    private readonly RSA signingKey = RSA.Create(2048);
    private readonly HttpListener iam = new();
    private readonly CancellationTokenSource lifetime = new();
    private Process? producer;
    private HttpClient? client;
    private Task? iamLoop;
    private int iamRequests;
    private int disposed;
    public HttpClient Client => client!;
    public int IamRequests => Volatile.Read(ref iamRequests);

    public async Task InitializeAsync()
    {
        try
        {
            var binary = Environment.GetEnvironmentVariable("MALIEV_SCB_ACCOUNTING_DLL");
            Assert.True(File.Exists(binary), "Build the exact-pinned private Accounting binary before running this boundary fixture.");
            await postgres.StartAsync();
            var iamOrigin = $"http://127.0.0.1:{FreePort()}/";
            iam.Prefixes.Add(iamOrigin);
            iam.Start();
            iamLoop = ServeIamAsync();
            var origin = new Uri($"http://127.0.0.1:{FreePort()}/");
            var start = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(binary)!,
            };
            start.ArgumentList.Add(binary!);
            foreach (var (key, value) in new Dictionary<string, string>
            {
                ["DOTNET_ENVIRONMENT"] = "Production",
                ["ASPNETCORE_ENVIRONMENT"] = "Production",
                ["ASPNETCORE_URLS"] = origin.ToString(),
                ["ConnectionStrings__PaymentDbContext"] = postgres.GetConnectionString(),
                ["ConnectionStrings__InvoiceDbContext"] = postgres.GetConnectionString(),
                ["ConnectionStrings__ReceiptDbContext"] = postgres.GetConnectionString(),
                ["Cache__RedisEnabled"] = "false",
                ["Jwt__PublicKey"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(signingKey.ExportSubjectPublicKeyInfoPem())),
                ["Jwt__Issuer"] = Issuer,
                ["Jwt__Audience"] = Audience,
                ["Services__IAMService__BaseUrl"] = iamOrigin,
                ["IAM__LivePermissionChecks__Credential"] = "disposable-boundary-only",
                ["Features__AllowExactServiceClaimsForLiveCheck"] = "false",
                ["Logging__LogLevel__Default"] = "Error",
            })
            {
                start.Environment[key] = value;
            }
            producer = Process.Start(start)!;
            // Drain without exposing request bodies, tokens, connection strings or provider logs.
            producer.OutputDataReceived += (_, _) => { };
            producer.ErrorDataReceived += (_, _) => { };
            producer.BeginOutputReadLine();
            producer.BeginErrorReadLine();
            client = new HttpClient { BaseAddress = origin, Timeout = TimeSpan.FromSeconds(10) };
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(40));
            while (true)
            {
                Assert.False(producer.HasExited, "The unmodified Accounting process exited before reaching its normal authentication boundary.");
                try
                {
                    using var response = await client.GetAsync("payments/accounts", deadline.Token);
                    if (response.StatusCode == HttpStatusCode.Unauthorized) break;
                }
                catch (HttpRequestException) { }
                await Task.Delay(100, deadline.Token);
            }
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    public string Token(string credential)
    {
        using var wrongKey = RSA.Create(2048);
        var now = DateTime.UtcNow;
        var claims = new List<Claim> { new("sub", "service:legacy-web-boundary"), new("identity_kind", "service") };
        if (credential != "missing-permission") claims.Add(new("permission", "legacy.accounting.read"));
        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            credential == "wrong-issuer" ? "https://untrusted.example.test" : Issuer,
            Audience, claims,
            now.AddMinutes(-10), credential == "expired" ? now.AddMinutes(-5) : now.AddMinutes(5),
            new SigningCredentials(new RsaSecurityKey(credential == "wrong-signature" ? wrongKey : signingKey), SecurityAlgorithms.RsaSha256)));
    }

    public async Task<string> DatabaseShapeAsync()
    {
        var result = await postgres.ExecAsync(["psql", "-U", "postgres", "-d", Database, "-At", "-c",
            "SELECT count(*) FROM pg_catalog.pg_tables WHERE schemaname='public'"]);
        Assert.Equal(0, result.ExitCode);
        return result.Stdout;
    }

    private async Task ServeIamAsync()
    {
        while (!lifetime.IsCancellationRequested)
        {
            try
            {
                var context = await iam.GetContextAsync().WaitAsync(lifetime.Token);
                Interlocked.Increment(ref iamRequests);
                var bytes = Encoding.UTF8.GetBytes("{\"allowed\":true}");
                context.Response.ContentType = "application/json";
                await context.Response.OutputStream.WriteAsync(bytes, lifetime.Token);
                context.Response.Close();
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { break; }
            catch (HttpListenerException) when (lifetime.IsCancellationRequested) { break; }
        }
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    public async Task DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        lifetime.Cancel();
        iam.Close();
        try
        {
            if (iamLoop is not null) await iamLoop.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            client?.Dispose();
            if (producer is not null)
            {
                if (!producer.HasExited) producer.Kill(entireProcessTree: true);
                await producer.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                producer.Dispose();
            }
            signingKey.Dispose();
            lifetime.Dispose();
            await postgres.DisposeAsync();
        }
    }
}

public sealed class ScbPaymentServiceBoundaryTests(ScbPaymentAccountingFixture fixture) : IClassFixture<ScbPaymentAccountingFixture>
{
    private HttpClient client => fixture.Client;
    private string Token(string credential) => fixture.Token(credential);
    private Task<string> DatabaseShapeAsync() => fixture.DatabaseShapeAsync();

    [Theory]
    [InlineData("anonymous", HttpStatusCode.Unauthorized)]
    [InlineData("wrong-signature", HttpStatusCode.Unauthorized)]
    [InlineData("wrong-issuer", HttpStatusCode.Unauthorized)]
    [InlineData("expired", HttpStatusCode.Unauthorized)]
    [InlineData("missing-permission", HttpStatusCode.Forbidden)]
    [InlineData("exact-permission", HttpStatusCode.Forbidden)]
    public async Task Accounts_UnmodifiedProductionProgram_FailsClosedWithoutRegisteredLiveIam(string credential, HttpStatusCode expected)
    {
        var before = await DatabaseShapeAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, "payments/accounts");
        if (credential != "anonymous") request.Headers.Authorization = new("Bearer", Token(credential));
        using var response = await client!.SendAsync(request);
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(0, fixture.IamRequests);
        Assert.Equal(before, await DatabaseShapeAsync());
        Assert.DoesNotContain("AccountNumber", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("en", "Bank-transfer details are temporarily unavailable.")]
    [InlineData("th", "รายละเอียดการโอนเงินผ่านธนาคารไม่พร้อมใช้งานชั่วคราว")]
    public async Task MemberClient_ActualAccountingDenial_RendersWarningWithoutPaymentDestination(string culture, string warning)
    {
        var factory = new SupplementFactory(client!.BaseAddress!, ownedInvoice: true, paid: false);
        var tokens = new BoundaryTokenProvider(Token("exact-permission"));
        var member = new CustomerMemberDetailClient(factory, tokens, NullLogger<CustomerMemberDetailClient>.Instance);
        var details = Quotation();
        var supplement = await member.GetQuotationSupplementAsync(42, details, default);
        Assert.NotNull(supplement.Invoice);
        Assert.False(supplement.Invoice.IsPaid);
        Assert.Empty(supplement.BankAccounts);
        Assert.Contains("Bank-transfer details are temporarily unavailable.", supplement.Warnings);
        Assert.Equal(1, factory.AccountRequests);
        Assert.Equal(1, tokens.Invalidations);
        Assert.Equal(0, fixture.IamRequests);
        var html = await RenderAsync(culture, MemberDetailLoaders.CreateQuotationDisplayModel(details, supplement, null, supplement.Warnings));
        Assert.Contains(warning, html, StringComparison.Ordinal);
        Assert.DoesNotContain("Account number", html, StringComparison.Ordinal);
        Assert.DoesNotContain("เลขที่บัญชี", html, StringComparison.Ordinal);
        Assert.DoesNotContain("SCB", html, StringComparison.Ordinal);
        Assert.DoesNotContain(tokens.Value, html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task MemberClient_WrongInvoiceOwnerOrPaidInvoice_DoesNotReadAccountCatalog(bool owned, bool paid)
    {
        var factory = new SupplementFactory(client!.BaseAddress!, owned, paid);
        var member = new CustomerMemberDetailClient(factory, new BoundaryTokenProvider(Token("exact-permission")), NullLogger<CustomerMemberDetailClient>.Instance);
        var supplement = await member.GetQuotationSupplementAsync(42, Quotation(), default);
        Assert.Empty(supplement.BankAccounts);
        Assert.Equal(0, factory.AccountRequests);
        if (!owned) Assert.Null(supplement.Invoice);
        else Assert.True(supplement.Invoice!.IsPaid);
        Assert.Equal(0, fixture.IamRequests);
    }

    [Theory]
    [InlineData("en", "Siam Commercial Bank (SCB)", "Maliev Co., Ltd.")]
    [InlineData("th", "ธนาคารไทยพาณิชย์ (SCB)", "บริษัท มาลีฟ จำกัด")]
    public async Task ControlledAccountHttp_PascalCaseOmittedMetadata_RealClientAndRendererPreserveLocalizedWire(string culture, string bank, string company)
    {
        // Synthetic non-payable wire, not a PostgreSQL row or normal live-IAM success.
        await using var upstream = new ControlledAccountServer(HttpStatusCode.OK,
            "[{\"Id\":1,\"Bank\":\"Siam Commercial Bank (SCB)\",\"AccountNumber\":\"BOUNDARY-NOT-PAYABLE\"}]");
        var tokens = new BoundaryTokenProvider(Token("exact-permission"));
        var factory = new SupplementFactory(upstream.Origin, true, false);
        var member = new CustomerMemberDetailClient(factory, tokens, NullLogger<CustomerMemberDetailClient>.Instance);
        var details = Quotation();
        var supplement = await member.GetQuotationSupplementAsync(42, details, default);
        var account = Assert.Single(supplement.BankAccounts);
        Assert.Equal("BOUNDARY-NOT-PAYABLE", account.AccountNumber);
        Assert.Null(account.Branch);
        Assert.Null(account.Swift);
        Assert.Empty(supplement.Warnings);
        Assert.Equal("Bearer " + tokens.Value, upstream.Authorization);
        var html = await RenderAsync(culture, MemberDetailLoaders.CreateQuotationDisplayModel(details, supplement, null, []));
        Assert.Contains(bank, html, StringComparison.Ordinal);
        Assert.Contains(company, html, StringComparison.Ordinal);
        Assert.Contains("BOUNDARY-NOT-PAYABLE", html, StringComparison.Ordinal);
        Assert.DoesNotContain("SWIFT", html, StringComparison.Ordinal);
        Assert.DoesNotContain(tokens.Value, html, StringComparison.Ordinal);
        Assert.Equal(0, tokens.Invalidations);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, "{\"error\":\"unavailable\"}", true)]
    [InlineData(HttpStatusCode.OK, "{", true)]
    [InlineData(HttpStatusCode.OK, "[{\"AccountNumber\":{}}]", true)]
    [InlineData(HttpStatusCode.OK, "[]", false)]
    public async Task ControlledAccountHttp_UnavailableMalformedOrEmpty_DoesNotInventDestination(HttpStatusCode status, string body, bool warns)
    {
        await using var upstream = new ControlledAccountServer(status, body);
        var tokens = new BoundaryTokenProvider(Token("exact-permission"));
        var member = new CustomerMemberDetailClient(new SupplementFactory(upstream.Origin, true, false), tokens, NullLogger<CustomerMemberDetailClient>.Instance);
        var details = Quotation();
        var supplement = await member.GetQuotationSupplementAsync(42, details, default);
        Assert.Empty(supplement.BankAccounts);
        Assert.Equal(warns, supplement.Warnings.Contains("Bank-transfer details are temporarily unavailable."));
        var html = await RenderAsync("en", MemberDetailLoaders.CreateQuotationDisplayModel(details, supplement, null, supplement.Warnings));
        Assert.DoesNotContain("Account number", html, StringComparison.Ordinal);
        Assert.DoesNotContain("SCB", html, StringComparison.Ordinal);
        Assert.DoesNotContain(tokens.Value, html, StringComparison.Ordinal);
        Assert.Equal(0, tokens.Invalidations);
    }

    [Fact]
    public async Task ControlledAccountHttp_CallerAbortDuringActualRequest_PropagatesCancellation()
    {
        await using var upstream = new ControlledAccountServer(HttpStatusCode.OK, "[]", holdResponse: true);
        var tokens = new BoundaryTokenProvider(Token("exact-permission"));
        var factory = new SupplementFactory(upstream.Origin, true, false);
        var member = new CustomerMemberDetailClient(factory, tokens, NullLogger<CustomerMemberDetailClient>.Instance);
        using var abort = new CancellationTokenSource();
        var operation = member.GetQuotationSupplementAsync(42, Quotation(), abort.Token);
        await upstream.RequestReceived.Task.WaitAsync(TimeSpan.FromSeconds(10));
        abort.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await operation);
        Assert.Equal(1, factory.AccountRequests);
        Assert.Equal(0, tokens.Invalidations);
    }

    private sealed class ControlledAccountServer : IAsyncDisposable
    {
        private readonly HttpListener listener = new();
        private readonly CancellationTokenSource shutdown = new();
        private readonly Task serve;
        public Uri Origin { get; }
        public string? Authorization { get; private set; }
        public TaskCompletionSource RequestReceived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ControlledAccountServer(HttpStatusCode status, string body, bool holdResponse = false)
        {
            using var port = new TcpListener(IPAddress.Loopback, 0);
            port.Start();
            var number = ((IPEndPoint)port.LocalEndpoint).Port;
            port.Stop();
            Origin = new Uri($"http://127.0.0.1:{number}/");
            listener.Prefixes.Add(Origin.ToString());
            listener.Start();
            serve = ServeAsync(status, body, holdResponse);
        }

        private async Task ServeAsync(HttpStatusCode status, string body, bool holdResponse)
        {
            HttpListenerContext? context = null;
            try
            {
                context = await listener.GetContextAsync().WaitAsync(shutdown.Token);
                Assert.Equal("/payments/accounts", context.Request.Url!.AbsolutePath);
                Authorization = context.Request.Headers["Authorization"];
                RequestReceived.TrySetResult();
                if (holdResponse) await Task.Delay(Timeout.InfiniteTimeSpan, shutdown.Token);
                context.Response.StatusCode = (int)status;
                context.Response.ContentType = "application/json";
                await context.Response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes(body), shutdown.Token);
                context.Response.Close();
            }
            catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { context?.Response.Abort(); }
            catch (HttpListenerException) when (shutdown.IsCancellationRequested) { }
        }

        public async ValueTask DisposeAsync()
        {
            shutdown.Cancel();
            try { await serve.WaitAsync(TimeSpan.FromSeconds(5)); }
            finally
            {
                listener.Close();
                shutdown.Dispose();
            }
        }
    }

    [Theory]
    [InlineData("outside", "must remain inside")]
    [InlineData("missing-dependencies", "dependencies are missing")]
    [InlineData("dirty", "dirty SCB")]
    [InlineData("wrong-head", "immutable commit")]
    [InlineData("wrong-origin", "immutable repository")]
    [InlineData("missing-binary", "binary is missing")]
    public async Task Preparation_UnsafeOrIncompletePrivateGraph_FailsClosedWithoutSkipping(string scenario, string diagnostic)
    {
        var repository = FindRoot();
        // Keep Git's pack/index paths below its separate Windows GIT_DIR bound.
        var root = Path.Combine(repository, "TestResults", ".scb280", Guid.NewGuid().ToString("N")[..16]);
        Directory.CreateDirectory(Path.Combine(root, "scripts"));
        var script = Path.Combine(root, "scripts", "prepare-scb-payment-boundary.ps1");
        File.Copy(Path.Combine(repository, "scripts", "prepare-scb-payment-boundary.ps1"), script);
        var dependencies = Path.Combine(root, ".dependencies", "p");
        if (scenario is not ("outside" or "missing-dependencies"))
        {
            var source = Path.Combine(repository, ".dependencies", "scb-payment-boundary");
            foreach (var (relative, name, pin) in new[]
            {
                ("producer", "Legacy.Maliev.AccountingService", "ae0826156b06c34476e95de8c53dfccfcf5a5972"),
                ("runtime/Legacy.Maliev.ServiceDefaults", "Legacy.Maliev.ServiceDefaults", "8f4f5f27b226ffe406c4c79b1903742e8c2e7dd3"),
                ("runtime/Legacy.Maliev.CompatibilityContracts", "Legacy.Maliev.CompatibilityContracts", "78e48ffc4ee000df0510cba5e7c7a3c4c4d539d7"),
            })
            {
                var target = Path.Combine(dependencies, relative);
                var clone = await CommandAsync("git", root, "-c", "core.longpaths=true", "clone", "--no-hardlinks", "--no-checkout", Path.Combine(source, relative), target);
                Assert.True(clone.ExitCode == 0, clone.Output);
                var checkout = await CommandAsync("git", root, "-c", "core.longpaths=true", "-C", target, "checkout", "--detach", pin);
                Assert.True(checkout.ExitCode == 0, checkout.Output);
                Assert.Equal(0, (await CommandAsync("git", root, "-C", target, "remote", "set-url", "origin", $"https://github.com/MALIEV-Co-Ltd/{name}.git")).ExitCode);
            }
            var producer = Path.Combine(dependencies, "producer");
            if (scenario == "dirty") File.WriteAllText(Path.Combine(producer, ".fixture-dirty"), "owned disposable negative control");
            if (scenario == "wrong-head")
            {
                // The preparation fetch is shallow; use the retained parent object
                // explicitly, copying it only from the owned local prepared clone.
                Assert.Equal(0, (await CommandAsync("git", root, "-c", "core.longpaths=true", "-C", producer, "fetch", "--no-tags", Path.Combine(source, "producer"), "0ec928ee470e29777151e8028b3f300a93f5b538")).ExitCode);
                Assert.Equal(0, (await CommandAsync("git", root, "-c", "core.longpaths=true", "-C", producer, "checkout", "--detach", "0ec928ee470e29777151e8028b3f300a93f5b538")).ExitCode);
            }
            if (scenario == "wrong-origin") Assert.Equal(0, (await CommandAsync("git", root, "-C", producer, "remote", "set-url", "origin", "https://example.invalid/not-allowed.git")).ExitCode);
        }
        var arguments = new List<string> { "-NoProfile", "-File", script, "-VerifyOnly", "-DependencyDirectory", scenario == "outside" ? "outside-boundary" : ".dependencies/p" };
        var result = await CommandAsync("pwsh", root, arguments.ToArray());
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(diagnostic, result.Output, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(root, "outside-boundary")));
    }

    [Fact]
    public void RequiredWorkflow_PreparesScbBinaryBeforeWebBuildWithoutConditionalOrFailOpenStep()
    {
        var workflow = File.ReadAllText(Path.Combine(FindRoot(), ".github", "workflows", "_build-and-test.yml"));
        var steps = Regex.Matches(workflow, @"(?ms)^      - name: (?<name>[^\r\n]+)\r?\n(?<body>.*?)(?=^      - |\z)");
        var preparation = Assert.Single(steps.Cast<Match>(), step => step.Groups["name"].Value == "Build exact-pinned disposable SCB payment boundary service");
        var properties = preparation.Groups["body"].Value.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim()).Select(line => line.Split(':', 2)).ToDictionary(parts => parts[0], parts => parts[1].Trim(), StringComparer.Ordinal);
        Assert.Equal(2, properties.Count);
        Assert.Equal("pwsh", properties["shell"]);
        Assert.Equal("./scripts/prepare-scb-payment-boundary.ps1", properties["run"]);
        var build = Assert.Single(steps.Cast<Match>(), step => step.Groups["name"].Value == "Build browser acceptance host");
        Assert.True(preparation.Index < build.Index);
        Assert.DoesNotContain("-VerifyOnly", properties["run"], StringComparison.Ordinal);
        // Exactly shell/run forbids step if/continue-on-error and alternate environment overrides.
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Web.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Web repository root was not found.");
    }

    private static async Task<(int ExitCode, string Output)> CommandAsync(string executable, string workingDirectory, params string[] arguments)
    {
        var start = new ProcessStartInfo(executable) { WorkingDirectory = workingDirectory, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        start.Environment.Remove("GITHUB_ENV");
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30)); }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
        return (process.ExitCode, await stdout + await stderr);
    }

    private static CustomerQuotationDetails Quotation() => new(new CustomerQuotation(
        15, 42, 81, 30, new DateTime(2027, 1, 1), 100, 7, 107, null, null, 764,
        null, null, null, null, null, null, null), [], [], []);

    private static async Task<string> RenderAsync(string culture, MemberQuotationDetailDisplayModel model)
    {
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            using var services = new ServiceCollection().AddLogging()
                .AddLocalization(options => options.ResourcesPath = "Resources").BuildServiceProvider();
            await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
            return await renderer.Dispatcher.InvokeAsync(async () =>
            {
                var output = await renderer.RenderComponentAsync<MemberQuotationDetailContent>(
                    ParameterView.FromDictionary(new Dictionary<string, object?> { ["Model"] = model }));
                return WebUtility.HtmlDecode(output.ToHtmlString());
            });
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    private sealed class BoundaryTokenProvider(string value) : IServiceAccessTokenProvider
    {
        public string Value => value;
        public int Invalidations { get; private set; }
        public ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<string?>(value);
        }
        public void Invalidate(string token)
        {
            Assert.Equal(value, token);
            Invalidations++;
        }
    }

    // Only the account HTTP request uses the actual producer. The invoice/quotation
    // preconditions are explicitly controlled, not evidence of a joined invoice workflow.
    private sealed class SupplementFactory(Uri accountingOrigin, bool ownedInvoice, bool paid) : IHttpClientFactory
    {
        private bool OwnedInvoice => ownedInvoice;
        private bool Paid => paid;
        public int AccountRequests { get; private set; }
        public HttpClient CreateClient(string name) => new(new SupplementHandler(this, name)) { BaseAddress = accountingOrigin };

        private sealed class SupplementHandler(SupplementFactory owner, string name) : DelegatingHandler(new HttpClientHandler { AllowAutoRedirect = false })
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var path = request.RequestUri!.AbsolutePath.TrimStart('/');
                if (name == "accounting" && path == "payments/accounts")
                {
                    owner.AccountRequests++;
                    return base.SendAsync(request, cancellationToken);
                }
                if (name == "accounting" && path == "invoices/81")
                {
                    var json = $"{{\"Id\":81,\"Number\":\"BOUNDARY-INV-81\",\"CustomerId\":{(owner.OwnedInvoice ? 42 : 99)},\"Currency\":\"THB\",\"IsPaid\":{owner.Paid.ToString().ToLowerInvariant()},\"Outstanding\":107}}";
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
                }
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }
        }
    }

}
