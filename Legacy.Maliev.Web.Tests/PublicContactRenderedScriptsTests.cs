using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace Legacy.Maliev.Web.Tests;

public sealed class PublicContactRenderedScriptsTests
{
    [Fact]
    public async Task CaptureReadsBothCulturesOnceAndRetainsScriptsWithoutOwningTheClient()
    {
        var changed = false;
        var handler = new CaptureHandler(uri =>
        {
            var culture = uri.Query.Contains("culture=th", StringComparison.Ordinal) ? "th" : "en";
            return Response(Html(culture, changed ? "changed" : culture));
        });
        using var client = Client(handler);

        var scripts = await PublicContactRenderedScripts.CaptureAsync(client);
        changed = true;
        for (var index = 0; index < 20; index++)
        {
            Assert.Equal("en", scripts.Get("en"));
            Assert.Equal("th", scripts.Get("th"));
        }

        Assert.Equal(new[] { "/contact?culture=en", "/contact?culture=th" }, handler.Requests);
        Assert.All(handler.Contents, content => Assert.True(content.Disposed));
        using var response = await client.GetAsync("/client-is-still-owned");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("blank")]
    [InlineData("duplicate")]
    [InlineData("wrong-culture")]
    [InlineData("bad-status")]
    public async Task InvalidEnglishCaptureFailsBeforeThaiAndDisposesItsResponse(string mode)
    {
        var body = mode switch
        {
            "missing" => "<html lang=\"en\"><body>No handler</body></html>",
            "blank" => Html("en", " \n "),
            "duplicate" => Html("en", "one") + Script("two"),
            "wrong-culture" => "<html lang=\"th\" data-lang=\"en\">" + Script("handler") + "</html>",
            _ => Html("en", "handler")
        };
        var handler = new CaptureHandler(_ => Response(body,
            mode == "bad-status" ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK));
        using var client = Client(handler);

        await Assert.ThrowsAsync<InvalidDataException>(() => PublicContactRenderedScripts.CaptureAsync(client));

        Assert.Equal("/contact?culture=en", Assert.Single(handler.Requests));
        Assert.True(Assert.Single(handler.Contents).Disposed);
    }

    [Fact]
    public async Task InvalidThaiCaptureCannotPublishAPartialEnglishResultAndDisposesBothResponses()
    {
        var handler = new CaptureHandler(_ => Response(Html("en", "english")));
        using var client = Client(handler);

        await Assert.ThrowsAsync<InvalidDataException>(() => PublicContactRenderedScripts.CaptureAsync(client));

        Assert.Equal(new[] { "/contact?culture=en", "/contact?culture=th" }, handler.Requests);
        Assert.All(handler.Contents, content => Assert.True(content.Disposed));
    }

    [Theory]
    [InlineData("fr")]
    [InlineData("EN")]
    public async Task LookupRejectsUncapturedCultureWithoutFallingBackOrRefetching(string culture)
    {
        var handler = new CaptureHandler(uri =>
            Response(Html(uri.Query.Contains("culture=th", StringComparison.Ordinal) ? "th" : "en", "handler")));
        using var client = Client(handler);
        var scripts = await PublicContactRenderedScripts.CaptureAsync(client);

        Assert.Throws<ArgumentOutOfRangeException>(() => scripts.Get(culture));

        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task FixtureDoesNotExposeScriptsBeforeInitializationAndCanDisposeWithoutStarting()
    {
        var fixture = new PublicContactBrowserFixture();
        Assert.Throws<InvalidOperationException>(() => fixture.GetRenderedContactScript("en"));
        await fixture.DisposeAsync();
        await fixture.DisposeAsync();
    }

    [Fact]
    public async Task FailedBodyReadPropagatesAndDisposesTheResponseWithoutRequestingThai()
    {
        var handler = new CaptureHandler(_ => Response(Html("en", "handler"), failRead: true));
        using var client = Client(handler);

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => PublicContactRenderedScripts.CaptureAsync(client));

        Assert.IsType<IOException>(error.InnerException);
        Assert.Equal("/contact?culture=en", Assert.Single(handler.Requests));
        Assert.True(Assert.Single(handler.Contents).Disposed);
    }

    private static HttpClient Client(CaptureHandler handler) => new(handler)
    {
        BaseAddress = new Uri("http://127.0.0.1:1")
    };

    private static string Html(string culture, string script) =>
        $"<!DOCTYPE html><html lang=\"{culture}\"><body>{Script(script)}</body></html>";

    private static string Script(string script) =>
        $"<script data-migration-component=\"public-contact-channel-analytics\">{script}</script>";

    private static HttpResponseMessage Response(string body, HttpStatusCode status = HttpStatusCode.OK, bool failRead = false) =>
        new(status) { Content = new TrackingContent(body, failRead) };

    private sealed class CaptureHandler(Func<Uri, HttpResponseMessage> respond) : HttpMessageHandler
    {
        internal List<string> Requests { get; } = [];
        internal List<TrackingContent> Contents { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var uri = request.RequestUri ?? throw new InvalidOperationException("Missing controlled request URI.");
            Requests.Add(uri.PathAndQuery);
            var response = respond(uri);
            Contents.Add((TrackingContent)response.Content);
            return Task.FromResult(response);
        }
    }

    private sealed class TrackingContent : HttpContent
    {
        private readonly byte[] bytes;
        private readonly bool failRead;

        internal TrackingContent(string body, bool failRead)
        {
            bytes = Encoding.UTF8.GetBytes(body);
            this.failRead = failRead;
            Headers.ContentType = new MediaTypeHeaderValue("text/html") { CharSet = "utf-8" };
        }

        internal bool Disposed { get; private set; }

        protected override bool TryComputeLength(out long length)
        {
            length = bytes.Length;
            return true;
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            failRead
                ? Task.FromException(new IOException("Controlled contact capture body read failure."))
                : stream.WriteAsync(bytes.AsMemory()).AsTask();

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}
