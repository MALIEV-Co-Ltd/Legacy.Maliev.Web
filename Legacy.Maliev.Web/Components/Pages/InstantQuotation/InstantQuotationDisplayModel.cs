namespace Legacy.Maliev.Web.Components.Pages.InstantQuotation;

public sealed record InstantQuotationDisplayModel(
    IReadOnlyList<InstantQuotationMaterialOption> Materials)
{
    public static InstantQuotationDisplayModel Empty { get; } = new([]);
}

public sealed record InstantQuotationMaterialOption(
    string Key,
    string DisplayName,
    bool Selected);
