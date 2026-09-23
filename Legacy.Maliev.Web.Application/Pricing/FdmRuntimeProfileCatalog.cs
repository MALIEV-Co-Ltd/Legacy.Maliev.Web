using System.Globalization;
using System.Text.Json;

namespace Legacy.Maliev.Web.Application.Pricing;

/// <summary>Immutable release decision for a material and FDM build preference.</summary>
public sealed record FdmRuntimeProfilePolicy(
    bool AutomaticPricingEligible,
    string ProfileVersion,
    IReadOnlyList<string> ReasonCodes);

/// <summary>
/// Validates the embedded manufacturing profile snapshot before any browser worker or
/// server quotation can consume it. Unknown or unqualified cells never authorize pricing.
/// </summary>
public sealed class FdmRuntimeProfileCatalog
{
    private const string ResourceName =
        "Legacy.Maliev.Web.Application.Pricing.Profiles.runtime-fdm-profiles.v1.json";
    private static readonly Lazy<FdmRuntimeProfileCatalog> Embedded = new(ReadEmbedded);
    private readonly RuntimeManifest manifest;

    private FdmRuntimeProfileCatalog(string browserManifestJson, RuntimeManifest manifest)
    {
        BrowserManifestJson = browserManifestJson;
        this.manifest = manifest;
    }

    /// <summary>Gets the exact, validated deployment-owned snapshot for the worker.</summary>
    public string BrowserManifestJson { get; }

    /// <summary>Gets the immutable release version used to bind worker evidence.</summary>
    public string ProfileVersion => manifest.ProfileVersion;

    /// <summary>Loads and validates the embedded release snapshot once per process.</summary>
    public static FdmRuntimeProfileCatalog LoadEmbedded() => Embedded.Value;

    /// <summary>Returns an ineligible policy when the material/build cell is unavailable.</summary>
    public FdmRuntimeProfilePolicy ResolvePolicy(string? materialKey, BuildPreference preference)
    {
        if (string.IsNullOrWhiteSpace(materialKey)
            || !manifest.Materials!.TryGetValue(materialKey, out var material)
            || !manifest.Builds!.ContainsKey(preference.ToString()))
        {
            return new(false, ProfileVersion, ["profile_unavailable"]);
        }

        return new(
            material.AutomaticPricingEligible,
            ProfileVersion,
            material.ReasonCodes!.OrderBy(static reason => reason, StringComparer.Ordinal).ToArray());
    }

    internal static FdmRuntimeProfileCatalog Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidDataException("The FDM runtime profile manifest is empty.");
        }

        RuntimeManifest manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<RuntimeManifest>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            }) ?? throw new InvalidDataException("The FDM runtime profile manifest is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The FDM runtime profile manifest is malformed.", exception);
        }

        Validate(manifest);
        return new(json, manifest);
    }

    private static FdmRuntimeProfileCatalog ReadEmbedded()
    {
        using var stream = typeof(FdmRuntimeProfileCatalog).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidDataException("The embedded FDM runtime profile manifest is missing.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    private static void Validate(RuntimeManifest manifest)
    {
        if (manifest.SchemaVersion != "1.0"
            || !HasBoundedValue(manifest.ProfileVersion, 128)
            || manifest.Machine is null
            || !ValidHash(manifest.Machine.SourceArtifactSha256)
            || !ValidSettings(manifest.Machine.Settings, machine: true)
            || manifest.Builds is null
            || manifest.Materials is null)
        {
            throw new InvalidDataException("The FDM runtime profile manifest is incomplete or unsupported.");
        }

        var expectedBuilds = Enum.GetNames<BuildPreference>().OrderBy(static key => key, StringComparer.Ordinal);
        if (!expectedBuilds.SequenceEqual(manifest.Builds.Keys.OrderBy(static key => key, StringComparer.Ordinal), StringComparer.Ordinal)
            || manifest.Builds.Values.Any(static build => build is null
                || !ValidHash(build.SourceArtifactSha256)
                || !ValidSettings(build.Settings, machine: false)))
        {
            throw new InvalidDataException("The FDM build profiles are incomplete or invalid.");
        }

        var expectedMaterials = PricingCatalog.Materials.Values
            .Where(static material => material.Process == PrintProcess.Fdm)
            .Select(static material => material.Key)
            .OrderBy(static key => key, StringComparer.Ordinal);
        if (!expectedMaterials.SequenceEqual(manifest.Materials.Keys.OrderBy(static key => key, StringComparer.Ordinal), StringComparer.Ordinal))
        {
            throw new InvalidDataException("The FDM material profiles do not match the pricing catalog.");
        }

        foreach (var (key, material) in manifest.Materials)
        {
            if (material is null
                || !HasBoundedValue(material.ProfileId, 128)
                || !HasBoundedValue(material.SourceAuthority, 256)
                || !ValidHash(material.SourceArtifactSha256)
                || !Positive(material.DensityGramsPerCm3)
                || !Positive(material.MaximumVolumetricFlowMm3PerSecond)
                || !NonNegative(material.MinimumLayerTimeSeconds)
                || !Positive(material.MinimumCoolingSpeedMmPerSecond)
                || material.ReasonCodes is null
                || material.ReasonCodes.Any(static reason => !HasBoundedValue(reason, 128))
                || (material.AutomaticPricingEligible && material.ReasonCodes.Count != 0))
            {
                throw new InvalidDataException($"The FDM material profile '{key}' is invalid.");
            }
        }
    }

    private static bool ValidSettings(Dictionary<string, string>? settings, bool machine)
    {
        if (settings is not { Count: > 0 and <= 256 }
            || !settings.All(pair => HasBoundedValue(pair.Key, 128)
                && HasBoundedValue(pair.Value, 4096)
                && ValidSettingValue(pair.Key, pair.Value, machine)))
        {
            return false;
        }

        string[] required = machine
            ? ["nozzle_diameter", "printable_width", "printable_depth", "printable_height"]
            : ["nozzle_diameter", "layer_height", "wall_loops", "sparse_infill_density"];
        return required.All(key => settings.TryGetValue(key, out var value)
            && ParseFinite(value.TrimEnd('%'), out var number) && number > 0);
    }

    private static bool ValidSettingValue(string key, string value, bool machine)
    {
        if (machine && key is "machine_id" or "supported_material_ids" or "excluded_zones" or "preparation_sequence_version"
            || !machine && key is "sparse_infill_pattern" or "support_mode")
        {
            return true;
        }

        if (!machine && key is "support_enabled" or "support_build_plate_only")
        {
            return value is "true" or "false";
        }

        var percentage = value.EndsWith('%');
        return ParseFinite(percentage ? value[..^1] : value, out var number)
            && number >= 0
            && number <= (percentage ? 100 : 100_000);
    }

    private static bool ValidHash(string? value) => value is { Length: 64 }
        && value.All(static character => character is >= '0' and <= '9'
            or >= 'A' and <= 'F' or >= 'a' and <= 'f');

    private static bool HasBoundedValue(string? value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maximumLength;

    private static bool Positive(string? value) => ParseFinite(value, out var number) && number > 0;

    private static bool NonNegative(string? value) => ParseFinite(value, out var number) && number >= 0;

    private static bool ParseFinite(string? value, out double number) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number)
        && double.IsFinite(number);

    internal sealed class RuntimeManifest
    {
        public string? SchemaVersion { get; set; }
        public string ProfileVersion { get; set; } = string.Empty;
        public ProfileSettings? Machine { get; set; }
        public Dictionary<string, ProfileSettings>? Builds { get; set; }
        public Dictionary<string, MaterialProfile>? Materials { get; set; }
    }

    internal sealed class ProfileSettings
    {
        public string? SourceArtifactSha256 { get; set; }
        public Dictionary<string, string>? Settings { get; set; }
    }

    internal sealed class MaterialProfile
    {
        public string? ProfileId { get; set; }
        public string? SourceAuthority { get; set; }
        public string? SourceArtifactSha256 { get; set; }
        public string? DensityGramsPerCm3 { get; set; }
        public string? MaximumVolumetricFlowMm3PerSecond { get; set; }
        public string? MinimumLayerTimeSeconds { get; set; }
        public string? MinimumCoolingSpeedMmPerSecond { get; set; }
        public bool AutomaticPricingEligible { get; set; }
        public List<string>? ReasonCodes { get; set; }
    }
}
