using System.Text.RegularExpressions;
using System.Security.Cryptography;

namespace Legacy.Maliev.Web.Tests;

public sealed partial class PublicWebpImageContractTests
{
    [Fact]
    public void SourceDerivedWebpAssets_MatchCommit6509356()
    {
        var expected = new Dictionary<string, (string Sha256, long Bytes)>(StringComparer.Ordinal)
        {
            ["apple-touch-icon.webp"] = ("0f4444eb5c23dcaae9f9c99ec4eb6b102fb8e07d979d61b7a01a77d92783552e", 3762),
            ["favicon-16x16.webp"] = ("d3bb9eddb84df6c8014e335e317399bdb80956844563e62c36d5e09a692948ea", 252),
            ["favicon-32x32.webp"] = ("59366423e121f91259ac8099ffb71839f4fe551ab1b800bf7fa1d98611726e2f", 612),
            ["favicon.webp"] = ("4873940545d64653135bcbf081bc8ed0ea773c618b75a7f9c63c8207ce74a936", 13516),
            ["src/images/account-forget-password.webp"] = ("391a5f400b411047f72e73bd6bc11e6fb1f7d9d1818418f49cdc37c4c4d59f51", 473786),
            ["src/images/account-signin.webp"] = ("6176aae040747d50df0f041a134fa55f837655d72f97d1764f3ef9b102046e13", 342852),
            ["src/images/account-signup.webp"] = ("0f5258766cd4af63c6dcac7c8ac7d245d2e708567b9f60f57ed721b139cc6db2", 76902),
            ["src/images/career-3dprinting.webp"] = ("3e42ddefbe2bff8fddbb8034aa59ea3de785ac68fdc524b19c73b4bc5f87b90f", 213822),
            ["src/images/Flags/sprite-flags.webp"] = ("db79ee0c2fc81438881a55381c09592f1be4904aec1b8e54677a373a502f885d", 780),
            ["src/images/f-ogo_RGB_HEX-58.webp"] = ("814f12d52cb054756af99429ecf478c8e1488be650a70a3e8456b56f61237fec", 234),
            ["src/images/homepage-cover-1.webp"] = ("bf3df298ff398b9a0ed82f729820dc02774da89bbb65b427c9a5c6bf12e69341", 72548),
            ["src/images/navbar_logo_black.webp"] = ("57185bb19cd458ab670b1e347dda9e3cec4c2dc6b1a7cc1f3ecfb40e429d0236", 3430),
            ["src/images/Payments/sprite-payments.webp"] = ("8453081f6794634f1330cbbb1a3dd95bedc092e02d8957868ac8f71f9645807b", 4172),
            ["src/images/ShippingCouriers/sprite-shippingcouriers.webp"] = ("aa6b8ebc2ea4f853c16767c81ed3fd05b282df0bf43f17ad2cc52557b896e1e5", 2260),
            ["src/images/services/injection-molding/injection-service-hero.webp"] = ("919c8ba58a49680f0bc04854f034cc283f6170ecd08d1e56b61362004d0766eb", 65768),
        };

        string webRoot = Path.Combine(FindRepositoryRoot(), "Legacy.Maliev.Web", "wwwroot");
        foreach ((string relativePath, (string sha256, long bytes)) in expected)
        {
            string path = Path.Combine(webRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), $"Missing source-derived WebP asset: {relativePath}");
            Assert.Equal(bytes, new FileInfo(path).Length);
            Assert.Equal(sha256, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant());
        }
    }

    [Fact]
    public void PublicRuntimeSources_ReferenceOnlyWebpRasterImages()
    {
        string root = FindRepositoryRoot();
        string webRoot = Path.Combine(root, "Legacy.Maliev.Web");
        string[] sourceRoots =
        [
            Path.Combine(webRoot, "Components"),
            Path.Combine(webRoot, "Pages"),
            Path.Combine(webRoot, "wwwroot", "src", "app"),
        ];

        string[] sourceFiles = sourceRoots
            .SelectMany(path => Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            .Where(path => Path.GetExtension(path) is ".razor" or ".cshtml" or ".cs" or ".css" or ".js")
            .Append(Path.Combine(webRoot, "Components", "App.razor"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        List<string> invalidReferences = [];
        foreach (string sourceFile in sourceFiles)
        {
            string source = File.ReadAllText(sourceFile);
            invalidReferences.AddRange(
                LegacyRasterReference().Matches(source)
                    .Select(match => $"{Path.GetRelativePath(root, sourceFile)}: {match.Value}"));
        }

        Assert.True(
            invalidReferences.Count == 0,
            "Public runtime sources still reference non-WebP raster images:" +
            Environment.NewLine +
            string.Join(Environment.NewLine, invalidReferences));
    }

    [Fact]
    public void PublicWebpReferences_ResolveToCommittedAssets()
    {
        string root = FindRepositoryRoot();
        string webRoot = Path.Combine(root, "Legacy.Maliev.Web");
        string[] sourceFiles = Directory
            .EnumerateFiles(webRoot, "*", SearchOption.AllDirectories)
            .Where(path =>
                !path.Contains($"{Path.DirectorySeparatorChar}dist{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                !path.Contains($"{Path.DirectorySeparatorChar}lib{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                Path.GetExtension(path) is ".razor" or ".cshtml" or ".cs" or ".css" or ".js")
            .ToArray();

        string[] missing = sourceFiles
            .SelectMany(path => WebpReference().Matches(File.ReadAllText(path)).Select(match => match.Groups["path"].Value))
            .Select(path => path.TrimStart('~', '/').Replace('/', Path.DirectorySeparatorChar))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(path => !File.Exists(Path.Combine(webRoot, "wwwroot", path)))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.True(missing.Length == 0, "Missing referenced WebP assets: " + string.Join(", ", missing));
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Web.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }

    [GeneratedRegex("""(?i)(?:~?/|url\(['"]?/?)(?:src/images/[^'"\)\s?#]+|favicon(?:-\d+x\d+)?|apple-touch-icon)\.(?:png|jpe?g|gif|ico)""")]
    private static partial Regex LegacyRasterReference();

    [GeneratedRegex("""(?i)(?<path>~?/(?:src/images/[^'"\)\s?#]+|favicon(?:-\d+x\d+)?|apple-touch-icon)\.webp)""")]
    private static partial Regex WebpReference();
}
