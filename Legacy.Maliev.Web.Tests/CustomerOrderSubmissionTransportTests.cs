using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace Legacy.Maliev.Web.Tests;

public sealed class CustomerOrderSubmissionTransportTests
{
    [Fact]
    public async Task Create_UsesTrustedCustomerAndExactOrderContract()
    {
        var orders = new RecordingHandler(async request =>
        {
            Assert.Equal("legacy-web-member-order-25b70f86", request.Headers.GetValues("Idempotency-Key").Single());
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.Equal(42, json.RootElement.GetProperty("customerId").GetInt32());
            Assert.Equal(3, json.RootElement.GetProperty("processId").GetInt32());
            Assert.Equal(5, json.RootElement.GetProperty("materialId").GetInt32());
            Assert.Equal(7, json.RootElement.GetProperty("surfaceFinishId").GetInt32());
            Assert.Equal(9, json.RootElement.GetProperty("colorId").GetInt32());
            Assert.False(json.RootElement.GetProperty("allowSocialMedia").GetBoolean());
            Assert.True(json.RootElement.GetProperty("allowCancellation").GetBoolean());
            Assert.False(json.RootElement.GetProperty("allowPayment").GetBoolean());
            Assert.False(json.RootElement.TryGetProperty("email", out _));
            Assert.Equal(
                [
                    "allowCancellation", "allowPayment", "allowSocialMedia", "colorId", "comment",
                    "currencyId", "customerId", "description", "discountPercent", "employeeId",
                    "finishedDate", "leadTime", "manufactured", "materialId", "name", "processId",
                    "promisedDate", "quantity", "surfaceFinishId", "trackingNumber", "unitPrice",
                ],
                json.RootElement.EnumerateObject()
                    .Select(property => property.Name)
                    .Order(StringComparer.Ordinal)
                    .ToArray());
            return Json(HttpStatusCode.Created, "{\"id\":731}");
        });
        var transport = CreateTransport(orders: orders);

        var result = await transport.CreateAsync(
            42,
            Draft([new TestUpload("part.stl", "model/stl", [1])]),
            "legacy-web-member-order-25b70f86",
            CancellationToken.None);

        Assert.Equal(731, result.OrderId);
        Assert.True(result.ServiceAvailable);
        Assert.True(result.Authorized);
        Assert.Equal("orders", Assert.Single(orders.Requests).Path);
    }

    [Fact]
    public async Task Upload_UsesStablePathAndOctetStreamFallback()
    {
        var files = new RecordingHandler(async request =>
        {
            Assert.Equal("legacy-web-member-order-op-upload", request.Headers.GetValues("Idempotency-Key").Single());
            var body = await request.Content!.ReadAsByteArrayAsync();
            var text = Encoding.Latin1.GetString(body);
            Assert.Contains("Content-Type: application/octet-stream", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("filename=part.step", text, StringComparison.OrdinalIgnoreCase);
            return Json(HttpStatusCode.Created, "{\"object\":[{\"bucket\":\"maliev.com\",\"objectName\":\"uploads/42/2026-8-2/part.step\",\"uri\":\"https://storage.googleapis.com/maliev.com/uploads/42/2026-8-2/part.step\"}]}");
        });
        var transport = CreateTransport(files: files, timeProvider: new FixedTimeProvider(new DateTimeOffset(2026, 8, 2, 7, 0, 0, TimeSpan.Zero)));

        var result = await transport.UploadAsync(
            42,
            [new TestUpload("part.step", string.Empty, [1, 2, 3])],
            "legacy-web-member-order-op-upload",
            CancellationToken.None);

        Assert.True(result.ServiceAvailable);
        Assert.Equal("uploads?bucket=maliev.com&path=uploads%2F42%2F2026-8-2", Assert.Single(files.Requests).Path);
        Assert.Equal("uploads/42/2026-8-2/part.step", Assert.Single(result.Objects!).ObjectName);
    }

    [Fact]
    public async Task Upload_MismatchedStorageResponse_FailsClosed()
    {
        var files = new RecordingHandler(_ => Json(
            HttpStatusCode.Created,
            "{\"object\":[{\"bucket\":\"unexpected\",\"objectName\":\"other/part.step\",\"uri\":\"https://example.test/part.step\"}]}"));
        var transport = CreateTransport(
            files: files,
            timeProvider: new FixedTimeProvider(new DateTimeOffset(2026, 8, 2, 7, 0, 0, TimeSpan.Zero)));

        var result = await transport.UploadAsync(
            42,
            [new TestUpload("part.step", "application/step", [1, 2, 3])],
            "legacy-web-member-order-op-upload",
            CancellationToken.None);

        Assert.True(result.ServiceAvailable);
        Assert.Null(result.Objects);
    }

    [Fact]
    public async Task Link_ExistingExactObject_DoesNotCreateDuplicateOrderFile()
    {
        var orders = new RecordingHandler(request => request.Method == HttpMethod.Get
            ? Json(HttpStatusCode.OK, "{\"order\":{\"id\":731,\"customerId\":42,\"processId\":3,\"quantity\":1,\"manufactured\":0,\"allowCancellation\":true,\"allowPayment\":false},\"process\":null,\"history\":[],\"files\":[{\"id\":3,\"orderId\":731,\"bucket\":\"maliev.com\",\"objectName\":\"uploads/42/2026-8-2/part.step\"}]}")
            : throw new InvalidOperationException("A replay must not create a duplicate file link."));
        var transport = CreateTransport(orders: orders);

        var result = await transport.LinkAsync(
            42,
            731,
            new CustomerOrderUploadedObject("maliev.com", "uploads/42/2026-8-2/part.step"),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(HttpMethod.Get, Assert.Single(orders.Requests).Method);
    }

    [Fact]
    public async Task Link_NewObject_VerifiesOwnershipBeforeCreatingOrderFile()
    {
        var orders = new RecordingHandler(request => request.Method == HttpMethod.Get
            ? Json(HttpStatusCode.OK, "{\"order\":{\"id\":731,\"customerId\":42},\"files\":[]}")
            : new HttpResponseMessage(HttpStatusCode.Created));
        var transport = CreateTransport(orders: orders);

        var result = await transport.LinkAsync(
            42,
            731,
            new CustomerOrderUploadedObject("maliev.com", "uploads/42/2026-8-2/part one.step"),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(2, orders.Requests.Count);
        Assert.Equal("orders/customers/42/731", orders.Requests[0].Path);
        Assert.Equal(HttpMethod.Post, orders.Requests[1].Method);
        Assert.Equal(
            "orders/731/files?bucket=maliev.com&objectName=uploads%2F42%2F2026-8-2%2Fpart%20one.step",
            orders.Requests[1].Path);
    }

    [Fact]
    public async Task AddNewStatus_UsesCanonicalKeyAndStatusRoute()
    {
        var orders = new RecordingHandler(request =>
        {
            Assert.Equal("legacy-web-member-order-op-status-new", request.Headers.GetValues("Idempotency-Key").Single());
            return new HttpResponseMessage(HttpStatusCode.Created);
        });
        var transport = CreateTransport(orders: orders);

        var result = await transport.AddNewStatusAsync(731, "legacy-web-member-order-op-status-new", CancellationToken.None);

        Assert.True(result.Succeeded);
        var request = Assert.Single(orders.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("orderstatuses/histories/731/new", request.Path);
    }

    private static CustomerOrderSubmissionTransport CreateTransport(
        RecordingHandler? orders = null,
        RecordingHandler? files = null,
        TimeProvider? timeProvider = null)
    {
        orders ??= new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        files ??= new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var clients = new Dictionary<string, HttpClient>(StringComparer.Ordinal)
        {
            ["orders"] = new(orders) { BaseAddress = new Uri("https://orders.test/") },
            ["files"] = new(files) { BaseAddress = new Uri("https://files.test/") },
        };
        var constructor = typeof(CustomerOrderSubmissionTransport).GetConstructors(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Single();
        return (CustomerOrderSubmissionTransport)constructor.Invoke([
            new NamedClientFactory(clients),
            new StubTokenProvider(),
            timeProvider ?? TimeProvider.System,
            NullLogger<CustomerOrderSubmissionTransport>.Instance,
        ]);
    }

    private static CustomerOrderDraft Draft(IReadOnlyList<ICustomerOrderUploadFile> files) => new(
        CustomerOrderKind.Additive,
        "Prototype bracket",
        "Please review tolerances.",
        3,
        5,
        7,
        9,
        2,
        false,
        files);

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    private sealed class TestUpload(string fileName, string contentType, byte[] content) : ICustomerOrderUploadFile
    {
        public string FileName { get; } = fileName;
        public string ContentType { get; } = contentType;
        public long Length => content.LongLength;
        public Stream OpenReadStream() => new MemoryStream(content, writable: false);
    }

    private sealed class NamedClientFactory(IReadOnlyDictionary<string, HttpClient> clients) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => clients[name];
    }

    private sealed class StubTokenProvider : IServiceAccessTokenProvider
    {
        public ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken) => ValueTask.FromResult<string?>("service-token");
        public void Invalidate(string token) { }
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
            : this(request => Task.FromResult(respond(request))) { }

        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new RecordedRequest(
                request.Method,
                request.RequestUri?.PathAndQuery.TrimStart('/') ?? string.Empty));
            return await respond(request);
        }
    }

    private sealed record RecordedRequest(HttpMethod Method, string Path);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
