using System.Net;
using System.Xml.Linq;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Legacy.Maliev.Web.Tests;

public sealed class KnowledgeThaiMeshSourceTests(TestingWebApplicationFactory factory)
    : IClassFixture<TestingWebApplicationFactory>
{
    public static IEnumerable<object[]> SourceValues()
    {
        yield return new object[] { "GuidelinesContent.th.resx", "/knowledges/guidelines", "KnowledgesGuidelines", "For solid models, include a STEP/STP export where available. For printing meshes, STL, OBJ, or 3MF may be useful; state the intended units and overall size. Confirm support in the chosen upload route.", "หากมีโมเดลทรงตัน ให้แนบ STEP/STP สำหรับ mesh งานพิมพ์ อาจใช้ STL, OBJ หรือ 3MF โดยระบุหน่วยและขนาดโดยรวม ตรวจสอบรูปแบบที่รองรับในช่องทางอัปโหลดที่เลือก", true };
        yield return new object[] { "KnowledgeIndexContent.th.resx", "/knowledges", "KnowledgesIndex", "Choose mesh or CAD deliverables and agree access, accuracy, datums, and reconstructed areas.", "เลือกรูปแบบ mesh หรือ CAD และตกลงการเข้าถึง ความแม่นยำ ดาตัม และบริเวณที่ต้องสร้างรูปทรงเพิ่ม", false };
        yield return new object[] { "KnowledgeIndexContent.th.resx", "/knowledges", "KnowledgesIndex", "Specify mesh or CAD deliverables, access, datums, and reconstructed areas.", "กำหนดไฟล์ mesh หรือ CAD การเข้าถึง ดาตัม และบริเวณที่ต้องสร้างรูปทรงเพิ่มเติม", true };
        yield return new object[] { "Specifications/ThreeDimensionalPrintingContent.th.resx", "/knowledges/specifications/3d-printing", "KnowledgesSpecifications3DPrinting", "Submit a closed, watertight model at the intended scale. Remove accidental bodies, overlapping shells, open edges, and self-intersections. Identify separate parts and their quantity rather than relying on a viewer screenshot to explain the order. State units and one overall reference dimension, especially with mesh files.", "ส่งโมเดลที่ปิดสนิทและมีสเกลตามต้องการ ลบชิ้นส่วนส่วนเกิน ผิวซ้อน ขอบเปิด และผิวตัดกัน ระบุชิ้นส่วนแยกและจำนวนของแต่ละชิ้น แทนการอธิบายด้วยภาพจากโปรแกรมดูไฟล์เพียงอย่างเดียว ระบุหน่วยและขนาดรวมอ้างอิงอย่างน้อยหนึ่งค่า โดยเฉพาะไฟล์ mesh", true };
        yield return new object[] { "Specifications/ThreeDimensionalPrintingContent.th.resx", "/knowledges/specifications/3d-printing", "KnowledgesSpecifications3DPrinting", "For a mesh export such as STL, OBJ, or 3MF, inspect curved surfaces for visible faceting. A smooth display shader can hide a coarse mesh; enlarging the file later does not recover missing design detail. Include the source solid or STEP export if available, and a drawing for critical requirements.", "เมื่อส่งออก mesh เช่น STL, OBJ หรือ 3MF ให้ตรวจผิวโค้งว่ามีเหลี่ยมที่มองเห็นหรือไม่ การแสดงผลผิวเรียบอาจซ่อน mesh ที่หยาบ และการเพิ่มความละเอียดภายหลังไม่ได้คืนรายละเอียดต้นฉบับ แนบโมเดลทรงตันหรือ STEP หากมี พร้อมแบบสำหรับข้อกำหนดสำคัญ", true };
        yield return new object[] { "Specifications/ThreeDimensionalScanningContent.th.resx", "/knowledges/specifications/3d-scanning", "KnowledgesSpecifications3DScanning", "Mesh, CAD, or comparison", "mesh CAD หรือการเปรียบเทียบ", true };
        yield return new object[] { "Specifications/ThreeDimensionalScanningContent.th.resx", "/knowledges/specifications/3d-scanning", "KnowledgesSpecifications3DScanning", "Surface mesh", "mesh พื้นผิว", true };
        yield return new object[] { "Specifications/ThreeDimensionalScanningContent.th.resx", "/knowledges/specifications/3d-scanning", "KnowledgesSpecifications3DScanning", "A triangulated surface represents captured shape. Agree the file format, units, coverage, and whether texture or color is needed. A mesh is not automatically a feature-based CAD model.", "ผิวสามเหลี่ยมแสดงรูปทรงที่เก็บข้อมูลได้ ตกลงรูปแบบไฟล์ หน่วย พื้นที่ครอบคลุม และความต้องการเท็กซ์เจอร์หรือสี mesh ไม่ได้เป็นโมเดล CAD ที่มีฟีเจอร์โดยอัตโนมัติ", true };
        yield return new object[] { "Specifications/ThreeDimensionalScanningContent.th.resx", "/knowledges/specifications/3d-scanning", "KnowledgesSpecifications3DScanning", "A scanner’s stated resolution may describe resolvable surface detail or point spacing; check the supplier’s definition. Accuracy concerns closeness to the true value of the measured quantity. Repeatability compares measurements over a short period with the same procedure, operators, system, location and operating conditions. A dense mesh or a smooth screen image does not establish that a critical dimension is within tolerance.", "ความละเอียดที่ผู้ผลิตสแกนเนอร์ระบุอาจหมายถึงรายละเอียดผิวที่แยกแยะได้หรือระยะห่างระหว่างจุด ควรตรวจสอบนิยามของผู้ผลิต ความถูกต้องเกี่ยวข้องกับความใกล้เคียงค่าจริงของปริมาณที่วัด ความสามารถทำซ้ำเปรียบเทียบผลในช่วงเวลาสั้นโดยใช้วิธี ผู้ปฏิบัติงาน ระบบ สถานที่ และสภาวะการทำงานเดิม mesh ที่หนาแน่นหรือภาพบนจอที่เรียบไม่ได้ยืนยันว่าขนาดสำคัญอยู่ในค่าความคลาดเคลื่อนที่กำหนด", true };
        yield return new object[] { "Specifications/ThreeDimensionalScanningContent.th.resx", "/knowledges/specifications/3d-scanning", "KnowledgesSpecifications3DScanning", "Agree what is measured, what is reconstructed and whether delivery is mesh or CAD. Illustrative image.", "ตกลงให้ชัดว่าส่วนใดวัดจริง ส่วนใดสร้างขึ้นใหม่ และต้องส่งมอบ mesh หรือ CAD ภาพประกอบ", true };
        yield return new object[] { "Specifications/SpecificationsIndexContent.th.resx", "/knowledges/specifications", "KnowledgesSpecifications", "Surface access, preparation, measurement requirements, mesh, and reverse-engineered CAD.", "การเข้าถึงผิว การเตรียมงาน ข้อกำหนดการวัด mesh และ CAD จากวิศวกรรมย้อนกลับ", true };
        yield return new object[] { "Specifications/SpecificationsIndexContent.th.resx", "/knowledges/specifications", "KnowledgesSpecifications", "Preparation permissions, mesh versus CAD, accuracy needs, and treatment of missing areas.", "การอนุญาตเตรียมผิว mesh หรือ CAD ความแม่นยำที่ต้องการ และวิธีจัดการบริเวณที่เก็บข้อมูลไม่ได้", true };
    }

    public static IEnumerable<object[]> ResourceValues() => SourceValues().Select(value => new[] { value[0], value[3], value[4] });

    [Theory]
    [MemberData(nameof(ResourceValues))]
    public void Resources_PreserveExactSourceWording(string file, string key, string expected)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "Legacy.Maliev.Web")))
        {
            root = root.Parent;
        }
        Assert.NotNull(root);
        var resource = Path.Combine(root.FullName, "Legacy.Maliev.Web", "Resources", "Components", "Pages", "Knowledges", file);
        var entry = Assert.Single(XDocument.Load(resource).Root!.Elements("data"), element => (string?)element.Attribute("name") == key);
        Assert.Equal(expected, entry.Element("value")?.Value);
    }

    public static IEnumerable<object[]> RenderedSourceValues() => SourceValues()
        .Where(value => (bool)value[5]).Select(value => new[] { value[1], value[2], value[4] });

    [Theory]
    [MemberData(nameof(RenderedSourceValues))]
    public async Task RenderedValues_MatchSourceInBothRoutingModes(string route, string flag, string expected)
    {
        foreach (var active in new[] { true, false })
        {
            await using var host = factory.WithWebHostBuilder(builder => builder.UseSetting("BlazorRouting:" + flag, active ? "true" : "false"));
            using var client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            using var response = await client.GetAsync(route + "?culture=th");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
            Assert.Contains(expected, html, StringComparison.Ordinal);
            Assert.Equal(active, html.Contains("data-migration-route-owner=\"blazor-static-ssr\"", StringComparison.Ordinal));
        }
    }
}
