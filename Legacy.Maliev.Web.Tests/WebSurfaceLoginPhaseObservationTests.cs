using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Xunit.Abstractions;

namespace Legacy.Maliev.Web.Tests;

public sealed class WebSurfaceLoginPhaseObservationTests
{
    [Fact]
    public void Observation_RestrictsOutputToFirstLoginPhasesAndDetachesItsOwnedSubscription()
    {
        using var listener = new DiagnosticListener("test");
        var output = new RecordingOutput();
        var observation = new WebSurfaceLoginPhaseObservation(listener, output);
        using var aborted = new CancellationTokenSource();
        var unrelated = new DefaultHttpContext();
        unrelated.Request.Path = "/not-login";
        listener.Write("Microsoft.AspNetCore.Hosting.BeginRequest", new { httpContext = unrelated });
        var first = new DefaultHttpContext();
        first.Request.Method = HttpMethods.Get;
        first.Request.Path = "/account/login";
        first.Request.QueryString = new QueryString("?secret=must-not-be-written");
        first.Request.Headers.Cookie = "must-not-be-written";
        first.RequestAborted = aborted.Token;
        first.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(), "fixed-login-endpoint"));
        try
        {
            listener.Write("Microsoft.AspNetCore.Hosting.BeginRequest", new { httpContext = first });
            listener.Write("Microsoft.AspNetCore.Routing.EndpointMatched", first);
            listener.Write("Microsoft.AspNetCore.Hosting.EndRequest", new { httpContext = unrelated });
            listener.Write("Microsoft.AspNetCore.Hosting.UnhandledException",
                new { httpContext = first, exception = new InvalidOperationException("must-not-be-written") });
            listener.Write("Microsoft.AspNetCore.Hosting.EndRequest", new { httpContext = first });
            aborted.Cancel();
            observation.WriteFailure(new TimeoutException("must-not-be-written"));

            Assert.Equal(6, output.Lines.Count);
            Assert.Single(output.Lines, line => line.Contains("phase=pipeline-entered ", StringComparison.Ordinal));
            Assert.Single(output.Lines, line => line.Contains("phase=endpoint-selected endpoint=fixed-login-endpoint ", StringComparison.Ordinal));
            Assert.Single(output.Lines, line => line.Contains("phase=pipeline-returned ", StringComparison.Ordinal));
            Assert.Single(output.Lines, line => line.Contains("phase=pipeline-faulted ", StringComparison.Ordinal));
            Assert.Single(output.Lines, line => line.Contains("phase=request-aborted ", StringComparison.Ordinal));
            Assert.Single(output.Lines, line => line.Contains("phase=client-faulted ", StringComparison.Ordinal));
            Assert.All(output.Lines, line =>
            {
                Assert.StartsWith("login-get-phase elapsed_ms=", line, StringComparison.Ordinal);
                Assert.DoesNotContain("must-not-be-written", line, StringComparison.Ordinal);
                Assert.DoesNotContain("/account/login", line, StringComparison.Ordinal);
            });
        }
        finally { observation.Dispose(); }
        observation.Dispose();
        Assert.False(listener.IsEnabled("Microsoft.AspNetCore.Hosting.BeginRequest"));
    }

    [Fact]
    public void Observation_OutputFailureCannotReplaceThePrimaryFailure()
    {
        using var listener = new DiagnosticListener("test");
        using var observation = new WebSurfaceLoginPhaseObservation(listener, new RecordingOutput(throwWrites: true));
        var primary = new InvalidOperationException("original");

        var actual = Assert.Throws<InvalidOperationException>((Action)(() =>
        {
            try { throw primary; }
            catch (Exception exception)
            {
                observation.WriteFailure(exception);
                throw;
            }
        }));

        Assert.Same(primary, actual);
    }

    private sealed class RecordingOutput(bool throwWrites = false) : ITestOutputHelper
    {
        internal List<string> Lines { get; } = [];
        public void WriteLine(string message)
        {
            if (throwWrites) throw new IOException("Synthetic output failure.");
            Lines.Add(message);
        }
        public void WriteLine(string format, params object[] args) => WriteLine(string.Format(format, args));
    }
}
