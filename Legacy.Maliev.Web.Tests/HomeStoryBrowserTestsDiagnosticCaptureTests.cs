using System.Globalization;
using System.Text.Json;
using Xunit.Abstractions;

namespace Legacy.Maliev.Web.Tests;

internal sealed class HomeClockDiagnosticCapture
{
    private const string Prefix = "HOME_CLOCK_STATE_V1 ";
    private static readonly string[] BooleanKeys =
    [
        "rootPresent", "ready", "stopped", "documentHidden", "rootVisible", "rootHidden",
        "focus", "hover", "reducedMotion", "assemblyHidden", "partnershipHidden", "journeyHidden",
    ];
    private static readonly string[] NumberKeys =
    [
        "timers", "scenes", "animationsRunning", "animationsPaused", "animationsFinished",
        "animationsIdle", "animationsPending",
    ];
    private readonly object gate = new();
    private readonly List<string> records = [];

    internal void Capture(string type, string text)
    {
        if (type != "debug" || text.Length > 2048 || !text.StartsWith(Prefix, StringComparison.Ordinal)) return;
        lock (gate)
        {
            if (records.Count >= 32) return;
            try
            {
                using var document = JsonDocument.Parse(text[Prefix.Length..]);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object) return;
                var properties = root.EnumerateObject().ToArray();
                if (properties.Length != BooleanKeys.Length + NumberKeys.Length ||
                    properties.Select(property => property.Name).Distinct(StringComparer.Ordinal).Count() != properties.Length) return;
                var fields = new List<string>();
                foreach (var key in BooleanKeys)
                {
                    if (!root.TryGetProperty(key, out var value) ||
                        value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return;
                    fields.Add("\"" + key + "\":" + (value.GetBoolean() ? "true" : "false"));
                }
                foreach (var key in NumberKeys)
                {
                    if (!root.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.Number ||
                        !value.TryGetInt32(out var number) || number is < 0 or > 100000) return;
                    fields.Add("\"" + key + "\":" + number.ToString(CultureInfo.InvariantCulture));
                }
                var canonical = "{" + string.Join(",", fields) + "}";
                if (records.Count == 0 || records[^1] != canonical) records.Add(canonical);
            }
            catch (JsonException)
            {
                // Reject invalid diagnostics without retaining their raw contents.
            }
        }
    }

    internal void WriteFailure(ITestOutputHelper output)
    {
        string[] snapshot;
        lock (gate) snapshot = records.ToArray();
        foreach (var record in snapshot)
        {
            try { output.WriteLine(Prefix + record); }
            catch (Exception) { /* Optional diagnostics must not replace the primary failure. */ }
        }
    }
}

public sealed class HomeStoryBrowserTestsDiagnosticCaptureTests
{
    private const string State = """{"rootPresent":true,"ready":true,"stopped":false,"documentHidden":false,"rootVisible":true,"rootHidden":false,"focus":false,"hover":false,"reducedMotion":false,"assemblyHidden":true,"partnershipHidden":false,"journeyHidden":true,"timers":0,"scenes":3,"animationsRunning":1,"animationsPaused":0,"animationsFinished":2,"animationsIdle":0,"animationsPending":0}""";
    private const string Prefix = "HOME_CLOCK_STATE_V1 ";

    [Fact]
    public void ValidState_IsSilentUntilFailureAndCanonicalOnOutput()
    {
        var capture = new HomeClockDiagnosticCapture();
        var output = new RecordingOutput();
        capture.Capture("debug", Prefix + State);
        Assert.Empty(output.Lines);
        capture.WriteFailure(output);
        Assert.Equal(Prefix + State, Assert.Single(output.Lines));
    }

    [Fact]
    public void DuplicateConsecutiveStates_AreNotRecordedTwice()
    {
        var capture = new HomeClockDiagnosticCapture();
        var output = new RecordingOutput();
        capture.Capture("debug", Prefix + State);
        capture.Capture("debug", Prefix + State);
        capture.Capture("debug", Prefix + State.Replace("\"timers\":0", "\"timers\":1", StringComparison.Ordinal));
        capture.WriteFailure(output);
        Assert.Equal(2, output.Lines.Count);
    }

    [Fact]
    public void ChangingStates_StopAtThirtyTwoRecords()
    {
        var capture = new HomeClockDiagnosticCapture();
        var output = new RecordingOutput();
        for (var i = 0; i < 40; i++)
            capture.Capture("debug", Prefix + State.Replace("\"timers\":0", "\"timers\":" + i.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal));
        capture.WriteFailure(output);
        Assert.Equal(32, output.Lines.Count);
        Assert.Contains("\"timers\":31", output.Lines[^1], StringComparison.Ordinal);
    }

    [Fact]
    public void ForeignOrDuplicateFields_AndCustomerLikeStringsAreNeverOutput()
    {
        foreach (var state in new[]
        {
            State.Replace("\"timers\":0", "\"timers\":0,\"message\":\"TOKEN_CUSTOMER_123\"", StringComparison.Ordinal),
            State.Replace("\"timers\":0", "\"timers\":0,\"timers\":1", StringComparison.Ordinal),
            State.Replace("\"focus\":false", "\"focus\":\"TOKEN_CUSTOMER_123\"", StringComparison.Ordinal),
            State.Replace("\"timers\":0", "\"timers\":\"TOKEN_CUSTOMER_123\"", StringComparison.Ordinal),
        })
        {
            var capture = new HomeClockDiagnosticCapture();
            var output = new RecordingOutput();
            capture.Capture("debug", Prefix + state);
            capture.WriteFailure(output);
            Assert.Empty(output.Lines);
        }
    }

    [Fact]
    public void MissingMalformedOversizedAndWrongChannelRecords_AreRefused()
    {
        foreach (var (type, text) in new[]
        {
            ("log", Prefix + State), ("debug", State), ("debug", Prefix + "{"),
            ("debug", Prefix + "[]"), ("debug", Prefix + State.Replace("\"scenes\":3,", "", StringComparison.Ordinal)),
            ("debug", Prefix + new string('x', 2049)),
        })
        {
            var capture = new HomeClockDiagnosticCapture();
            var output = new RecordingOutput();
            capture.Capture(type, text);
            capture.WriteFailure(output);
            Assert.Empty(output.Lines);
        }
    }

    [Fact]
    public void NegativeFractionalOverflowAndExcessiveCounts_AreRefused()
    {
        foreach (var number in new[] { "-1", "0.5", "2147483648", "100001" })
        {
            var capture = new HomeClockDiagnosticCapture();
            var output = new RecordingOutput();
            capture.Capture("debug", Prefix + State.Replace("\"timers\":0", "\"timers\":" + number, StringComparison.Ordinal));
            capture.WriteFailure(output);
            Assert.Empty(output.Lines);
        }
    }

    [Fact]
    public void OutputFailure_DoesNotEscapeOrReplacePrimaryFailure()
    {
        var capture = new HomeClockDiagnosticCapture();
        capture.Capture("debug", Prefix + State);
        var exception = Record.Exception(() => capture.WriteFailure(new ThrowingOutput()));
        Assert.Null(exception);
    }

    private sealed class RecordingOutput : ITestOutputHelper
    {
        internal List<string> Lines { get; } = [];
        public void WriteLine(string message) => Lines.Add(message);
        public void WriteLine(string format, params object[] args) => WriteLine(string.Format(CultureInfo.InvariantCulture, format, args));
    }

    private sealed class ThrowingOutput : ITestOutputHelper
    {
        public void WriteLine(string message) => throw new InvalidOperationException("synthetic output failure");
        public void WriteLine(string format, params object[] args) => throw new InvalidOperationException("synthetic output failure");
    }
}
