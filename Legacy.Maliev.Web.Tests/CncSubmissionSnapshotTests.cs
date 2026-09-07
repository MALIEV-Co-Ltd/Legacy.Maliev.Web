using System.Reflection;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Legacy.Maliev.Web.Components.Pages.InstantQuotation;
using static Legacy.Maliev.Web.Components.Pages.InstantQuotation.CncSubmissionAdmission;

namespace Legacy.Maliev.Web.Tests;

public sealed class CncSubmissionSnapshotTests
{
    [Theory]
    [InlineData("null")]
    [InlineData("1")]
    [InlineData("[]")]
    [InlineData("\"not-an-object\"")]
    public void MalformedLineItem_FailsClosedWithoutThrowing(string invalid)
    {
        var snapshot = JObject.Parse(SnapshotJson());
        snapshot["lineItems"] = new JArray(JToken.Parse(invalid));
        Assert.False(Validate(snapshot.ToString(Formatting.None)).Valid);
    }

    [Theory]
    [InlineData("vatRate", "1.01")]
    [InlineData("calculatedAtUtc", "\"2026-08-25T03:04:05+07:00\"")]
    [InlineData("stockPlan.pieces", "1000001")]
    [InlineData("setupPlan.setupCount", "1001")]
    [InlineData("operationSummary.operationCount", "10001")]
    [InlineData("operationSummary.operationCount", "-1")]
    [InlineData("quantityPlan.quantity", "1000001")]
    [InlineData("stockPlan.supplierShippingBeforeVat", "-1")]
    [InlineData("stockPlan.bufferedPiecePriceBeforeVat", "-1")]
    [InlineData("versions", "null")]
    [InlineData("stockPlan", "null")]
    [InlineData("reviewReasons", "null")]
    public void NestedShape_BoundsAndRequiredNullsFailClosed(string path, string value)
    {
        var snapshot = JObject.Parse(SnapshotJson());
        snapshot.SelectToken(path)!.Replace(JsonConvert.DeserializeObject<JToken>(value,
            new JsonSerializerSettings { DateParseHandling = DateParseHandling.None })!);
        Assert.False(Validate(snapshot.ToString(Formatting.None)).Valid);
    }

    [Fact]
    public void JsonEnvelope_RejectsDepthTrailingTokensNullOptionalObjectsAndPreservesSourceIntegerCoercion()
    {
        Assert.False(Validate(SnapshotJson() + " {}").Valid);
        // Source Newtonsoft ToObject rounds fractional count fields. These remain untrusted review evidence.
        Assert.True(Validate(SnapshotJson().Replace("\"setupCount\":2", "\"setupCount\":2.5", StringComparison.Ordinal)).Valid);
        AssertInvalid(value => value["manufacturingEvidence"] = JValue.CreateNull());
        AssertInvalid(value => value["lineItems"] = JValue.CreateNull());
        Assert.False(Validate("{\"extra\":" + new string('[', 25) + "0" + new string(']', 25) + "}").Valid);
    }

    private const int MaximumSnapshotBytes = 65_536;

    private const double MaximumCncMoney = 90_071_992_547_409.9D;

    private const decimal MaximumCncMoneyExact = 90_071_992_547_409.90M;

    private const string QuotationSessionId = "11111111-1111-1111-1111-111111111111";

    [Fact]
    public void Task5BrowserSnapshot_RoundTripsTheExactCamelCaseWireShape()
    {
        (bool valid, object snapshot, string canonical) = Validate(SnapshotJson());

        Assert.True(valid);
        Assert.NotNull(snapshot);
        Assert.Equal(
            new[]
            {
                    "calculatedAtUtc", "clientGenerated", "confidence", "currency", "estimateStatus",
                    "estimatedMinutesPerPart", "estimatedPriceAfterVat", "estimatedPriceBeforeVat", "geometrySummary",
                    "operationSummary", "quantityPlan", "reviewReasons", "setupPlan", "stockPlan", "vatRate", "versions",
            },
            JObject.Parse(canonical).Properties().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray());

        Type snapshotType = snapshot.GetType();
        Assert.Equal("CncEstimateSnapshot", snapshotType.Name);
        AssertJsonProperties(
            snapshotType,
            "estimateStatus", "clientGenerated", "calculatedAtUtc", "versions", "geometrySummary", "stockPlan",
            "setupPlan", "operationSummary", "quantityPlan", "confidence", "reviewReasons", "estimatedMinutesPerPart",
            "estimatedPriceBeforeVat", "vatRate", "estimatedPriceAfterVat", "currency", "manufacturingEvidence", "lineItems");
        AssertJsonProperties(
            snapshotType.GetProperty("Versions")!.PropertyType,
            "estimatorVersion", "stockRulesVersion", "materialPriceModelVersion", "commercialRulesVersion",
            "materialCatalogVersion", "toolLibraryVersion", "reachRulesVersion", "finishingRulesVersion");
        AssertJsonProperties(snapshotType.GetProperty("GeometrySummary")!.PropertyType, "category", "confidence", "reviewReasons");
        AssertJsonProperties(
            snapshotType.GetProperty("StockPlan")!.PropertyType,
            "strategy", "pieces", "supplierShippingBeforeVat", "bufferedPiecePriceBeforeVat", "confidence", "reviewReasons");
        AssertJsonProperties(snapshotType.GetProperty("SetupPlan")!.PropertyType, "setupCount", "category");
        AssertJsonProperties(snapshotType.GetProperty("OperationSummary")!.PropertyType, "category", "operationCount");
        AssertJsonProperties(snapshotType.GetProperty("QuantityPlan")!.PropertyType, "quantity", "category");

        JObject roundTrip = JObject.Parse(canonical);
        Assert.Equal("cnc-local-v2", (string)roundTrip["versions"]!["estimatorVersion"]!);
        Assert.Equal("rectangular_block", (string)roundTrip["stockPlan"]!["strategy"]!);
        Assert.Equal(4680D, (double)roundTrip["estimatedPriceAfterVat"]!);
    }

    /// <summary>
    /// New browser estimates may add bounded manufacturing evidence while snapshots created
    /// before the extension remain valid and canonicalizable.
    /// </summary>
    [Fact]
    public void SnapshotValidator_AcceptsOptionalManufacturingEvidenceAndLegacySnapshots()
    {
        Assert.True(Validate(SnapshotJson()).Valid);

        JObject snapshot = JObject.Parse(SnapshotJson());
        snapshot["versions"]!["materialCatalogVersion"] = "cnc-materials-th-2026-08-26";
        snapshot["versions"]!["toolLibraryVersion"] = "cnc-tools-default-2026-08-26";
        snapshot["versions"]!["reachRulesVersion"] = "cnc-reach-default-2026-08-26";
        snapshot["versions"]!["finishingRulesVersion"] = "cnc-finishing-th-2026-08-26";
        snapshot["manufacturingEvidence"] = JObject.FromObject(new
        {
            stockShape = "round",
            setupCount = 2,
            toolFamilies = new[] { "face_mill", "flat_end_mill", "tap" },
            residualVolumeMm3 = 18.5D,
        });
        snapshot["lineItems"] = PreliminaryLineItems();

        (bool valid, object deserialized, string canonical) = Validate(snapshot.ToString(Formatting.None));
        Assert.True(valid);
        Assert.NotNull(deserialized);
        JObject roundTrip = JObject.Parse(canonical);
        Assert.Equal("round", (string)roundTrip["manufacturingEvidence"]!["stockShape"]!);
        Assert.Equal("cnc-finishing-th-2026-08-26", (string)roundTrip["versions"]!["finishingRulesVersion"]!);
        Assert.Equal(3, roundTrip["manufacturingEvidence"]!["toolFamilies"]!.Count());
        Assert.Equal(18.5D, (double)roundTrip["manufacturingEvidence"]!["residualVolumeMm3"]!);
        Assert.Equal("anodize-clear", (string)roundTrip["lineItems"]![5]!["finishCode"]!);
    }

    [Fact]
    public void PreliminaryLineItems_RejectUnknownRawAndUnboundedValues()
    {
        AssertInvalid(snapshot => snapshot["lineItems"] = new JArray());
        AssertInvalid(snapshot => snapshot["lineItems"] = new JArray(Enumerable.Range(0, 25).Select(index =>
            JObject.FromObject(new { code = $"line_{index}", category = "Public", amountBeforeVat = 1D }))));
        AssertInvalid(snapshot => snapshot["lineItems"] = new JArray(
            JObject.FromObject(new { code = "finish", category = "Finish", amountBeforeVat = -1D, finishCode = "anodize-clear" })));
        AssertInvalid(snapshot => snapshot["lineItems"] = new JArray(
            JObject.FromObject(new { code = "finish", category = "Finish", amountBeforeVat = 900D, finishCode = "anodize-clear", margin = 0.4D })));
        AssertInvalid(snapshot =>
        {
            JArray lines = PreliminaryLineItems();
            lines.Add(JObject.FromObject(new { code = "raw_toolpath", category = "Internal tool path", amountBeforeVat = 0D }));
            snapshot["lineItems"] = lines;
        });
        AssertInvalid(snapshot =>
        {
            snapshot["estimatedPriceBeforeVat"] = double.MaxValue;
            snapshot["lineItems"] = PreliminaryLineItems(double.MaxValue);
        });
        AssertInvalid(snapshot =>
        {
            double aboveMaximum = MaximumCncMoney + 1D;
            snapshot["estimatedPriceBeforeVat"] = aboveMaximum;
            JArray lines = PreliminaryLineItems(2050D);
            foreach (JObject line in lines.Cast<JObject>())
            {
                line["amountBeforeVat"] = 0D;
            }

            lines[0]!["amountBeforeVat"] = aboveMaximum;
            snapshot["lineItems"] = lines;
        });
        AssertInvalid(snapshot =>
        {
            double halfMaximum = (MaximumCncMoney / 2D) + 1D;
            snapshot["estimatedPriceBeforeVat"] = halfMaximum * 2D;
            JArray lines = PreliminaryLineItems(2050D);
            foreach (JObject line in lines.Cast<JObject>())
            {
                line["amountBeforeVat"] = 0D;
            }

            lines[0]!["amountBeforeVat"] = halfMaximum;
            lines[1]!["amountBeforeVat"] = halfMaximum;
            snapshot["lineItems"] = lines;
        });
        AssertInvalid(snapshot =>
        {
            snapshot["estimatedPriceBeforeVat"] = 0.001M;
            JArray lines = ZeroedPreliminaryLineItems();
            lines[0]!["amountBeforeVat"] = 0.001M;
            snapshot["lineItems"] = lines;
        });
        AssertInvalid(snapshot =>
        {
            decimal reviewerCase = MaximumCncMoneyExact + 0.001M;
            snapshot["estimatedPriceBeforeVat"] = reviewerCase;
            JArray lines = ZeroedPreliminaryLineItems();
            lines[0]!["amountBeforeVat"] = reviewerCase;
            snapshot["lineItems"] = lines;
        });
        AssertInvalid(snapshot =>
        {
            snapshot["estimatedPriceBeforeVat"] = 100M;
            JArray lines = ZeroedPreliminaryLineItems();
            lines[0]!["amountBeforeVat"] = new JRaw("1e2");
            snapshot["lineItems"] = lines;
        });
        AssertInvalid(snapshot =>
        {
            snapshot["estimatedPriceBeforeVat"] = new JRaw("1e1000");
            JArray lines = ZeroedPreliminaryLineItems();
            lines[0]!["amountBeforeVat"] = new JRaw("1e1000");
            snapshot["lineItems"] = lines;
        });
    }

    [Fact]
    public void PreliminaryLineItems_AcceptExactMoneyCeilingBoundary()
    {
        JObject snapshot = JObject.Parse(SnapshotJson());
        snapshot["estimatedPriceBeforeVat"] = MaximumCncMoneyExact;
        JArray lines = ZeroedPreliminaryLineItems();
        lines[0]!["amountBeforeVat"] = MaximumCncMoneyExact;
        snapshot["lineItems"] = lines;

        Assert.True(Validate(snapshot.ToString(Formatting.None)).Valid);
    }

    /// <summary>
    /// The optional audit summary is deliberately bounded and cannot be widened into raw
    /// geometry, tool-path, or voxel payloads.
    /// </summary>
    [Fact]
    public void ManufacturingEvidence_RejectsUnknownRawOrUnboundedValues()
    {
        AssertInvalid(snapshot => snapshot["manufacturingEvidence"] = JObject.FromObject(new
        {
            stockShape = "round",
            setupCount = 0,
            toolFamilies = new[] { "face_mill" },
            residualVolumeMm3 = 0D,
        }));
        AssertInvalid(snapshot => snapshot["manufacturingEvidence"] = JObject.FromObject(new
        {
            stockShape = new string('x', 81),
            setupCount = 2,
            toolFamilies = new[] { "face_mill" },
            residualVolumeMm3 = 0D,
        }));
        AssertInvalid(snapshot => snapshot["manufacturingEvidence"] = JObject.FromObject(new
        {
            stockShape = "round",
            setupCount = 2,
            toolFamilies = Enumerable.Range(0, 17).Select(index => $"tool_{index}").ToArray(),
            residualVolumeMm3 = 0D,
        }));
        AssertInvalid(snapshot => snapshot["manufacturingEvidence"] = JObject.FromObject(new
        {
            stockShape = "round",
            setupCount = 2,
            toolFamilies = new[] { "face_mill" },
            residualVolumeMm3 = -0.1D,
        }));

        JObject raw = JObject.Parse(SnapshotJson());
        raw["manufacturingEvidence"] = JObject.FromObject(new
        {
            stockShape = "round",
            setupCount = 2,
            toolFamilies = new[] { "face_mill" },
            residualVolumeMm3 = 0D,
            surfaceClusters = new[] { new { id = "cluster-1" } },
        });
        Assert.False(Validate(raw.ToString(Formatting.None)).Valid);
    }

    [Fact]
    public void SnapshotByteLimit_AcceptsExactly64KiBAndRejectsOneMultibyteCharacterBeyondIt()
    {
        string json = SnapshotJson();
        int padding = MaximumSnapshotBytes - Encoding.UTF8.GetByteCount(json);
        Assert.True(padding > 0);
        string atLimit = json + new string(' ', padding);

        Assert.Equal(MaximumSnapshotBytes, Encoding.UTF8.GetByteCount(atLimit));
        Assert.True(Validate(atLimit).Valid);
        Assert.False(Validate(atLimit + "ก").Valid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("42")]
    [InlineData("\"snapshot\"")]
    [InlineData("{")]
    public void SnapshotContainer_RejectsMissingNullArrayScalarAndMalformedJson(string json)
    {
        Assert.False(Validate(json).Valid);
    }

    [Fact]
    public void SnapshotConstantsNumbersAndNestedShape_FailClosed()
    {
        AssertInvalid(snapshot => snapshot["estimateStatus"] = "Final");
        AssertInvalid(snapshot => snapshot["clientGenerated"] = false);
        AssertInvalid(snapshot => snapshot["currency"] = "USD");
        AssertInvalid(snapshot => snapshot["confidence"] = "Certain");
        AssertInvalid(snapshot => snapshot["estimatedMinutesPerPart"] = -1);
        AssertInvalid(snapshot => snapshot["estimatedPriceBeforeVat"] = -1);
        AssertInvalid(snapshot => snapshot["vatRate"] = -0.01);
        AssertInvalid(snapshot => snapshot["estimatedPriceAfterVat"] = -1);
        AssertInvalid(snapshot => snapshot["quantityPlan"]!["quantity"] = 0);
        AssertInvalid(snapshot => snapshot["setupPlan"]!["setupCount"] = 0);
        AssertInvalid(snapshot => snapshot["versions"]!["estimatorVersion"] = string.Empty);
        AssertInvalid(snapshot => snapshot["versions"]!["stockRulesVersion"] = "invalid version with spaces");
        AssertInvalid(snapshot => snapshot["geometrySummary"]!["confidence"] = "Unknown");
        AssertInvalid(snapshot => snapshot["serverPriceAccepted"] = true);

        string nonFinite = SnapshotJson().Replace("\"vatRate\":0.07", "\"vatRate\":NaN", StringComparison.Ordinal);
        Assert.False(Validate(nonFinite).Valid);

        string duplicate = SnapshotJson().Replace(
            "{\"estimateStatus\":\"Preliminary\"",
            "{\"estimateStatus\":\"Preliminary\",\"estimateStatus\":\"Preliminary\"",
            StringComparison.Ordinal);
        Assert.False(Validate(duplicate).Valid);
    }

    [Fact]
    public void SnapshotPropertyNames_RejectTopLevelAndNestedAliasesDuplicatesAndMissingKeys()
    {
        AssertInvalid(snapshot => snapshot["EstimateStatus"] = "Preliminary");
        AssertInvalid(snapshot => snapshot["ESTIMATESTATUS"] = "Preliminary");
        AssertInvalid(snapshot => snapshot["versions"]!["EstimatorVersion"] = "alias");
        AssertInvalid(snapshot => snapshot["versions"]!["finishingRulesVersion"] = "invalid version with spaces");
        AssertInvalid(snapshot => snapshot["versions"]!["unknownRulesVersion"] = "cnc-unknown-v1");
        AssertInvalid(snapshot => snapshot["stockPlan"]!["Strategy"] = "alias");
        AssertInvalid(snapshot => snapshot["quantityPlan"]!["Quantity"] = 1);
        AssertInvalid(snapshot => snapshot["geometrySummary"]!.AsJEnumerable().First().Remove());

        string nestedDuplicate = SnapshotJson().Replace(
            "\"strategy\":\"rectangular_block\"",
            "\"strategy\":\"rectangular_block\",\"strategy\":\"rectangular_block\"",
            StringComparison.Ordinal);
        Assert.False(Validate(nestedDuplicate).Valid);
    }

    [Fact]
    public void ReviewCodes_AreLimitedToFiftyItemsAndEightyCharacters()
    {
        AssertInvalid(snapshot => snapshot["reviewReasons"] = new JArray(Enumerable.Range(0, 51).Select(index => $"reason_{index}")));
        AssertInvalid(snapshot => snapshot["reviewReasons"] = new JArray(new string('a', 81)));
        AssertInvalid(snapshot => snapshot["stockPlan"]!["reviewReasons"] = new JArray(Enumerable.Range(0, 51).Select(index => $"reason_{index}")));

        JObject accepted = JObject.Parse(SnapshotJson());
        accepted["reviewReasons"] = new JArray(Enumerable.Range(0, 50).Select(index => new string((char)('a' + (index % 20)), 80)));
        Assert.True(Validate(accepted.ToString(Formatting.None)).Valid);
    }

    [Fact]
    public void ShapeValidTamperedPrice_RemainsUntrustedReviewTextWithoutRecalculation()
    {
        JObject tampered = JObject.Parse(SnapshotJson());
        tampered["estimatedPriceBeforeVat"] = 1.23D;
        tampered["vatRate"] = 0D;
        tampered["estimatedPriceAfterVat"] = 1.23D;

        (bool valid, object snapshot, string canonical) = Validate(tampered.ToString(Formatting.None));
        string summary = BuildSummary(snapshot, canonical, NewItem("tampered.step", tampered.ToString(Formatting.None), 1));

        Assert.True(valid);
        Assert.Equal(1.23D, (double)JObject.Parse(canonical)["estimatedPriceAfterVat"]!);
        Assert.Contains("CNC preliminary estimate (client generated; engineering review required)", summary, StringComparison.Ordinal);
        Assert.Contains("After VAT: 1.23 THB", summary, StringComparison.Ordinal);
        Assert.Contains("Canonical untrusted snapshot:", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("recalculated", summary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("accepted", summary, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertInvalid(Action<JObject> mutate)
    {
        JObject snapshot = JObject.Parse(SnapshotJson());
        mutate(snapshot);
        Assert.False(Validate(snapshot.ToString(Formatting.None)).Valid);
    }
    private static void AssertJsonProperties(Type type, params string[] expected)
    {
        string[] actual = type
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.GetCustomAttribute<JsonPropertyAttribute>()?.PropertyName)
            .Where(name => name != null)
            .OrderBy(name => name, StringComparer.Ordinal)
            .Select(name => name!).ToArray();
        Assert.Equal(expected.OrderBy(name => name, StringComparer.Ordinal), actual);
    }
    internal static string SnapshotJson(double beforeVat = 4370D, double afterVat = 4680D, int quantity = 1, string reason = "deep_reach")
    {
        return new JObject
        {
            ["estimateStatus"] = "Preliminary",
            ["clientGenerated"] = true,
            ["calculatedAtUtc"] = "2026-08-25T03:04:05.000Z",
            ["versions"] = new JObject
            {
                ["estimatorVersion"] = "cnc-local-v2",
                ["stockRulesVersion"] = "cnc-stock-th-2026-08-25",
                ["materialPriceModelVersion"] = "al-one-supplier-2026-08-25",
                ["commercialRulesVersion"] = "cnc-commercial-v1",
            },
            ["geometrySummary"] = new JObject
            {
                ["category"] = "Model analysis",
                ["confidence"] = "Medium",
                ["reviewReasons"] = new JArray(reason),
            },
            ["stockPlan"] = new JObject
            {
                ["strategy"] = "rectangular_block",
                ["pieces"] = 1,
                ["supplierShippingBeforeVat"] = 200D,
                ["bufferedPiecePriceBeforeVat"] = 200D,
                ["confidence"] = "High",
                ["reviewReasons"] = new JArray(),
            },
            ["setupPlan"] = new JObject
            {
                ["setupCount"] = 2,
                ["category"] = "Multi-setup machining",
            },
            ["operationSummary"] = new JObject
            {
                ["category"] = "Machining, tooling, inspection and finishing",
                ["operationCount"] = 4,
            },
            ["quantityPlan"] = new JObject
            {
                ["quantity"] = quantity,
                ["category"] = quantity > 1 ? "Batch quantity" : "Single part",
            },
            ["confidence"] = "Medium",
            ["reviewReasons"] = new JArray(reason),
            ["estimatedMinutesPerPart"] = 42D,
            ["estimatedPriceBeforeVat"] = beforeVat,
            ["vatRate"] = 0.07D,
            ["estimatedPriceAfterVat"] = afterVat,
            ["currency"] = "THB",
        }.ToString(Formatting.None);
    }
    internal static JArray PreliminaryLineItems(double beforeVat = 4370D)
    {
        return new JArray(
            JObject.FromObject(new { code = "material", category = "Material", amountBeforeVat = 200D }),
            JObject.FromObject(new { code = "shipping", category = "Shipping", amountBeforeVat = 200D }),
            JObject.FromObject(new { code = "setup", category = "Setup", amountBeforeVat = 600D }),
            JObject.FromObject(new { code = "machining", category = "Machining", amountBeforeVat = beforeVat - 2050D }),
            JObject.FromObject(new { code = "tooling", category = "Tooling", amountBeforeVat = 150D }),
            JObject.FromObject(new { code = "finish", category = "Finish", amountBeforeVat = 900D, finishCode = "anodize-clear" }));
    }
    private static JArray ZeroedPreliminaryLineItems()
    {
        JArray lines = PreliminaryLineItems(2050D);
        foreach (JObject line in lines.Cast<JObject>())
        {
            line["amountBeforeVat"] = 0M;
        }

        return lines;
    }
    private static (bool Valid, object Snapshot, string Canonical) Validate(string json) { var valid = ValidateCncSnapshotForPersistence(json, out var snapshot, out var canonical); return (valid, snapshot, canonical); }
    private static string BuildSummary(object snapshot, string canonical, ItemDetail item) => BuildCncReviewSummary(item, (CncEstimateSnapshot)snapshot, canonical);
    private static ItemDetail NewItem(string name, string json, int quantity) => new() { FileName = name, CncEstimateJson = json, Quantity = quantity.ToString(System.Globalization.CultureInfo.InvariantCulture), Process = "CncMilling", Material = "6061", CncTolerance = "ISO 2768-m", CncThreads = "none", CncRoughness = "default", CncInspection = "standard", CncCertificate = "not-required", CncRequirementsNote = "Keep edges sharp." };

}
