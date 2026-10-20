using Legacy.Maliev.Web.Components.Pages.InstantQuotation;

namespace Legacy.Maliev.Web.Tests;

public sealed class MaterialCardPresentationCatalogTests
{
    [Fact]
    public void SourceCatalogue_PreservesAllOriginalKeysAndCategoryOrder()
    {
        Assert.Equal(new[] { "PLA", "PETG", "HIPS", "ABS", "ASA", "TPU", "PC", "PA6", "PA12", "PLA-CF", "PETG-CF", "PET-CF", "PA-CF", "ASA-CF", "PETG-ESD", "PA612-ESD", "ABS-ESD", "ABS-FR", "PC-FR", "M68", "K", "G217", "F80", "CASTWAX", "PVA" },
            MaterialCardPresentationCatalog.All.Select(material => material.Key));
        Assert.Equal(9, MaterialCardPresentationCatalog.All.Select(material => material.Category).Distinct().Count());
        Assert.All(MaterialCardPresentationCatalog.All, material => Assert.False(string.IsNullOrWhiteSpace(material.ThaiDescription)));
    }

    [Theory]
    [InlineData(" eco-friendly ", "PLA")]
    [InlineData("เป็นมิตร", "PLA")]
    [InlineData("carbon nanotubes", "PA612-ESD")]
    public void Search_AcceptsBothSourceLanguagesEvenWhenCurrentLocaleIsEnglish(string search, string expectedKey)
    {
        var result = MaterialCardPresentationCatalog.All.Where(material => MaterialCardPresentationCatalog.Matches(material, search, key => key));
        Assert.Contains(result, material => material.Key == expectedKey);
    }

    [Fact]
    public void OfferedMaterials_KeepSourceOrderAndDoNotInventUnavailableMaterials()
    {
        var result = MaterialCardPresentationCatalog.ForMaterials([
            new("M68", "Resin"), new("PLA", "PLA"), new("FUTURE", "Future material"),
        ]);
        Assert.Equal(new[] { "PLA", "M68", "FUTURE" }, result.Select(material => material.Key));
        Assert.Empty(result[^1].Description);
        Assert.Empty(result[^1].ThaiDescription);
    }
}
