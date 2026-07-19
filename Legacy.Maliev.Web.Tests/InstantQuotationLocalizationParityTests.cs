using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Legacy.Maliev.Web.Tests;

public sealed partial class InstantQuotationLocalizationParityTests
{
    private static readonly IReadOnlyDictionary<string, string> ReviewedThaiTranslations =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Email"] = "อีเมล",
            ["Ready for manufacturing"] = "พร้อมเข้าสู่กระบวนการผลิต",
            ["We will get back to you with a quotation"] = "เราจะติดต่อกลับพร้อมใบเสนอราคา",
            ["{0}–{1} business days"] = "{0}–{1} วันทำการ",
            ["3D preview"] = "ตัวอย่าง 3 มิติ",
            ["3D preview is unavailable. You can continue with your quotation."] = "ไม่สามารถแสดงตัวอย่าง 3 มิติได้ คุณยังสามารถดำเนินการขอใบเสนอราคาต่อได้",
            ["Cancel"] = "ยกเลิก",
            ["Company"] = "บริษัท",
            ["Enter customer details to continue."] = "กรอกข้อมูลลูกค้าเพื่อดำเนินการต่อ",
            ["File uploaded. Configure the part to continue."] = "อัปโหลดไฟล์แล้ว กรุณาตั้งค่าชิ้นงานเพื่อดำเนินการต่อ",
            ["Fullscreen"] = "เต็มหน้าจอ",
            ["Interactive 3D part preview"] = "ตัวอย่างชิ้นงาน 3 มิติแบบโต้ตอบ",
            ["Multiple parts are ready to configure."] = "ชิ้นงานหลายชิ้นพร้อมให้ตั้งค่าแล้ว",
            ["Part configuration is ready for review."] = "ตั้งค่าชิ้นงานเรียบร้อย พร้อมตรวจสอบรายการ",
            ["Quotation steps"] = "ขั้นตอนการขอใบเสนอราคา",
            ["Remove"] = "ลบ",
            ["Request not submitted"] = "ยังไม่ได้ส่งคำขอ",
            ["Request reference"] = "เลขอ้างอิงคำขอ",
            ["Request saved"] = "บันทึกคำขอแล้ว",
            ["Return to your quotation"] = "กลับไปยังรายการประเมินราคา",
            ["Review"] = "ตรวจสอบรายการ",
            ["Review the order before entering customer details."] = "ตรวจสอบรายการก่อนกรอกข้อมูลลูกค้า",
            ["Subtotal"] = "ยอดรวมย่อย",
            ["Tax number"] = "เลขประจำตัวผู้เสียภาษี",
            ["The file could not be uploaded. Try again."] = "ไม่สามารถอัปโหลดไฟล์ได้ โปรดลองอีกครั้ง",
            ["The uploaded part preview will appear here."] = "ตัวอย่างชิ้นงานที่อัปโหลดจะแสดงที่นี่",
            ["Try again"] = "ลองอีกครั้ง",
            ["Upload cancelled"] = "ยกเลิกการอัปโหลดแล้ว",
            ["Uploading files…"] = "กำลังอัปโหลดไฟล์…",
            ["Upload progress"] = "ความคืบหน้าการอัปโหลด",
            ["Use arrow keys to rotate, plus or minus to zoom, 0 to reset, and Home to fit."] = "ใช้ปุ่มลูกศรเพื่อหมุน ปุ่มบวกหรือลบเพื่อซูม ปุ่ม 0 เพื่อรีเซ็ต และปุ่ม Home เพื่อปรับมุมมองให้พอดี",
            ["View"] = "ดู",
            ["Waiting"] = "กำลังรอ",
            ["Your quotation request was submitted."] = "ส่งคำขอใบเสนอราคาแล้ว",
            ["Your request could not be submitted. Please review the form and try again."] = "ไม่สามารถส่งคำขอได้ กรุณาตรวจสอบแบบฟอร์มแล้วลองอีกครั้ง",
            ["Your request was not submitted."] = "ยังไม่ได้ส่งคำขอของคุณ",
            ["Your request was saved. File processing is pending. Do not resubmit."] = "บันทึกคำขอแล้ว ระบบกำลังประมวลผลไฟล์ กรุณาอย่าส่งซ้ำ",
        };

    [Fact]
    public void ThaiResources_CoverEveryVisibleInstantQuotationKeyWithoutEnglishFallback()
    {
        var root = FindRepositoryRoot();
        var componentDirectory = Path.Combine(
            root.FullName,
            "Legacy.Maliev.Web",
            "Components",
            "Pages",
            "InstantQuotation");
        var usedKeys = Directory.EnumerateFiles(componentDirectory, "*.*", SearchOption.TopDirectoryOnly)
            .Where(path => Path.GetExtension(path) is ".razor" or ".cs")
            .SelectMany(path => LocalizerKey().Matches(File.ReadAllText(path)).Select(match => match.Groups[1].Value))
            .ToHashSet(StringComparer.Ordinal);
        var resourcePath = Path.Combine(
            root.FullName,
            "Legacy.Maliev.Web",
            "Resources",
            "Components",
            "Pages",
            "InstantQuotation",
            "ThreeDimensionalPrintingEstimateContent.th.resx");
        var resources = XDocument.Load(resourcePath)
            .Root!
            .Elements("data")
            .ToDictionary(
                element => element.Attribute("name")!.Value,
                element => element.Element("value")?.Value ?? string.Empty,
                StringComparer.Ordinal);

        var missing = usedKeys.Where(key => !resources.ContainsKey(key)).Order(StringComparer.Ordinal).ToArray();
        var englishFallback = usedKeys
            .Where(resources.ContainsKey)
            .Where(key => !ThaiText().IsMatch(resources[key]))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(missing);
        Assert.Empty(englishFallback);
        Assert.All(
            ReviewedThaiTranslations,
            expected => Assert.Equal(expected.Value, resources[expected.Key]));
    }

    private static DirectoryInfo FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Web.slnx")))
        {
            directory = directory.Parent;
        }

        return directory ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }

    [GeneratedRegex("Localizer\\[\\\"([^\\\"]+)\\\"")]
    private static partial Regex LocalizerKey();

    [GeneratedRegex("[ก-๙]")]
    private static partial Regex ThaiText();
}
