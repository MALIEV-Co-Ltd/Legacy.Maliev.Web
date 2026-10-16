namespace Legacy.Maliev.Web.Components.Pages.InstantQuotation;

/// <summary>Source material-card copy and grouping, without pricing or manufacturing authority.</summary>
public sealed record MaterialCardPresentation(string Key, string Name, string Category, string Description, string ThaiDescription);

/// <summary>Preserves the source catalogue order independently of pricing dictionary enumeration.</summary>
public static class MaterialCardPresentationCatalog
{
    /// <summary>Source card metadata from 29214f53, model-viewer.js blob1544fbf; monetary values remain runtime-owned.</summary>
    public static IReadOnlyList<MaterialCardPresentation> All { get; } = Array.AsReadOnly<MaterialCardPresentation>(
    [
        new("PLA", "PLA — Polylactic Acid", "Rapid prototyping", "Easy to print, eco-friendly and dimensionally stable. Best for prototypes, display models and low-stress parts.", "พิมพ์ง่าย เป็นมิตรต่อสิ่งแวดล้อม และคงรูปได้ดี เหมาะกับงานต้นแบบ โมเดลจัดแสดง และชิ้นงานที่รับแรงไม่มาก"),
        new("PETG", "PETG — PET Glycol-modified", "Rapid prototyping", "Tougher and more heat-resistant than PLA with good layer adhesion. A solid all-round choice for functional parts.", "เหนียวและทนความร้อนกว่า PLA ยึดเกาะระหว่างชั้นได้ดี เป็นตัวเลือกรอบด้านสำหรับชิ้นงานใช้งานจริง"),
        new("HIPS", "HIPS — High Impact Polystyrene", "Rapid prototyping", "Lightweight and easy to machine or sand; also used as a dissolvable support material for ABS.", "น้ำหนักเบา ขัดแต่งและกลึงง่าย ใช้เป็นวัสดุรองรับที่ละลายออกได้สำหรับ ABS ด้วย"),
        new("ABS", "ABS — Acrylonitrile Butadiene Styrene", "Functional & outdoor", "Heat resistant and impact-tough, but shrinks and warps more during printing. Common for enclosures and fixtures.", "ทนความร้อนและแรงกระแทกได้ดี แต่หดตัวและบิดงอระหว่างพิมพ์มากกว่า นิยมใช้ทำกล่องครอบและฟิกซ์เจอร์"),
        new("ASA", "ASA — Acrylonitrile Styrene Acrylate", "Functional & outdoor", "Like ABS but UV-stable — holds up outdoors without yellowing or turning brittle.", "คุณสมบัติใกล้เคียง ABS แต่ทนรังสียูวี ใช้งานกลางแจ้งได้นานโดยไม่เหลืองหรือกรอบ"),
        new("TPU", "TPU 95A — Flexible Thermoplastic Polyurethane", "Flexible", "Flexible, rubber-like material at 95 Shore A hardness — for gaskets, grips, wearables and parts that need to bend.", "ยืดหยุ่นคล้ายยาง ความแข็ง 95 Shore A เหมาะกับปะเก็น ด้ามจับ อุปกรณ์สวมใส่ และชิ้นงานที่ต้องโค้งงอ"),
        new("PC", "PC — Polycarbonate", "Engineering plastics", "High-strength, heat- and impact-resistant engineering plastic for demanding mechanical parts.", "แข็งแรงสูง ทนความร้อนและแรงกระแทก เหมาะกับชิ้นส่วนกลไกที่ใช้งานหนัก"),
        new("PA6", "PA6 — Nylon 6", "Engineering plastics", "Tough, wear-resistant nylon with good fatigue resistance — ideal for gears, snap-fits and living hinges.", "ไนลอนเหนียว ทนการสึกหรอและความล้า เหมาะกับเฟือง สแนปฟิต และบานพับยืดหยุ่น"),
        new("PA12", "PA12 — eSUN ePA12", "Engineering plastics", "Low-moisture-absorption eSUN ePA12 nylon with excellent chemical resistance and durability.", "ไนลอน eSUN ePA12 ที่ดูดความชื้นต่ำ ทนสารเคมีได้ดีเยี่ยม และมีความทนทานสูง"),
        new("PLA-CF", "PLA-CF — PLA + Carbon Fiber", "Carbon-fiber reinforced", "Carbon-fiber-reinforced PLA — stiffer and lighter than standard PLA, with a matte finish.", "PLA เสริมคาร์บอนไฟเบอร์ แข็งกว่าและเบากว่า PLA ทั่วไป ให้ผิวด้าน"),
        new("PETG-CF", "PETG-rCF08 — Fiberon recycled-carbon PETG", "Carbon-fiber reinforced", "Fiberon PETG reinforced with 8% recycled carbon fiber for stiff, tough functional parts.", "Fiberon PETG เสริมคาร์บอนไฟเบอร์รีไซเคิล 8% เพิ่มความแข็งโดยยังคงความเหนียวสำหรับชิ้นงานใช้งานจริง"),
        new("PET-CF", "PET-CF17 — Fiberon carbon-fiber PET", "Carbon-fiber reinforced", "Fiberon PET reinforced with 17% carbon fiber for high-stiffness, low-warp jigs and tooling.", "Fiberon PET เสริมคาร์บอนไฟเบอร์ 17% ให้ความแข็งสูงและบิดงอน้อย เหมาะกับจิ๊กและเครื่องมือช่วยผลิต"),
        new("PA-CF", "PA-CF — Nylon + Carbon Fiber", "Carbon-fiber reinforced", "Carbon-fiber nylon — very high strength-to-weight ratio for demanding mechanical applications.", "ไนลอนเสริมคาร์บอนไฟเบอร์ อัตราส่วนความแข็งแรงต่อน้ำหนักสูงมาก เหมาะกับงานกลไกที่รับแรงมาก"),
        new("ASA-CF", "ASA-CF — Bambu ASA-CF", "Carbon-fiber reinforced", "Bambu carbon-fiber ASA — UV-stable and rigid, suited to outdoor structural parts.", "Bambu ASA เสริมคาร์บอนไฟเบอร์ แข็งและทนรังสียูวี เหมาะกับชิ้นส่วนโครงสร้างที่ใช้งานกลางแจ้ง"),
        new("PETG-ESD", "PETG-ESD — ESD-Safe PETG", "ESD-safe for electronics", "Static-dissipative PETG for parts that handle or house sensitive electronics.", "PETG ที่สลายประจุไฟฟ้าสถิต เหมาะกับชิ้นงานที่ต้องจับหรือใส่อุปกรณ์อิเล็กทรอนิกส์ที่ไวต่อไฟฟ้าสถิต"),
        new("PA612-ESD", "PA612-ESD — Fiberon ESD Nylon", "ESD-safe for electronics", "Fiberon PA612 nylon with carbon nanotubes and carbon fiber for electronics fixtures. ESD performance depends on printing and must be tested for the application.", "ไนลอน Fiberon PA612 ผสมคาร์บอนนาโนทิวบ์และคาร์บอนไฟเบอร์สำหรับฟิกซ์เจอร์อิเล็กทรอนิกส์ สมบัติ ESD ขึ้นกับการพิมพ์และต้องทดสอบตามการใช้งาน"),
        new("ABS-ESD", "ABS-ESD — eSUN ESD ABS", "ESD-safe for electronics", "eSUN static-dissipative ABS for electronics fixtures and housings. Supplier currently reports out of stock; procurement and delivery must be confirmed. Application testing is required.", "ABS สลายประจุไฟฟ้าสถิตจาก eSUN สำหรับฟิกซ์เจอร์และกล่องอิเล็กทรอนิกส์ ผู้ขายแจ้งสินค้าหมด ต้องยืนยันการจัดหาและกำหนดส่ง และทดสอบตามการใช้งาน"),
        new("ABS-FR", "ABS-FR — Flame-Retardant ABS", "Flame-retardant", "Flame-retardant ABS for enclosures with fire-safety requirements.", "ABS หน่วงไฟ สำหรับกล่องครอบที่ต้องผ่านข้อกำหนดด้านความปลอดภัยจากอัคคีภัย"),
        new("PC-FR", "PC-FR — Bambu PC FR", "Flame-retardant", "Bambu flame-retardant polycarbonate for enclosures and components with fire-safety requirements.", "Bambu โพลีคาร์บอเนตหน่วงไฟ สำหรับกล่องครอบและชิ้นส่วนที่ต้องผ่านข้อกำหนดด้านความปลอดภัยจากอัคคีภัย"),
        new("M68", "Standard Resin", "Resin (SLA / DLP)", "Smooth, highly detailed finish. Great for miniatures, visual prototypes and presentation models.", "ผิวเรียบ เก็บรายละเอียดได้คมชัด เหมาะกับโมเดลขนาดเล็ก ต้นแบบเพื่อความสวยงาม และงานนำเสนอ"),
        new("K", "Tough Resin", "Resin (SLA / DLP)", "Higher impact and flex resistance than standard resin, for functional snap-fit and mechanical parts.", "ทนแรงกระแทกและการงอได้ดีกว่าเรซิ่นมาตรฐาน เหมาะกับสแนปฟิตและชิ้นส่วนกลไก"),
        new("G217", "Transparent Resin", "Resin (SLA / DLP)", "Optically clear resin for lenses, light guides and see-through prototypes.", "เรซิ่นใสสำหรับเลนส์ ตัวนำแสง และต้นแบบที่ต้องมองทะลุได้"),
        new("F80", "Flexible Resin", "Resin (SLA / DLP)", "Soft, rubber-like resin for gaskets, seals and flexible details.", "เรซิ่นนุ่มคล้ายยาง เหมาะกับปะเก็น ซีล และรายละเอียดที่ต้องการความยืดหยุ่น"),
        new("CASTWAX", "Castable Wax Resin", "Resin (SLA / DLP)", "Burns out cleanly for jewelry and investment-casting workflows.", "เผาไหม้หมดจดไม่เหลือเถ้า เหมาะกับงานเครื่องประดับและการหล่อแบบขี้ผึ้งหาย"),
        new("PVA", "PVA — Water-Soluble Support", "Support material", "Water-soluble support material that dissolves away cleanly — ideal for complex overhangs and internal channels.", "วัสดุรองรับที่ละลายน้ำได้ ล้างออกสะอาด เหมาะกับชิ้นงานที่มีส่วนยื่นหรือช่องภายในซับซ้อน"),
    ]);

    /// <summary>Offers only current server materials, retaining unknown future keys without inventing descriptions.</summary>
    public static IReadOnlyList<MaterialCardPresentation> ForMaterials(IReadOnlyList<InstantQuotationWorkflowMaterialOption> materials)
    {
        var keys = materials.Select(material => material.Key).ToHashSet(StringComparer.Ordinal);
        var known = All.Where(material => keys.Contains(material.Key));
        var unknown = materials.Where(material => All.All(card => card.Key != material.Key))
            .OrderBy(material => material.Key, StringComparer.Ordinal)
            .Select(material => new MaterialCardPresentation(material.Key, material.DisplayName, "Other materials", string.Empty, string.Empty));
        return known.Concat(unknown).ToArray();
    }

    /// <summary>Searches source names, both description languages and the current category label.</summary>
    public static bool Matches(MaterialCardPresentation material, string search, Func<string, string> localize)
    {
        var needle = search.Trim();
        return needle.Length == 0
            || material.Name.Contains(needle, StringComparison.OrdinalIgnoreCase)
            || material.Description.Contains(needle, StringComparison.OrdinalIgnoreCase)
            || material.ThaiDescription.Contains(needle, StringComparison.OrdinalIgnoreCase)
            || localize(material.Description).Contains(needle, StringComparison.OrdinalIgnoreCase)
            || localize(material.Category).Contains(needle, StringComparison.OrdinalIgnoreCase);
    }
}
