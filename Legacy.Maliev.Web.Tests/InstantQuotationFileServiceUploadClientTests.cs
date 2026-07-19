using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Infrastructure;

namespace Legacy.Maliev.Web.Tests;

public sealed class InstantQuotationFileServiceUploadClientTests
{
    private const string WebSessionId = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string OwnerIdentity = "member-42";
    private static readonly Guid FileSessionId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid FirstFileId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid SecondFileId = Guid.Parse("00000000-0000-0000-0000-000000000002");

    [Fact]
    public async Task UploadAsync_ConcurrentFirstUploads_CreateOneOwnerBoundCapabilityAndSpoolNonSeekableStreams()
    {
        var bytes = Encoding.UTF8.GetBytes("solid part endsolid part");
        var sha256 = Sha256(bytes);
        var createCount = 0;
        var uploadCount = 0;
        var fileIds = new ConcurrentQueue<Guid>([FirstFileId, SecondFileId]);
        var handler = new RecordingHandler(async (request, cancellationToken) =>
        {
            if (request.RequestUri!.AbsolutePath == "/file/v1/instant-quotation/sessions")
            {
                Interlocked.Increment(ref createCount);
                await Task.Delay(25, cancellationToken);
                return ValidSession();
            }

            Assert.Equal($"/file/v1/instant-quotation/sessions/{FileSessionId:D}/files", request.RequestUri.AbsolutePath);
            Interlocked.Increment(ref uploadCount);
            Assert.True(fileIds.TryDequeue(out var fileId));
            return ValidUpload(fileId, bytes.Length, sha256);
        });
        var store = new RecordingCapabilityStore();
        var adapter = CreateAdapter(handler, store);

        var first = adapter.UploadAsync(
            WebSessionId,
            OwnerIdentity,
            new NonSeekableReadStream(bytes),
            "part.stl",
            "model/stl",
            bytes.Length,
            Claim(sha256),
            "upload-operation-0001",
            CancellationToken.None);
        var second = adapter.UploadAsync(
            WebSessionId,
            OwnerIdentity,
            new NonSeekableReadStream(bytes),
            "part.stl",
            "model/stl",
            bytes.Length,
            Claim(sha256),
            "upload-operation-0002",
            CancellationToken.None);

        var results = await Task.WhenAll(first, second);

        Assert.All(results, result => Assert.Equal(InstantQuotationOperationStatus.Succeeded, result.Status));
        Assert.All(results, result => Assert.Equal(sha256, result.ContentSha256));
        Assert.All(results, result => Assert.True(Guid.TryParseExact(result.UploadReference?.Value, "D", out _)));
        Assert.Equal(1, createCount);
        Assert.Equal(2, uploadCount);
        Assert.Equal(1, store.PutCount);
        Assert.All(store.Bindings, binding => Assert.Equal((WebSessionId, OwnerIdentity), binding));
    }

    [Fact]
    public async Task UploadAsync_ReturnedDigestDiffersFromGeometryClaim_RemovesFileAndFailsClosed()
    {
        var bytes = Encoding.UTF8.GetBytes("solid part endsolid part");
        var uploadedSha256 = Sha256(bytes);
        var removedFileIds = new List<Guid>();
        var handler = new RecordingHandler((request, _) =>
        {
            if (request.Method == HttpMethod.Post)
            {
                return Task.FromResult(ValidUpload(FirstFileId, bytes.Length, uploadedSha256));
            }

            Assert.Equal(HttpMethod.Delete, request.Method);
            removedFileIds.Add(FirstFileId);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        });
        var store = new RecordingCapabilityStore(Capability());
        var temporaryPath = NewTemporaryPath();
        var adapter = CreateAdapter(handler, store, () => temporaryPath);

        var result = await adapter.UploadAsync(
            WebSessionId,
            OwnerIdentity,
            new NonSeekableReadStream(bytes),
            "part.stl",
            "model/stl",
            bytes.Length,
            Claim(new string('b', 64)),
            "upload-operation-0003",
            CancellationToken.None);

        Assert.Equal(InstantQuotationOperationStatus.Failed, result.Status);
        Assert.Equal(InstantQuotationProblemCategory.Validation, result.ProblemCategory);
        Assert.Null(result.UploadReference);
        Assert.Equal([FirstFileId], removedFileIds);
        Assert.False(File.Exists(temporaryPath));
    }

    [Theory]
    [InlineData(HttpStatusCode.Conflict, "upload_in_progress")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "outcome_unknown")]
    public async Task UploadAsync_IdenticalReplayProblem_PreservesRetryDisposition(
        HttpStatusCode status,
        string code)
    {
        var bytes = Encoding.UTF8.GetBytes("solid part endsolid part");
        var handler = new RecordingHandler((_, _) => Task.FromResult(Problem(status, code)));
        var adapter = CreateAdapter(handler, new RecordingCapabilityStore(Capability()));

        var result = await adapter.UploadAsync(
            WebSessionId,
            OwnerIdentity,
            new NonSeekableReadStream(bytes),
            "part.stl",
            "model/stl",
            bytes.Length,
            Claim(Sha256(bytes)),
            "upload-operation-replay",
            CancellationToken.None);

        Assert.Equal(InstantQuotationOperationStatus.Failed, result.Status);
        Assert.Equal(InstantQuotationUploadRetryDisposition.RetryIdentical, result.RetryDisposition);
    }

    [Theory]
    [InlineData("00000000000000000000000000000001")]
    [InlineData("00000000-0000-0000-0000-00000000000A")]
    [InlineData("not-a-guid")]
    public async Task RemoveAsync_NoncanonicalUploadReference_FailsValidationWithoutTransport(string value)
    {
        var handler = new RecordingHandler((_, _) => throw new InvalidOperationException("No request expected."));
        var adapter = CreateAdapter(handler, new RecordingCapabilityStore(Capability()));

        var result = await adapter.RemoveAsync(
            WebSessionId,
            OwnerIdentity,
            new InstantQuotationUploadReference(value),
            "remove-operation-001",
            CancellationToken.None);

        Assert.Equal(InstantQuotationOperationStatus.Failed, result.Status);
        Assert.Equal(InstantQuotationProblemCategory.Validation, result.ProblemCategory);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task RemoveAsync_CanonicalReference_MapsSuccessfulTransportStatus()
    {
        var handler = new RecordingHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Delete, request.Method);
            Assert.EndsWith($"/files/{FirstFileId:D}", request.RequestUri!.AbsolutePath, StringComparison.Ordinal);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        });
        var adapter = CreateAdapter(handler, new RecordingCapabilityStore(Capability()));

        var result = await adapter.RemoveAsync(
            WebSessionId,
            OwnerIdentity,
            new InstantQuotationUploadReference(FirstFileId.ToString("D")),
            "remove-operation-002",
            CancellationToken.None);

        Assert.Equal(InstantQuotationOperationStatus.Succeeded, result.Status);
        Assert.Equal(InstantQuotationProblemCategory.None, result.ProblemCategory);
        Assert.Equal(InstantQuotationAuthorizationStatus.Authorized, result.AuthorizationStatus);
    }

    [Fact]
    public async Task FinalizeAsync_ServiceReturnsDifferentOrder_CorrelatesByFileIdAndKeepsCoordinatesInternal()
    {
        var handler = new RecordingHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            return Task.FromResult(ValidFinalization(FirstFileId, SecondFileId));
        });
        var adapter = CreateAdapter(handler, new RecordingCapabilityStore(Capability()));

        var result = await adapter.FinalizeAsync(
            WebSessionId,
            OwnerIdentity,
            417,
            [
                new InstantQuotationUploadReference(SecondFileId.ToString("D")),
                new InstantQuotationUploadReference(FirstFileId.ToString("D")),
            ],
            "finalize-operation-01",
            CancellationToken.None);

        Assert.Equal(InstantQuotationOperationStatus.Succeeded, result.Status);
        Assert.Equal([FirstFileId, SecondFileId], result.Files.Select(file => file.FileId));
        Assert.Equal("private", result.Files[0].Bucket);
        Assert.DoesNotContain("private", result.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("instant-quotation/417", result.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(
            typeof(InstantQuotationFinalizationResult).GetProperties(),
            property => property.Name is "Files" or "Bucket" or "ObjectName");
    }

    [Fact]
    public async Task FinalizeAsync_DuplicateReferences_FailsValidationWithoutTransport()
    {
        var handler = new RecordingHandler((_, _) => throw new InvalidOperationException("No request expected."));
        var adapter = CreateAdapter(handler, new RecordingCapabilityStore(Capability()));
        var reference = new InstantQuotationUploadReference(FirstFileId.ToString("D"));

        var result = await adapter.FinalizeAsync(
            WebSessionId,
            OwnerIdentity,
            417,
            [reference, reference],
            "finalize-operation-02",
            CancellationToken.None);

        Assert.Equal(InstantQuotationOperationStatus.Failed, result.Status);
        Assert.Equal(InstantQuotationProblemCategory.Validation, result.ProblemCategory);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task UploadAsync_CapabilityStoreThrows_FailsAsDependencyWithoutTransport()
    {
        var bytes = Encoding.UTF8.GetBytes("solid part endsolid part");
        var handler = new RecordingHandler((_, _) => throw new InvalidOperationException("No request expected."));
        var store = new RecordingCapabilityStore { ThrowOnGet = true };
        var adapter = CreateAdapter(handler, store);

        var result = await adapter.UploadAsync(
            WebSessionId,
            null,
            new MemoryStream(bytes),
            "part.stl",
            "model/stl",
            bytes.Length,
            Claim(Sha256(bytes)),
            "upload-operation-0004",
            CancellationToken.None);

        Assert.Equal(InstantQuotationServiceStatus.Unavailable, result.ServiceStatus);
        Assert.Equal(InstantQuotationProblemCategory.DependencyUnavailable, result.ProblemCategory);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task UploadAsync_FileServiceTransportThrows_FailsClosedAsDependency()
    {
        var bytes = Encoding.UTF8.GetBytes("solid part endsolid part");
        var handler = new RecordingHandler((_, _) => throw new HttpRequestException("Transport unavailable."));
        var adapter = CreateAdapter(handler, new RecordingCapabilityStore(Capability()));

        var result = await adapter.UploadAsync(
            WebSessionId,
            OwnerIdentity,
            new MemoryStream(bytes),
            "part.stl",
            "model/stl",
            bytes.Length,
            Claim(Sha256(bytes)),
            "upload-operation-0007",
            CancellationToken.None);

        Assert.Equal(InstantQuotationOperationStatus.Failed, result.Status);
        Assert.Equal(InstantQuotationServiceStatus.Unavailable, result.ServiceStatus);
        Assert.Equal(InstantQuotationProblemCategory.DependencyUnavailable, result.ProblemCategory);
    }

    [Fact]
    public async Task UploadAsync_NewCapabilityCannotBePersisted_FailsClosedBeforeSpooling()
    {
        var bytes = Encoding.UTF8.GetBytes("solid part endsolid part");
        var handler = new RecordingHandler((request, _) =>
        {
            Assert.Equal("/file/v1/instant-quotation/sessions", request.RequestUri!.AbsolutePath);
            return Task.FromResult(ValidSession());
        });
        var store = new RecordingCapabilityStore { PutResult = false };
        var adapter = CreateAdapter(handler, store);

        var result = await adapter.UploadAsync(
            WebSessionId,
            OwnerIdentity,
            new MemoryStream(bytes),
            "part.stl",
            "model/stl",
            bytes.Length,
            Claim(Sha256(bytes)),
            "upload-operation-0008",
            CancellationToken.None);

        Assert.Equal(InstantQuotationOperationStatus.Failed, result.Status);
        Assert.Equal(InstantQuotationServiceStatus.Unavailable, result.ServiceStatus);
        Assert.Equal(InstantQuotationProblemCategory.DependencyUnavailable, result.ProblemCategory);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task UploadAsync_CallerCancellation_PropagatesWithoutTransport()
    {
        var bytes = Encoding.UTF8.GetBytes("solid part endsolid part");
        var handler = new RecordingHandler((_, _) => throw new InvalidOperationException("No request expected."));
        var adapter = CreateAdapter(handler, new RecordingCapabilityStore(Capability()));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => adapter.UploadAsync(
            WebSessionId,
            OwnerIdentity,
            new MemoryStream(bytes),
            "part.stl",
            "model/stl",
            bytes.Length,
            Claim(Sha256(bytes)),
            "upload-operation-0005",
            cancellation.Token));

        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task UploadAsync_StreamHasExtraByte_FailsValidationAndDeletesSpool()
    {
        var bytes = Encoding.UTF8.GetBytes("1234");
        var handler = new RecordingHandler((_, _) => throw new InvalidOperationException("No request expected."));
        var temporaryPath = NewTemporaryPath();
        var adapter = CreateAdapter(
            handler,
            new RecordingCapabilityStore(Capability()),
            () => temporaryPath);

        var result = await adapter.UploadAsync(
            WebSessionId,
            OwnerIdentity,
            new NonSeekableReadStream(bytes),
            "part.stl",
            "model/stl",
            bytes.Length - 1,
            Claim(Sha256(bytes)),
            "upload-operation-0006",
            CancellationToken.None);

        Assert.Equal(InstantQuotationProblemCategory.Validation, result.ProblemCategory);
        Assert.Equal(0, handler.RequestCount);
        Assert.False(File.Exists(temporaryPath));
    }

    private static InstantQuotationFileServiceUploadClient CreateAdapter(
        RecordingHandler handler,
        IInstantQuotationFileCapabilityStore store,
        Func<string>? temporaryPathFactory = null)
    {
        var transport = new InstantQuotationFileServiceTransport(
            new NamedHttpClientFactory(new HttpClient(handler) { BaseAddress = new Uri("http://files/") }),
            new StubTokenProvider());
        return new InstantQuotationFileServiceUploadClient(
            transport,
            store,
            temporaryPathFactory ?? NewTemporaryPath);
    }

    private static InstantQuotationGeometryClaim Claim(string sha256) => new(
        1,
        sha256,
        10,
        10,
        10,
        500,
        600,
        Enumerable.Repeat(1d, 64).ToArray(),
        Enumerable.Repeat(1d, 64).ToArray(),
        100,
        1,
        true,
        false,
        false,
        1);

    private static InstantQuotationFileCapability Capability() => new(
        FileSessionId,
        "opaque-capability-000000000000000",
        DateTimeOffset.Parse("2099-07-20T12:00:00Z"),
        209_715_200,
        100,
        [".stl", ".obj", ".3mf", ".step", ".stp", ".iges", ".igs", ".glb", ".gltf"]);

    private static HttpResponseMessage ValidSession()
    {
        var response = Json(
            HttpStatusCode.Created,
            $$"""
            {"sessionId":"{{FileSessionId:D}}","sessionToken":"opaque-capability-000000000000000","expiresAt":"2099-07-20T12:00:00Z","maxUploadBytes":209715200,"maxFilesPerSession":100,"supportedExtensions":[".stl",".obj",".3mf",".step",".stp",".iges",".igs",".glb",".gltf"]}
            """);
        response.Headers.Location = new Uri(
            $"/file/v1/instant-quotation/sessions/{FileSessionId:D}",
            UriKind.Relative);
        return response;
    }

    private static HttpResponseMessage ValidUpload(Guid fileId, int sizeBytes, string sha256)
    {
        var response = Json(
            HttpStatusCode.Created,
            $$"""
            {"fileId":"{{fileId:D}}","fileName":"part.stl","contentType":"model/stl","sizeBytes":{{sizeBytes}},"sha256":"{{sha256}}","status":"clean"}
            """);
        response.Headers.Location = new Uri(
            $"/file/v1/instant-quotation/sessions/{FileSessionId:D}/files/{fileId:D}",
            UriKind.Relative);
        return response;
    }

    private static HttpResponseMessage ValidFinalization(Guid firstFileId, Guid secondFileId) => Json(
        HttpStatusCode.OK,
        $$"""
        {"quotationRequestId":417,"files":[{"fileId":"{{firstFileId:D}}","bucket":"private","objectName":"instant-quotation/417/first.stl","fileName":"first.stl","contentType":"model/stl","sizeBytes":123,"sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","status":"finalized"},{"fileId":"{{secondFileId:D}}","bucket":"private","objectName":"instant-quotation/417/second.stl","fileName":"second.stl","contentType":"model/stl","sizeBytes":456,"sha256":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb","status":"finalized"}]}
        """);

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    private static HttpResponseMessage Problem(HttpStatusCode status, string code)
    {
        var (title, detail) = code switch
        {
            "upload_in_progress" => (
                "Instant quotation operation is in progress",
                "Retry the identical request with the same idempotency key."),
            "outcome_unknown" => (
                "Instant quotation outcome is unknown",
                "Retry the identical request with the same idempotency key."),
            _ => throw new ArgumentOutOfRangeException(nameof(code)),
        };
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(
                $$"""
                {"type":"https://docs.maliev.com/problems/{{code}}","title":"{{title}}","status":{{(int)status}},"detail":"{{detail}}","code":"{{code}}"}
                """,
                Encoding.UTF8,
                "application/problem+json"),
        };
    }

    private static string Sha256(byte[] bytes) => Convert
        .ToHexString(SHA256.HashData(bytes))
        .ToLowerInvariant();

    private static string NewTemporaryPath() => Path.Combine(
        Path.GetTempPath(),
        $"legacy-web-instant-quotation-{Guid.NewGuid():N}.tmp");

    private sealed class RecordingCapabilityStore(InstantQuotationFileCapability? capability = null)
        : IInstantQuotationFileCapabilityStore
    {
        private readonly object gate = new();
        private InstantQuotationFileCapability? capability = capability;

        public int PutCount { get; private set; }

        public bool ThrowOnGet { get; init; }

        public bool PutResult { get; init; } = true;

        public List<(string SessionId, string? OwnerIdentity)> Bindings { get; } = [];

        public Task<bool> PutAsync(
            string webSessionId,
            string? ownerIdentity,
            InstantQuotationFileCapability value,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (gate)
            {
                PutCount++;
                Bindings.Add((webSessionId, ownerIdentity));
                if (PutResult)
                {
                    capability = value;
                }
            }

            return Task.FromResult(PutResult);
        }

        public Task<InstantQuotationFileCapability?> GetAsync(
            string webSessionId,
            string? ownerIdentity,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ThrowOnGet)
            {
                throw new IOException("Capability storage is unavailable.");
            }

            lock (gate)
            {
                Bindings.Add((webSessionId, ownerIdentity));
                return Task.FromResult(capability);
            }
        }

        public Task<bool> RemoveAsync(
            string webSessionId,
            string? ownerIdentity,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (gate)
            {
                Bindings.Add((webSessionId, ownerIdentity));
                capability = null;
            }

            return Task.FromResult(true);
        }
    }

    private sealed class StubTokenProvider : IServiceAccessTokenProvider
    {
        public ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<string?>("service-jwt");
        }

        public void Invalidate(string accessToken)
        {
        }
    }

    private sealed class NamedHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            Assert.Equal("files", name);
            return client;
        }
    }

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        private int requestCount;

        public int RequestCount => requestCount;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref requestCount);
            return respond(request, cancellationToken);
        }
    }

    private sealed class NonSeekableReadStream(byte[] bytes) : Stream
    {
        private readonly MemoryStream inner = new(bytes, writable: false);

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default) => inner.ReadAsync(buffer, cancellationToken);

        public override void Flush() => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
