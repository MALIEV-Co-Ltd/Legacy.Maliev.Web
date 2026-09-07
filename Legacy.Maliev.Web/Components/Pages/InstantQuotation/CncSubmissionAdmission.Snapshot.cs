using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Legacy.Maliev.Web.Components.Pages.InstantQuotation;

/// <summary>Source-faithful admission of untrusted CNC review snapshots; never authoritative pricing.</summary>
internal static partial class CncSubmissionAdmission
{
    private const int MaximumCncSnapshotUtf8Bytes = 65_536;
    private const int MaximumCncItemCount = 20;
    private const int MaximumCncAggregateSnapshotUtf8Bytes = 40_960;
    private const int MaximumCncGeneratedTextUtf8Bytes = 262_144;
    private const int MaximumCncReviewReasonCount = 50;
    private const int MaximumCncReviewReasonLength = 80;
    private const int MaximumCncManufacturingEvidenceSetupCount = 16;
    private const int MaximumCncManufacturingEvidenceToolFamilyCount = 16;
    private const int MaximumCncPreliminaryLineItemCount = 24;
    private const long MaximumCncMoneyMinorUnits = 9_007_199_254_740_990L;
    internal static bool ValidateCncSnapshotForPersistence(
        string json,
        out CncEstimateSnapshot snapshot,
        out string canonicalJson)
    {
        snapshot = null!;
        canonicalJson = null!;
        if (string.IsNullOrWhiteSpace(json) || Encoding.UTF8.GetByteCount(json) > MaximumCncSnapshotUtf8Bytes)
        {
            return false;
        }

        if (!IsValidCncPreliminaryLineItemMoneyJson(json))
        {
            return false;
        }

        try
        {
            JObject jsonObject;
            using (var stringReader = new StringReader(json))
            using (var jsonReader = new JsonTextReader(stringReader)
            {
                DateParseHandling = DateParseHandling.None,
                FloatParseHandling = FloatParseHandling.Double,
                MaxDepth = 24,
            })
            {
                JToken token = JToken.ReadFrom(
                    jsonReader,
                    new JsonLoadSettings
                    {
                        CommentHandling = CommentHandling.Ignore,
                        DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
                        LineInfoHandling = LineInfoHandling.Ignore,
                    });
                if (token.Type != JTokenType.Object || jsonReader.Read())
                {
                    return false;
                }

                jsonObject = (JObject)token;
            }

            if (!HasExactCncSnapshotProperties(jsonObject))
            {
                return false;
            }

            JsonSerializer serializer = JsonSerializer.Create(
                new JsonSerializerSettings
                {
                    Culture = CultureInfo.InvariantCulture,
                    FloatParseHandling = FloatParseHandling.Double,
                    MissingMemberHandling = MissingMemberHandling.Error,
                });
            snapshot = jsonObject.ToObject<CncEstimateSnapshot>(serializer)!;
            if (!IsValidCncSnapshotShape(snapshot))
            {
                snapshot = null!;
                return false;
            }

            canonicalJson = JsonConvert.SerializeObject(
                snapshot,
                Formatting.None,
                new JsonSerializerSettings { Culture = CultureInfo.InvariantCulture });
            return Encoding.UTF8.GetByteCount(canonicalJson) <= MaximumCncSnapshotUtf8Bytes;
        }
        catch (JsonException)
        {
            snapshot = null!;
            canonicalJson = null!;
            return false;
        }
        catch (ArgumentException)
        {
            snapshot = null!;
            canonicalJson = null!;
            return false;
        }
    }

    private static bool HasExactCncSnapshotProperties(JObject root)
    {
        return HasRequiredAndOptionalProperties(
                root,
                new[]
                {
                        "estimateStatus", "clientGenerated", "calculatedAtUtc", "versions", "geometrySummary", "stockPlan",
                        "setupPlan", "operationSummary", "quantityPlan", "confidence", "reviewReasons", "estimatedMinutesPerPart",
                        "estimatedPriceBeforeVat", "vatRate", "estimatedPriceAfterVat", "currency",
                },
                "manufacturingEvidence", "lineItems")
            && HasRequiredAndOptionalProperties(
                root["versions"] as JObject,
                new[] { "estimatorVersion", "stockRulesVersion", "materialPriceModelVersion", "commercialRulesVersion" },
                "materialCatalogVersion", "toolLibraryVersion", "reachRulesVersion", "finishingRulesVersion")
            && HasExactProperties(root["geometrySummary"] as JObject, "category", "confidence", "reviewReasons")
            && HasExactProperties(root["stockPlan"] as JObject,
                "strategy", "pieces", "supplierShippingBeforeVat", "bufferedPiecePriceBeforeVat", "confidence", "reviewReasons")
            && HasExactProperties(root["setupPlan"] as JObject, "setupCount", "category")
            && HasExactProperties(root["operationSummary"] as JObject, "category", "operationCount")
            && HasExactProperties(root["quantityPlan"] as JObject, "quantity", "category")
            && (root["manufacturingEvidence"] == null
                || HasExactProperties(
                    root["manufacturingEvidence"] as JObject,
                    "stockShape", "setupCount", "toolFamilies", "residualVolumeMm3"))
            && (root["lineItems"] == null
                || (root["lineItems"] is JArray lineItems
                    && lineItems.All(item => HasRequiredAndOptionalProperties(
                        item as JObject,
                        new[] { "code", "category", "amountBeforeVat" },
                        "finishCode"))));
    }

    private static bool HasRequiredAndOptionalProperties(JObject? value, IReadOnlyCollection<string> required, params string[] optional)
    {
        if (value == null)
        {
            return false;
        }

        var allowed = new HashSet<string>(required.Concat(optional), StringComparer.Ordinal);
        return value.Properties().All(property => allowed.Remove(property.Name))
            && required.All(name => value.Property(name, StringComparison.Ordinal) != null);
    }

    private static bool HasExactProperties(JObject? value, params string[] expected)
    {
        if (value == null || value.Count != expected.Length)
        {
            return false;
        }

        var allowed = new HashSet<string>(expected, StringComparer.Ordinal);
        return value.Properties().All(property => allowed.Remove(property.Name)) && allowed.Count == 0;
    }

    private static bool IsValidCncSnapshotShape(CncEstimateSnapshot snapshot)
    {
        if (snapshot == null
            || !string.Equals(snapshot.EstimateStatus, "Preliminary", StringComparison.Ordinal)
            || !snapshot.ClientGenerated
            || !string.Equals(snapshot.Currency, "THB", StringComparison.Ordinal)
            || !DateTimeOffset.TryParse(snapshot.CalculatedAtUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset calculatedAt)
            || calculatedAt.Offset != TimeSpan.Zero
            || !IsCncConfidence(snapshot.Confidence)
            || !IsCncReviewReasons(snapshot.ReviewReasons)
            || !IsFiniteNonnegative(snapshot.EstimatedMinutesPerPart)
            || !IsFiniteNonnegative(snapshot.EstimatedPriceBeforeVat)
            || !IsFiniteNonnegative(snapshot.VatRate)
            || snapshot.VatRate > 1D
            || !IsFiniteNonnegative(snapshot.EstimatedPriceAfterVat)
            || snapshot.Versions == null
            || !IsCncIdentifier(snapshot.Versions.EstimatorVersion)
            || !IsCncIdentifier(snapshot.Versions.StockRulesVersion)
            || !IsCncIdentifier(snapshot.Versions.MaterialPriceModelVersion)
            || !IsCncIdentifier(snapshot.Versions.CommercialRulesVersion)
            || (!string.IsNullOrEmpty(snapshot.Versions.MaterialCatalogVersion) && !IsCncIdentifier(snapshot.Versions.MaterialCatalogVersion))
            || (!string.IsNullOrEmpty(snapshot.Versions.ToolLibraryVersion) && !IsCncIdentifier(snapshot.Versions.ToolLibraryVersion))
            || (!string.IsNullOrEmpty(snapshot.Versions.ReachRulesVersion) && !IsCncIdentifier(snapshot.Versions.ReachRulesVersion))
            || (!string.IsNullOrEmpty(snapshot.Versions.FinishingRulesVersion) && !IsCncIdentifier(snapshot.Versions.FinishingRulesVersion))
            || snapshot.GeometrySummary == null
            || !IsBoundedCncText(snapshot.GeometrySummary.Category, 120, required: true)
            || !IsCncConfidence(snapshot.GeometrySummary.Confidence)
            || !IsCncReviewReasons(snapshot.GeometrySummary.ReviewReasons)
            || snapshot.StockPlan == null
            || !IsCncIdentifier(snapshot.StockPlan.Strategy)
            || snapshot.StockPlan.Pieces <= 0
            || snapshot.StockPlan.Pieces > 1_000_000
            || !IsFiniteNonnegative(snapshot.StockPlan.SupplierShippingBeforeVat)
            || !IsFiniteNonnegative(snapshot.StockPlan.BufferedPiecePriceBeforeVat)
            || !IsCncConfidence(snapshot.StockPlan.Confidence)
            || !IsCncReviewReasons(snapshot.StockPlan.ReviewReasons)
            || snapshot.SetupPlan == null
            || snapshot.SetupPlan.SetupCount <= 0
            || snapshot.SetupPlan.SetupCount > 1_000
            || !IsBoundedCncText(snapshot.SetupPlan.Category, 120, required: true)
            || snapshot.OperationSummary == null
            || snapshot.OperationSummary.OperationCount < 0
            || snapshot.OperationSummary.OperationCount > 10_000
            || !IsBoundedCncText(snapshot.OperationSummary.Category, 160, required: true)
            || snapshot.QuantityPlan == null
            || snapshot.QuantityPlan.Quantity <= 0
            || snapshot.QuantityPlan.Quantity > 1_000_000
            || !IsBoundedCncText(snapshot.QuantityPlan.Category, 120, required: true)
            || !IsValidCncManufacturingEvidence(snapshot.ManufacturingEvidence)
            || !IsValidCncPreliminaryLineItems(snapshot.LineItems))
        {
            return false;
        }

        return true;
    }

    private static bool IsValidCncManufacturingEvidence(CncManufacturingEvidence evidence)
    {
        return evidence == null
            || (IsCncIdentifier(evidence.StockShape)
                && evidence.SetupCount > 0
                && evidence.SetupCount <= MaximumCncManufacturingEvidenceSetupCount
                && evidence.ToolFamilies != null
                && evidence.ToolFamilies.Count <= MaximumCncManufacturingEvidenceToolFamilyCount
                && evidence.ToolFamilies.All(IsCncIdentifier)
                && evidence.ToolFamilies.Distinct(StringComparer.Ordinal).Count() == evidence.ToolFamilies.Count
                && IsFiniteNonnegative(evidence.ResidualVolumeMm3));
    }

    private static bool IsValidCncPreliminaryLineItems(IReadOnlyCollection<CncPreliminaryLineItem> items)
    {
        if (items == null)
        {
            return true;
        }

        string[] requiredCodes = { "material", "shipping", "setup", "machining", "tooling", "finish" };
        string[] allowedCodes =
        {
                "material", "shipping", "cam", "setup", "machining", "tooling", "workholding", "inspection", "deburr",
                "finish", "commercial_adjustment", "minimum_order_adjustment", "rounding_adjustment",
            };
        return items.Count > 0
            && items.Count <= MaximumCncPreliminaryLineItemCount
            && items.All(item => item != null
                && IsCncIdentifier(item.Code)
                && allowedCodes.Contains(item.Code, StringComparer.Ordinal)
                && IsBoundedCncText(item.Category, 80, required: true)
                && IsFiniteNonnegative(item.AmountBeforeVat)
                && (string.IsNullOrEmpty(item.FinishCode) || IsCncIdentifier(item.FinishCode))
                && (!string.Equals(item.Code, "finish", StringComparison.Ordinal) || !string.IsNullOrEmpty(item.FinishCode)))
            && items.Select(item => item.Code).Distinct(StringComparer.Ordinal).Count() == items.Count
            && requiredCodes.All(code => items.Any(item => string.Equals(item.Code, code, StringComparison.Ordinal)));
    }

    private static bool IsValidCncPreliminaryLineItemMoneyJson(string json)
    {
        try
        {
            using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(
                json,
                new System.Text.Json.JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = System.Text.Json.JsonCommentHandling.Skip,
                    MaxDepth = 24,
                });
            System.Text.Json.JsonElement root = document.RootElement;
            if (root.ValueKind != System.Text.Json.JsonValueKind.Object)
            {
                return false;
            }

            if (!root.TryGetProperty("lineItems", out System.Text.Json.JsonElement lineItems))
            {
                return true;
            }

            if (!root.TryGetProperty("estimatedPriceBeforeVat", out System.Text.Json.JsonElement estimatedPrice)
                || !TryReadCncMoneyMinorUnits(estimatedPrice, out long estimatedPriceMinorUnits)
                || lineItems.ValueKind != System.Text.Json.JsonValueKind.Array)
            {
                return false;
            }

            long reconciledMinorUnits = 0L;
            foreach (System.Text.Json.JsonElement lineItem in lineItems.EnumerateArray())
            {
                if (lineItem.ValueKind != System.Text.Json.JsonValueKind.Object
                    || !lineItem.TryGetProperty("amountBeforeVat", out System.Text.Json.JsonElement amount)
                    || !TryReadCncMoneyMinorUnits(amount, out long amountMinorUnits)
                    || reconciledMinorUnits > MaximumCncMoneyMinorUnits - amountMinorUnits)
                {
                    return false;
                }

                reconciledMinorUnits = checked(reconciledMinorUnits + amountMinorUnits);
            }

            long difference = reconciledMinorUnits >= estimatedPriceMinorUnits
                ? reconciledMinorUnits - estimatedPriceMinorUnits
                : estimatedPriceMinorUnits - reconciledMinorUnits;
            return difference <= 1L;
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static bool TryReadCncMoneyMinorUnits(System.Text.Json.JsonElement element, out long minorUnits)
    {
        minorUnits = 0L;
        if (element.ValueKind != System.Text.Json.JsonValueKind.Number)
        {
            return false;
        }

        string raw = element.GetRawText();
        if (!Regex.IsMatch(raw, @"\A(?:0|[1-9][0-9]*)(?:\.[0-9]{1,2})?\z", RegexOptions.CultureInvariant)
            || !decimal.TryParse(raw, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal amount)
            || amount < 0M)
        {
            return false;
        }

        decimal minorUnitAmount = amount * 100M;
        if (minorUnitAmount > MaximumCncMoneyMinorUnits)
        {
            return false;
        }

        minorUnits = decimal.ToInt64(minorUnitAmount);
        return true;
    }

    private static bool IsFiniteNonnegative(double value) => double.IsFinite(value) && value >= 0D;

    private static bool IsCncConfidence(string value) =>
        string.Equals(value, "High", StringComparison.Ordinal)
        || string.Equals(value, "Medium", StringComparison.Ordinal)
        || string.Equals(value, "Low", StringComparison.Ordinal);

    private static bool IsCncIdentifier(string value)
    {
        return IsBoundedCncText(value, MaximumCncReviewReasonLength, required: true)
            && value.All(character => char.IsAsciiLetterOrDigit(character) || character == '_' || character == '-' || character == '.');
    }

    private static bool IsCncReviewReasons(IReadOnlyCollection<string> reasons)
    {
        return reasons != null
            && reasons.Count <= MaximumCncReviewReasonCount
            && reasons.All(IsCncIdentifier);
    }

    private static bool IsBoundedCncText(string value, int maximumLength, bool required)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return !required;
        }

        return value.Length <= maximumLength && !value.Any(character => character == '\0');
    }

    private static bool IsAllowedCncRequirement(string value, params string[] allowed) =>
        allowed.Any(candidate => string.Equals(candidate, value, StringComparison.Ordinal));
    [JsonObject(MemberSerialization.OptIn)]
    internal sealed class CncEstimateSnapshot
    {
        [JsonProperty("estimateStatus", Required = Required.Always)]
        public string EstimateStatus { get; set; } = null!;

        [JsonProperty("clientGenerated", Required = Required.Always)]
        public bool ClientGenerated { get; set; }

        [JsonProperty("calculatedAtUtc", Required = Required.Always)]
        public string CalculatedAtUtc { get; set; } = null!;

        [JsonProperty("versions", Required = Required.Always)]
        public CncEstimateVersions Versions { get; set; } = null!;

        [JsonProperty("geometrySummary", Required = Required.Always)]
        public CncGeometrySummary GeometrySummary { get; set; } = null!;

        [JsonProperty("stockPlan", Required = Required.Always)]
        public CncStockPlan StockPlan { get; set; } = null!;

        [JsonProperty("setupPlan", Required = Required.Always)]
        public CncSetupPlan SetupPlan { get; set; } = null!;

        [JsonProperty("operationSummary", Required = Required.Always)]
        public CncOperationSummary OperationSummary { get; set; } = null!;

        [JsonProperty("quantityPlan", Required = Required.Always)]
        public CncQuantityPlan QuantityPlan { get; set; } = null!;

        [JsonProperty("confidence", Required = Required.Always)]
        public string Confidence { get; set; } = null!;

        [JsonProperty("reviewReasons", Required = Required.Always)]
        public List<string> ReviewReasons { get; set; } = null!;

        [JsonProperty("estimatedMinutesPerPart", Required = Required.Always)]
        public double EstimatedMinutesPerPart { get; set; }

        [JsonProperty("estimatedPriceBeforeVat", Required = Required.Always)]
        public double EstimatedPriceBeforeVat { get; set; }

        [JsonProperty("vatRate", Required = Required.Always)]
        public double VatRate { get; set; }

        [JsonProperty("estimatedPriceAfterVat", Required = Required.Always)]
        public double EstimatedPriceAfterVat { get; set; }

        [JsonProperty("currency", Required = Required.Always)]
        public string Currency { get; set; } = null!;

        [JsonProperty("manufacturingEvidence", Required = Required.Default, NullValueHandling = NullValueHandling.Ignore)]
        public CncManufacturingEvidence ManufacturingEvidence { get; set; } = null!;

        [JsonProperty("lineItems", Required = Required.Default, NullValueHandling = NullValueHandling.Ignore)]
        public List<CncPreliminaryLineItem> LineItems { get; set; } = null!;
    }

    [JsonObject(MemberSerialization.OptIn)]
    internal sealed class CncEstimateVersions
    {
        [JsonProperty("estimatorVersion", Required = Required.Always)]
        public string EstimatorVersion { get; set; } = null!;

        [JsonProperty("stockRulesVersion", Required = Required.Always)]
        public string StockRulesVersion { get; set; } = null!;

        [JsonProperty("materialPriceModelVersion", Required = Required.Always)]
        public string MaterialPriceModelVersion { get; set; } = null!;

        [JsonProperty("commercialRulesVersion", Required = Required.Always)]
        public string CommercialRulesVersion { get; set; } = null!;

        [JsonProperty("materialCatalogVersion", Required = Required.Default, NullValueHandling = NullValueHandling.Ignore)]
        public string MaterialCatalogVersion { get; set; } = null!;

        [JsonProperty("toolLibraryVersion", Required = Required.Default, NullValueHandling = NullValueHandling.Ignore)]
        public string ToolLibraryVersion { get; set; } = null!;

        [JsonProperty("reachRulesVersion", Required = Required.Default, NullValueHandling = NullValueHandling.Ignore)]
        public string ReachRulesVersion { get; set; } = null!;

        [JsonProperty("finishingRulesVersion", Required = Required.Default, NullValueHandling = NullValueHandling.Ignore)]
        public string FinishingRulesVersion { get; set; } = null!;
    }

    [JsonObject(MemberSerialization.OptIn)]
    internal sealed class CncManufacturingEvidence
    {
        [JsonProperty("stockShape", Required = Required.Always)]
        public string StockShape { get; set; } = null!;

        [JsonProperty("setupCount", Required = Required.Always)]
        public int SetupCount { get; set; }

        [JsonProperty("toolFamilies", Required = Required.Always)]
        public List<string> ToolFamilies { get; set; } = null!;

        [JsonProperty("residualVolumeMm3", Required = Required.Always)]
        public double ResidualVolumeMm3 { get; set; }
    }

    [JsonObject(MemberSerialization.OptIn)]
    internal sealed class CncPreliminaryLineItem
    {
        [JsonProperty("code", Required = Required.Always)]
        public string Code { get; set; } = null!;

        [JsonProperty("category", Required = Required.Always)]
        public string Category { get; set; } = null!;

        [JsonProperty("amountBeforeVat", Required = Required.Always)]
        public double AmountBeforeVat { get; set; }

        [JsonProperty("finishCode", Required = Required.Default, NullValueHandling = NullValueHandling.Ignore)]
        public string FinishCode { get; set; } = null!;
    }

    [JsonObject(MemberSerialization.OptIn)]
    internal sealed class CncGeometrySummary
    {
        [JsonProperty("category", Required = Required.Always)]
        public string Category { get; set; } = null!;

        [JsonProperty("confidence", Required = Required.Always)]
        public string Confidence { get; set; } = null!;

        [JsonProperty("reviewReasons", Required = Required.Always)]
        public List<string> ReviewReasons { get; set; } = null!;
    }

    [JsonObject(MemberSerialization.OptIn)]
    internal sealed class CncStockPlan
    {
        [JsonProperty("strategy", Required = Required.Always)]
        public string Strategy { get; set; } = null!;

        [JsonProperty("pieces", Required = Required.Always)]
        public int Pieces { get; set; }

        [JsonProperty("supplierShippingBeforeVat", Required = Required.Always)]
        public double SupplierShippingBeforeVat { get; set; }

        [JsonProperty("bufferedPiecePriceBeforeVat", Required = Required.Always)]
        public double BufferedPiecePriceBeforeVat { get; set; }

        [JsonProperty("confidence", Required = Required.Always)]
        public string Confidence { get; set; } = null!;

        [JsonProperty("reviewReasons", Required = Required.Always)]
        public List<string> ReviewReasons { get; set; } = null!;
    }

    [JsonObject(MemberSerialization.OptIn)]
    internal sealed class CncSetupPlan
    {
        [JsonProperty("setupCount", Required = Required.Always)]
        public int SetupCount { get; set; }

        [JsonProperty("category", Required = Required.Always)]
        public string Category { get; set; } = null!;
    }

    [JsonObject(MemberSerialization.OptIn)]
    internal sealed class CncOperationSummary
    {
        [JsonProperty("category", Required = Required.Always)]
        public string Category { get; set; } = null!;

        [JsonProperty("operationCount", Required = Required.Always)]
        public int OperationCount { get; set; }
    }

    [JsonObject(MemberSerialization.OptIn)]
    internal sealed class CncQuantityPlan
    {
        [JsonProperty("quantity", Required = Required.Always)]
        public int Quantity { get; set; }

        [JsonProperty("category", Required = Required.Always)]
        public string Category { get; set; } = null!;
    }

    /// <summary>
    /// Item Detail.
    /// </summary>
    internal sealed class ItemDetail
    {
        /// <summary>
        /// Gets or sets the submitted manufacturing process discriminator.
        /// </summary>
        public string Process { get; set; } = null!;

        /// <summary>
        /// Gets or sets the untrusted client-generated CNC estimate JSON.
        /// </summary>
        public string CncEstimateJson { get; set; } = null!;

        /// <summary>
        /// Gets or sets the requested CNC tolerance policy.
        /// </summary>
        public string CncTolerance { get; set; } = null!;

        /// <summary>
        /// Gets or sets the submitted CNC thread requirement.
        /// </summary>
        public string CncThreads { get; set; } = null!;

        /// <summary>
        /// Gets or sets the submitted CNC roughness requirement.
        /// </summary>
        public string CncRoughness { get; set; } = null!;

        /// <summary>
        /// Gets or sets the submitted CNC inspection requirement.
        /// </summary>
        public string CncInspection { get; set; } = null!;

        /// <summary>
        /// Gets or sets the submitted CNC material-certificate requirement.
        /// </summary>
        public string CncCertificate { get; set; } = null!;

        /// <summary>
        /// Gets or sets the customer's optional CNC requirements or drawing note.
        /// </summary>
        public string CncRequirementsNote { get; set; } = null!;

        /// <summary>
        /// Gets or sets the per-item CNC technical drawing filename.
        /// </summary>
        public string CncDrawingFileName { get; set; } = null!;

        /// <summary>
        /// Gets or sets the owned per-item CNC technical drawing storage path.
        /// </summary>
        public string CncDrawingStoragePath { get; set; } = null!;

        /// <summary>
        /// Gets or sets the item index bound into CNC upload receipts.
        /// </summary>
        public string CncItemId { get; set; } = null!;

        /// <summary>
        /// Gets or sets the protected server-issued CNC model upload receipt.
        /// </summary>
        public string CncModelUploadReceipt { get; set; } = null!;

        /// <summary>
        /// Gets or sets the protected server-issued CNC drawing upload receipt.
        /// </summary>
        public string CncDrawingUploadReceipt { get; set; } = null!;

        internal CncEstimateSnapshot ValidatedCncEstimate { get; set; } = null!;

        internal string CanonicalCncEstimateJson { get; set; } = null!;

        internal CncProtectedReceipt ValidatedCncModelReceipt { get; set; } = null!;

        internal CncProtectedReceipt ValidatedCncDrawingReceipt { get; set; } = null!;

        /// <summary>
        /// Gets or sets the name of the file.
        /// </summary>
        /// <value>
        /// The name of the file.
        /// </value>
        public string FileName { get; set; } = null!;

        /// <summary>
        /// Gets or sets the storage path.
        /// </summary>
        /// <value>
        /// The storage path.
        /// </value>
        public string StoragePath { get; set; } = null!;

        /// <summary>
        /// Gets or sets the dimension.
        /// </summary>
        /// <value>
        /// The dimension.
        /// </value>
        public string Dimension { get; set; } = null!;

        /// <summary>
        /// Gets or sets the quantity.
        /// </summary>
        /// <value>
        /// The quantity.
        /// </value>
        public string Quantity { get; set; } = null!;

        /// <summary>
        /// Gets or sets the material.
        /// </summary>
        /// <value>
        /// The material.
        /// </value>
        public string Material { get; set; } = null!;

    }


}
