namespace Legacy.Maliev.Web.Application;

/// <summary>The declared length unit of a newly selected glTF asset.</summary>
public enum InstantQuotationGltfLengthUnit
{
    /// <summary>Printing compatibility: one source unit is one millimetre.</summary>
    Millimetres,
    /// <summary>Standard glTF: one source unit is one metre.</summary>
    Metres,
}

/// <summary>Frozen interpretation of raw glTF bytes; never an upload or pricing authority marker.</summary>
/// <param name="Version">Zero preserves restored legacy millimetres; new glTF requires version one.</param>
/// <param name="Unit">The explicit source length unit.</param>
public sealed record InstantQuotationGltfUnitPolicy(
    [property: System.Text.Json.Serialization.JsonRequired] int Version,
    [property: System.Text.Json.Serialization.JsonRequired] InstantQuotationGltfLengthUnit Unit)
{
    /// <summary>Gets the interpretation used before explicit glTF unit selection existed.</summary>
    public static InstantQuotationGltfUnitPolicy Legacy { get; } = new(0, InstantQuotationGltfLengthUnit.Millimetres);

    internal bool IsValid => (Version == 0 && Unit == InstantQuotationGltfLengthUnit.Millimetres)
        || (Version == 1 && Unit is InstantQuotationGltfLengthUnit.Millimetres or InstantQuotationGltfLengthUnit.Metres);

    internal double UnitsToMillimetres => Unit == InstantQuotationGltfLengthUnit.Metres ? 1000 : 1;

    internal bool IsValidForFile(string? fileName, bool newUpload)
    {
        var extension = Path.GetExtension(fileName);
        var gltf = string.Equals(extension, ".glb", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".gltf", StringComparison.OrdinalIgnoreCase);
        return IsValid && (gltf ? !newUpload || Version == 1 : this == Legacy);
    }
}
