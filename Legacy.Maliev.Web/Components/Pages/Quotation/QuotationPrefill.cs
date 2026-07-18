using System.Text;

namespace Legacy.Maliev.Web.Components.Pages.Quotation;

public sealed record QuotationPrefill(string ServiceContext, string Message)
{
    public static QuotationPrefill Create(
        string? culture,
        string? item,
        string? process,
        string? material)
    {
        var serviceContext = NormalizeServiceContext(item);
        var supportedItems = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "3d-scanning",
            "3d-printing",
            "cnc-machining"
        };
        if (string.IsNullOrWhiteSpace(item) || !supportedItems.Contains(item))
        {
            return new QuotationPrefill(serviceContext, string.Empty);
        }

        var thai = string.Equals(culture, "th", StringComparison.OrdinalIgnoreCase);
        var builder = new StringBuilder();
        builder.AppendLine(thai ? $"สินค้าที่ต้องการ: {item}" : $"I want: {item}");
        if (!string.IsNullOrWhiteSpace(process))
        {
            builder.AppendLine(thai ? $"ระบบเทคโนโลยี: {process}" : $"Please use: {process}");
        }

        builder.AppendLine("---");
        builder.AppendLine(thai ? "กรุณาทิ้งข้อความไว้ข้างล่าง:" : "Your message below:");
        if (string.Equals(item, "3d-scanning", StringComparison.OrdinalIgnoreCase))
        {
            builder.AppendLine(thai
                ? "ขนาดชิ้นงาน (กว้าง x ยาว x สูง): 0 x 0 x 0 mm"
                : "Dimensions (Length x Width x Height): 0 x 0 x 0 mm");
            builder.AppendLine(thai ? "นามสกุลไฟล์งานที่ต้องการ: STL" : "Desired output format: STL");
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(material))
            {
                builder.AppendLine(thai ? $"วัสดุ: {material}" : $"Material: {material}");
            }

            builder.AppendLine(thai ? "จำนวน: 1 ชิ้น" : "Quantity: 1 piece");
        }

        return new QuotationPrefill(serviceContext, builder.ToString());
    }

    public static string NormalizeServiceContext(string? service) => service?.Trim().ToLowerInvariant() switch
    {
        "3d-printing" or "3d_printing" => "3d_printing",
        "3d-scanning" or "3d_scanning" => "3d_scanning",
        "cnc-machining" or "cnc_machining" => "cnc_machining",
        "injection-molding" or "injection_molding" => "injection_molding",
        _ => "custom_manufacturing",
    };
}
