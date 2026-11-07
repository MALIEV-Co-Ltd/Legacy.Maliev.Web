namespace Legacy.Maliev.Web.Components.Pages.InstantQuotation;

/// <summary>Carries the catalog-backed material choices used to render the initial quotation selector.</summary>
/// <param name="Materials">The material options in display order.</param>
public sealed record InstantQuotationDisplayModel(
    IReadOnlyList<InstantQuotationMaterialOption> Materials)
{
    /// <summary>Gets an initial selector model with no available material options.</summary>
    public static InstantQuotationDisplayModel Empty { get; } = new([]);
}

/// <summary>Describes a material choice without carrying pricing authority.</summary>
/// <param name="Key">The stable material key used in quotation requests.</param>
/// <param name="DisplayName">The human-readable material label.</param>
/// <param name="Selected">Whether the option is initially selected.</param>
public sealed record InstantQuotationMaterialOption(
    string Key,
    string DisplayName,
    bool Selected);
