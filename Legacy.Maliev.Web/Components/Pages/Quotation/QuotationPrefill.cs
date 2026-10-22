using System.Text;
using Legacy.Maliev.Web.Pages.Quotation;

namespace Legacy.Maliev.Web.Components.Pages.Quotation;

/// <summary>
/// Carries a normalized service context and localized starting message for a quotation enquiry.
/// </summary>
/// <param name="ServiceContext">The canonical manufacturing-service key attached to the enquiry.</param>
/// <param name="Message">The suggested enquiry text containing available item, process, material, and finishing context.</param>
public sealed record QuotationPrefill(string ServiceContext, string Message)
{
    /// <summary>
    /// Builds localized enquiry prompts for supported service-page links and optional finishing references.
    /// </summary>
    /// <param name="culture">The requested language; Thai is selected only for the Thai culture key.</param>
    /// <param name="item">The service-page item key used to choose supported prefill prompts.</param>
    /// <param name="process">The optional manufacturing process to mention in the enquiry.</param>
    /// <param name="material">The optional material to mention for non-scanning work.</param>
    /// <param name="finishHex">The optional hexadecimal finishing color reference.</param>
    /// <param name="finishHlc">The optional HLC finishing color reference.</param>
    /// <param name="finishLab">The optional Lab finishing color reference.</param>
    /// <param name="finishPantone">The optional Pantone finishing color reference.</param>
    /// <param name="finishSheen">The optional finishing sheen requirement.</param>
    /// <returns>The normalized service context and suggested message; unsupported items produce an empty message.</returns>
    public static QuotationPrefill Create(
        string? culture,
        string? item,
        string? process,
        string? material,
        string? finishHex = null,
        string? finishHlc = null,
        string? finishLab = null,
        string? finishPantone = null,
        string? finishSheen = null)
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

        var finishingReference = FinishingColorQuotePrefill.Build(
            culture,
            finishHex,
            finishHlc,
            finishLab,
            finishPantone,
            finishSheen);
        if (!string.IsNullOrEmpty(finishingReference))
        {
            builder.AppendLine(finishingReference.TrimEnd());
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

    /// <summary>
    /// Normalizes supported public service aliases to their canonical quotation-context keys.
    /// </summary>
    /// <param name="service">The optional service alias supplied by the incoming public link.</param>
    /// <returns>The canonical service key, or custom_manufacturing for an unsupported or missing alias.</returns>
    public static string NormalizeServiceContext(string? service) => service?.Trim().ToLowerInvariant() switch
    {
        "3d-printing" or "3d_printing" => "3d_printing",
        "3d-scanning" or "3d_scanning" => "3d_scanning",
        "cnc-machining" or "cnc_machining" => "cnc_machining",
        "injection-molding" or "injection_molding" => "injection_molding",
        _ => "custom_manufacturing",
    };
}
