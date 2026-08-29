using System.Security.Cryptography;
using System.Text;
using Legacy.Maliev.Web.Components.Layout;
using Microsoft.AspNetCore.Http;

namespace Legacy.Maliev.Web.Tests;

public sealed class CncMachiningPartGalleryTests
{
    private static readonly string[] ExpectedAssetStems =
    [
        "part-bento-cnc-relief-block",
        "part-bento-cnc-arched-linkage",
        "part-bento-cnc-sprocket-plate",
        "part-bento-cnc-pocketed-plate",
        "part-bento-cnc-controller-faceplate",
        "part-bento-cnc-turned-shaft",
        "part-bento-cnc-brass-relief",
        "part-bento-cnc-mold-blocks",
        "part-bento-cnc-oval-cover",
        "part-bento-cnc-folding-handle",
        "part-bento-cnc-anodized-handle-scale",
        "part-bento-cnc-upright-housing",
        "part-bento-cnc-cube-billet",
        "part-bento-cnc-angular-micro-part",
        "part-bento-cnc-slotted-rail-block",
        "part-bento-cnc-upright-bracket",
        "part-bento-cnc-die-blocks",
        "part-bento-cnc-machined-disc",
        "part-bento-cnc-stainless-component",
    ];

    [Fact]
    public void CncPartGallery_PreservesProductionPhotoOrderAndAccessibleExpansion()
    {
        var content = ReadRepositoryFile("Legacy.Maliev.Web", "Components", "Pages", "Services", "CncMachiningContent.razor");
        var section = Slice(content, "id=\"cnc-part-proof\"", "id=\"cnc-process\"");

        Assert.True(content.IndexOf("id=\"cnc-capabilities\"", StringComparison.Ordinal) < content.IndexOf("id=\"cnc-part-proof\"", StringComparison.Ordinal));
        Assert.True(content.IndexOf("id=\"cnc-part-proof\"", StringComparison.Ordinal) < content.IndexOf("id=\"cnc-process\"", StringComparison.Ordinal));
        Assert.Equal(19, Count(section, "class=\"service-part-bento-tile"));
        Assert.Equal(19, Count(section, "tabindex=\"0\""));
        Assert.Equal(19, Count(section, "class=\"service-part-bento-overlay\""));
        Assert.Equal(19, Count(section, "data-part-proof=\"source-derived\""));
        Assert.Equal(11, Count(section, "data-part-gallery-extra"));
        Assert.Equal(11, Count(section, "data-src=\"/src/images/services/cnc/part-bento-"));
        Assert.Equal(11, Count(section, "data-srcset=\"/src/images/services/cnc/part-bento-"));
        Assert.Contains("id=\"cnc-part-gallery-extra\"", section, StringComparison.Ordinal);
        Assert.Contains("data-part-gallery-toggle", section, StringComparison.Ordinal);
        Assert.Contains("aria-controls=\"cnc-part-gallery-extra\"", section, StringComparison.Ordinal);
        Assert.Contains("aria-expanded=\"false\"", section, StringComparison.Ordinal);
        Assert.Contains("service-part-bento-cta", section, StringComparison.Ordinal);
        Assert.Contains("Actual CNC parts produced by MALIEV", section, StringComparison.Ordinal);
        Assert.Contains("ตัวอย่างชิ้นงาน CNC จริงจากการผลิตของ MALIEV", section, StringComparison.Ordinal);
        Assert.DoesNotContain("AI-enhanced", section, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ภาพนำเสนอที่ปรับด้วย AI", section, StringComparison.Ordinal);
        AssertCaptionsExclude(section, "DSC_");
        AssertCaptionsExclude(section, "AI");
    }

    [Fact]
    public void CncPartGallery_UsesFinalDesktopTabletMobileAndReducedMotionContracts()
    {
        var css = ReadRepositoryFile("Legacy.Maliev.Web", "wwwroot", "src", "app", "css", "service-pages.css");
        var motionCss = ReadRepositoryFile("Legacy.Maliev.Web", "wwwroot", "src", "app", "css", "motion.css");
        var motionScript = ReadRepositoryFile("Legacy.Maliev.Web", "wwwroot", "src", "app", "js", "motion.js");

        Assert.Contains("#cnc-part-proof .service-part-bento--cnc { grid-template-columns: repeat(14, minmax(0, 1fr))", css, StringComparison.Ordinal);
        Assert.Contains("#cnc-part-proof .service-part-bento--cnc-expanded", css, StringComparison.Ordinal);
        Assert.Contains("\"cncrelief cncrelief cncrelief\"", css, StringComparison.Ordinal);
        Assert.Contains("\"cncfolding cncfolding\"", css, StringComparison.Ordinal);
        Assert.Contains(".service-part-bento-tile--cnc-stainless { grid-area: cncstainless; }", css, StringComparison.Ordinal);
        Assert.Contains("@media (max-width: 47.99rem)", css, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns: repeat(3, minmax(0, 1fr))", css, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns: repeat(2, minmax(0, 1fr))", css, StringComparison.Ordinal);
        Assert.Contains(".service-part-bento-tile:hover .service-part-bento-overlay", css, StringComparison.Ordinal);
        Assert.Contains(".service-part-bento-tile:focus-visible", css, StringComparison.Ordinal);
        Assert.Contains("@media (prefers-reduced-motion: reduce)", css, StringComparison.Ordinal);
        Assert.Contains(".service-part-bento > figure", motionScript, StringComparison.Ordinal);
        Assert.Contains("[data-motion=\"ready\"] .service-part-bento > figure", motionCss, StringComparison.Ordinal);
    }

    [Fact]
    public void CncPartGallery_AssetsMatchTheCommittedSourceDerivedManifest()
    {
        var directory = RepositoryPath("Legacy.Maliev.Web", "wwwroot", "src", "images", "services", "cnc");
        var expectedNames = ExpectedAssetStems
            .SelectMany(stem => new[] { $"{stem}.webp", $"{stem}-1024.webp", $"{stem}-640.webp" })
            .Order(StringComparer.Ordinal)
            .ToArray();
        var actualNames = Directory.GetFiles(directory, "part-bento-*.webp")
            .Select(Path.GetFileName)
            .OfType<string>()
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expectedNames, actualNames);
        Assert.All(actualNames, name => Assert.True(new FileInfo(Path.Combine(directory, name)).Length > 3_000, $"Bento asset is unexpectedly small: {name}"));

        var manifest = string.Join('\n', actualNames.Select(name =>
            $"{name}:{Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(directory, name)))).ToLowerInvariant()}"));
        var manifestHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(manifest))).ToLowerInvariant();
        Assert.Equal("abd40e218f5e68d6eccc2162a400a48ab36fe2065ceae84842f6a3fb5f4ed687", manifestHash);
    }

    [Fact]
    public void CncRoute_OwnsTheGalleryModuleWithoutLoadingPrintingOnlyFeatures()
    {
        var routeEntry = ReadRepositoryFile("Legacy.Maliev.Web", "assets", "route-service-cnc.js");

        Assert.Equal(["route-service-cnc.js"], PublicRouteAssets.GetScripts(new PathString("/services/cnc-machining")));
        Assert.Contains("service-toc.js", routeEntry, StringComparison.Ordinal);
        Assert.Contains("service-part-gallery.js", routeEntry, StringComparison.Ordinal);
        Assert.DoesNotContain("material-comparison.js", routeEntry, StringComparison.Ordinal);
    }

    private static void AssertCaptionsExclude(string source, string forbiddenValue)
    {
        var searchFrom = 0;
        while ((searchFrom = source.IndexOf("<figcaption", searchFrom, StringComparison.Ordinal)) >= 0)
        {
            var close = source.IndexOf("</figcaption>", searchFrom, StringComparison.Ordinal);
            Assert.True(close > searchFrom, "A bento figcaption is not closed.");
            Assert.DoesNotContain(forbiddenValue, source[searchFrom..close], StringComparison.Ordinal);
            searchFrom = close + "</figcaption>".Length;
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
