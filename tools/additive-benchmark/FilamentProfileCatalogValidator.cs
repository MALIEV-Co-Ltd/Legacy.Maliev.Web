using System.Text.Json;
using System.Text.RegularExpressions;

namespace Maliev.AdditiveBenchmark;

/// <summary>
/// Validates the versioned mapping between customer-facing material keys and resolved slicer profiles.
/// </summary>
public static partial class FilamentProfileCatalogValidator
{
    /// <summary>The only catalog version supported by this validator.</summary>
    public const string SchemaVersion = "1.0";

    /// <summary>
    /// Validates catalog structure, complete expected coverage, immutable digests, and fail-closed eligibility.
    /// </summary>
    /// <param name="catalogJson">Catalog JSON.</param>
    /// <param name="expectedMaterialKeys">Material keys that require an explicit decision.</param>
    /// <returns>Validation outcome and parsed entries.</returns>
    public static FilamentProfileCatalogValidation Validate(
        string catalogJson,
        IEnumerable<string> expectedMaterialKeys)
    {
        var errors = new List<string>();
        var entries = new List<FilamentProfileCatalogEntry>();

        JsonDocument catalog;
        try
        {
            catalog = JsonDocument.Parse(catalogJson);
        }
        catch (JsonException exception)
        {
            return new FilamentProfileCatalogValidation(false, [exception.Message], [], []);
        }

        using (catalog)
        {
            JsonElement root = catalog.RootElement;
            if (!root.TryGetProperty("schemaVersion", out JsonElement version)
                || version.GetString() != SchemaVersion)
            {
                errors.Add($"schemaVersion must equal {SchemaVersion}.");
            }

            if (!root.TryGetProperty("slicer", out JsonElement slicer)
                || slicer.GetString() != "Bambu Studio")
            {
                errors.Add("slicer must equal Bambu Studio.");
            }

            if (!root.TryGetProperty("slicerVersion", out JsonElement slicerVersion)
                || string.IsNullOrWhiteSpace(slicerVersion.GetString()))
            {
                errors.Add("slicerVersion is required.");
            }

            if (!root.TryGetProperty("entries", out JsonElement catalogEntries)
                || catalogEntries.ValueKind != JsonValueKind.Array)
            {
                errors.Add("entries must be an array.");
            }
            else
            {
                foreach (JsonElement item in catalogEntries.EnumerateArray())
                {
                    entries.Add(ParseEntry(item, errors));
                }
            }
        }

        string[] expected = expectedMaterialKeys
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();
        string[] actual = entries
            .Select(entry => entry.MaterialKey)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();

        foreach (IGrouping<string, FilamentProfileCatalogEntry> duplicate in entries
                     .GroupBy(entry => entry.MaterialKey, StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Count() > 1))
        {
            errors.Add($"Material {duplicate.Key} appears more than once.");
        }

        foreach (string missing in expected.Except(actual, StringComparer.OrdinalIgnoreCase))
        {
            errors.Add($"Material {missing} has no profile decision.");
        }

        foreach (string unexpected in actual.Except(expected, StringComparer.OrdinalIgnoreCase))
        {
            errors.Add($"Catalog contains unexpected material {unexpected}.");
        }

        return new FilamentProfileCatalogValidation(errors.Count == 0, errors, actual, entries);
    }

    private static FilamentProfileCatalogEntry ParseEntry(JsonElement item, List<string> errors)
    {
        string materialKey = ReadRequiredString(item, "materialKey", errors);
        string? profileId = ReadOptionalString(item, "profileId", errors);
        string? resolvedSha256 = ReadOptionalString(item, "resolvedSha256", errors);
        bool exactMaterialMatch = ReadRequiredBoolean(item, "exactMaterialMatch", errors);
        bool operatorApproved = ReadRequiredBoolean(item, "operatorApproved", errors);
        bool automationEligible = ReadRequiredBoolean(item, "automationEligible", errors);

        if ((profileId is null) != (resolvedSha256 is null))
        {
            errors.Add($"Material {materialKey} must supply profileId and resolvedSha256 together.");
        }

        if (resolvedSha256 is not null && !Sha256Pattern().IsMatch(resolvedSha256))
        {
            errors.Add($"Material {materialKey} has an invalid resolvedSha256.");
        }

        if (automationEligible && (!exactMaterialMatch || !operatorApproved || profileId is null))
        {
            errors.Add($"Material {materialKey} cannot be automationEligible without an exact approved profile.");
        }

        return new FilamentProfileCatalogEntry(
            materialKey,
            profileId,
            resolvedSha256,
            exactMaterialMatch,
            operatorApproved,
            automationEligible);
    }

    private static string ReadRequiredString(JsonElement item, string propertyName, List<string> errors)
    {
        if (!item.TryGetProperty(propertyName, out JsonElement value)
            || value.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(value.GetString()))
        {
            errors.Add($"{propertyName} is required on every entry.");
            return string.Empty;
        }

        return value.GetString()!;
    }

    private static string? ReadOptionalString(JsonElement item, string propertyName, List<string> errors)
    {
        if (!item.TryGetProperty(propertyName, out JsonElement value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
        {
            errors.Add($"{propertyName} must be a non-empty string or null.");
            return null;
        }

        return value.GetString();
    }

    private static bool ReadRequiredBoolean(JsonElement item, string propertyName, List<string> errors)
    {
        if (!item.TryGetProperty(propertyName, out JsonElement value)
            || value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            errors.Add($"{propertyName} must be a boolean on every entry.");
            return false;
        }

        return value.GetBoolean();
    }

    [GeneratedRegex("^[A-F0-9]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Pattern();
}

/// <summary>Parsed profile-decision fields used by downstream gates.</summary>
public sealed record FilamentProfileCatalogEntry(
    string MaterialKey,
    string? ProfileId,
    string? ResolvedSha256,
    bool ExactMaterialMatch,
    bool OperatorApproved,
    bool AutomationEligible);

/// <summary>Catalog validation outcome.</summary>
public sealed record FilamentProfileCatalogValidation(
    bool IsValid,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> MaterialKeys,
    IReadOnlyList<FilamentProfileCatalogEntry> Entries);
