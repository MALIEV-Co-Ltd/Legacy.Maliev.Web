using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Maliev.AdditiveBenchmark;

/// <summary>
/// Validates version 1 additive benchmark manifests and creates deterministic reports.
/// </summary>
public static partial class AdditiveBenchmarkRunner
{
    /// <summary>The supported manifest schema version.</summary>
    public const string ManifestSchemaVersion = "1.0";

    /// <summary>The report schema version emitted by this runner.</summary>
    public const string ReportSchemaVersion = "1.0";

    private static readonly string[] RequiredCaseStrings =
    [
        "caseId",
        "modelId",
        "family",
        "split",
        "preference",
        "availability",
    ];

    private static readonly string[] ProfileFields =
    [
        "machineProfileId",
        "processProfileId",
        "filamentProfileId",
        "profileSha256",
        "slicerVersion",
        "supportMode",
        "orientationPolicyVersion",
    ];

    private static readonly string[] MetricFields =
    [
        "modelPrintSeconds",
        "preparationSeconds",
        "totalMachineSeconds",
        "modelGrams",
        "supportGrams",
        "purgeWasteGrams",
        "resinMilliliters",
    ];

    private static readonly string[] RootFields =
    [
        "schemaVersion",
        "benchmarkVersion",
        "status",
        "coverageRequirements",
        "evidenceBlockers",
        "cases",
    ];

    private static readonly string[] CaseFields =
    [
        "caseId",
        "modelId",
        "family",
        "split",
        "availability",
        "modelSha256",
        "sourceUri",
        "sourceUnits",
        "transform4x4",
        "materialSku",
        "machineProfileId",
        "processProfileId",
        "filamentProfileId",
        "profileSha256",
        "slicerVersion",
        "supportMode",
        "orientationPolicyVersion",
        "preference",
        "quantity",
        "expected",
        "workbookInputs",
        "slicerReference",
        "productionActual",
        "provenance",
        "missingDataReasons",
    ];

    private static readonly JsonSerializerOptions ReportJsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// Validates a manifest and produces a deterministic report. Blocked evidence is a reportable
    /// state and never counts as a passing benchmark.
    /// </summary>
    /// <param name="manifestPath">Path to the manifest JSON.</param>
    /// <param name="schemaPath">Path to the versioned JSON Schema.</param>
    /// <returns>The validation result and canonical report JSON.</returns>
    public static BenchmarkRunResult Run(string manifestPath, string schemaPath)
    {
        byte[] manifestBytes = File.ReadAllBytes(manifestPath);
        byte[] schemaBytes = File.ReadAllBytes(schemaPath);
        List<BenchmarkIssue> invalid = [];
        List<BenchmarkIssue> blocked = [];
        List<BenchmarkCaseResult> caseResults = [];

        ValidateSchema(schemaBytes, invalid);

        JsonDocument manifest;
        try
        {
            manifest = JsonDocument.Parse(manifestBytes);
        }
        catch (JsonException exception)
        {
            invalid.Add(new BenchmarkIssue(
                "manifest.invalid_json",
                "$",
                null,
                exception.Message));
            return CreateResult(manifestBytes, invalid, blocked, caseResults);
        }

        using (manifest)
        {
            ValidateManifest(manifest.RootElement, invalid, blocked, caseResults);
        }

        return CreateResult(manifestBytes, invalid, blocked, caseResults);
    }

    private static void ValidateSchema(byte[] schemaBytes, List<BenchmarkIssue> invalid)
    {
        try
        {
            using JsonDocument schema = JsonDocument.Parse(schemaBytes);
            JsonElement root = schema.RootElement;
            RequireExactString(
                root,
                "$schema",
                "https://json-schema.org/draft/2020-12/schema",
                "$schema",
                "schema.unsupported_draft",
                invalid);

            if (!root.TryGetProperty("properties", out JsonElement properties)
                || !properties.TryGetProperty("schemaVersion", out JsonElement version)
                || !version.TryGetProperty("const", out JsonElement constant)
                || constant.ValueKind != JsonValueKind.String
                || constant.GetString() != ManifestSchemaVersion)
            {
                invalid.Add(new BenchmarkIssue(
                    "schema.version_contract_missing",
                    "$.properties.schemaVersion.const",
                    null,
                    $"Schema must pin schemaVersion to {ManifestSchemaVersion}."));
            }

            if (!root.TryGetProperty("additionalProperties", out JsonElement additional)
                || additional.ValueKind != JsonValueKind.False)
            {
                invalid.Add(new BenchmarkIssue(
                    "schema.open_root_contract",
                    "$.additionalProperties",
                    null,
                    "Schema root must reject undeclared properties."));
            }
        }
        catch (JsonException exception)
        {
            invalid.Add(new BenchmarkIssue(
                "schema.invalid_json",
                "$schema",
                null,
                exception.Message));
        }
    }

    private static void ValidateManifest(
        JsonElement root,
        List<BenchmarkIssue> invalid,
        List<BenchmarkIssue> blocked,
        List<BenchmarkCaseResult> caseResults)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            invalid.Add(new BenchmarkIssue("manifest.root_type", "$", null, "Manifest root must be an object."));
            return;
        }

        RejectUnknownProperties(root, RootFields, "$", null, invalid);

        RequireExactString(
            root,
            "schemaVersion",
            ManifestSchemaVersion,
            "$.schemaVersion",
            "manifest.unsupported_version",
            invalid);
        RequireNonEmptyString(root, "benchmarkVersion", "$.benchmarkVersion", invalid);
        string? declaredStatus = ReadRequiredString(root, "status", "$.status", invalid);
        ValidateEnum(declaredStatus, ["ready", "blocked"], "manifest.status", "$.status", null, invalid);

        Dictionary<string, (string Family, string Split)> models = new(StringComparer.Ordinal);
        HashSet<string> caseIds = new(StringComparer.Ordinal);
        HashSet<string> preferences = new(StringComparer.Ordinal);

        if (!root.TryGetProperty("cases", out JsonElement cases) || cases.ValueKind != JsonValueKind.Array)
        {
            invalid.Add(new BenchmarkIssue("manifest.cases_missing", "$.cases", null, "Cases must be an array."));
        }
        else
        {
            int index = 0;
            foreach (JsonElement benchmarkCase in cases.EnumerateArray())
            {
                ValidateCase(benchmarkCase, index, invalid, blocked, caseResults, caseIds, models, preferences);
                index++;
            }
        }

        ValidateDeclaredBlockers(root, invalid, blocked);
        ValidateCoverage(root, models, caseResults.Count, preferences, invalid, blocked);

        if (declaredStatus == "ready" && (invalid.Count > 0 || blocked.Count > 0))
        {
            invalid.Add(new BenchmarkIssue(
                "manifest.ready_with_blockers",
                "$.status",
                null,
                "Manifest cannot declare ready while validation or evidence blockers remain."));
        }
        else if (declaredStatus == "blocked" && invalid.Count == 0 && blocked.Count == 0)
        {
            invalid.Add(new BenchmarkIssue(
                "manifest.blocked_without_blocker",
                "$.status",
                null,
                "Manifest cannot declare blocked when no validation or evidence blocker remains."));
        }
    }

    private static void ValidateCase(
        JsonElement benchmarkCase,
        int index,
        List<BenchmarkIssue> invalid,
        List<BenchmarkIssue> blocked,
        List<BenchmarkCaseResult> caseResults,
        HashSet<string> caseIds,
        Dictionary<string, (string Family, string Split)> models,
        HashSet<string> preferences)
    {
        string path = $"$.cases[{index}]";
        if (benchmarkCase.ValueKind != JsonValueKind.Object)
        {
            invalid.Add(new BenchmarkIssue("case.type", path, null, "Case must be an object."));
            return;
        }

        RejectUnknownProperties(benchmarkCase, CaseFields, path, null, invalid);

        Dictionary<string, string?> values = [];
        foreach (string property in RequiredCaseStrings)
        {
            values[property] = ReadRequiredString(benchmarkCase, property, $"{path}.{property}", invalid);
        }

        string? caseId = values["caseId"];
        if (caseId is not null && !caseIds.Add(caseId))
        {
            invalid.Add(new BenchmarkIssue("case.duplicate_id", $"{path}.caseId", caseId, "Case ID must be unique."));
        }

        ValidateEnum(values["family"],
            ["thin_lettering", "flat_plates", "hollow_parts", "tall_walls", "curved_surfaces", "dense_parts", "small_details", "support_heavy"],
            "case.family",
            $"{path}.family",
            caseId,
            invalid);
        ValidateEnum(values["split"], ["calibration", "validation"], "case.split", $"{path}.split", caseId, invalid);
        ValidateEnum(values["preference"], ["quality", "standard", "strength"], "case.preference", $"{path}.preference", caseId, invalid);
        ValidateEnum(values["availability"], ["ready", "blocked"], "case.availability", $"{path}.availability", caseId, invalid);

        if (values["modelId"] is string modelId
            && values["family"] is string family
            && values["split"] is string split)
        {
            if (models.TryGetValue(modelId, out (string Family, string Split) existing)
                && (existing.Family != family || existing.Split != split))
            {
                invalid.Add(new BenchmarkIssue(
                    "case.model_split_mismatch",
                    $"{path}.modelId",
                    caseId,
                    "All configurations of a model must stay in one family and split."));
            }
            else
            {
                models[modelId] = (family, split);
            }
        }

        if (values["preference"] is string preference)
        {
            preferences.Add(preference);
        }

        ValidateNullableSha(benchmarkCase, "modelSha256", $"{path}.modelSha256", caseId, invalid);
        ValidateNullableSha(benchmarkCase, "profileSha256", $"{path}.profileSha256", caseId, invalid);
        ValidateNullableTransform(benchmarkCase, path, caseId, invalid);
        ValidatePositiveInteger(benchmarkCase, "quantity", $"{path}.quantity", caseId, invalid);

        foreach (string field in ProfileFields.Where(field => field != "profileSha256"))
        {
            ValidateNullableString(benchmarkCase, field, $"{path}.{field}", caseId, invalid);
        }

        ValidateNullableString(benchmarkCase, "sourceUri", $"{path}.sourceUri", caseId, invalid);
        ValidateNullableString(benchmarkCase, "sourceUnits", $"{path}.sourceUnits", caseId, invalid);
        ValidateNullableString(benchmarkCase, "materialSku", $"{path}.materialSku", caseId, invalid);
        ValidateMetricsObject(benchmarkCase, "expected", path, caseId, invalid);
        ValidateMetricsObject(benchmarkCase, "slicerReference", path, caseId, invalid);
        ValidateMetricsObject(benchmarkCase, "productionActual", path, caseId, invalid);
        ValidateWorkbookInputs(benchmarkCase, path, caseId, invalid);
        ValidateProvenance(benchmarkCase, path, caseId, invalid);

        List<string> missingReasons = ReadMissingReasons(benchmarkCase, path, caseId, invalid);
        bool ready = values["availability"] == "ready";
        if (ready)
        {
            ValidateReadyCase(benchmarkCase, path, caseId, missingReasons, invalid);
        }
        else if (values["availability"] == "blocked")
        {
            if (missingReasons.Count == 0)
            {
                invalid.Add(new BenchmarkIssue(
                    "case.blocked_without_reason",
                    $"{path}.missingDataReasons",
                    caseId,
                    "Blocked cases must state at least one missing-data reason."));
            }

            foreach (string reason in missingReasons)
            {
                blocked.Add(new BenchmarkIssue("case.blocked", $"{path}.missingDataReasons", caseId, reason));
            }
        }

        caseResults.Add(new BenchmarkCaseResult(
            caseId ?? $"invalid-case-{index}",
            values["availability"] ?? "invalid",
            missingReasons.Order(StringComparer.Ordinal).ToArray()));
    }

    private static void ValidateReadyCase(
        JsonElement benchmarkCase,
        string path,
        string? caseId,
        IReadOnlyCollection<string> missingReasons,
        List<BenchmarkIssue> invalid)
    {
        if (missingReasons.Count > 0)
        {
            invalid.Add(new BenchmarkIssue(
                "case.ready_has_missing_data",
                $"{path}.missingDataReasons",
                caseId,
                "Ready cases cannot retain missing-data reasons."));
        }

        string[] requiredReadyStrings = ["modelSha256", "sourceUri", "sourceUnits", .. ProfileFields];
        foreach (string field in requiredReadyStrings)
        {
            if (!benchmarkCase.TryGetProperty(field, out JsonElement value)
                || value.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(value.GetString()))
            {
                invalid.Add(new BenchmarkIssue(
                    "case.ready_missing_reference",
                    $"{path}.{field}",
                    caseId,
                    $"Ready case requires {field}."));
            }
        }

        if (!benchmarkCase.TryGetProperty("transform4x4", out JsonElement transform)
            || transform.ValueKind != JsonValueKind.Array
            || transform.GetArrayLength() != 16)
        {
            invalid.Add(new BenchmarkIssue(
                "case.ready_missing_transform",
                $"{path}.transform4x4",
                caseId,
                "Ready case requires the exact 4x4 transform."));
        }

        if (!benchmarkCase.TryGetProperty("slicerReference", out JsonElement reference)
            || reference.ValueKind != JsonValueKind.Object)
        {
            invalid.Add(new BenchmarkIssue(
                "case.ready_missing_slicer_reference",
                $"{path}.slicerReference",
                caseId,
                "Ready case requires slicer reference metrics."));
            return;
        }

        foreach (string field in new[] { "modelPrintSeconds", "totalMachineSeconds" })
        {
            if (!reference.TryGetProperty(field, out JsonElement metric)
                || metric.ValueKind != JsonValueKind.Number
                || !metric.TryGetDouble(out double value)
                || value <= 0)
            {
                invalid.Add(new BenchmarkIssue(
                    "case.ready_missing_slicer_metric",
                    $"{path}.slicerReference.{field}",
                    caseId,
                    $"Ready case requires positive {field}."));
            }
        }
    }

    private static void ValidateDeclaredBlockers(
        JsonElement root,
        List<BenchmarkIssue> invalid,
        List<BenchmarkIssue> blocked)
    {
        if (!root.TryGetProperty("evidenceBlockers", out JsonElement blockers)
            || blockers.ValueKind != JsonValueKind.Array)
        {
            invalid.Add(new BenchmarkIssue(
                "manifest.evidence_blockers_missing",
                "$.evidenceBlockers",
                null,
                "Evidence blockers must be an array."));
            return;
        }

        int index = 0;
        foreach (JsonElement blocker in blockers.EnumerateArray())
        {
            string path = $"$.evidenceBlockers[{index}]";
            RejectUnknownProperties(blocker, ["blockerId", "status", "missingDataReason"], path, null, invalid);
            string? blockerId = ReadRequiredString(blocker, "blockerId", $"{path}.blockerId", invalid);
            string? reason = ReadRequiredString(blocker, "missingDataReason", $"{path}.missingDataReason", invalid);
            RequireExactString(blocker, "status", "blocked", $"{path}.status", "blocker.status", invalid);
            if (blockerId is not null && reason is not null)
            {
                blocked.Add(new BenchmarkIssue("evidence.blocked", path, null, $"{blockerId}: {reason}"));
            }

            index++;
        }

    }

    private static void ValidateCoverage(
        JsonElement root,
        IReadOnlyDictionary<string, (string Family, string Split)> models,
        int caseCount,
        IReadOnlySet<string> preferences,
        List<BenchmarkIssue> invalid,
        List<BenchmarkIssue> blocked)
    {
        if (!root.TryGetProperty("coverageRequirements", out JsonElement coverage)
            || coverage.ValueKind != JsonValueKind.Object)
        {
            invalid.Add(new BenchmarkIssue(
                "manifest.coverage_missing",
                "$.coverageRequirements",
                null,
                "Coverage requirements must be declared."));
            return;
        }

        RejectUnknownProperties(
            coverage,
            ["modelCount", "calibrationModelCount", "validationModelCount", "minimumCases", "families", "preferences"],
            "$.coverageRequirements",
            null,
            invalid);

        int requiredModels = ReadRequiredPositiveInteger(coverage, "modelCount", "$.coverageRequirements.modelCount", invalid);
        int calibrationModels = ReadRequiredPositiveInteger(coverage, "calibrationModelCount", "$.coverageRequirements.calibrationModelCount", invalid);
        int validationModels = ReadRequiredPositiveInteger(coverage, "validationModelCount", "$.coverageRequirements.validationModelCount", invalid);
        int minimumCases = ReadRequiredPositiveInteger(coverage, "minimumCases", "$.coverageRequirements.minimumCases", invalid);

        AddCoverageBlocker(blocked, "coverage.model_count", "$.cases", models.Count, requiredModels, "distinct models");
        AddCoverageBlocker(blocked, "coverage.calibration_models", "$.cases", models.Count(item => item.Value.Split == "calibration"), calibrationModels, "calibration models");
        AddCoverageBlocker(blocked, "coverage.validation_models", "$.cases", models.Count(item => item.Value.Split == "validation"), validationModels, "validation models");
        AddCoverageBlocker(blocked, "coverage.minimum_cases", "$.cases", caseCount, minimumCases, "benchmark cases");

        if (coverage.TryGetProperty("families", out JsonElement families) && families.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement familyElement in families.EnumerateArray())
            {
                if (familyElement.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(familyElement.GetString()))
                {
                    invalid.Add(new BenchmarkIssue(
                        "coverage.family_invalid",
                        "$.coverageRequirements.families",
                        null,
                        "Coverage families must be non-empty strings."));
                    continue;
                }

                string family = familyElement.GetString()!;
                int actual = models.Count(item => item.Value.Family == family);
                AddCoverageBlocker(blocked, "coverage.family", "$.cases", actual, 3, $"models in family {family}");
            }
        }
        else
        {
            invalid.Add(new BenchmarkIssue(
                "coverage.families_missing",
                "$.coverageRequirements.families",
                null,
                "Coverage families must be declared."));
        }

        if (coverage.TryGetProperty("preferences", out JsonElement requiredPreferences)
            && requiredPreferences.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement preferenceElement in requiredPreferences.EnumerateArray())
            {
                if (preferenceElement.ValueKind == JsonValueKind.String
                    && preferenceElement.GetString() is string preference
                    && !preferences.Contains(preference))
                {
                    blocked.Add(new BenchmarkIssue(
                        "coverage.preference",
                        "$.cases",
                        null,
                        $"No supplied case covers required preference {preference}."));
                }
            }
        }
        else
        {
            invalid.Add(new BenchmarkIssue(
                "coverage.preferences_missing",
                "$.coverageRequirements.preferences",
                null,
                "Coverage preferences must be declared."));
        }
    }

    private static BenchmarkRunResult CreateResult(
        byte[] manifestBytes,
        IEnumerable<BenchmarkIssue> invalidIssues,
        IEnumerable<BenchmarkIssue> blockedIssues,
        IEnumerable<BenchmarkCaseResult> cases)
    {
        BenchmarkIssue[] invalid = invalidIssues
            .OrderBy(issue => issue.Path, StringComparer.Ordinal)
            .ThenBy(issue => issue.Code, StringComparer.Ordinal)
            .ThenBy(issue => issue.Message, StringComparer.Ordinal)
            .ToArray();
        BenchmarkIssue[] blocked = blockedIssues
            .OrderBy(issue => issue.Path, StringComparer.Ordinal)
            .ThenBy(issue => issue.Code, StringComparer.Ordinal)
            .ThenBy(issue => issue.Message, StringComparer.Ordinal)
            .ToArray();
        BenchmarkCaseResult[] orderedCases = cases.OrderBy(item => item.CaseId, StringComparer.Ordinal).ToArray();

        string status = invalid.Length > 0 ? "invalid" : blocked.Length > 0 ? "blocked" : "ready";
        BenchmarkReportSummary summary = new(
            orderedCases.Length,
            orderedCases.Count(item => item.Availability == "ready"),
            orderedCases.Count(item => item.Availability == "blocked"),
            invalid.Length,
            blocked.Length);
        BenchmarkDeterministicResult deterministic = new(status, summary, invalid, blocked, orderedCases);
        string deterministicJson = JsonSerializer.Serialize(deterministic);
        string resultSha256 = Sha256(Encoding.UTF8.GetBytes(deterministicJson));
        BenchmarkReport report = new(
            ReportSchemaVersion,
            ManifestSchemaVersion,
            Sha256NormalizedText(manifestBytes),
            resultSha256,
            status,
            summary,
            invalid,
            blocked,
            orderedCases);
        string reportJson = JsonSerializer.Serialize(report, ReportJsonOptions) + Environment.NewLine;
        int exitCode = status == "ready" ? 0 : status == "blocked" ? 2 : 1;
        return new BenchmarkRunResult(status, exitCode, report, reportJson);
    }

    private static void ValidateMetricsObject(
        JsonElement benchmarkCase,
        string property,
        string parentPath,
        string? caseId,
        List<BenchmarkIssue> invalid)
    {
        string path = $"{parentPath}.{property}";
        if (!benchmarkCase.TryGetProperty(property, out JsonElement metrics) || metrics.ValueKind != JsonValueKind.Object)
        {
            invalid.Add(new BenchmarkIssue("case.metrics_missing", path, caseId, $"{property} must be an object."));
            return;
        }

        RejectUnknownProperties(metrics, MetricFields, path, caseId, invalid);

        foreach (string field in MetricFields)
        {
            if (!metrics.TryGetProperty(field, out JsonElement metric))
            {
                invalid.Add(new BenchmarkIssue("case.metric_missing", $"{path}.{field}", caseId, "Metric must be present; use null when unknown."));
                continue;
            }

            if (metric.ValueKind == JsonValueKind.Null)
            {
                continue;
            }

            if (metric.ValueKind != JsonValueKind.Number
                || !metric.TryGetDouble(out double number)
                || number < 0
                || !double.IsFinite(number))
            {
                invalid.Add(new BenchmarkIssue("case.metric_invalid", $"{path}.{field}", caseId, "Metric must be null or a finite non-negative number."));
            }
        }
    }

    private static void ValidateWorkbookInputs(
        JsonElement benchmarkCase,
        string parentPath,
        string? caseId,
        List<BenchmarkIssue> invalid)
    {
        string path = $"{parentPath}.workbookInputs";
        if (!benchmarkCase.TryGetProperty("workbookInputs", out JsonElement inputs) || inputs.ValueKind != JsonValueKind.Object)
        {
            invalid.Add(new BenchmarkIssue("case.workbook_inputs_missing", path, caseId, "workbookInputs must be an object."));
            return;
        }

        RejectUnknownProperties(inputs, ["printTimeSeconds", "materialGrams", "priceThb"], path, caseId, invalid);

        foreach (string field in new[] { "printTimeSeconds", "materialGrams", "priceThb" })
        {
            if (!inputs.TryGetProperty(field, out JsonElement value)
                || (value.ValueKind != JsonValueKind.Null
                    && (value.ValueKind != JsonValueKind.Number
                        || !value.TryGetDouble(out double number)
                        || number < 0
                        || !double.IsFinite(number))))
            {
                invalid.Add(new BenchmarkIssue("case.workbook_input_invalid", $"{path}.{field}", caseId, "Workbook input must be null or a finite non-negative number."));
            }
        }
    }

    private static void ValidateProvenance(
        JsonElement benchmarkCase,
        string parentPath,
        string? caseId,
        List<BenchmarkIssue> invalid)
    {
        string path = $"{parentPath}.provenance";
        if (!benchmarkCase.TryGetProperty("provenance", out JsonElement provenance)
            || provenance.ValueKind != JsonValueKind.Array
            || provenance.GetArrayLength() == 0)
        {
            invalid.Add(new BenchmarkIssue("case.provenance_missing", path, caseId, "At least one provenance record is required."));
            return;
        }

        int index = 0;
        foreach (JsonElement record in provenance.EnumerateArray())
        {
            RejectUnknownProperties(
                record,
                ["sourceKind", "uri", "sha256", "observedAtUtc", "notes"],
                $"{path}[{index}]",
                caseId,
                invalid);
            ReadRequiredString(record, "sourceKind", $"{path}[{index}].sourceKind", invalid, caseId);
            ReadRequiredString(record, "uri", $"{path}[{index}].uri", invalid, caseId);
            ReadRequiredString(record, "notes", $"{path}[{index}].notes", invalid, caseId);
            ValidateNullableSha(record, "sha256", $"{path}[{index}].sha256", caseId, invalid);
            ValidateNullableUtc(record, "observedAtUtc", $"{path}[{index}].observedAtUtc", caseId, invalid);
            index++;
        }
    }

    private static List<string> ReadMissingReasons(
        JsonElement benchmarkCase,
        string path,
        string? caseId,
        List<BenchmarkIssue> invalid)
    {
        List<string> reasons = [];
        if (!benchmarkCase.TryGetProperty("missingDataReasons", out JsonElement values)
            || values.ValueKind != JsonValueKind.Array)
        {
            invalid.Add(new BenchmarkIssue("case.missing_reasons_type", $"{path}.missingDataReasons", caseId, "missingDataReasons must be an array."));
            return reasons;
        }

        foreach (JsonElement value in values.EnumerateArray())
        {
            if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
            {
                invalid.Add(new BenchmarkIssue("case.missing_reason_invalid", $"{path}.missingDataReasons", caseId, "Missing-data reasons must be non-empty strings."));
            }
            else
            {
                reasons.Add(value.GetString()!);
            }
        }

        return reasons;
    }

    private static void ValidateNullableTransform(
        JsonElement benchmarkCase,
        string path,
        string? caseId,
        List<BenchmarkIssue> invalid)
    {
        if (!benchmarkCase.TryGetProperty("transform4x4", out JsonElement transform))
        {
            invalid.Add(new BenchmarkIssue("case.transform_missing", $"{path}.transform4x4", caseId, "Transform must be present; use null when unknown."));
            return;
        }

        if (transform.ValueKind == JsonValueKind.Null)
        {
            return;
        }

        if (transform.ValueKind != JsonValueKind.Array || transform.GetArrayLength() != 16)
        {
            invalid.Add(new BenchmarkIssue("case.transform_invalid", $"{path}.transform4x4", caseId, "Transform must be null or contain exactly 16 finite numbers."));
            return;
        }

        foreach (JsonElement value in transform.EnumerateArray())
        {
            if (value.ValueKind != JsonValueKind.Number
                || !value.TryGetDouble(out double number)
                || !double.IsFinite(number))
            {
                invalid.Add(new BenchmarkIssue("case.transform_invalid", $"{path}.transform4x4", caseId, "Transform must be null or contain exactly 16 finite numbers."));
                return;
            }
        }
    }

    private static void ValidatePositiveInteger(
        JsonElement element,
        string property,
        string path,
        string? caseId,
        List<BenchmarkIssue> invalid)
    {
        if (!element.TryGetProperty(property, out JsonElement value)
            || value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt32(out int number)
            || number <= 0)
        {
            invalid.Add(new BenchmarkIssue("case.positive_integer", path, caseId, $"{property} must be a positive integer."));
        }
    }

    private static int ReadRequiredPositiveInteger(
        JsonElement element,
        string property,
        string path,
        List<BenchmarkIssue> invalid)
    {
        if (!element.TryGetProperty(property, out JsonElement value)
            || value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt32(out int number)
            || number <= 0)
        {
            invalid.Add(new BenchmarkIssue("coverage.positive_integer", path, null, $"{property} must be a positive integer."));
            return 0;
        }

        return number;
    }

    private static void ValidateNullableString(
        JsonElement element,
        string property,
        string path,
        string? caseId,
        List<BenchmarkIssue> invalid)
    {
        if (!element.TryGetProperty(property, out JsonElement value)
            || (value.ValueKind != JsonValueKind.Null
                && (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))))
        {
            invalid.Add(new BenchmarkIssue("case.nullable_string", path, caseId, $"{property} must be null or a non-empty string."));
        }
    }

    private static void ValidateNullableSha(
        JsonElement element,
        string property,
        string path,
        string? caseId,
        List<BenchmarkIssue> invalid)
    {
        if (!element.TryGetProperty(property, out JsonElement value))
        {
            invalid.Add(new BenchmarkIssue("case.sha_missing", path, caseId, $"{property} must be present; use null when unknown."));
            return;
        }

        if (value.ValueKind == JsonValueKind.Null)
        {
            return;
        }

        if (value.ValueKind != JsonValueKind.String || !Sha256Regex().IsMatch(value.GetString() ?? string.Empty))
        {
            invalid.Add(new BenchmarkIssue("case.sha_invalid", path, caseId, $"{property} must be null or a 64-character SHA-256 digest."));
        }
    }

    private static void ValidateNullableUtc(
        JsonElement element,
        string property,
        string path,
        string? caseId,
        List<BenchmarkIssue> invalid)
    {
        if (!element.TryGetProperty(property, out JsonElement value))
        {
            invalid.Add(new BenchmarkIssue("case.utc_missing", path, caseId, $"{property} must be present; use null when unknown."));
            return;
        }

        if (value.ValueKind == JsonValueKind.Null)
        {
            return;
        }

        if (value.ValueKind != JsonValueKind.String
            || value.GetString() is not string text
            || !text.EndsWith('Z')
            || !DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out _))
        {
            invalid.Add(new BenchmarkIssue("case.utc_invalid", path, caseId, $"{property} must be null or an ISO-8601 UTC timestamp."));
        }
    }

    private static string? ReadRequiredString(
        JsonElement element,
        string property,
        string path,
        List<BenchmarkIssue> invalid,
        string? caseId = null)
    {
        if (!element.TryGetProperty(property, out JsonElement value)
            || value.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(value.GetString()))
        {
            invalid.Add(new BenchmarkIssue("manifest.required_string", path, caseId, $"{property} must be a non-empty string."));
            return null;
        }

        return value.GetString();
    }

    private static void RequireNonEmptyString(
        JsonElement element,
        string property,
        string path,
        List<BenchmarkIssue> invalid)
    {
        ReadRequiredString(element, property, path, invalid);
    }

    private static void RequireExactString(
        JsonElement element,
        string property,
        string expected,
        string path,
        string code,
        List<BenchmarkIssue> invalid)
    {
        if (!element.TryGetProperty(property, out JsonElement value)
            || value.ValueKind != JsonValueKind.String
            || value.GetString() != expected)
        {
            invalid.Add(new BenchmarkIssue(code, path, null, $"{property} must equal {expected}."));
        }
    }

    private static void ValidateEnum(
        string? value,
        IReadOnlyCollection<string> allowed,
        string code,
        string path,
        string? caseId,
        List<BenchmarkIssue> invalid)
    {
        if (value is not null && !allowed.Contains(value, StringComparer.Ordinal))
        {
            invalid.Add(new BenchmarkIssue(code, path, caseId, $"Value must be one of: {string.Join(", ", allowed)}."));
        }
    }

    private static void RejectUnknownProperties(
        JsonElement element,
        IReadOnlyCollection<string> allowed,
        string path,
        string? caseId,
        List<BenchmarkIssue> invalid)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (!allowed.Contains(property.Name, StringComparer.Ordinal))
            {
                invalid.Add(new BenchmarkIssue(
                    "manifest.undeclared_property",
                    $"{path}.{property.Name}",
                    caseId,
                    $"Property {property.Name} is not declared by the versioned manifest contract."));
            }
        }
    }

    private static void AddCoverageBlocker(
        List<BenchmarkIssue> blocked,
        string code,
        string path,
        int actual,
        int required,
        string label)
    {
        if (required > 0 && actual < required)
        {
            blocked.Add(new BenchmarkIssue(code, path, null, $"Coverage has {actual} {label}; {required} required."));
        }
    }

    private static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static string Sha256NormalizedText(byte[] bytes)
    {
        string normalized = Encoding.UTF8.GetString(bytes).Replace("\r\n", "\n", StringComparison.Ordinal);
        return Sha256(Encoding.UTF8.GetBytes(normalized));
    }

    [GeneratedRegex("^[A-Fa-f0-9]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Regex();
}

/// <summary>A single validation or evidence-blocking issue.</summary>
/// <param name="Code">Stable machine-readable issue code.</param>
/// <param name="Path">JSON path associated with the issue.</param>
/// <param name="CaseId">Case identifier, when applicable.</param>
/// <param name="Message">Human-readable issue detail.</param>
public sealed record BenchmarkIssue(string Code, string Path, string? CaseId, string Message);

/// <summary>Per-case status included in the deterministic report.</summary>
/// <param name="CaseId">Stable case identifier.</param>
/// <param name="Availability">Ready, blocked, or invalid.</param>
/// <param name="MissingDataReasons">Sorted missing-data reasons.</param>
public sealed record BenchmarkCaseResult(string CaseId, string Availability, string[] MissingDataReasons);

/// <summary>Aggregate report counts.</summary>
/// <param name="TotalCases">Total supplied cases.</param>
/// <param name="ReadyCases">Cases eligible for benchmark execution.</param>
/// <param name="BlockedCases">Cases blocked on reference evidence.</param>
/// <param name="InvalidIssueCount">Manifest or schema contract errors.</param>
/// <param name="BlockingIssueCount">Evidence and coverage blockers.</param>
public sealed record BenchmarkReportSummary(
    int TotalCases,
    int ReadyCases,
    int BlockedCases,
    int InvalidIssueCount,
    int BlockingIssueCount);

/// <summary>Stable fields hashed independently of report metadata.</summary>
/// <param name="Status">Ready, blocked, or invalid.</param>
/// <param name="Summary">Aggregate counts.</param>
/// <param name="InvalidIssues">Contract validation errors.</param>
/// <param name="BlockingIssues">Reference evidence and coverage blockers.</param>
/// <param name="Cases">Sorted case results.</param>
public sealed record BenchmarkDeterministicResult(
    string Status,
    BenchmarkReportSummary Summary,
    BenchmarkIssue[] InvalidIssues,
    BenchmarkIssue[] BlockingIssues,
    BenchmarkCaseResult[] Cases);

/// <summary>Versioned additive benchmark report.</summary>
/// <param name="SchemaVersion">Report schema version.</param>
/// <param name="ManifestSchemaVersion">Validated manifest schema version.</param>
/// <param name="ManifestSha256">SHA-256 of UTF-8 manifest text with normalized LF line endings.</param>
/// <param name="DeterministicResultSha256">SHA-256 of sorted result fields.</param>
/// <param name="Status">Ready, blocked, or invalid.</param>
/// <param name="Summary">Aggregate counts.</param>
/// <param name="InvalidIssues">Contract validation errors.</param>
/// <param name="BlockingIssues">Reference evidence and coverage blockers.</param>
/// <param name="Cases">Sorted case results.</param>
public sealed record BenchmarkReport(
    string SchemaVersion,
    string ManifestSchemaVersion,
    string ManifestSha256,
    string DeterministicResultSha256,
    string Status,
    BenchmarkReportSummary Summary,
    BenchmarkIssue[] InvalidIssues,
    BenchmarkIssue[] BlockingIssues,
    BenchmarkCaseResult[] Cases);

/// <summary>In-memory result returned by the benchmark runner.</summary>
/// <param name="Status">Ready, blocked, or invalid.</param>
/// <param name="ExitCode">Zero for ready, two for blocked, and one for invalid.</param>
/// <param name="Report">Structured report.</param>
/// <param name="ReportJson">Deterministic JSON representation.</param>
public sealed record BenchmarkRunResult(
    string Status,
    int ExitCode,
    BenchmarkReport Report,
    string ReportJson);
