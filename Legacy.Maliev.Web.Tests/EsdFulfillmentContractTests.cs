using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Application.Pricing;
using Legacy.Maliev.Web.Infrastructure;
using Legacy.Maliev.Web.Components.Pages.InstantQuotation;
using Microsoft.Extensions.Logging.Abstractions;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Controlled HTTP proof, not deployed Catalog/PostgreSQL or browser acceptance.</summary>
public sealed class EsdFulfillmentContractTests
{
    private const string SubmissionId = "ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789";
    private const int CustomerId = 42;
    private const int OrderId = 901;
    private static readonly InstantQuotationFinalizedFile File = new(
        Guid.Parse("22222222-2222-2222-2222-222222222222"), "test-owned-bucket", "clean/fixture part.stl",
        "fixture.stl", "model/stl", 100, new string('a', 64));

    [Theory]
    [InlineData("PA612-ESD", BuildPreference.Standard)]
    [InlineData("PA612-ESD", BuildPreference.Quality)]
    [InlineData("PA612-ESD", BuildPreference.Strength)]
    [InlineData("ABS-ESD", BuildPreference.Standard)]
    [InlineData("ABS-ESD", BuildPreference.Quality)]
    [InlineData("ABS-ESD", BuildPreference.Strength)]
    public async Task Provision_EsdOffer_PreservesCatalogIdsAndExactOrderWire(string material, BuildPreference build)
    {
        using var boundary = new Boundary(material);
        var part = Part(material, build);
        // Synthetic pricing supplies a transport input only; it is not a physical calibration proof.
        var quote = SyntheticPhysicalPricingTestData.Quote(new([part])).Parts.Single();

        var result = await boundary.Client.ProvisionOrderAsync(
            SubmissionId, 0, CustomerId, "ตรวจสอบชิ้นงาน / inspect fixture", part, quote, 7, File, default);

        Assert.True(result.Succeeded);
        Assert.Equal(OrderId, result.OrderId);
        Assert.True(result.ServiceAvailable);
        Assert.True(result.Authorized);
        Assert.False(result.Conflict);
        Assert.Equal("additive-2026-10-10.v12", PricingCatalog.AdditivePricingPolicyVersion);
        var create = Assert.Single(boundary.Requests, request => request.Method == "POST" && request.Path == "orders");
        using var document = JsonDocument.Parse(create.Body!);
        var wire = document.RootElement;
        Assert.Equal(new[] { "allowCancellation", "allowPayment", "allowSocialMedia", "colorId", "comment", "currencyId",
            "customerId", "description", "discountPercent", "employeeId", "finishedDate", "leadTime", "manufactured",
            "materialId", "name", "operationKey", "processId", "promisedDate", "quantity", "surfaceFinishId", "trackingNumber", "unitPrice" },
            wire.EnumerateObject().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal));
        Assert.Equal(CustomerId, wire.GetProperty("customerId").GetInt32());
        Assert.Equal(boundary.MaterialId, wire.GetProperty("materialId").GetInt32());
        Assert.NotEqual(999, wire.GetProperty("materialId").GetInt32()); // Historical PC-ESD decoy.
        Assert.Equal(3, wire.GetProperty("processId").GetInt32());
        Assert.Equal(9, wire.GetProperty("colorId").GetInt32());
        Assert.Equal(7, wire.GetProperty("surfaceFinishId").GetInt32());
        Assert.Equal(764, wire.GetProperty("currencyId").GetInt32());
        Assert.Equal(2, wire.GetProperty("quantity").GetInt32());
        Assert.Equal(0, wire.GetProperty("manufactured").GetInt32());
        Assert.Equal(7, wire.GetProperty("leadTime").GetInt32());
        Assert.Equal(Convert.ToDecimal(quote.UnitPrice), wire.GetProperty("unitPrice").GetDecimal());
        Assert.Equal(create.IdempotencyKey, wire.GetProperty("operationKey").GetString());
        Assert.Equal(64, create.IdempotencyKey!.Length);
        foreach (var field in new[] { "employeeId", "discountPercent", "promisedDate", "finishedDate", "trackingNumber" })
            Assert.Equal(JsonValueKind.Null, wire.GetProperty(field).ValueKind);
        foreach (var field in new[] { "allowPayment", "allowCancellation", "allowSocialMedia" })
            Assert.False(wire.GetProperty(field).GetBoolean());
        Assert.Contains(material, wire.GetProperty("description").GetString()!, StringComparison.Ordinal);
        var comment = wire.GetProperty("comment").GetString()!;
        Assert.Contains($"Material key: {material}", comment, StringComparison.Ordinal);
        Assert.Contains("Pricing policy: additive-2026-10-10.v12", comment, StringComparison.Ordinal);
        Assert.Contains("Surface finish: As printed", comment, StringComparison.Ordinal);
        Assert.Contains("Color: Black", comment, StringComparison.Ordinal);
        Assert.Contains("ตรวจสอบชิ้นงาน / inspect fixture", comment, StringComparison.Ordinal);
        Assert.DoesNotContain("PC-ESD", comment, StringComparison.Ordinal);
        Assert.DoesNotContain(File.Bucket, comment, StringComparison.Ordinal);
        Assert.DoesNotContain(File.ObjectName, comment, StringComparison.Ordinal);
        Assert.Equal(new[] { "orders/processes/additive", "orders/fileformats", "materials/printable", "materials/materialgroups",
            $"materials/{boundary.MaterialId}/colors", $"materials/{boundary.MaterialId}/surfacefinishes", "currencies", "orders",
            $"orderstatuses/histories/{OrderId}/new", $"orders/customers/{CustomerId}/{OrderId}",
            $"orders/{OrderId}/files?bucket=test-owned-bucket&objectName=clean%2Ffixture%20part.stl" },
            boundary.Requests.Select(request => request.Path));
        Assert.All(boundary.Requests, request => Assert.Equal("Bearer controlled-service-token", request.Authorization));
        Assert.All(boundary.Requests.Where(request => request.Path.StartsWith("materials", StringComparison.Ordinal)
            || request.Path == "currencies"), request => Assert.Equal("catalog.test", request.Host));
        var status = Assert.Single(boundary.Requests, request => request.Path == $"orderstatuses/histories/{OrderId}/new");
        Assert.NotEqual(create.IdempotencyKey, status.IdempotencyKey);
        Assert.Equal(3, boundary.Requests.Count(request => request.Method == "POST"));
        Assert.Empty(boundary.Tokens.Invalidated);
    }

    [Theory]
    [InlineData(BuildPreference.Quality)]
    [InlineData(BuildPreference.Standard)]
    [InlineData(BuildPreference.Strength)]
    public async Task Provision_ResinOrderWireDescribesResinDespiteStaleFdmPreference(BuildPreference preference)
    {
        using var boundary = new Boundary("M68", databaseName: "Resin Standard");
        var part = Part("M68", preference);
        var quote = SyntheticPhysicalPricingTestData.Quote(new([part])).Parts.Single();
        Assert.Equal(PrintProcess.Resin, quote.Process);

        var result = await boundary.Client.ProvisionOrderAsync(
            SubmissionId, 0, CustomerId, null, part, quote, 7, File, default);

        Assert.True(result.Succeeded);
        var create = Assert.Single(boundary.Requests, request => request.Method == "POST" && request.Path == "orders");
        using var document = JsonDocument.Parse(create.Body!);
        var wire = document.RootElement;
        const string resin = "Standard resin - 0.05 mm layers, full-layer exposure, wash and post-cure";
        Assert.Equal($"3D printing: Standard Resin (M68); {resin}", wire.GetProperty("description").GetString());
        var comment = wire.GetProperty("comment").GetString()!;
        Assert.Contains($"Build: {resin}", comment, StringComparison.Ordinal);
        Assert.Contains("Review state: engineer_review_required", comment, StringComparison.Ordinal);
        Assert.Contains("Estimate confidence: provisional", comment, StringComparison.Ordinal);
        Assert.DoesNotContain("Build: Quality", comment, StringComparison.Ordinal);
        Assert.DoesNotContain("Build: Strength", comment, StringComparison.Ordinal);
        Assert.Equal(boundary.MaterialId, wire.GetProperty("materialId").GetInt32());
        Assert.Equal(3, wire.GetProperty("processId").GetInt32());
    }

    public static IEnumerable<object[]> FailedCatalogCases()
    {
        foreach (var material in new[] { "PA612-ESD", "ABS-ESD" })
            foreach (var failure in new[] { "missing-material", "missing-color", "missing-finish", "missing-currency",
                "material-403", "colors-401", "finish-403", "currency-403", "material-502", "currency-502", "material-network", "colors-timeout",
                "currency-network", "currency-timeout" })
                yield return [material, failure];
    }

    [Theory]
    [InlineData("PA612-ESD", " pa612-esd ", "PA612-ESD")]
    [InlineData("ABS-ESD", "\tabs-esd\r\n", "ABS-ESD")]
    [InlineData("TPU", " tpu ", "TPU (Shore 95A)")]
    [InlineData("PC", " pc ", "Polycarbonate (PC)")]
    [InlineData("M68", " m68 ", "Resin Standard")]
    public async Task Configuration_RecognizedPaddedMaterialKey_PersistsCanonicalIdentityBeforeFulfillment(
        string canonicalKey, string suppliedKey, string databaseName)
    {
        using var boundary = new Boundary(canonicalKey, databaseName: databaseName);
        var original = Part(canonicalKey, BuildPreference.Standard);
        var store = new AdmissionStore(original);
        var pricing = new AdmissionPricing();
        await using var workflow = new InstantQuotationWorkflowCoordinator(
            store, Unexpected<IInstantQuotationUploadClient>(), pricing, null, authoritativePricingService: pricing);
        await workflow.InitializeAsync(store.State.SessionId, default);
        await workflow.UpdateConfigurationAsync(original.PartId, suppliedKey, "Black", 2, default);
        var part = Assert.Single(store.State.Parts);
        Assert.Equal(canonicalKey, part.Configuration.MaterialKey);
        Assert.Equal(canonicalKey, Assert.Single(workflow.Parts).Configuration.MaterialKey);
        var quote = Assert.Single(workflow.OrderQuote!.Parts);

        var result = await boundary.Client.ProvisionOrderAsync(
            SubmissionId, 0, CustomerId, null, part, quote, 7, File, default);

        Assert.True(result.Succeeded);
        Assert.Equal(OrderId, result.OrderId);
        var create = Assert.Single(boundary.Requests, request => request.Method == "POST" && request.Path == "orders");
        using var document = JsonDocument.Parse(create.Body!);
        Assert.Equal(boundary.MaterialId, document.RootElement.GetProperty("materialId").GetInt32());
        Assert.Equal(9, document.RootElement.GetProperty("colorId").GetInt32());
        Assert.Equal(764, document.RootElement.GetProperty("currencyId").GetInt32());
        Assert.Contains("3D printing:", document.RootElement.GetProperty("description").GetString()!, StringComparison.Ordinal);
        Assert.Contains(boundary.Requests, request => request.Path == $"materials/{boundary.MaterialId}/colors");
        Assert.DoesNotContain(boundary.Requests, request => request.Path.StartsWith("materials/999/", StringComparison.Ordinal));
        Assert.Equal(3, boundary.Requests.Count(request => request.Method == "POST"));
        Assert.All(boundary.Requests, request => Assert.Equal("Bearer controlled-service-token", request.Authorization));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t ")]
    public async Task Configuration_NullOrBlankColor_IsRejectedBeforePersistenceCatalogOrOrder(string? suppliedColor)
    {
        using var boundary = new Boundary("PA612-ESD");
        var validPart = Part("PA612-ESD", BuildPreference.Standard);
        var store = new AdmissionStore(validPart);
        var pricing = new AdmissionPricing();
        await using var workflow = new InstantQuotationWorkflowCoordinator(
            store, Unexpected<IInstantQuotationUploadClient>(), pricing, null, authoritativePricingService: pricing);
        await workflow.InitializeAsync(store.State.SessionId, default);
        var saves = store.Saves;
        var prices = pricing.Calls;

        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await workflow.UpdateConfigurationAsync(validPart.PartId, "PA612-ESD", suppliedColor!, 2, default);
            var part = Assert.Single(store.State.Parts);
            await boundary.Client.ProvisionOrderAsync(
                SubmissionId, 0, CustomerId, null, part, Assert.Single(workflow.OrderQuote!.Parts), 7, File, default);
        });

        Assert.Equal(saves, store.Saves);
        Assert.Equal(prices, pricing.Calls);
        Assert.Equal("Black", Assert.Single(store.State.Parts).Configuration.Color);
        Assert.Equal("Black", Assert.Single(workflow.Parts).Configuration.Color);
        Assert.Empty(boundary.Requests);
    }

    private sealed class AdmissionStore(InstantQuotationPart part) : IInstantQuotationSessionStore
    {
        public InstantQuotationSessionState State { get; private set; } = new(
            "owned-admission-session", SubmissionId, new([part]), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        public int Saves { get; private set; }
        public Task<InstantQuotationSessionState> CreateAsync(string? ownerIdentity,
            InstantQuotationOrderState requestState, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The owned protected session must be restored.");
        public Task<InstantQuotationSessionState?> GetAsync(string sessionId, string? ownerIdentity,
            CancellationToken cancellationToken) => Task.FromResult<InstantQuotationSessionState?>(State);
        public Task<bool> PutAsync(InstantQuotationSessionState session, string? ownerIdentity,
            CancellationToken cancellationToken)
        {
            State = session;
            Saves++;
            return Task.FromResult(true);
        }
        public Task<bool> RemoveAsync(string sessionId, string? ownerIdentity, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Admission proof must not remove the protected session.");
    }

    private sealed class AdmissionPricing : IInstantQuotationPricingService, IInstantQuotationAuthoritativePricingService
    {
        public int Calls { get; private set; }
        public InstantQuotationOrderQuote Quote(InstantQuotationOrderState state)
        {
            Calls++;
            return SyntheticPhysicalPricingTestData.Quote(state);
        }
        public Task<InstantQuotationOrderQuote?> QuoteAsync(InstantQuotationSessionState session, string? ownerIdentity,
            bool includeComparisons, CancellationToken cancellationToken) =>
            Task.FromResult<InstantQuotationOrderQuote?>(Quote(session.RequestState));
    }

    [Theory]
    [MemberData(nameof(FailedCatalogCases))]
    public async Task Provision_MissingDeniedOrTransientCatalog_DoesNotWriteOrders(string material, string failure)
    {
        using var boundary = new Boundary(material, failure);
        var part = Part(material, BuildPreference.Standard);
        var quote = SyntheticPhysicalPricingTestData.Quote(new([part])).Parts.Single();

        var result = await boundary.Client.ProvisionOrderAsync(
            SubmissionId, 0, CustomerId, null, part, quote, 7, File, default);

        Assert.False(result.Succeeded);
        Assert.Null(result.OrderId);
        Assert.DoesNotContain(boundary.Requests, request => request.Method != "GET");
        Assert.Contains(boundary.Requests, request => request.Path == "materials/printable");
        if (failure.EndsWith("401", StringComparison.Ordinal) || failure.EndsWith("403", StringComparison.Ordinal))
            Assert.Contains("controlled-service-token", boundary.Tokens.Invalidated);
        if (failure.EndsWith("502", StringComparison.Ordinal) || failure.EndsWith("network", StringComparison.Ordinal)
            || failure.EndsWith("timeout", StringComparison.Ordinal) || failure == "currency-403") Assert.False(result.ServiceAvailable);
        if (failure.StartsWith("missing-", StringComparison.Ordinal)) Assert.True(result.ServiceAvailable);
    }

    private static InstantQuotationPart Part(string material, BuildPreference build) => new(
        Guid.Parse("11111111-1111-1111-1111-111111111111"), "fixture.stl", new(Guid.NewGuid().ToString("D")),
        AuthoritativeInstantQuotationGeometry.RestoreFromProtectedSession(1, new string('a', 64), 10, 20, 30, 1000, 700,
            Enumerable.Repeat(100d, 64).ToArray(), Enumerable.Repeat(60d, 64).ToArray(), 12, 1, true, false, false, 0.8),
        new(material, "Black", 2, build));

    [Theory]
    [InlineData("PA612-ESD", "currency-502", true)]
    [InlineData("ABS-ESD", "currency-502", true)]
    [InlineData("PA612-ESD", "currency-network", true)]
    [InlineData("ABS-ESD", "currency-network", true)]
    [InlineData("PA612-ESD", "currency-502", false)]
    [InlineData("ABS-ESD", "currency-502", false)]
    public async Task Fulfill_CurrencyOutageOrLostFence_PreservesCheckpointWithoutCompensationAcrossRetry(
        string material, string failure, bool validFence)
    {
        using var boundary = new Boundary(material, failure);
        var pending = Part(material, BuildPreference.Standard) with { UploadReference = new(File.FileId.ToString("D")) };
        var completedId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var completed = pending with { PartId = completedId, UploadReference = new(completedId.ToString("D")) };
        var state = new InstantQuotationOrderState([completed, pending]);
        var time = new DateTimeOffset(2026, 10, 2, 0, 0, 0, TimeSpan.Zero);
        var session = new InstantQuotationSessionState("controlled-session", SubmissionId, state, time, time);
        var checkpoint = new InstantQuotationSubmissionCheckpoint(SubmissionId, 417,
            InstantQuotationSubmissionCheckpointStatus.OrdersProvisioning, new string('b', 64),
            [File with { FileId = completedId }, File], CustomerId, CustomerCreated: true,
            OrderIds: [900], TransactionId: "preserved-transaction", JourneyId: completedId);
        await using var lease = new Lease(checkpoint, validFence);
        var coordinator = new InstantQuotationFulfillmentCoordinator(boundary.Client);
        var customer = new InstantQuotationCustomerSubmission("Mali", "Ev", "controlled@example.test", null,
            "Thailand", null, null, "ตรวจสอบชิ้นงาน / inspect fixture");
        var quote = SyntheticPhysicalPricingTestData.Quote(state);

        var first = await coordinator.FulfillAsync(session, quote, "customer:42", customer, lease, checkpoint, default);
        var retry = await coordinator.FulfillAsync(session, quote, "customer:42", customer, lease, lease.Checkpoint, default);

        Assert.All(new[] { first, retry }, result =>
        {
            Assert.Equal(InstantQuotationSubmissionOutcome.Partial, result.Outcome);
            Assert.Equal(validFence ? InstantQuotationProblemCategory.DependencyUnavailable : InstantQuotationProblemCategory.Conflict,
                result.ProblemCategory);
            Assert.Equal(417, result.RequestReference);
        });
        Assert.Same(checkpoint, lease.Checkpoint);
        Assert.False(lease.Checkpoint.CompensationRequired);
        Assert.Equal([900], lease.Checkpoint.OrderIds);
        Assert.Equal(CustomerId, lease.Checkpoint.CustomerId);
        Assert.True(lease.Checkpoint.CustomerCreated);
        Assert.Equal(2, lease.Renewals);
        Assert.Empty(lease.Writes);
        Assert.DoesNotContain(boundary.Requests, request => request.Method != "GET");
        if (validFence)
            Assert.Equal(2, boundary.Requests.Count(request => request.Path == "currencies"));
        else
            Assert.Empty(boundary.Requests);
    }

    private sealed class Lease(InstantQuotationSubmissionCheckpoint checkpoint, bool validFence) : IInstantQuotationSubmissionLease
    {
        public InstantQuotationSubmissionCheckpoint Checkpoint { get; private set; } = checkpoint;
        public List<InstantQuotationSubmissionCheckpoint> Writes { get; } = [];
        public int Renewals { get; private set; }
        public Task<bool> RenewAsync(CancellationToken cancellationToken)
        {
            Renewals++;
            return Task.FromResult(validFence);
        }
        public Task<InstantQuotationSubmissionCheckpointRead> ReadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new InstantQuotationSubmissionCheckpointRead(validFence, Checkpoint));
        public Task<bool> TryPutAsync(InstantQuotationSubmissionCheckpoint value,
            InstantQuotationSubmissionCheckpointStatus? expectedPriorStatus, CancellationToken cancellationToken)
        {
            if (!validFence || Checkpoint.Status != expectedPriorStatus) return Task.FromResult(false);
            Checkpoint = value;
            Writes.Add(value);
            return Task.FromResult(true);
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class Boundary : IDisposable
    {
        private readonly Dictionary<string, HttpClient> clients;
        private readonly string material;
        private readonly string processName;
        private readonly string? failure;
        public int MaterialId { get; }
        public List<Request> Requests { get; } = [];
        public Tokens Tokens { get; } = new();
        public InstantQuotationFulfillmentClient Client { get; }

        public Boundary(string material, string? failure = null, string? databaseName = null)
        {
            this.material = databaseName ?? material;
            processName = PricingCatalog.ResolveMaterial(material)?.Process == PrintProcess.Resin ? "SLA" : "FDM";
            this.failure = failure;
            MaterialId = material == "PA612-ESD" ? 612 : 970;
            clients = new(StringComparer.Ordinal)
            {
                ["orders"] = new(new Handler(this)) { BaseAddress = new("https://orders.test/") },
                ["catalog"] = new(new Handler(this)) { BaseAddress = new("https://catalog.test/") },
            };
            var factory = new Factory(clients);
            var catalog = new CustomerOrderCatalogClient(factory, Tokens, NullLogger<CustomerOrderCatalogClient>.Instance);
            var transport = new CustomerOrderSubmissionTransport(factory, Tokens, TimeProvider.System,
                NullLogger<CustomerOrderSubmissionTransport>.Instance);
            Client = new(Unexpected<ICountryClient>(), Unexpected<ICustomerProfileClient>(), Unexpected<ICustomerAuthenticationClient>(),
                catalog, transport, Unexpected<INotificationClient>(), NullLogger<InstantQuotationFulfillmentClient>.Instance);
        }

        public HttpResponseMessage Respond(Request request)
        {
            var path = request.Path;
            if ((failure == "material-network" && path == "materials/printable")) throw new HttpRequestException("controlled unavailable boundary");
            if (failure == "colors-timeout" && path.EndsWith("/colors", StringComparison.Ordinal)) throw new TaskCanceledException("controlled timeout");
            if (failure == "currency-network" && path == "currencies") throw new HttpRequestException("controlled currency unavailable boundary");
            if (failure == "currency-timeout" && path == "currencies") throw new TaskCanceledException("controlled currency timeout");
            if ((failure == "material-403" || failure == "material-502") && path == "materials/printable")
                return new(failure == "material-403" ? HttpStatusCode.Forbidden : HttpStatusCode.BadGateway);
            if (failure == "colors-401" && path.EndsWith("/colors", StringComparison.Ordinal)) return new(HttpStatusCode.Unauthorized);
            if (failure == "finish-403" && path.EndsWith("/surfacefinishes", StringComparison.Ordinal)) return new(HttpStatusCode.Forbidden);
            if ((failure == "currency-403" || failure == "currency-502") && path == "currencies")
                return new(failure == "currency-403" ? HttpStatusCode.Forbidden : HttpStatusCode.BadGateway);
            return path switch
            {
                "orders/processes/additive" => Json(JsonSerializer.Serialize(
                    new[] { new { id = 3, categoryId = 1, name = processName } })),
                "orders/fileformats" => Json("[{\"id\":2,\"name\":\"STL\",\"extension\":\".stl\"}]"),
                "materials/printable" => Json(JsonSerializer.Serialize(failure == "missing-material"
                    ? new[] { new { id = 999, materialGroupId = 8, printable = true, name = "PC-ESD" } }
                    : [new { id = 999, materialGroupId = 8, printable = true, name = "PC-ESD" },
                        new { id = MaterialId, materialGroupId = 8, printable = true, name = material }])),
                "materials/materialgroups" => Json("[{\"id\":8,\"name\":\"Plastics\"}]"),
                var value when value == $"materials/{MaterialId}/colors" => Json(failure == "missing-color" ? "[]" : "[{\"id\":9,\"name\":\"Black\"}]"),
                var value when value == $"materials/{MaterialId}/surfacefinishes" => Json(failure == "missing-finish" ? "[]" : "[{\"id\":7,\"name\":\"As printed\"}]"),
                "currencies" => Json(failure == "missing-currency" ? "[]" : "[{\"id\":764,\"shortName\":\"THB\",\"longName\":\"Thai Baht\"}]"),
                "orders" when request.Method == "POST" => Json("{\"id\":901}", HttpStatusCode.Created),
                "orderstatuses/histories/901/new" when request.Method == "POST" => new(HttpStatusCode.NoContent),
                "orders/customers/42/901" => Json("{\"order\":{\"id\":901,\"customerId\":42},\"files\":[]}"),
                "orders/901/files?bucket=test-owned-bucket&objectName=clean%2Ffixture%20part.stl" when request.Method == "POST" => new(HttpStatusCode.NoContent),
                _ => throw new InvalidOperationException($"Unreviewed controlled request: {request.Method} {path}"),
            };
        }

        public void Dispose() { foreach (var client in clients.Values) client.Dispose(); }
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    private static T Unexpected<T>() where T : class => DispatchProxy.Create<T, UnexpectedDependency>();
    public class UnexpectedDependency : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new InvalidOperationException($"Unowned dependency invoked: {targetMethod?.Name}");
    }

    private sealed class Factory(IReadOnlyDictionary<string, HttpClient> clients) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => clients[name];
    }

    private sealed class Tokens : IServiceAccessTokenProvider
    {
        public List<string> Invalidated { get; } = [];
        public ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken) => ValueTask.FromResult<string?>("controlled-service-token");
        public void Invalidate(string token) => Invalidated.Add(token);
    }

    private sealed class Handler(Boundary boundary) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var recorded = new Request(request.Method.Method, request.RequestUri!.Host, request.RequestUri.PathAndQuery.TrimStart('/'),
                request.Headers.Authorization?.ToString(), request.Headers.TryGetValues("Idempotency-Key", out var keys) ? keys.Single() : null,
                request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken));
            boundary.Requests.Add(recorded);
            return boundary.Respond(recorded);
        }
    }

    private sealed record Request(string Method, string Host, string Path, string? Authorization, string? IdempotencyKey, string? Body);
}
