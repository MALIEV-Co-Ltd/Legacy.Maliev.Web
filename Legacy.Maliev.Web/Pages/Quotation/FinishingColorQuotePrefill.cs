using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Legacy.Maliev.Web.Pages.Quotation;

/// <summary>
/// Validates the finishing-colour handoff and renders a localized quotation note.
/// </summary>
public static partial class FinishingColorQuotePrefill
{
    private static readonly HashSet<string> Sheens = new(StringComparer.Ordinal)
    {
        "matte",
        "satin",
        "gloss",
    };

    /// <summary>
    /// Builds a localized finishing reference after validating every query value.
    /// </summary>
    public static string Build(
        string? culture,
        string? hex,
        string? hlc,
        string? lab,
        string? pantone,
        string? sheen)
    {
        if (string.IsNullOrEmpty(hex)
            || string.IsNullOrEmpty(hlc)
            || string.IsNullOrEmpty(lab)
            || string.IsNullOrEmpty(sheen)
            || !HexPattern().IsMatch(hex)
            || !HlcPattern().IsMatch(hlc)
            || !LabPattern().IsMatch(lab)
            || !Sheens.Contains(sheen))
        {
            return string.Empty;
        }

        var isThai = string.Equals(culture, "th", StringComparison.OrdinalIgnoreCase);
        var builder = new StringBuilder();
        builder.AppendLine(isThai ? "ข้อมูลอ้างอิงสีและผิว:" : "Color and finish reference:");
        builder.AppendLine(string.Format(
            CultureInfo.InvariantCulture,
            isThai ? "สีจากหน้าจอ: {0}" : "Screen color: {0}",
            hex.ToUpperInvariant()));
        builder.AppendLine(string.Format(
            CultureInfo.InvariantCulture,
            isThai ? "รหัสอ้างอิง HLC: {0}" : "HLC reference: {0}",
            hlc));
        builder.AppendLine(string.Format(
            CultureInfo.InvariantCulture,
            isThai ? "ค่า CIELAB (D50): {0}" : "CIELAB (D50): {0}",
            lab));

        if (!string.IsNullOrWhiteSpace(pantone))
        {
            var safePantone = pantone.Trim();
            if (safePantone.Length <= 80 && !safePantone.Contains('\r') && !safePantone.Contains('\n'))
            {
                builder.AppendLine(string.Format(
                    CultureInfo.InvariantCulture,
                    isThai ? "รหัส Pantone ที่ลูกค้าระบุ: {0}" : "Customer-supplied Pantone code: {0}",
                    safePantone));
            }
        }

        var sheenLabel = sheen switch
        {
            "matte" => isThai ? "ด้าน" : "Matte",
            "satin" => isThai ? "ซาติน" : "Satin",
            "gloss" => isThai ? "เงา" : "Gloss",
            _ => string.Empty,
        };
        builder.AppendLine(string.Format(
            CultureInfo.InvariantCulture,
            isThai ? "ระดับความเงา: {0}" : "Finish sheen: {0}",
            sheenLabel));
        builder.AppendLine(isThai
            ? "หมายเหตุ: สีบนหน้าจอเป็นข้อมูลเบื้องต้น โปรดยืนยันสีจากตัวอย่างจริงก่อนผลิต"
            : "Note: Screen color is advisory; confirm against a physical reference before production.");

        return builder.ToString();
    }

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex HexPattern();

    [GeneratedRegex("^H\\d{3}_L\\d{2}_C\\d{3}$", RegexOptions.CultureInvariant)]
    private static partial Regex HlcPattern();

    [GeneratedRegex("^-?\\d{1,3}(?:\\.\\d{1,3})?,-?\\d{1,3}(?:\\.\\d{1,3})?,-?\\d{1,3}(?:\\.\\d{1,3})?$", RegexOptions.CultureInvariant)]
    private static partial Regex LabPattern();
}
