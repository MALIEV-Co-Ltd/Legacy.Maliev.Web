using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Legacy.Maliev.Web.Application.Pricing;
using Legacy.Maliev.Web.Infrastructure;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Original 1c611bb contract against immutable accepted Catalog definitions, not current broad Web lists.</summary>
public sealed class MaterialCatalogCrossLayerContractTests
{
    private const string CatalogSourceSha256 = "2d4f2ff7df35bfcb9087f5a99bfddfefe68b7af049fafb7540e4789f7e933222";
    private const string FixtureSha256 = "64805979cbcf36e0271f4c69a59d079a951392480b009cc52121428b9793a115";

    internal static string[] ExpectedMaterialKeys => ExpectedColors().Keys.Order(StringComparer.Ordinal).ToArray();

    internal static string[] ExpectedBrowserColors(string key) => Fixture().GetProperty("materials")
        .EnumerateArray().Single(material => material.GetProperty("key").GetString() == key)
        .GetProperty("browserColors").EnumerateArray().Select(color => color.GetString()!).ToArray();

    public static IEnumerable<object[]> OfferedMaterials() => ExpectedMaterialKeys.Select(key => new object[] { key });

    [Fact]
    public void CompleteOfferedKeysMatchImmutableCatalog()
    {
        Assert.Equal(25, ExpectedMaterialKeys.Length);
        Assert.Equal(ExpectedMaterialKeys, PricingCatalog.Materials.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(ExpectedMaterialKeys, PricingCatalog.MaterialColors.Keys.Order(StringComparer.Ordinal));
        Assert.DoesNotContain("PC-ESD", ExpectedMaterialKeys);
    }

    [Theory]
    [MemberData(nameof(OfferedMaterials))]
    public void EveryCanonicalColorSetMatchesImmutableCatalog(string materialKey)
    {
        var expected = ExpectedColors()[materialKey].Order(StringComparer.Ordinal).ToArray();
        var canonical = PricingCatalog.MaterialColors[materialKey].Select(NormalizeSubmittedColor)
            .ToHashSet(StringComparer.Ordinal);
        if (PricingCatalog.IsColorSupported(materialKey, "#123456")) canonical.Add("Other");
        Assert.Equal(expected, canonical.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void CustomColorAdmissionRemainsOriginalPlaOnly()
    {
        Assert.Equal(new[] { "PLA" }, ExpectedMaterialKeys
            .Where(key => PricingCatalog.IsColorSupported(key, "#123456")));
    }

    [Theory]
    [InlineData("Any", "Random color")]
    [InlineData("Natural", "Raw")]
    [InlineData("Clear", "Transparent")]
    [InlineData("Translucent", "Transparent")]
    [InlineData("#123456", "Other")]
    [InlineData("Silver", "Silver")]
    public void SubmittedColorsInvokeUnchangedProductionNormalizer(string submitted, string expected) =>
        Assert.Equal(expected, NormalizeSubmittedColor(submitted));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void NullOrBlankColorsRemainRejectedForEveryOffer(string? color)
    {
        Assert.All(ExpectedMaterialKeys, key => Assert.False(PricingCatalog.IsColorSupported(key, color)));
    }

    private static string NormalizeSubmittedColor(string color)
    {
        // Invoke the retained production helper directly without changing its visibility or inventing a price/transport.
        // This is a helper contract, not a live Catalog/Order or physical-pricing witness.
        var method = typeof(InstantQuotationFulfillmentClient).GetMethod(
            "DatabaseColorName", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return method.CreateDelegate<Func<string, string>>()(color);
    }

    private static IReadOnlyDictionary<string, string[]> ExpectedColors()
    {
        var fixture = Fixture();
        var catalog = fixture.GetProperty("catalogSource");
        Assert.Equal(CatalogSourceSha256, catalog.GetProperty("sha256").GetString());
        var bytes = Convert.FromBase64String(catalog.GetProperty("textBase64").GetString()!);
        Assert.Equal(CatalogSourceSha256, Convert.ToHexStringLower(SHA256.HashData(bytes)));
        var definitions = Encoding.UTF8.GetString(bytes).Split('\n')
            .Where(line => line.TrimStart().StartsWith("Define(", StringComparison.Ordinal))
            .Select(line => Regex.Matches(line, "\"([^\"]+)\"").Select(match => match.Groups[1].Value).ToArray())
            .ToArray();
        Assert.Equal(25, definitions.Length);
        Assert.All(definitions, definition => Assert.True(definition.Length >= 3));
        return definitions.ToDictionary(definition => definition[0], definition => definition[2..], StringComparer.Ordinal);
    }

    private static JsonElement Fixture()
    {
        var path = Path.GetFullPath(Path.Combine(BrowserHostIdentityVerifier.SourceProjectDirectory(), "..",
            "Legacy.Maliev.Web.Tests", "TestAssets", "MaterialCatalog", "material-catalog-7b470-d2f1eef.json"));
        var bytes = Encoding.UTF8.GetBytes(File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal));
        Assert.Equal(FixtureSha256, Convert.ToHexStringLower(SHA256.HashData(bytes)));
        using var document = JsonDocument.Parse(bytes);
        Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("dc08124d25d7ec73aaf43043b05ae667bcc800a1f4ff0245d8336e1eb8408b4d",
            document.RootElement.GetProperty("browserSource").GetProperty("sha256").GetString());
        Assert.Equal(25, document.RootElement.GetProperty("materials").GetArrayLength());
        return document.RootElement.Clone();
    }
}
