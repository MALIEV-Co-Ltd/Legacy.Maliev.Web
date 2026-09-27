using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Infrastructure;

namespace Legacy.Maliev.Web.Tests;

public sealed class InstantQuotationPhysicalAnalysisInputReaderTests
{
    private const string WebSessionId = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string Owner = "member-42";
    private static readonly Guid FileSessionId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid FileId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly byte[] Bytes = Encoding.UTF8.GetBytes("solid verified\nendsolid verified\n");

    [Fact]
    public async Task ReadAsync_UsesOwnedSessionAndCapabilityForExactCleanUpload()
    {
        var (reader, handler, part) = CreateReader();

        var result = await reader.ReadAsync(WebSessionId, Owner, part.PartId, default);

        Assert.True(result.IsReady);
        Assert.Equal(Bytes, result.Content);
        Assert.Single(handler.Requests);
        var request = handler.Requests.Single();
        Assert.Equal($"/file/v1/instant-quotation/sessions/{FileSessionId:D}/files/{FileId:D}/content", request.RequestUri!.AbsolutePath);
        Assert.Equal("opaque-capability-000000000000000", Assert.Single(request.Headers.GetValues("X-Quote-Session-Token")));
    }

    [Fact]
    public async Task ReadAsync_OwnerOrSessionMismatch_ReturnsNoBytesWithoutFileRequest()
    {
        var (reader, handler, part) = CreateReader();

        var wrongOwner = await reader.ReadAsync(WebSessionId, "different-owner", part.PartId, default);
        var wrongSession = await reader.ReadAsync("different-session", Owner, part.PartId, default);

        Assert.Equal(InstantQuotationPhysicalAnalysisInputFailure.SessionUnavailable, wrongOwner.Failure);
        Assert.Equal(InstantQuotationPhysicalAnalysisInputFailure.SessionUnavailable, wrongSession.Failure);
        Assert.Null(wrongOwner.Content);
        Assert.Null(wrongSession.Content);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ReadAsync_MissingOrAlteredUploadDescriptor_FailsBeforeFileRequest()
    {
        var original = Part();
        var cases = new[]
        {
            original with { PhysicalAnalysisUpload = null },
            original with { PhysicalAnalysisUpload = original.PhysicalAnalysisUpload! with { FileId = Guid.NewGuid() } },
            original with { PhysicalAnalysisUpload = original.PhysicalAnalysisUpload! with { Sha256 = new string('a', 64) } },
            original with { PhysicalAnalysisUpload = original.PhysicalAnalysisUpload! with { Status = "pending" } },
            original with { PhysicalAnalysisUpload = original.PhysicalAnalysisUpload! with { SizeBytes = 32L * 1024 * 1024 + 1 } },
        };

        foreach (var part in cases)
        {
            var (reader, handler, _) = CreateReader(part);
            var result = await reader.ReadAsync(WebSessionId, Owner, part.PartId, default);
            Assert.False(result.IsReady);
            Assert.Null(result.Content);
            Assert.Empty(handler.Requests);
        }
    }

    [Fact]
    public async Task ReadAsync_ExpiredCapabilityOrAlteredDownloadedBytes_FailsClosed()
    {
        var (missingCapability, firstHandler, part) = CreateReader(capability: null, hasCapability: false);
        var unavailable = await missingCapability.ReadAsync(WebSessionId, Owner, part.PartId, default);
        Assert.Equal(InstantQuotationPhysicalAnalysisInputFailure.CapabilityUnavailable, unavailable.Failure);
        Assert.Empty(firstHandler.Requests);

        var (alteredReader, _, alteredPart) = CreateReader(responseBytes: Encoding.UTF8.GetBytes("solid tampered\nendsolid tampered\n"));
        var altered = await alteredReader.ReadAsync(WebSessionId, Owner, alteredPart.PartId, default);
        Assert.Equal(InstantQuotationPhysicalAnalysisInputFailure.ContentUnavailable, altered.Failure);
        Assert.Null(altered.Content);
    }

    [Fact]
    public async Task ReadAsync_Cancelled_DoesNotMakeFileRequest()
    {
        var (reader, handler, part) = CreateReader();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => reader.ReadAsync(WebSessionId, Owner, part.PartId, cancellation.Token));
        Assert.Empty(handler.Requests);
    }

    private static (InstantQuotationPhysicalAnalysisInputReader Reader, RecordingHandler Handler, InstantQuotationPart Part) CreateReader(
        InstantQuotationPart? chosenPart = null,
        InstantQuotationFileCapability? capability = null,
        bool hasCapability = true,
        byte[]? responseBytes = null)
    {
        var part = chosenPart ?? Part();
        var handler = new RecordingHandler(responseBytes ?? Bytes);
        var transport = new InstantQuotationFileServiceTransport(
            new NamedClientFactory(new HttpClient(handler) { BaseAddress = new Uri("http://files/") }),
            new TokenProvider());
        var session = new InstantQuotationSessionState(
            WebSessionId,
            "submission",
            new InstantQuotationOrderState([part]),
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);
        var reader = new InstantQuotationPhysicalAnalysisInputReader(
            new SessionStore(session),
            new CapabilityStore(hasCapability ? capability ?? Capability() : null),
            transport);
        return (reader, handler, part);
    }

    private static InstantQuotationPart Part()
    {
        var sha = Convert.ToHexStringLower(SHA256.HashData(Bytes));
        var claim = new InstantQuotationGeometryClaim(
            1, sha, 10, 10, 10, 500, 600,
            Enumerable.Repeat(1d, 64).ToArray(),
            Enumerable.Repeat(1d, 64).ToArray(),
            100, 1, true, false, false, 1);
        var upload = InstantQuotationUploadResult.Succeeded(
            "operation", new InstantQuotationUploadReference(FileId.ToString("D")), sha);
        return new InstantQuotationPart(
            Guid.NewGuid(),
            "part.stl",
            upload.UploadReference!,
            AuthoritativeInstantQuotationGeometry.FromCompletedLegacyUpload(upload, claim)!,
            new InstantQuotationPartConfiguration("PLA", "Black", 1),
            new InstantQuotationPhysicalAnalysisUpload(FileId, "part.stl", "model/stl", Bytes.Length, sha, "clean"));
    }

    private static InstantQuotationFileCapability Capability() => new(
        FileSessionId, "opaque-capability-000000000000000", DateTimeOffset.Parse("2099-01-01T00:00:00Z"),
        209_715_200, 100, [".stl", ".obj", ".3mf", ".step", ".stp", ".iges", ".igs", ".glb", ".gltf"]);

    private sealed class SessionStore(InstantQuotationSessionState session) : IInstantQuotationSessionStore
    {
        public Task<InstantQuotationSessionState?> GetAsync(string sessionId, string? ownerIdentity, CancellationToken cancellationToken) =>
            Task.FromResult<InstantQuotationSessionState?>(
                sessionId == session.SessionId && ownerIdentity == Owner ? session : null);

        public Task<InstantQuotationSessionState> CreateAsync(string? ownerIdentity, InstantQuotationOrderState requestState, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> PutAsync(InstantQuotationSessionState state, string? ownerIdentity, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> RemoveAsync(string sessionId, string? ownerIdentity, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class CapabilityStore(InstantQuotationFileCapability? capability) : IInstantQuotationFileCapabilityStore
    {
        public Task<InstantQuotationFileCapability?> GetAsync(string sessionId, string? ownerIdentity, CancellationToken cancellationToken) =>
            Task.FromResult(sessionId == WebSessionId && ownerIdentity == Owner ? capability : null);

        public Task<bool> PutAsync(string sessionId, string? ownerIdentity, InstantQuotationFileCapability value, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> RemoveAsync(string sessionId, string? ownerIdentity, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingHandler(byte[] responseBytes) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(responseBytes) };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("model/stl");
            response.Headers.CacheControl = new CacheControlHeaderValue { NoStore = true };
            response.Headers.Add("X-Content-Type-Options", "nosniff");
            return Task.FromResult(response);
        }
    }

    private sealed class NamedClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class TokenProvider : IServiceAccessTokenProvider
    {
        public ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken) => ValueTask.FromResult<string?>("service-jwt");

        public void Invalidate(string token) { }
    }
}
