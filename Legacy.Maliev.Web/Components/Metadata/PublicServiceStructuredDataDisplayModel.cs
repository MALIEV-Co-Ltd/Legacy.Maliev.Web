using System.Globalization;
using System.Text.Json;

namespace Legacy.Maliev.Web.Components.Metadata;

public sealed record PublicServiceStructuredDataDisplayModel(string ServiceJson)
{
    private sealed record ServiceDefinition(
        string NameTh,
        string NameEn,
        string DescriptionTh,
        string DescriptionEn,
        string Url,
        string Image);

    private static readonly IReadOnlyDictionary<string, ServiceDefinition> Services =
        new Dictionary<string, ServiceDefinition>(StringComparer.Ordinal)
        {
            ["CNC Machining"] = new(
                "บริการรับงาน CNC ตามแบบ",
                "CNC Machining Services",
                "ผลิตชิ้นส่วนโลหะและพลาสติกวิศวกรรมจากไฟล์ CAD หรือแบบระบุขนาด โดยตรวจสอบวัสดุ จำนวน จุดสำคัญ และการใช้งานก่อนยืนยันขอบเขตงาน",
                "CNC machining for engineering metal and plastic parts from CAD or dimensioned drawings, with material, quantity, critical features, and intended use reviewed before scope is confirmed.",
                "https://www.maliev.com/services/cnc-machining",
                "https://www.maliev.com/src/images/services/cnc/cnc-hero.webp"),
            ["3D Printing"] = new(
                "บริการรับพิมพ์ 3D",
                "3D Printing Services",
                "ผลิตต้นแบบและชิ้นงานใช้งานด้วยกระบวนการ FDM หรือ Resin โดยเลือกกระบวนการและวัสดุจากไฟล์ การใช้งาน จำนวน และผิวที่ต้องการ",
                "FDM and resin 3D printing for prototypes and functional parts, with process and material selected from the file, intended use, quantity, and finish requirements.",
                "https://www.maliev.com/services/3d-printing",
                "https://www.maliev.com/src/images/services/printing/printing-hero.webp"),
            ["3D Scanning"] = new(
                "บริการรับสแกน 3D",
                "3D Scanning Services",
                "เก็บข้อมูลรูปทรงสำหรับไฟล์สแกนดิบ Reverse Engineering และการเปรียบเทียบความคลาดเคลื่อน โดยยืนยันความเป็นไปได้และสิ่งส่งมอบตามแต่ละโครงการ",
                "3D geometry capture for raw scan data, reverse-engineering input, and deviation analysis, with feasibility and deliverables confirmed for each project.",
                "https://www.maliev.com/services/3d-scanning",
                "https://www.maliev.com/src/images/services/scanning/scanning-hero.webp"),
            ["Custom Manufacturing"] = new(
                "รับผลิตชิ้นงานตามแบบ",
                "Custom Part Manufacturing",
                "ช่วยกำหนดเส้นทางประเมินระหว่าง CNC, 3D Printing และ 3D Scanning จากแบบหรือตัวอย่าง วัสดุ จำนวน จุดสำคัญ และการใช้งาน ก่อนส่งต่อไปยังบริการเฉพาะทาง",
                "Manufacturing process selection for projects that may need CNC machining, 3D printing, or 3D scanning, based on the drawing or sample, material, quantity, critical features, and intended use.",
                "https://www.maliev.com/services/custom-manufacturing",
                string.Empty)
        };

    public static PublicServiceStructuredDataDisplayModel Create(string? serviceName)
    {
        var normalizedServiceName = serviceName ?? "CNC Machining";
        var service = Services.TryGetValue(normalizedServiceName, out var selectedService)
            ? selectedService
            : Services["CNC Machining"];
        var isThai = string.Equals(
            CultureInfo.CurrentCulture.TwoLetterISOLanguageName,
            "th",
            StringComparison.OrdinalIgnoreCase);
        var schema = new Dictionary<string, object>
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "Service",
            ["serviceType"] = normalizedServiceName,
            ["name"] = isThai ? service.NameTh : service.NameEn,
            ["description"] = isThai ? service.DescriptionTh : service.DescriptionEn,
            ["url"] = service.Url,
            ["provider"] = new Dictionary<string, object>
            {
                ["@type"] = "LocalBusiness",
                ["@id"] = "https://www.maliev.com/#organization",
                ["name"] = "Maliev Co., Ltd."
            },
            ["areaServed"] = new Dictionary<string, object>
            {
                ["@type"] = "Country",
                ["name"] = "Thailand"
            }
        };

        if (!string.IsNullOrWhiteSpace(service.Image))
        {
            schema["image"] = service.Image;
        }

        return new PublicServiceStructuredDataDisplayModel(JsonSerializer.Serialize(schema));
    }
}
