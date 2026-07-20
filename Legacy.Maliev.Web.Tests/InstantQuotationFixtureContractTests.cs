using System.Security.Cryptography;
using System.Text.Json;

namespace Legacy.Maliev.Web.Tests;

public sealed class InstantQuotationFixtureContractTests
{
    private static readonly IReadOnlyDictionary<string, string> SupportedFormats =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [".stl"] = "model/stl",
            [".obj"] = "model/obj",
            [".3mf"] = "application/vnd.ms-package.3dmanufacturing-3dmodel+xml",
            [".glb"] = "model/gltf-binary",
            [".gltf"] = "model/gltf+json",
            [".stp"] = "model/step",
            [".step"] = "model/step",
            [".igs"] = "model/iges",
            [".iges"] = "model/iges",
        };

    [Fact]
    public void FixtureManifest_CoversEverySupportedFormatWithSafeFirstPartyProvenance()
    {
        var fixtureRoot = Path.Combine(FindRepositoryRoot().FullName, "tests", "instant-quotation", "fixtures");
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(fixtureRoot, "fixture-manifest.json")));
        var root = manifest.RootElement;

        Assert.Equal("MALIEV-authored", root.GetProperty("origin").GetString());
        Assert.False(root.GetProperty("containsThirdPartyContent").GetBoolean());
        Assert.False(root.GetProperty("containsCustomerData").GetBoolean());
        Assert.False(root.GetProperty("containsProductionData").GetBoolean());
        Assert.Equal("LicenseRef-MALIEV-Internal-Test-Fixture", root.GetProperty("license").GetString());

        var fixtures = root.GetProperty("fixtures").EnumerateArray().ToArray();
        Assert.Equal(SupportedFormats.Count, fixtures.Length);
        Assert.Equal(
            SupportedFormats.Keys.Order(StringComparer.Ordinal),
            fixtures.Select(item => item.GetProperty("extension").GetString()!).Order(StringComparer.Ordinal));

        foreach (var fixture in fixtures)
        {
            var relativePath = fixture.GetProperty("path").GetString();
            Assert.NotNull(relativePath);
            var path = Path.Combine(fixtureRoot, relativePath);
            var extension = fixture.GetProperty("extension").GetString();
            Assert.NotNull(extension);
            Assert.Equal(SupportedFormats[extension], fixture.GetProperty("mimeType").GetString());
            Assert.Equal("application/octet-stream", fixture.GetProperty("alternateAcceptedMimeType").GetString());
            Assert.True(File.Exists(path), $"Fixture is missing: {relativePath}");
            Assert.Equal(new FileInfo(path).Length, fixture.GetProperty("bytes").GetInt64());
            Assert.Equal(
                fixture.GetProperty("sha256").GetString(),
                Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))));
            Assert.Equal([10d, 10d, 10d], fixture.GetProperty("expectedBoundingBoxMm").EnumerateArray().Select(value => value.GetDouble()));
            Assert.Equal(1, fixture.GetProperty("expectedPartCount").GetInt32());
        }

        Assert.Equal(
            File.ReadAllBytes(Path.Combine(fixtureRoot, "valid", "cube-10mm.step")),
            File.ReadAllBytes(Path.Combine(fixtureRoot, "valid", "cube-10mm.stp")));
        Assert.Equal(
            File.ReadAllBytes(Path.Combine(fixtureRoot, "valid", "cube-10mm.iges")),
            File.ReadAllBytes(Path.Combine(fixtureRoot, "valid", "cube-10mm.igs")));
        Assert.Contains(
            "data:application/octet-stream;base64,",
            File.ReadAllText(Path.Combine(fixtureRoot, "valid", "cube-10mm.gltf")),
            StringComparison.Ordinal);
    }

    private static DirectoryInfo FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Web.slnx")))
        {
            directory = directory.Parent;
        }

        return directory ?? throw new InvalidOperationException("Could not locate repository root.");
    }
}
