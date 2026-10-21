using System.Text.Json;

namespace Maliev.AdditiveBenchmark;

/// <summary>
/// Parses Bambu Studio's structured CLI result and rejects empty false-success responses.
/// </summary>
public static class BambuStudioResultParser
{
    private static readonly HashSet<string> KnownFeatureRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "Outer wall",
        "Inner wall",
        "Sparse infill",
        "Internal solid infill",
        "Top surface",
        "Bottom surface",
        "Bridge",
        "Support",
        "Support interface",
        "Skirt",
        "Brim",
    };

    /// <summary>
    /// Parses one completed slice result.
    /// </summary>
    /// <param name="json">Contents of Bambu Studio's <c>result.json</c>.</param>
    /// <returns>Aggregated authoritative metrics.</returns>
    public static BambuStudioSliceMetrics Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        int returnCode = ReadRequiredInt32(root, "return_code");
        if (returnCode != 0)
        {
            string message = root.TryGetProperty("error_string", out JsonElement error)
                ? error.GetString() ?? "Unknown Bambu Studio error."
                : "Unknown Bambu Studio error.";
            throw new InvalidDataException($"Bambu Studio slice failed with code {returnCode}: {message}");
        }

        if (!root.TryGetProperty("sliced_plates", out JsonElement slicedPlates)
            || slicedPlates.ValueKind != JsonValueKind.Array
            || slicedPlates.GetArrayLength() == 0)
        {
            throw new InvalidDataException(
                "Bambu Studio returned success without sliced_plates; no material or time may be published.");
        }

        double modelSeconds = 0;
        double totalSeconds = 0;
        double materialGrams = 0;
        foreach (JsonElement plate in slicedPlates.EnumerateArray())
        {
            modelSeconds += ReadRequiredNonNegativeDouble(plate, "main_predication");
            totalSeconds += ReadRequiredNonNegativeDouble(plate, "total_predication");

            if (!plate.TryGetProperty("filaments", out JsonElement filaments)
                || filaments.ValueKind != JsonValueKind.Array
                || filaments.GetArrayLength() == 0)
            {
                throw new InvalidDataException("Bambu Studio sliced plate has no filament metrics.");
            }

            foreach (JsonElement filament in filaments.EnumerateArray())
            {
                materialGrams += ReadRequiredNonNegativeDouble(filament, "total_used_g");
            }
        }

        if (totalSeconds < modelSeconds)
        {
            throw new InvalidDataException("Bambu Studio total time is less than model time.");
        }

        double layerHeight = ReadRequiredPositiveDouble(root, "layer_height");
        double sparseInfill = ReadRequiredNonNegativeDouble(root, "sparse_infill_density");
        int wallLoops = ReadRequiredInt32(root, "wall_loops");
        if (wallLoops <= 0)
        {
            throw new InvalidDataException("Bambu Studio wall_loops must be positive for a completed slice.");
        }

        return new BambuStudioSliceMetrics(
            modelSeconds,
            totalSeconds,
            totalSeconds - modelSeconds,
            materialGrams,
            layerHeight,
            sparseInfill,
            wallLoops,
            slicedPlates.GetArrayLength());
    }

    /// <summary>
    /// Parses positive extrusion length by feature while honoring common G-code extrusion modes.
    /// Unknown feature roles make support evidence unavailable for certification.
    /// </summary>
    /// <param name="gcode">One or more concatenated plate G-code streams.</param>
    /// <returns>Extrusion evidence and any ambiguity reasons.</returns>
    public static GCodeExtrusionEvidence ParseGCodeEvidence(string gcode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gcode);
        bool absolute = true;
        int tool = 0;
        var positions = new Dictionary<int, double>();
        string? feature = null;
        double total = 0;
        double support = 0;
        var reasons = new HashSet<string>(StringComparer.Ordinal);

        foreach (string raw in gcode.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            string line = raw.Trim();
            if (line.StartsWith("; FEATURE:", StringComparison.OrdinalIgnoreCase))
            {
                feature = line[10..].Trim();
                if (!KnownFeatureRoles.Contains(feature))
                {
                    reasons.Add("ambiguous_feature_role");
                }

                continue;
            }

            string command = line.Split(';', 2)[0].Trim();
            if (command.Equals("M82", StringComparison.OrdinalIgnoreCase))
            {
                absolute = true;
                continue;
            }

            if (command.Equals("M83", StringComparison.OrdinalIgnoreCase))
            {
                absolute = false;
                continue;
            }

            if (command.Length > 1 && command[0] == 'T' && int.TryParse(command[1..], out int selectedTool))
            {
                tool = selectedTool;
                continue;
            }

            if (command.StartsWith("G92", StringComparison.OrdinalIgnoreCase)
                && TryReadWord(command, 'E', out double reset))
            {
                positions[tool] = reset;
                continue;
            }

            if (!(command.StartsWith("G0 ", StringComparison.OrdinalIgnoreCase)
                || command.StartsWith("G1 ", StringComparison.OrdinalIgnoreCase)
                || command.StartsWith("G2 ", StringComparison.OrdinalIgnoreCase)
                || command.StartsWith("G3 ", StringComparison.OrdinalIgnoreCase))
                || !TryReadWord(command, 'E', out double eValue))
            {
                continue;
            }

            double prior = positions.GetValueOrDefault(tool);
            double delta = absolute ? eValue - prior : eValue;
            positions[tool] = absolute ? eValue : prior + eValue;
            if (delta <= 0)
            {
                continue;
            }

            total += delta;
            if (feature is not null && feature.Contains("Support", StringComparison.OrdinalIgnoreCase))
            {
                support += delta;
            }
            else if (feature is null)
            {
                reasons.Add("missing_feature_role");
            }
        }

        double? certifyingSupport = reasons.Count == 0 ? support : null;
        return new GCodeExtrusionEvidence(total, support, certifyingSupport, reasons.Order().ToArray());
    }

    private static bool TryReadWord(string command, char word, out double value)
    {
        foreach (string token in command.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.Length > 1
                && char.ToUpperInvariant(token[0]) == word
                && double.TryParse(token[1..], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out value))
            {
                return true;
            }
        }

        value = 0;
        return false;
    }

    private static int ReadRequiredInt32(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out JsonElement value)
            || !value.TryGetInt32(out int result))
        {
            throw new InvalidDataException($"Bambu Studio result requires integer '{propertyName}'.");
        }

        return result;
    }

    private static double ReadRequiredPositiveDouble(JsonElement parent, string propertyName)
    {
        double result = ReadRequiredNonNegativeDouble(parent, propertyName);
        if (result <= 0)
        {
            throw new InvalidDataException($"Bambu Studio result '{propertyName}' must be positive.");
        }

        return result;
    }

    private static double ReadRequiredNonNegativeDouble(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out JsonElement value)
            || !value.TryGetDouble(out double result)
            || !double.IsFinite(result)
            || result < 0)
        {
            throw new InvalidDataException(
                $"Bambu Studio result requires finite non-negative number '{propertyName}'.");
        }

        return result;
    }
}

/// <summary>
/// Parsed metrics from one or more successfully sliced plates.
/// </summary>
/// <param name="ModelSeconds">Model printing prediction in seconds.</param>
/// <param name="TotalSeconds">Total machine prediction in seconds.</param>
/// <param name="PreparationSeconds">Difference between total and model predictions.</param>
/// <param name="TotalMaterialGrams">Total filament consumption across sliced plates.</param>
/// <param name="LayerHeightMillimetres">Resolved process layer height.</param>
/// <param name="SparseInfillPercent">Resolved sparse infill percentage.</param>
/// <param name="WallLoops">Resolved wall loop count.</param>
/// <param name="PlateCount">Number of sliced plates.</param>
public sealed record BambuStudioSliceMetrics(
    double ModelSeconds,
    double TotalSeconds,
    double PreparationSeconds,
    double TotalMaterialGrams,
    double LayerHeightMillimetres,
    double SparseInfillPercent,
    int WallLoops,
    int PlateCount);

/// <summary>Feature-level extrusion evidence parsed from reference G-code.</summary>
/// <param name="TotalPositiveExtrusionMillimetres">Positive filament-axis motion after retractions.</param>
/// <param name="SupportExtrusionMillimetres">Observed extrusion tagged with a support role.</param>
/// <param name="SupportExtrusionMillimetresForCertification">Support extrusion only when every feature role was unambiguous.</param>
/// <param name="ReasonCodes">Stable evidence limitations.</param>
public sealed record GCodeExtrusionEvidence(
    double TotalPositiveExtrusionMillimetres,
    double SupportExtrusionMillimetres,
    double? SupportExtrusionMillimetresForCertification,
    IReadOnlyList<string> ReasonCodes);
