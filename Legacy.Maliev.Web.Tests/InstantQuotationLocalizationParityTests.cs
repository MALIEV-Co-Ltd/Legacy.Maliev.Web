using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Globalization;
using System.Reflection;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Application.Pricing;
using Legacy.Maliev.Web.Components.Pages.InstantQuotation;

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
            ["Cancel upload for {0}"] = "ยกเลิกการอัปโหลด {0}",
            ["Try upload again for {0}"] = "ลองอัปโหลด {0} อีกครั้ง",
            ["View {0}"] = "ดู {0}",
            ["Remove {0}"] = "ลบ {0}",
            ["Supported files: STL, OBJ, 3MF, GLB, GLTF, STP, STEP, IGS, and IGES. Maximum 100 files, 200 MB each."] = "ไฟล์ที่รองรับ: STL, OBJ, 3MF, GLB, GLTF, STP, STEP, IGS และ IGES สูงสุด 100 ไฟล์ ไฟล์ละ 200 MB",
            ["Thailand"] = "ประเทศไทย",
            ["Any"] = "สีใดก็ได้",
            ["Black"] = "ดำ",
            ["White"] = "ขาว",
            ["Gray"] = "เทา",
            ["Silver"] = "เงิน",
            ["Red"] = "แดง",
            ["Orange"] = "ส้ม",
            ["Yellow"] = "เหลือง",
            ["Green"] = "เขียว",
            ["Blue"] = "น้ำเงิน",
            ["Purple"] = "ม่วง",
            ["Pink"] = "ชมพู",
            ["Natural"] = "สีธรรมชาติ",
            ["Clear"] = "ใส",
            ["Translucent"] = "โปร่งแสง",
            ["PLA — Polylactic Acid"] = "PLA — กรดโพลิแลกติก",
            ["PLA-CF — PLA + Carbon Fiber"] = "PLA-CF — PLA ผสมเส้นใยคาร์บอน",
            ["PETG — PET Glycol-modified"] = "PETG — PET ดัดแปรด้วยไกลคอล",
            ["PETG-CF — PETG + Carbon Fiber"] = "PETG-CF — PETG ผสมเส้นใยคาร์บอน",
            ["PETG-ESD — Electrostatic Discharge Safe"] = "PETG-ESD — ป้องกันการคายประจุไฟฟ้าสถิต",
            ["PET-CF — PET + Carbon Fiber"] = "PET-CF — PET ผสมเส้นใยคาร์บอน",
            ["ABS — Acrylonitrile Butadiene Styrene"] = "ABS — อะคริโลไนไตรล์ บิวทาไดอีน สไตรีน",
            ["ABS-FR — Flame-Retardant ABS"] = "ABS-FR — ABS ชนิดหน่วงการลามไฟ",
            ["ASA — Acrylonitrile Styrene Acrylate"] = "ASA — อะคริโลไนไตรล์ สไตรีน อะคริเลต",
            ["ASA-CF — ASA + Carbon Fiber"] = "ASA-CF — ASA ผสมเส้นใยคาร์บอน",
            ["HIPS — High Impact Polystyrene"] = "HIPS — โพลิสไตรีนทนแรงกระแทกสูง",
            ["PC — Polycarbonate"] = "PC — โพลีคาร์บอเนต",
            ["PC-FR — Flame-Retardant Polycarbonate"] = "PC-FR — โพลีคาร์บอเนตชนิดหน่วงการลามไฟ",
            ["PC-ESD — ESD-Safe Polycarbonate (3DXTech)"] = "PC-ESD — โพลีคาร์บอเนตป้องกันไฟฟ้าสถิต (3DXTech)",
            ["PA6 — Nylon 6"] = "PA6 — ไนลอน 6",
            ["PA12 — Nylon 12"] = "PA12 — ไนลอน 12",
            ["PA-CF — Nylon + Carbon Fiber"] = "PA-CF — ไนลอนผสมเส้นใยคาร์บอน",
            ["TPU — Flexible Thermoplastic Polyurethane"] = "TPU — เทอร์โมพลาสติกโพลียูรีเทนชนิดยืดหยุ่น",
            ["PVA — Water-Soluble Support"] = "PVA — วัสดุรองรับชนิดละลายน้ำ",
            ["Standard Resin (M68)"] = "เรซินมาตรฐาน (M68)",
            ["Tough Resin (K)"] = "เรซินเหนียว (K)",
            ["Transparent Resin (G217)"] = "เรซินใส (G217)",
            ["Flexible Resin (F80)"] = "เรซินยืดหยุ่น (F80)",
            ["Castable Wax Resin"] = "เรซินแวกซ์สำหรับงานหล่อ",
            ["Required fields are marked with an asterisk."] = "ช่องที่จำเป็นมีเครื่องหมายดอกจัน",
            ["There are problems with the customer details."] = "ข้อมูลลูกค้าบางช่องไม่ถูกต้อง",
            ["Please correct {0}."] = "กรุณาแก้ไข{0}",
            ["Please correct this field."] = "กรุณาแก้ไขช่องนี้",
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
        usedKeys.UnionWith(PricingCatalog.Materials.Values.Select(static material => material.DisplayName));
        usedKeys.UnionWith(PricingCatalog.MaterialColors.Values.SelectMany(static colors => colors));
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

    [Fact]
    public void ThaiCountryCatalog_CoversEveryIsoCountryAndSupportedKosovoWithoutEnglishFallback()
    {
        const string expectedCodes = "AD AE AF AG AI AL AM AO AQ AR AS AT AU AW AX AZ BA BB BD BE BF BG BH BI BJ BL BM BN BO BQ BR BS BT BV BW BY BZ CA CC CD CF CG CH CI CK CL CM CN CO CR CU CV CW CX CY CZ DE DJ DK DM DO DZ EC EE EG EH ER ES ET FI FJ FK FM FO FR GA GB GD GE GF GG GH GI GL GM GN GP GQ GR GS GT GU GW GY HK HM HN HR HT HU ID IE IL IM IN IO IQ IR IS IT JE JM JO JP KE KG KH KI KM KN KP KR KW KY KZ LA LB LC LI LK LR LS LT LU LV LY MA MC MD ME MF MG MH MK ML MM MN MO MP MQ MR MS MT MU MV MW MX MY MZ NA NC NE NF NG NI NL NO NP NR NU NZ OM PA PE PF PG PH PK PL PM PN PR PS PT PW PY QA RE RO RS RU RW SA SB SC SD SE SG SH SI SJ SK SL SM SN SO SR SS ST SV SX SY SZ TC TD TF TG TH TJ TK TL TM TN TO TR TT TV TW TZ UA UG UM US UY UZ VA VC VE VG VI VN VU WF WS XK YE YT ZA ZM ZW";
        var expected = expectedCodes.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        var catalogField = typeof(InstantQuotationCountryLabels).GetField(
            "ThaiByIso2",
            BindingFlags.NonPublic | BindingFlags.Static);
        var catalog = Assert.IsAssignableFrom<IReadOnlyDictionary<string, string>>(catalogField?.GetValue(null));

        Assert.Equal(expected.Order(StringComparer.Ordinal), catalog.Keys.Order(StringComparer.Ordinal));
        Assert.All(
            expected,
            iso2 =>
            {
                var country = new Country(0, iso2, null, null, iso2, null, null, null);
                var localized = InstantQuotationCountryLabels.DisplayName(country, CultureInfo.GetCultureInfo("th-TH"));
                Assert.Matches(ThaiText(), localized);
            });
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
