using System.Xml.Linq;

namespace Legacy.Maliev.Web.Tests;

public sealed class ThaiLocalizationQualityTests
{
    private static readonly string[] ForbiddenLegacySpellings =
    [
        "อีเมล์",
        "อัพโหลด",
        "ตรวจเช็ค",
        "โปรเจ็ค",
        "ต่างๆ",
        "social media",
        "ปริ้น",
        "แลกเปลี่ยม",
        "ลิงค์"
    ];

    [Fact]
    public void ThaiResourceFiles_AreWellFormedAndUseApprovedCustomerFacingSpelling()
    {
        var resources = Path.Combine(FindRepositoryRoot(), "Legacy.Maliev.Web", "Resources");
        var files = Directory
            .EnumerateFiles(resources, "*.th.resx", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.NotEmpty(files);

        foreach (var file in files)
        {
            var document = XDocument.Load(file, LoadOptions.PreserveWhitespace);
            var entries = document.Root?.Elements("data").ToArray() ?? [];
            Assert.NotEmpty(entries);

            var duplicates = entries
                .GroupBy(entry => (string?)entry.Attribute("name"), StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToArray();
            Assert.True(duplicates.Length == 0, $"Duplicate resource keys in {file}: {string.Join(", ", duplicates)}");

            foreach (var value in entries.Select(entry => entry.Element("value")?.Value ?? string.Empty))
            {
                Assert.DoesNotContain('\uFFFD', value);
                foreach (var forbidden in ForbiddenLegacySpellings)
                {
                    Assert.DoesNotContain(forbidden, value, StringComparison.Ordinal);
                }
            }
        }
    }

    [Fact]
    public void HomeCopy_UsesTheReviewedEnglishAndThaiWording()
    {
        var resources = Path.Combine(FindRepositoryRoot(), "Legacy.Maliev.Web", "Resources", "Components", "Pages", "Home");
        var english = ReadValues(Path.Combine(resources, "HomeContent.resx"));
        var thai = ReadValues(Path.Combine(resources, "HomeContent.th.resx"));

        Assert.Equal("Once you're satisfied with our offer, we'll start production.", english["Once you're satisfied with our offer. We start the production."]);
        Assert.Equal("You can submit your request by contacting us directly or using our upload portal. These can be CAD files or other documents.", english["You submit your request by contact us directly or using our upload portal. This can be CAD file or plain documents"]);
        Assert.Equal("3. ตรวจสอบใบเสนอราคา", thai["3. Review the quotation"]);
        Assert.Equal("ขั้นตอนการสั่งงานกับเราเป็นอย่างไร?", thai["How does it work?"]);
        Assert.Equal("มีไอเดียที่ต้องการผู้ช่วยผลิตหรือไม่?", thai["Have an ideas which need someone to help manufacture it?"]);
        Assert.Equal("วิศวกรของเราจะตรวจสอบงานและจัดทำใบเสนอราคาให้คุณพิจารณา", thai["Your request is checked by our engineer and the quotation offer is generated for you to examine and review"]);
    }

    [Fact]
    public void ThaiResources_MatchEnglishSiblingKeysWhenBothResourcesExist()
    {
        var resources = Path.Combine(FindRepositoryRoot(), "Legacy.Maliev.Web", "Resources");
        var englishFiles = Directory
            .EnumerateFiles(resources, "*.resx", SearchOption.AllDirectories)
            .Where(path => !path.EndsWith(".th.resx", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));

        foreach (var englishPath in englishFiles)
        {
            var thaiPath = Path.Combine(
                Path.GetDirectoryName(englishPath)!,
                $"{Path.GetFileNameWithoutExtension(englishPath)}.th.resx");
            if (!File.Exists(thaiPath))
            {
                continue;
            }

            var englishKeys = ReadValues(englishPath).Keys.Order().ToArray();
            var thaiKeys = ReadValues(thaiPath).Keys.Order().ToArray();
            Assert.Equal(englishKeys, thaiKeys);
        }
    }

    private static Dictionary<string, string> ReadValues(string path)
    {
        return XDocument.Load(path)
            .Root!
            .Elements("data")
            .ToDictionary(
                entry => (string)entry.Attribute("name")!,
                entry => entry.Element("value")?.Value ?? string.Empty,
                StringComparer.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Web.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the Legacy.Maliev.Web repository root.");
    }
}
