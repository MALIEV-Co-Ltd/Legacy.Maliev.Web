using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace Legacy.Maliev.Web.Tests;

public sealed class NotificationClientTests
{
    [Fact]
    public async Task Send_UsesAuthenticatedJsonContractWithoutPiiInUrl()
    {
        var handler = new RecordingHandler(async request =>
        {
            Assert.Equal(new AuthenticationHeaderValue("Bearer", "service-token"), request.Headers.Authorization);
            var body = await request.Content!.ReadAsStringAsync();
            using var json = JsonDocument.Parse(body);
            Assert.Equal("customer@example.com", json.RootElement.GetProperty("to").GetString());
            Assert.Equal("<p>Received</p>", json.RootElement.GetProperty("body").GetString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"providerMessageId\":\"brevo-id\"}", Encoding.UTF8, "application/json")
            };
        });
        var client = new NotificationClient(
            new NamedHttpClientFactory(new HttpClient(handler) { BaseAddress = new Uri("http://notifications/") }),
            new StubTokenProvider("service-token"),
            NullLogger<NotificationClient>.Instance);

        var result = await client.SendAsync(
            NotificationChannel.Info,
            new EmailNotification(
                "customer@example.com",
                "Contact request #42",
                "<p>Received</p>",
                null,
                null,
                ["info@maliev.com"]),
            CancellationToken.None);

        Assert.True(result.Sent);
        Assert.True(result.ServiceAvailable);
        Assert.True(result.Authorized);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("/notifications/v1/email/Info", request.RequestUri?.AbsolutePath);
        Assert.DoesNotContain("customer%40example.com", request.RequestUri?.OriginalString, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Contact", request.RequestUri?.Query, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Send_MissingServiceTokenFailsClosedWithoutCallingNotificationService()
    {
        var handler = new RecordingHandler(_ => Task.FromException<HttpResponseMessage>(
            new InvalidOperationException("NotificationService must not be called.")));
        var client = new NotificationClient(
            new NamedHttpClientFactory(new HttpClient(handler) { BaseAddress = new Uri("http://notifications/") }),
            new StubTokenProvider(null),
            NullLogger<NotificationClient>.Instance);

        var result = await client.SendAsync(
            NotificationChannel.Info,
            new EmailNotification("customer@example.com", "Subject", "Body", null, null, null),
            CancellationToken.None);

        Assert.False(result.Sent);
        Assert.False(result.ServiceAvailable);
        Assert.False(result.Authorized);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task SendIdempotent_UsesCanonicalStableOperationKey()
    {
        var operationId = Guid.Parse("3c12214d-8794-4dd0-a812-d32040752010");
        var handler = new RecordingHandler(request =>
        {
            Assert.Equal(
                operationId.ToString("D"),
                request.Headers.GetValues("Idempotency-Key").Single());
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var client = new NotificationClient(
            new NamedHttpClientFactory(new HttpClient(handler) { BaseAddress = new Uri("http://notifications/") }),
            new StubTokenProvider("service-token"),
            NullLogger<NotificationClient>.Instance);
        var notification = new EmailNotification(
            "customer@example.com",
            "Welcome",
            "<p>Welcome</p>",
            null,
            null,
            null);

        Assert.True((await client.SendIdempotentAsync(
            NotificationChannel.Info,
            notification,
            operationId,
            CancellationToken.None)).Sent);
        Assert.True((await client.SendIdempotentAsync(
            NotificationChannel.Info,
            notification,
            operationId,
            CancellationToken.None)).Sent);

        Assert.Equal(2, handler.Requests.Count);
    }

    private sealed class StubTokenProvider(string? token) : IServiceAccessTokenProvider
    {
        public ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken) => ValueTask.FromResult(token);

        public void Invalidate(string token)
        {
        }
    }

    private sealed class NamedHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            Assert.Equal("notifications", name);
            return client;
        }
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
            : this(request => Task.FromResult(respond(request)))
        {
        }

        public List<HttpRequestMessage> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return await respond(request);
        }
    }
}
