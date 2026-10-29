using System.Text.Json;

namespace Legacy.Maliev.Web.Tests;

public sealed class PendingLocalParserReadEvidenceTests
{
    private const string Valid = """
        {"present":true,"healthyEntered":2,"healthyCompleted":1,"healthyFailed":1,"pendingEntered":0,"pendingCompleted":0,"healthyDigestEntered":1,"healthyDigestCompleted":1,"healthyDigestFailed":0}
        """;

    [Fact]
    public void Parse_RetainsOnlyFixedBoundedCountersAndPresence()
    {
        var evidence = PendingLocalParserReadEvidence.Parse(Valid);
        Assert.True(evidence.Present);
        Assert.Equal(2, evidence.HealthyEntered);
        Assert.Equal(1, evidence.HealthyCompleted);
        Assert.Equal(1, evidence.HealthyFailed);
        Assert.Equal(0, evidence.PendingEntered);
        Assert.Equal(0, evidence.PendingCompleted);
        Assert.Equal(1, evidence.HealthyDigestEntered);
        Assert.Equal(1, evidence.HealthyDigestCompleted);
        Assert.Equal(0, evidence.HealthyDigestFailed);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(evidence));
        Assert.Equal(new[] { "HealthyCompleted", "HealthyDigestCompleted", "HealthyDigestEntered", "HealthyDigestFailed", "HealthyEntered", "HealthyFailed", "PendingCompleted", "PendingEntered", "Present" },
            document.RootElement.EnumerateObject().Select(property => property.Name).Order().ToArray());
        Assert.All(document.RootElement.EnumerateObject(), property =>
            Assert.Contains(property.Value.ValueKind, new[] { JsonValueKind.Number, JsonValueKind.True, JsonValueKind.False }));
    }

    [Fact]
    public void Parse_AbsentStateCannotBeMisreportedAsObservedReads()
    {
        var absent = """
            {"present":false,"healthyEntered":0,"healthyCompleted":0,"healthyFailed":0,"pendingEntered":0,"pendingCompleted":0,"healthyDigestEntered":0,"healthyDigestCompleted":0,"healthyDigestFailed":0}
            """;
        Assert.False(PendingLocalParserReadEvidence.Parse(absent).Present);
        Assert.Throws<FormatException>(() => PendingLocalParserReadEvidence.Parse(absent.Replace("\"healthyEntered\":0", "\"healthyEntered\":1")));
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("duplicate")]
    [InlineData("missing")]
    [InlineData("string")]
    [InlineData("negative")]
    [InlineData("overflow")]
    [InlineData("fraction")]
    [InlineData("boolean")]
    [InlineData("outcome")]
    [InlineData("digest-outcome")]
    [InlineData("oversize")]
    [InlineData("invalid")]
    public void Parse_RefusesPrivateUnboundedOrMalformedInputWithoutEchoingIt(string defect)
    {
        const string sentinel = "private-parser-error-sentinel";
        var raw = defect switch
        {
            "unknown" => Valid.Replace("}", ",\"error\":\"" + sentinel + "\"}"),
            "duplicate" => Valid.Replace("}", ",\"healthyEntered\":2}"),
            "missing" => Valid.Replace("\"healthyFailed\":1,", string.Empty),
            "string" => Valid.Replace("\"healthyEntered\":2", "\"healthyEntered\":\"" + sentinel + "\""),
            "negative" => Valid.Replace("\"healthyEntered\":2", "\"healthyEntered\":-1"),
            "overflow" => Valid.Replace("\"healthyEntered\":2", "\"healthyEntered\":33"),
            "fraction" => Valid.Replace("\"healthyEntered\":2", "\"healthyEntered\":2.5"),
            "boolean" => Valid.Replace("\"present\":true", "\"present\":1"),
            "outcome" => Valid.Replace("\"healthyCompleted\":1", "\"healthyCompleted\":2"),
            "digest-outcome" => Valid.Replace("\"healthyDigestFailed\":0", "\"healthyDigestFailed\":1"),
            "oversize" => new string(' ', 513) + sentinel,
            "invalid" => sentinel,
            _ => throw new ArgumentOutOfRangeException(nameof(defect)),
        };
        var failure = Assert.Throws<FormatException>(() => PendingLocalParserReadEvidence.Parse(raw));
        Assert.DoesNotContain(sentinel, failure.Message, StringComparison.Ordinal);
        Assert.Null(failure.InnerException);
    }
}

internal sealed record PendingLocalParserReadSnapshot(bool Present, int HealthyEntered, int HealthyCompleted,
    int HealthyFailed, int PendingEntered, int PendingCompleted, int HealthyDigestEntered,
    int HealthyDigestCompleted, int HealthyDigestFailed);

internal static class PendingLocalParserReadEvidence
{
    internal static PendingLocalParserReadSnapshot Parse(string raw)
    {
        const string failure = "Parser read evidence must contain only the fixed bounded numeric and boolean fields.";
        if (raw is null || raw.Length > 512) throw new FormatException(failure);
        try
        {
            using var document = JsonDocument.Parse(raw, new JsonDocumentOptions { MaxDepth = 2 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new FormatException(failure);
            var names = root.EnumerateObject().Select(property => property.Name).ToArray();
            string[] expected = ["present", "healthyEntered", "healthyCompleted", "healthyFailed", "pendingEntered", "pendingCompleted",
                "healthyDigestEntered", "healthyDigestCompleted", "healthyDigestFailed"];
            if (names.Length != expected.Length || names.Distinct(StringComparer.Ordinal).Count() != expected.Length
                || names.Except(expected, StringComparer.Ordinal).Any()) throw new FormatException(failure);
            var presentValue = root.GetProperty("present");
            if (presentValue.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw new FormatException(failure);
            int Count(string name)
            {
                var value = root.GetProperty(name);
                if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var count) || count is < 0 or > 32)
                    throw new FormatException(failure);
                return count;
            }
            var result = new PendingLocalParserReadSnapshot(presentValue.GetBoolean(), Count("healthyEntered"),
                Count("healthyCompleted"), Count("healthyFailed"), Count("pendingEntered"), Count("pendingCompleted"),
                Count("healthyDigestEntered"), Count("healthyDigestCompleted"), Count("healthyDigestFailed"));
            if (result.HealthyCompleted + result.HealthyFailed > result.HealthyEntered
                || result.PendingCompleted > result.PendingEntered
                || result.HealthyDigestCompleted + result.HealthyDigestFailed > result.HealthyDigestEntered
                || (!result.Present && (result.HealthyEntered != 0 || result.PendingEntered != 0 || result.HealthyDigestEntered != 0)))
                throw new FormatException(failure);
            return result;
        }
        catch (JsonException) { throw new FormatException(failure); }
    }
}
