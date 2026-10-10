using System.Reflection;
using System.Text.Json;
using Microsoft.Playwright;
using Xunit.Abstractions;

namespace Legacy.Maliev.Web.Tests;

public sealed class ContactFallbackNavigationEvidenceTests
{
    private static readonly Uri Origin = new("http://127.0.0.1:46767");
    [Theory]
    [InlineData("http://127.0.0.1:46767/contact?token=private-token#private-file.step", "contact")]
    [InlineData("http://127.0.0.1:46767/CONTACT", "contact")]
    [InlineData("http://127.0.0.1:46767/legal?email=private-email@example.test", "legal")]
    [InlineData("http://127.0.0.1:46767/legal/nondisclosureagreement", "nda")]
    [InlineData("http://127.0.0.1:46767/contact/private-file.step", "same-origin-other")]
    [InlineData("http://127.0.0.1:46767/private-token", "same-origin-other")]
    [InlineData("http://127.0.0.1:46767/contact%2Fprivate-file.step", "same-origin-other")]
    [InlineData("http://private-token@127.0.0.1:46767/contact", "cross-origin")]
    [InlineData("https://127.0.0.1:46767/contact", "cross-origin")]
    [InlineData("http://127.0.0.1:46768/contact", "cross-origin")]
    [InlineData("http://private.example/contact", "cross-origin")]
    [InlineData("not a url private-token", "invalid")]
    public void NetworkPathEvidenceExportsOnlyFixedLabels(string url, string expected)
    {
        var value = ContactFallbackNavigationEvidence.SafePath(new Uri("http://127.0.0.1:46767"), url);
        Assert.Equal(expected, value);
        Assert.DoesNotContain("private", value, StringComparison.Ordinal);
        Assert.DoesNotContain('@', value);
        Assert.DoesNotContain('?', value);
        Assert.DoesNotContain('#', value);
    }

    [Fact]
    public void FailureOutputCapsDocumentEventsAndNeverSerializesPrivateTransportData()
    {
        var (page, proxy) = Stub<IPage>();
        proxy.Values["IsClosed"] = false;
        proxy.Values["Url"] = Origin + "contact?token=private-token&email=private@example.test#contact-us";
        using var evidence = new ContactFallbackNavigationEvidence(page, Origin);
        for (var index = 0; index < 48; index++)
            proxy.Emit("Request", Request("document", Origin + "contact?email=private@example.test&token=private-token"));
        var output = new Output();
        evidence.WriteFailure(output, "private-token");
        using var document = output.Read();
        Assert.Equal(32, document.RootElement.GetProperty("events").GetArrayLength());
        Assert.Equal("en", document.RootElement.GetProperty("culture").GetString());
        Assert.Equal("contact", document.RootElement.GetProperty("finalPath").GetString());
        Assert.True(document.RootElement.GetProperty("finalContactFragment").GetBoolean());
        Assert.All(document.RootElement.GetProperty("events").EnumerateArray(), item =>
        {
            Assert.Equal("request", item.GetProperty("stage").GetString());
            Assert.Equal("contact", item.GetProperty("path").GetString());
            Assert.InRange(item.GetProperty("elapsedMs").GetInt64(), 0, 60000);
        });
        Assert.InRange(output.Line!.Length, 1, 8192);
        foreach (var value in new[] { "private", "token=", "email=", "127.0.0.1", "46767", "http:" })
            Assert.DoesNotContain(value, output.Line, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("script")]
    [InlineData("image")]
    [InlineData("fetch")]
    public void NonDocumentRequestsAndResponsesAreExcluded(string resourceType)
    {
        var (page, proxy) = Stub<IPage>();
        proxy.Values["IsClosed"] = true;
        using var evidence = new ContactFallbackNavigationEvidence(page, Origin);
        var request = Request(resourceType, Origin + "contact?token=private-token");
        var (response, responseProxy) = Stub<IResponse>();
        responseProxy.Values["Request"] = request;
        responseProxy.Values["Url"] = Origin + "contact";
        responseProxy.Values["Status"] = 503;
        proxy.Emit("Request", request);
        proxy.Emit("Response", response);
        proxy.Emit("RequestFinished", request);
        proxy.Emit("RequestFailed", request);
        var output = new Output();
        evidence.WriteFailure(output, "th");
        using var document = output.Read();
        Assert.Equal(0, document.RootElement.GetProperty("events").GetArrayLength());
        Assert.Equal("th", document.RootElement.GetProperty("culture").GetString());
        Assert.Equal("closed", document.RootElement.GetProperty("finalPath").GetString());
        Assert.False(document.RootElement.GetProperty("finalContactFragment").GetBoolean());
    }

    [Fact]
    public void DocumentLifecycleAndMainFrameCommitRemainDistinguishable()
    {
        var (page, proxy) = Stub<IPage>();
        proxy.Values["IsClosed"] = true;
        var (main, mainProxy) = Stub<IFrame>();
        mainProxy.Values["Url"] = Origin + "contact?private=secret#contact-us";
        proxy.Values["MainFrame"] = main;
        var (child, childProxy) = Stub<IFrame>();
        childProxy.Values["Url"] = "https://private.example/secret";
        var request = Request("document", Origin + "contact?private=secret");
        var (response, responseProxy) = Stub<IResponse>();
        responseProxy.Values["Request"] = request;
        responseProxy.Values["Url"] = Origin + "contact?private=secret";
        responseProxy.Values["Status"] = 302;
        using var evidence = new ContactFallbackNavigationEvidence(page, Origin);
        proxy.Emit("Request", request);
        proxy.Emit("Response", response);
        proxy.Emit("RequestFinished", request);
        proxy.Emit("RequestFailed", request);
        proxy.Emit("FrameNavigated", child);
        proxy.Emit("FrameNavigated", main);
        var output = new Output();
        evidence.WriteFailure(output, "en");
        using var document = output.Read();
        var events = document.RootElement.GetProperty("events").EnumerateArray().ToArray();
        Assert.Equal(new[] { "request", "response", "finished", "failed", "committed" },
            events.Select(item => item.GetProperty("stage").GetString()));
        Assert.Equal(302, events[1].GetProperty("status").GetInt32());
        Assert.DoesNotContain("private", output.Line!, StringComparison.Ordinal);
    }

    [Fact]
    public void DisposalDetachesAllFiveCallbacksAndPreventsFurtherEvidence()
    {
        var (page, proxy) = Stub<IPage>();
        proxy.Values["IsClosed"] = true;
        var evidence = new ContactFallbackNavigationEvidence(page, Origin);
        Assert.Equal(5, proxy.Handlers.Count);
        var retained = proxy.Handlers["Request"];
        evidence.Dispose();
        Assert.Empty(proxy.Handlers);
        Assert.Equal(5, proxy.Removes.Count);
        Assert.Equal(0, evidence.DetachFailures);
        retained.DynamicInvoke(page, Request("document", Origin + "contact"));
        var output = new Output();
        evidence.WriteFailure(output, "en");
        using var document = output.Read();
        Assert.Equal(0, document.RootElement.GetProperty("events").GetArrayLength());
        evidence.Dispose();
        Assert.Equal(5, proxy.Removes.Count);
    }

    [Theory]
    [InlineData("Response")]
    [InlineData("RequestFailed")]
    [InlineData("FrameNavigated")]
    public void PartialSubscriptionFailureRollsBackAndPreservesOriginalFault(string eventName)
    {
        var (page, proxy) = Stub<IPage>();
        var original = new InvalidOperationException("private subscription failure");
        proxy.AddFailure = (eventName, original);
        var caught = Assert.Throws<InvalidOperationException>(() => new ContactFallbackNavigationEvidence(page, Origin));
        Assert.Same(original, caught);
        Assert.Empty(proxy.Handlers);
        Assert.Equal(5, proxy.Removes.Count);
    }

    [Fact]
    public async Task DiagnosticOutputFailurePreservesActualNavigationException()
    {
        var original = new TimeoutException("original navigation failure");
        var (page, proxy) = Stub<IPage>();
        proxy.Values["IsClosed"] = true;
        using var evidence = new ContactFallbackNavigationEvidence(page, Origin);
        var caught = await Assert.ThrowsAsync<TimeoutException>(() => ContactFallbackNavigationEvidence.PreserveFailureAsync(
            () => Task.FromException(original), () => evidence.WriteFailure(new Output { Fail = true }, "en")));
        Assert.Same(original, caught);
    }

    private static IRequest Request(string resourceType, string url)
    {
        var (request, proxy) = Stub<IRequest>();
        proxy.Values["ResourceType"] = resourceType;
        proxy.Values["Url"] = url;
        return request;
    }
    private static (T Object, EvidenceProxy Proxy) Stub<T>() where T : class
    {
        var value = DispatchProxy.Create<T, EvidenceProxy>();
        return (value, (EvidenceProxy)(object)value);
    }
    public class EvidenceProxy : DispatchProxy
    {
        public Dictionary<string, object?> Values { get; } = new();
        public Dictionary<string, Delegate> Handlers { get; } = new();
        public List<string> Removes { get; } = [];
        public (string Event, Exception Error)? AddFailure { get; set; }
        public void Emit(string name, object value)
        {
            if (Handlers.TryGetValue(name, out var callback)) callback.DynamicInvoke(this, value);
        }
        protected override object? Invoke(MethodInfo? method, object?[]? arguments)
        {
            var name = method!.Name;
            if (name.StartsWith("get_", StringComparison.Ordinal)) return Values.GetValueOrDefault(name[4..]);
            if (name.StartsWith("add_", StringComparison.Ordinal))
            {
                var eventName = name[4..];
                if (AddFailure is { } fault && fault.Event == eventName) throw fault.Error;
                Handlers.Add(eventName, (Delegate)arguments![0]!);
                return null;
            }
            if (name.StartsWith("remove_", StringComparison.Ordinal))
            {
                Removes.Add(name[7..]);
                Handlers.Remove(name[7..]);
                return null;
            }
            throw new NotSupportedException("Unowned interface member");
        }
    }
    private sealed class Output : ITestOutputHelper
    {
        public string? Line { get; private set; }
        public bool Fail { get; init; }
        public void WriteLine(string message)
        {
            if (Fail) throw new InvalidOperationException("private output failure");
            Line = message;
        }
        public void WriteLine(string format, params object[] arguments) => WriteLine(string.Format(format, arguments));
        public JsonDocument Read() => JsonDocument.Parse(Line!["WEB_CONTACT_FALLBACK_NAVIGATION_FAILURE ".Length..]);
    }
}
