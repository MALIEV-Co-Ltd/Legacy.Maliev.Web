using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Legacy.Maliev.Web.Tests;

public sealed class ServiceHeroSourceCopyTests(TestingWebApplicationFactory factory) : IClassFixture<TestingWebApplicationFactory>
{
    [Theory]
    [InlineData("/services/3d-design", "Turn your idea, drawing or scanned part into a 3D CAD model ready for the next step.", "เปลี่ยนไอเดีย แบบ หรือชิ้นงานสแกนให้เป็นแบบ 3 มิติ พร้อมนำไปพัฒนาต่อ", false)]
    [InlineData("/services/3d-printing", "MALIEV provides 3D printing services across Thailand. Upload a file for an FDM or resin estimate, or ask our engineers to review industrial materials.", "พิมพ์ต้นแบบและชิ้นส่วนใช้งานจริงด้วย FDM เรซิ่น และวัสดุอุตสาหกรรม อัปโหลดไฟล์ประเมินราคา หรือให้วิศวกรช่วยดูงาน", true)]
    [InlineData("/services/3d-scanning", "MALIEV provides reverse engineering in Thailand. Scan an existing part, build editable CAD or compare it with your original design.", "สแกนชิ้นงานจริงเพื่อเก็บข้อมูล 3 มิติ สร้าง CAD ที่แก้ไขได้ หรือเปรียบเทียบกับแบบต้นฉบับ", true)]
    [InlineData("/services/cnc-machining", "CNC milling and turning for metal and engineering plastic parts, from one-off pieces to repeat production.", "รับกัดและกลึง CNC ตามแบบ ทั้งโลหะและพลาสติกวิศวกรรม ตั้งแต่งานชิ้นเดียวถึงงานผลิตซ้ำ", true)]
    [InlineData("/services/custom-manufacturing", "Send us your drawing, sample or idea. We will help you choose a manufacturing process for your part.", "ส่งแบบ ตัวอย่างชิ้นงาน หรือไอเดีย แล้วให้เราช่วยเลือกกระบวนการผลิตที่เหมาะกับงานของคุณ", false)]
    [InlineData("/services/finishing-and-color", "Plan the colour, sheen and surface finish of your 3D-printed part before production.", "กำหนดสี ความเงา และผิวสำเร็จของชิ้นงานพิมพ์ 3 มิติก่อนเริ่มผลิต", false)]
    [InlineData("/services/low-volume-injection-molding", "Make small batches of injection-molded parts for trials, fit checks and repeat orders.", "ฉีดพลาสติกจำนวนน้อยสำหรับงานทดลอง ทดสอบประกอบ และงานผลิตซ้ำ", false)]
    [InlineData("/services/silicone-casting", "Make flexible silicone prototypes and short runs using a 3D-printed mold.", "ผลิตต้นแบบและชิ้นงานซิลิโคนยืดหยุ่นจำนวนน้อยด้วยแม่พิมพ์พิมพ์ 3 มิติ", false)]
    public async Task HeroLead_RendersFinalSourceCopy(string route, string english, string thai, bool hasFallback)
    {
        foreach (bool active in hasFallback ? new[] { true, false } : new[] { true })
        {
            await using var host = factory.WithWebHostBuilder(builder => builder.UseSetting("BlazorRouting:Services", active.ToString()));
            using var client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            foreach (var (culture, expected) in new[] { ("en", english), ("th", thai) })
            {
                var html = WebUtility.HtmlDecode(await client.GetStringAsync($"{route}?culture={culture}"));
                var lead = Assert.Single(Regex.Matches(html, "<p class=\"service-hero-lead\">(.*?)</p>", RegexOptions.Singleline).Cast<Match>());
                Assert.Equal(expected, lead.Groups[1].Value);
            }
        }
    }

    [Theory]
    [InlineData("/services/3d-printing", "Use engineering review for MJF, SLS, SLM, DMLS, safety-critical work", "ใช้การประเมินโดยวิศวกรสำหรับงาน MJF, SLS, SLM, DMLS งานที่มีข้อกำหนดด้านความปลอดภัย")]
    [InlineData("/services/silicone-casting", "Yes, for suitable short-run applications. Mold geometry, print material, surface, release strategy, vents, and silicone behavior must be reviewed first.", "ได้สำหรับงานจำนวนน้อยที่เหมาะสม ต้องประเมินรูปทรง วัสดุพิมพ์ ผิว น้ำยาถอดแบบ ช่องระบาย และพฤติกรรมซิลิโคนก่อน")]
    [InlineData("/services/finishing-and-color", "Screen colours and the sheen preview guide the quotation; production colour is confirmed with a physical spray-out or reference.", "สีบนหน้าจอและภาพจำลองความเงาใช้ประกอบใบเสนอราคา ส่วนสีสำหรับผลิตจริงจะยืนยันด้วยแผ่นพ่นทดสอบหรือชิ้นอ้างอิง")]
    public async Task ServiceQualifications_RemainVisible(string route, string english, string thai)
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        foreach (var (culture, expected) in new[] { ("en", english), ("th", thai) })
        {
            var html = WebUtility.HtmlDecode(await client.GetStringAsync($"{route}?culture={culture}"));
            Assert.Contains(expected, html, StringComparison.Ordinal);
        }
    }
}
