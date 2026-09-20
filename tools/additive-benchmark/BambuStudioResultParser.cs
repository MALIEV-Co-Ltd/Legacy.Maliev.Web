using System.Text.Json;

namespace Maliev.AdditiveBenchmark;

/// <summary>
/// Parses Bambu Studio's structured CLI result and rejects empty false-success responses.
/// </summary>
public static class BambuStudioResultParser
{
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
