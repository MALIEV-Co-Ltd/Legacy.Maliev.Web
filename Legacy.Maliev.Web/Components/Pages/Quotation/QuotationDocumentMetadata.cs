namespace Legacy.Maliev.Web.Components.Pages.Quotation;

/// <summary>Provides the source service-specific document copy without changing quotation form state.</summary>
/// <param name="Title">The localized document title.</param>
/// <param name="Description">The localized document description.</param>
/// <param name="Keywords">The localized document keywords.</param>
public sealed record QuotationDocumentMetadata(string Title, string Description, string Keywords)
{
    /// <summary>Resolves the original seven-service allowlist and safe unknown-item fallback.</summary>
    /// <param name="item">The requested service from the GET query.</param>
    /// <param name="isThai">Whether the current UI culture is Thai.</param>
    /// <returns>The localized title, description and keywords.</returns>
    public static QuotationDocumentMetadata Create(string? item, bool isThai)
    {
        var requestedItem = (item ?? string.Empty).Trim().ToLowerInvariant();
        var title = isThai
            ? requestedItem switch
            {
                "3d-design" => "ขอราคางานออกแบบ 3D | MALIEV",
                "3d-printing" => "ขอราคางานพิมพ์ 3D | MALIEV",
                "3d-scanning" => "ขอราคางานสแกน 3D | MALIEV",
                "cnc-machining" => "ขอราคา CNC ตามแบบ | MALIEV",
                "custom-manufacturing" => "ขอราคาผลิตชิ้นงานตามแบบ | MALIEV",
                "low-volume-injection-molding" => "ขอราคาฉีดพลาสติกจำนวนน้อย | MALIEV",
                "silicone-casting" => "ขอราคางานหล่อซิลิโคน | MALIEV",
                _ => "ขอใบเสนอราคางานผลิต | MALIEV"
            }
            : requestedItem switch
            {
                "3d-design" => "3D Design Quote Request | MALIEV",
                "3d-printing" => "3D Printing Quote Request | MALIEV",
                "3d-scanning" => "3D Scanning Quote Request | MALIEV",
                "cnc-machining" => "CNC Machining Quote Request | MALIEV",
                "custom-manufacturing" => "Custom Manufacturing Quote | MALIEV",
                "low-volume-injection-molding" => "Low-Volume Injection Quote | MALIEV",
                "silicone-casting" => "Silicone Casting Quote | MALIEV",
                _ => "Manufacturing Quote Request | MALIEV"
            };
        var description = isThai
            ? requestedItem switch
            {
                "3d-design" => "ส่งแบบ ภาพร่าง หรือไฟล์ CAD เพื่อขอประเมินงานออกแบบ 3D และแนวทางเตรียมชิ้นงานสำหรับผลิตจาก MALIEV",
                "3d-printing" => "ส่งไฟล์ 3D พร้อมวัสดุ จำนวน และการใช้งาน เพื่อขอใบเสนอราคางานพิมพ์ 3D จาก MALIEV",
                "3d-scanning" => "ส่งรายละเอียดชิ้นงานจริง ขนาด และผลลัพธ์ที่ต้องการ เพื่อขอใบเสนอราคางานสแกน 3D จาก MALIEV",
                "cnc-machining" => "ส่งแบบ วัสดุ จำนวน และค่าความคลาดเคลื่อน เพื่อขอใบเสนอราคางาน CNC ตามแบบจาก MALIEV",
                "custom-manufacturing" => "ส่งไฟล์ แบบ วัสดุ จำนวน และการใช้งาน เพื่อให้ MALIEV ช่วยเลือกกระบวนการผลิตที่เหมาะสม",
                "low-volume-injection-molding" => "ส่งรายละเอียดชิ้นงาน แม่พิมพ์ วัสดุ และจำนวน เพื่อขอประเมินงานฉีดพลาสติกจำนวนน้อย",
                "silicone-casting" => "ส่งรูปทรง วัสดุ จำนวน และการใช้งาน เพื่อขอประเมินงานหล่อซิลิโคนจาก MALIEV",
                _ => "ส่งไฟล์ แบบ หรือรายละเอียดงาน เพื่อขอใบเสนอราคา CNC พิมพ์ 3D สแกน 3D และงานผลิตตามแบบจาก MALIEV"
            }
            : requestedItem switch
            {
                "3d-design" => "Send a drawing, sketch, or CAD file for a 3D design review and a practical path toward manufacturing with MALIEV.",
                "3d-printing" => "Send your 3D file, material, quantity, and use case for a reviewed 3D printing quotation from MALIEV.",
                "3d-scanning" => "Share the physical-part details and desired output for a 3D scanning quotation from MALIEV.",
                "cnc-machining" => "Send the drawing, material, quantity, and tolerance requirements for a CNC machining quotation from MALIEV.",
                "custom-manufacturing" => "Send files, material, quantity, and intended use so MALIEV can recommend a suitable manufacturing route.",
                "low-volume-injection-molding" => "Send part, mold, material, and quantity details for a low-volume injection molding review.",
                "silicone-casting" => "Send geometry, material behavior, quantity, and use case for a silicone casting quotation from MALIEV.",
                _ => "Send your files, drawings, and requirements for a reviewed CNC, 3D printing, 3D scanning, or custom manufacturing quote from MALIEV."
            };
        var keywords = isThai
            ? "ขอใบเสนอราคา, ราคางาน CNC, ราคางานพิมพ์ 3 มิติ, ราคางานสแกน 3 มิติ"
            : "manufacturing quotation, CNC machining quote, 3D printing quote, 3D scanning quote";
        return new(title, description, keywords);
    }
}
