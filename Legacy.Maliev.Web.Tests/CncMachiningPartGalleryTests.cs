using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Components.Layout;
using Microsoft.AspNetCore.Http;

namespace Legacy.Maliev.Web.Tests;

public sealed class CncMachiningPartGalleryTests
{
    private static readonly ExpectedTile[] ExpectedTiles =
    [
        ParseTile("""cnc-relief|Aluminum (CNC)/DSC_3567.JPG|part-bento-cnc-relief-block|(max-width: 767px) 100vw, (max-width: 1023px) 50vw, 50vw|1536|1024|false|Actual machined aluminum block with flowing relief channels and a semicircular notch|ชิ้นงานบล็อกอะลูมิเนียมกัดจริง มีร่องนูนลายไหลและบากครึ่งวงกลม|CNC · Aluminum|CNC · อะลูมิเนียม|Flowing relief channels and semicircular notch|ร่องนูนลายไหลและบากครึ่งวงกลม"""),
        ParseTile("""cnc-linkage|Aluminum (CNC)/DSC_3615.JPG|part-bento-cnc-arched-linkage|(max-width: 767px) 100vw, (max-width: 1023px) 50vw, 25vw|1536|1024|false|Actual arched machined aluminum linkage with multiple circular mounting features|ชิ้นงานลิงก์อะลูมิเนียมกัดทรงโค้งที่ผลิตจริง มีจุดยึดวงกลมหลายตำแหน่ง|CNC · Aluminum|CNC · อะลูมิเนียม|Arched profile and multiple mounting features|รูปทรงโค้งและจุดยึดหลายตำแหน่ง"""),
        ParseTile("""cnc-sprocket|Aluminum (CNC)/DSC_3619.JPG|part-bento-cnc-sprocket-plate|(max-width: 767px) 100vw, (max-width: 1023px) 50vw, 25vw|1536|1024|false|Actual circular machined aluminum plate with external teeth and large openings|เพลทอะลูมิเนียมกัดทรงกลมที่ผลิตจริง มีฟันรอบนอกและช่องเปิดขนาดใหญ่|CNC · Aluminum|CNC · อะลูมิเนียม|External teeth, curved openings, and mounting bores|ฟันรอบนอก ช่องเปิดโค้ง และรูยึด"""),
        ParseTile("""cnc-plate|Aluminum (CNC)/DSC_3867.JPG|part-bento-cnc-pocketed-plate|(max-width: 767px) 100vw, (max-width: 1023px) 50vw, 34vw|1536|1024|false|Actual compact machined aluminum plate with pockets, slots, and mounting holes|เพลทอะลูมิเนียมกัดขนาดกะทัดรัดที่ผลิตจริง มีโพรง ร่อง และรูยึด|CNC · Aluminum|CNC · อะลูมิเนียม|Pockets, slots, countersinks, and through holes|โพรง ร่อง รูบ่า และรูทะลุ"""),
        ParseTile("""cnc-controller|Aluminum (CNC)/DSC_3871.JPG|part-bento-cnc-controller-faceplate|(max-width: 767px) 100vw, (max-width: 1023px) 50vw, 42vw|1536|1024|false|Actual large machined aluminum faceplate with many curved openings and mounting holes|เฟซเพลทอะลูมิเนียมกัดขนาดใหญ่ที่ผลิตจริง มีช่องเปิดโค้งและรูยึดหลายตำแหน่ง|CNC · Aluminum|CNC · อะลูมิเนียม|Large faceplate with nested openings|เฟซเพลทขนาดใหญ่พร้อมช่องเปิดซ้อนกัน"""),
        ParseTile("""cnc-shaft|Aluminum (CNC)/DSC_3941.JPG|part-bento-cnc-turned-shaft|(max-width: 767px) 100vw, (max-width: 1023px) 50vw, 42vw|1536|1025|false|Actual turned aluminum shaft with stepped diameters and threads|เพลาอะลูมิเนียมกลึงที่ผลิตจริง มีบ่าต่างขนาดและเกลียว|CNC turning · Aluminum|CNC กลึง · อะลูมิเนียม|Stepped diameters and external threads|บ่าต่างขนาดและเกลียวนอก"""),
        ParseTile("""cnc-brass|Brass (CNC)/DSC_3810.JPG|part-bento-cnc-brass-relief|(max-width: 767px) 100vw, (max-width: 1023px) 50vw, 25vw|1536|1024|false|Actual circular CNC-carved brass relief with layered contours|ชิ้นงานทองเหลืองแกะสลัก CNC ทรงกลมที่ผลิตจริง มีรายละเอียดนูนหลายระดับ|CNC · Brass|CNC · ทองเหลือง|Layered relief and fine carved contours|รายละเอียดนูนและแนวแกะสลักละเอียด"""),
        ParseTile("""cnc-molds|Aluminum (CNC)/DSC_3952.JPG|part-bento-cnc-mold-blocks|(max-width: 767px) 100vw, (max-width: 1023px) 50vw, 25vw|1536|1024|false|Actual paired machined aluminum blocks with circular cavities and hole patterns|บล็อกอะลูมิเนียมกัดคู่ที่ผลิตจริง มีโพรงวงกลมและรูปแบบรู|CNC · Aluminum|CNC · อะลูมิเนียม|Paired blocks, circular cavities, and locating holes|บล็อกคู่ โพรงวงกลม และรูระบุตำแหน่ง"""),
        ParseTile("""cnc-cover|Aluminum (CNC)/DSC_3574.JPG|part-bento-cnc-oval-cover|(max-width: 767px) 100vw, (max-width: 1023px) 50vw, 25vw|1536|1024|true|Actual compact machined aluminum cover with an oval recess and four circular inserts|ฝาครอบอะลูมิเนียมกัดขนาดกะทัดรัดที่ผลิตจริง มีโพรงวงรีและวงกลมสี่ตำแหน่ง|CNC · Aluminum|CNC · อะลูมิเนียม|Oval recess and four circular features|โพรงวงรีและวงกลมสี่ตำแหน่ง"""),
        ParseTile("""cnc-folding|Aluminum (CNC)/DSC_3858.JPG|part-bento-cnc-folding-handle|(max-width: 767px) 100vw, (max-width: 1023px) 50vw, 42vw|1536|1024|true|Actual slim machined aluminum folding-handle assembly with pivot features|ชุดด้ามอะลูมิเนียมกัดทรงยาวที่ผลิตจริง มีจุดหมุน|CNC · Aluminum|CNC · อะลูมิเนียม|Slim profile, textured scale, and pivot features|รูปทรงยาว ผิวมีลาย และจุดหมุน"""),
        ParseTile("""cnc-scale|Aluminum (CNC)/DSC_3861.JPG|part-bento-cnc-anodized-handle-scale|(max-width: 767px) 100vw, (max-width: 1023px) 50vw, 42vw|1536|1024|true|Actual dark anodized machined aluminum handle scale with a flowing surface pattern|แผ่นด้ามอะลูมิเนียมกัดชุบสีเข้มที่ผลิตจริง มีลายพื้นผิวไหล|CNC · Anodized aluminum|CNC · อะลูมิเนียมชุบสี|Long contour, two holes, and flowing surface pattern|แนวยาว รูสองตำแหน่ง และลายพื้นผิวไหล"""),
        ParseTile("""cnc-housing|Aluminum (CNC)/DSC_3874.JPG|part-bento-cnc-upright-housing|(max-width: 767px) 100vw, (max-width: 1023px) 50vw, 25vw|1536|1024|true|Actual upright machined aluminum housing with vertical slots and a top elbow fitting|ตัวเรือนอะลูมิเนียมกัดตั้งตรงที่ผลิตจริง มีร่องแนวตั้งและข้อต่อข้อศอกด้านบน|CNC · Aluminum|CNC · อะลูมิเนียม|Upright housing, vertical slots, and top fitting|ตัวเรือนตั้งตรง ร่องแนวตั้ง และข้อต่อด้านบน"""),
        ParseTile("""cnc-billet|Aluminum (CNC)/DSC_3898.JPG|part-bento-cnc-cube-billet|(max-width: 767px) 100vw, (max-width: 1023px) 50vw, 25vw|1536|1024|true|Actual square machined aluminum billet with crisp planar faces|บิลเล็ตอะลูมิเนียมกัดทรงสี่เหลี่ยมที่ผลิตจริง มีผิวระนาบและขอบคม|CNC · Aluminum|CNC · อะลูมิเนียม|Planar faces and crisp square edges|ผิวระนาบและขอบสี่เหลี่ยมคม"""),
        ParseTile("""cnc-micro|Aluminum (CNC)/DSC_3900.JPG|part-bento-cnc-angular-micro-part|(max-width: 767px) 100vw, (max-width: 1023px) 50vw, 25vw|1536|1024|true|Actual compact angular machined aluminum component with a stepped profile and opening|ชิ้นส่วนอะลูมิเนียมกัดทรงเหลี่ยมขนาดกะทัดรัดที่ผลิตจริง มีบ่าและช่องเปิด|CNC · Aluminum|CNC · อะลูมิเนียม|Compact stepped profile and side opening|รูปทรงขั้นขนาดกะทัดรัดและช่องเปิดด้านข้าง"""),
        ParseTile("""cnc-rail|Aluminum (CNC)/DSC_3904.JPG|part-bento-cnc-slotted-rail-block|(max-width: 767px) 100vw, (max-width: 1023px) 50vw, 34vw|1536|1024|true|Actual small slotted machined aluminum rail block with parallel walls|บล็อกรางอะลูมิเนียมกัดขนาดเล็กที่ผลิตจริง มีร่องและผนังคู่ขนาน|CNC · Aluminum|CNC · อะลูมิเนียม|Parallel rails, central slot, and stepped base|รางคู่ขนาน ร่องกลาง และฐานแบบขั้น"""),
        ParseTile("""cnc-bracket|Aluminum (CNC)/DSC_3917.JPG|part-bento-cnc-upright-bracket|(max-width: 767px) 100vw, (max-width: 1023px) 50vw, 25vw|1536|1024|true|Actual upright machined aluminum bracket with a large bore and base mounting holes|ขายึดอะลูมิเนียมกัดตั้งตรงที่ผลิตจริง มีรูขนาดใหญ่และรูยึดที่ฐาน|CNC · Aluminum|CNC · อะลูมิเนียม|Upright plate, large bore, and base holes|เพลทตั้งตรง รูขนาดใหญ่ และรูยึดที่ฐาน"""),
        ParseTile("""cnc-disc|Aluminum (CNC)/DSC_3922.JPG|part-bento-cnc-machined-disc|(max-width: 767px) 100vw, (max-width: 1023px) 50vw, 25vw|1536|1024|true|Actual thin circular machined aluminum disc with a central bore and small holes|จานอะลูมิเนียมกัดทรงกลมบางที่ผลิตจริง มีรูกลางและรูขนาดเล็ก|CNC · Aluminum|CNC · อะลูมิเนียม|Thin circular profile, central bore, and small holes|รูปทรงกลมบาง รูกลาง และรูขนาดเล็ก"""),
        ParseTile("""cnc-dies|Aluminum (CNC)/DSC_3993.JPG|part-bento-cnc-die-blocks|(max-width: 767px) 100vw, (max-width: 1023px) 50vw, 42vw|1536|1024|true|Actual paired machined aluminum die blocks with engraved channels, bores, and pockets|บล็อกไดอะลูมิเนียมกัดคู่ที่ผลิตจริง มีร่องแกะ รู และโพรง|CNC · Aluminum|CNC · อะลูมิเนียม|Paired blocks with channels, bores, and pockets|บล็อกคู่พร้อมร่อง รู และโพรง"""),
        ParseTile("""cnc-stainless|Stainless Steel (CNC)/DSC_3855.JPG|part-bento-cnc-stainless-component|(max-width: 767px) 100vw, (max-width: 1023px) 50vw, 42vw|1536|1024|true|Actual polished stainless steel folding component with a curved hook, pivot, and recessed panel|ชิ้นส่วนสเตนเลสขัดเงาแบบพับได้ที่ผลิตจริง มีปลายโค้ง จุดหมุน และโพรงยาว|CNC · Stainless steel|CNC · สเตนเลส|Curved end, pivot feature, and recessed panel|ปลายโค้ง จุดหมุน และโพรงยาว"""),
    ];

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
        AssertTileFixtures(section);
        Assert.Equal("https://www.facebook.com/maliev.manufacturing/", SocialNetworks.Facebook);
        Assert.Equal("https://www.instagram.com/maliev.manufacturing/", SocialNetworks.Instagram);
        Assert.Equal(1, Count(section, "href=\"@Legacy.Maliev.Web.Application.SocialNetworks.Facebook\""));
        Assert.Equal(1, Count(section, "href=\"@Legacy.Maliev.Web.Application.SocialNetworks.Instagram\""));
        Assert.Equal(1, Count(section, "href=\"/Quotation?item=CNC-Machining\""));
        Assert.Equal(1, Count(section, "href=\"/Contact\""));
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

        var desktop = css[..css.IndexOf("@media (max-width: 63.99rem)", css.IndexOf("#cnc-part-proof", StringComparison.Ordinal), StringComparison.Ordinal)];
        var tablet = FindMediaBlockContaining(css, "@media (max-width: 63.99rem)", "#cnc-part-proof .service-part-bento--cnc-expanded");
        var mobile = FindMediaBlockContaining(css, "@media (max-width: 47.99rem)", "#cnc-part-proof .service-part-bento--cnc-expanded");
        var narrow = FindMediaBlockContaining(css, "@media (max-width: 359px)", "#cnc-part-proof .service-part-bento--cnc-expanded");

        Assert.Contains("#cnc-part-proof .service-part-bento--cnc { grid-template-columns: repeat(14, minmax(0, 1fr))", desktop, StringComparison.Ordinal);
        Assert.Contains("#cnc-part-proof .service-part-bento--cnc-expanded { grid-template-rows: repeat(6", desktop, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns: repeat(12, minmax(0, 1fr))", tablet, StringComparison.Ordinal);
        Assert.Contains("#cnc-part-proof .service-part-bento--cnc-expanded", tablet, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns: repeat(3, minmax(0, 1fr))", mobile, StringComparison.Ordinal);
        Assert.Contains("#cnc-part-proof .service-part-bento--cnc { grid-template-rows: repeat(29", mobile, StringComparison.Ordinal);
        Assert.Contains("#cnc-part-proof .service-part-bento--cnc-expanded { grid-template-rows: repeat(20", mobile, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns: repeat(2, minmax(0, 1fr))", narrow, StringComparison.Ordinal);
        Assert.Contains("#cnc-part-proof .service-part-bento--cnc { grid-template-rows: repeat(21", narrow, StringComparison.Ordinal);
        Assert.Contains("#cnc-part-proof .service-part-bento--cnc-expanded { grid-template-rows: repeat(24", narrow, StringComparison.Ordinal);
        Assert.Contains(".service-part-bento-tile--cnc-stainless { grid-area: cncstainless; }", css, StringComparison.Ordinal);
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

    private static void AssertTileFixtures(string section)
    {
        var figures = Regex.Matches(section, "<figure class=\"service-part-bento-tile service-part-bento-tile--(?<class>cnc-[^\"]+)\"(?<body>.*?)</figure>", RegexOptions.Singleline);
        Assert.Equal(ExpectedTiles.Length, figures.Count);

        for (var index = 0; index < ExpectedTiles.Length; index++)
        {
            var expected = ExpectedTiles[index];
            var markup = figures[index].Value;
            var translations = Regex.Matches(markup, "@T\\(\"(?<en>[^\"]*)\", \"(?<th>[^\"]*)\"\\)");
            Assert.Equal(3, translations.Count);

            var actual = new ExpectedTile(
                figures[index].Groups["class"].Value,
                Attribute(markup, "data-source-photo"),
                Regex.Match(markup, "(?:data-src|src)=\"/src/images/services/cnc/(?<asset>part-bento-[^\"]+)\\.webp\"").Groups["asset"].Value,
                Attribute(markup, "sizes"),
                int.Parse(Attribute(markup, "width"), System.Globalization.CultureInfo.InvariantCulture),
                int.Parse(Attribute(markup, "height"), System.Globalization.CultureInfo.InvariantCulture),
                markup.Contains("data-part-gallery-extra", StringComparison.Ordinal),
                translations[0].Groups["en"].Value,
                translations[0].Groups["th"].Value,
                translations[1].Groups["en"].Value,
                translations[1].Groups["th"].Value,
                translations[2].Groups["en"].Value,
                translations[2].Groups["th"].Value);

            Assert.Equal(expected, actual);
            Assert.Equal("lazy", Attribute(markup, "loading"));
            Assert.Equal("async", Attribute(markup, "decoding"));
            var imagePath = $"/src/images/services/cnc/{expected.AssetStem}";
            var expectedSrcSet = $"{imagePath}-640.webp 640w, {imagePath}-1024.webp 1024w, {imagePath}.webp 1536w";
            if (expected.Deferred)
            {
                Assert.Equal("data:image/webp;base64,UklGRhoAAABXRUJQVlA4TA0AAAAvAAAAEAcQERGIiP4HAA==", Attribute(markup, "src"));
                Assert.Equal($"{imagePath}.webp", Attribute(markup, "data-src"));
                Assert.Equal(expectedSrcSet, Attribute(markup, "data-srcset"));
            }
            else
            {
                Assert.Equal($"{imagePath}.webp", Attribute(markup, "src"));
                Assert.Equal(expectedSrcSet, Attribute(markup, "srcset"));
                Assert.DoesNotContain("data-src=", markup, StringComparison.Ordinal);
            }
        }
    }

    private static string Attribute(string markup, string name)
    {
        var match = Regex.Match(markup, $"(?:^|\\s){Regex.Escape(name)}=\"(?<value>[^\"]*)\"");
        Assert.True(match.Success, $"Missing {name} attribute.");
        return match.Groups["value"].Value;
    }

    private static ExpectedTile ParseTile(string fixture)
    {
        var fields = fixture.Split('|');
        if (fields.Length != 13)
        {
            throw new InvalidDataException($"Expected 13 fixture fields, found {fields.Length}.");
        }

        return new ExpectedTile(
            fields[0], fields[1], fields[2], fields[3],
            int.Parse(fields[4], System.Globalization.CultureInfo.InvariantCulture),
            int.Parse(fields[5], System.Globalization.CultureInfo.InvariantCulture),
            bool.Parse(fields[6]), fields[7], fields[8], fields[9], fields[10], fields[11], fields[12]);
    }

    private static string FindMediaBlockContaining(string css, string mediaQuery, string requiredSelector)
    {
        var searchFrom = 0;
        while ((searchFrom = css.IndexOf(mediaQuery, searchFrom, StringComparison.Ordinal)) >= 0)
        {
            var open = css.IndexOf('{', searchFrom);
            var depth = 0;
            for (var index = open; index < css.Length; index++)
            {
                depth += css[index] == '{' ? 1 : css[index] == '}' ? -1 : 0;
                if (depth == 0)
                {
                    var block = css[searchFrom..(index + 1)];
                    if (block.Contains(requiredSelector, StringComparison.Ordinal))
                    {
                        return block;
                    }

                    searchFrom = index + 1;
                    break;
                }
            }
        }

        throw new Xunit.Sdk.XunitException($"No {mediaQuery} block contains {requiredSelector}.");
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

    private sealed record ExpectedTile(
        string CssClass,
        string SourcePhoto,
        string AssetStem,
        string Sizes,
        int Width,
        int Height,
        bool Deferred,
        string AltEnglish,
        string AltThai,
        string TitleEnglish,
        string TitleThai,
        string CaptionEnglish,
        string CaptionThai);
}
