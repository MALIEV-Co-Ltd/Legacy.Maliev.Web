using System.Security.Cryptography;
using System.Text;

namespace Legacy.Maliev.Web.Tests;

public sealed class ThreeDimensionalPrintingPartGalleryTests
{
    private static readonly string[] ExpectedAssetStems =
    [
        "part-bento-sls-pa11-impeller-wide",
        "part-bento-pc-esd-threaded-component",
        "part-bento-mjf-pa12-linkage",
        "part-bento-petg-cat",
        "part-bento-resin-component",
        "part-bento-tpu-sandal",
        "part-bento-pa-cf-wheel",
        "part-bento-pa6-detail",
        "part-bento-abs-fan",
        "part-bento-asa-bracket",
        "part-bento-bluecast-lattice-ring",
        "part-bento-castable-wax-dragon",
        "part-bento-hips-flanged-part",
        "part-bento-pla-character",
        "part-bento-pla-cf-bracket",
        "part-bento-rubber-resin-pulley",
        "part-bento-abs-hips-lattice-cube",
        "part-bento-petg-shell",
        "part-bento-resin-frame",
        "part-bento-tpu-handle",
    ];

    [Fact]
    public void PrintingPartGallery_PreservesSourceProvenanceOrderAndAccessibleExpansion()
    {
        var content = ReadRepositoryFile("Legacy.Maliev.Web", "Components", "Pages", "Services", "ThreeDimensionalPrintingContent.razor");
        var section = Slice(content, "id=\"printing-part-proof\"", "id=\"printing-tolerances\"");

        Assert.True(content.IndexOf("id=\"printing-engineering-review\"", StringComparison.Ordinal) < content.IndexOf("id=\"printing-part-proof\"", StringComparison.Ordinal));
        Assert.True(content.IndexOf("id=\"printing-part-proof\"", StringComparison.Ordinal) < content.IndexOf("id=\"printing-tolerances\"", StringComparison.Ordinal));
        Assert.Contains("class=\"service-hero-proof-link\" href=\"#printing-part-proof\"", content, StringComparison.Ordinal);
        Assert.Contains("See actual MALIEV-produced parts", content, StringComparison.Ordinal);
        Assert.Contains("ดูตัวอย่างชิ้นงานผลิตจริงจาก MALIEV", content, StringComparison.Ordinal);
        Assert.Contains("data-source-photo=\"DSC_3824.JPG\"", section, StringComparison.Ordinal);
        Assert.Contains("data-source-photo=\"DSC_3934.JPG\"", section, StringComparison.Ordinal);
        Assert.Contains("data-source-photo=\"DSC_3637.JPG\"", section, StringComparison.Ordinal);
        Assert.Contains("data-source-photo=\"DSC_3391.JPG\"", section, StringComparison.Ordinal);
        Assert.Contains("data-source-photo=\"DSC_3424.JPG\"", section, StringComparison.Ordinal);
        Assert.Contains("data-source-photo=\"DSC_3652.JPG\"", section, StringComparison.Ordinal);
        Assert.Contains("data-source-photo=\"DSC_3775.JPG\"", section, StringComparison.Ordinal);
        Assert.Contains("data-source-photo=\"DSC_3757.JPG\"", section, StringComparison.Ordinal);
        Assert.Equal(20, Count(section, "class=\"service-part-bento-tile"));
        Assert.Equal(20, Count(section, "tabindex=\"0\""));
        Assert.Equal(20, Count(section, "class=\"service-part-bento-overlay\""));
        Assert.Equal(20, Count(section, "data-part-proof=\"source-derived\""));
        Assert.Equal(12, Count(section, "data-part-gallery-extra"));
        Assert.Equal(12, Count(section, "data-src=\"/src/images/services/printing/part-bento-"));
        Assert.Equal(12, Count(section, "data-srcset=\"/src/images/services/printing/part-bento-"));
        Assert.Contains("id=\"printing-part-gallery-extra\"", section, StringComparison.Ordinal);
        Assert.Contains("data-part-gallery-toggle", section, StringComparison.Ordinal);
        Assert.Contains("aria-controls=\"printing-part-gallery-extra\"", section, StringComparison.Ordinal);
        Assert.Contains("aria-expanded=\"false\"", section, StringComparison.Ordinal);
        Assert.Contains("Legacy.Maliev.Web.Application.SocialNetworks.Facebook", section, StringComparison.Ordinal);
        Assert.Contains("Legacy.Maliev.Web.Application.SocialNetworks.Instagram", section, StringComparison.Ordinal);
        Assert.Contains("target=\"_blank\" rel=\"noopener\"", section, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Facebook\"", section, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Instagram\"", section, StringComparison.Ordinal);
        Assert.DoesNotContain("part-bento-sls-pa11-impeller-detail", section, StringComparison.Ordinal);
        Assert.DoesNotContain("part-bento-sls-pa11-ai-review", section, StringComparison.Ordinal);
    }

    [Fact]
    public void PrintingPartGallery_UsesFinalResponsiveAndReducedMotionContracts()
    {
        var content = ReadRepositoryFile("Legacy.Maliev.Web", "Components", "Pages", "Services", "ThreeDimensionalPrintingContent.razor");
        var css = ReadRepositoryFile("Legacy.Maliev.Web", "wwwroot", "src", "app", "css", "service-pages.css");
        var motionCss = ReadRepositoryFile("Legacy.Maliev.Web", "wwwroot", "src", "app", "css", "motion.css");
        var motionScript = ReadRepositoryFile("Legacy.Maliev.Web", "wwwroot", "src", "app", "js", "motion.js");

        Assert.Contains(".service-part-bento {", css, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns: repeat(12, minmax(0, 1fr))", css, StringComparison.Ordinal);
        Assert.Contains("\"hero hero hero hero hero hero petg petg petg pacf pacf pacf\"", css, StringComparison.Ordinal);
        Assert.Contains("\"abs abs abs abs abs abs abs abs bluecast bluecast bluecast bluecast\"", css, StringComparison.Ordinal);
        Assert.Contains("\"hips hips hips hips hips pla pla pla abshipscube abshipscube abshipscube abshipscube\"", css, StringComparison.Ordinal);
        Assert.Contains("\"rubber rubber rubber petgshell petgshell petgshell resinframe resinframe resinframe tpuhandle tpuhandle tpuhandle\"", css, StringComparison.Ordinal);
        Assert.Contains("\"abs abs bluecast\"", css, StringComparison.Ordinal);
        Assert.Contains("\"abs abs\" \"abs abs\" \"abs abs\"", css, StringComparison.Ordinal);
        Assert.Contains("srcset=\"/src/images/services/printing/part-bento-petg-cat-640.webp 640w, /src/images/services/printing/part-bento-petg-cat-1024.webp 1024w", content, StringComparison.Ordinal);
        Assert.Contains("srcset=\"/src/images/services/printing/part-bento-resin-component-640.webp 640w, /src/images/services/printing/part-bento-resin-component-1024.webp 1024w", content, StringComparison.Ordinal);
        Assert.Contains(".service-part-bento-tile:hover .service-part-bento-overlay", css, StringComparison.Ordinal);
        Assert.Contains(".service-part-bento-tile:focus .service-part-bento-overlay", css, StringComparison.Ordinal);
        Assert.Contains(".service-part-bento-tile:focus-visible", css, StringComparison.Ordinal);
        Assert.Contains("@media (prefers-reduced-motion: reduce)", css, StringComparison.Ordinal);
        Assert.Contains(".service-part-bento-tile .service-part-bento-media", css, StringComparison.Ordinal);
        Assert.Contains(".service-part-bento > figure", motionScript, StringComparison.Ordinal);
        Assert.Contains("[data-motion=\"ready\"] .service-part-bento > figure", motionCss, StringComparison.Ordinal);
        Assert.Contains(
            "data-src=\"/src/images/services/printing/part-bento-abs-fan.webp\" data-srcset=\"/src/images/services/printing/part-bento-abs-fan-640.webp 640w, /src/images/services/printing/part-bento-abs-fan-1024.webp 1024w, /src/images/services/printing/part-bento-abs-fan.webp 1536w\" sizes=\"(max-width: 767px) 100vw, (max-width: 1023px) 50vw, 64vw\"",
            content,
            StringComparison.Ordinal);
    }

    [Fact]
    public void PrintingPartGallery_AssetsMatchTheCommittedSourceDerivedManifest()
    {
        var directory = RepositoryPath("Legacy.Maliev.Web", "wwwroot", "src", "images", "services", "printing");
        var historicalAssets = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["part-bento-sls-pa11-ai-review-1024.webp"] = "5bd607db1b4d310868854af88a8837d7960f4c1c",
            ["part-bento-sls-pa11-ai-review-640.webp"] = "b92968af358e05a74927f5f631e819ab27eadd72",
            ["part-bento-sls-pa11-ai-review.webp"] = "c03d0ebc0a8a46ac3d967cdbd9e46cde779b97aa",
            ["part-bento-sls-pa11-impeller-detail-1024.webp"] = "c2b5bf01ba2d507b78d65e17f679f3e12d700e20",
            ["part-bento-sls-pa11-impeller-detail-640.webp"] = "5dda76749ad2a22b0cb78dc7f9a1bead28d31d2d",
            ["part-bento-sls-pa11-impeller-detail.webp"] = "42bf5834884ae940b0dfc452ea38608b6e20830a",
        };
        var expectedNames = ExpectedAssetStems
            .SelectMany(stem => new[] { $"{stem}.webp", $"{stem}-1024.webp", $"{stem}-640.webp" })
            .Order(StringComparer.Ordinal)
            .ToArray();
        var actualNames = Directory.GetFiles(directory, "part-bento-*.webp")
            .Select(Path.GetFileName)
            .OfType<string>()
            .Where(name => !historicalAssets.ContainsKey(name))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expectedNames, actualNames);
        Assert.All(actualNames, name => Assert.True(new FileInfo(Path.Combine(directory, name)).Length > 3_000, $"Bento asset is unexpectedly small: {name}"));
        Assert.All(actualNames.Where(name => name is "part-bento-petg-cat.webp" or "part-bento-resin-component.webp"),
            name => Assert.True(new FileInfo(Path.Combine(directory, name)).Length > 10000, $"Source process asset is unexpectedly small: {name}"));

        var manifest = string.Join('\n', actualNames.Select(name =>
            $"{name}:{Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(directory, name)))).ToLowerInvariant()}"));
        var manifestHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(manifest))).ToLowerInvariant();
        Assert.Equal("475997ee53e875dcb174a35b00b440535d0a1160e907df96d7fa902dae405370", manifestHash);

        foreach (var (name, sourceBlob) in historicalAssets)
        {
            var path = Path.Combine(directory, name);
            Assert.True(File.Exists(path), $"Missing retained source asset: {name}");
            var contents = File.ReadAllBytes(path);
            Assert.True(contents.Length > 3_000, $"Retained source asset is unexpectedly small: {name}");
            var header = Encoding.ASCII.GetBytes($"blob {contents.Length}\0");
            var input = new byte[header.Length + contents.Length];
            Buffer.BlockCopy(header, 0, input, 0, header.Length);
            Buffer.BlockCopy(contents, 0, input, header.Length, contents.Length);
            Assert.Equal(sourceBlob, Convert.ToHexStringLower(SHA1.HashData(input)));
        }
    }

    private static string ReadRepositoryFile(params string[] parts) => File.ReadAllText(RepositoryPath(parts));

    private static string RepositoryPath(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Web.slnx")))
        {
            directory = directory.Parent;
        }

        return Path.Combine(directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found."), Path.Combine(parts));
    }

    private static string Slice(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing start marker {startMarker}.");
        Assert.True(end > start, $"Missing end marker {endMarker}.");
        return source[start..end];
    }

    private static int Count(string source, string value) => source.Split(value, StringSplitOptions.None).Length - 1;
}
