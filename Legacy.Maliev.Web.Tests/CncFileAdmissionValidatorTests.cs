using System.Text;
using System.Globalization;
using Legacy.Maliev.Web.Components.Pages.InstantQuotation;

namespace Legacy.Maliev.Web.Tests;

public sealed class CncFileAdmissionValidatorTests
{
    [Fact]
    public void StructuralAdmission_RejectsUnownedIgesParameterAfterValidPayloads()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestAssets", "cube.iges");
        Assert.True(CncFileAdmissionValidator.IsValidIges(File.ReadAllBytes(path)));
        var lines = File.ReadAllLines(path).ToList();
        var terminateIndex = lines.FindIndex(line => line[72] == 'T');
        var parameterCount = lines.Count(line => line[72] == 'P');
        var startCount = lines.Count(line => line[72] == 'S');
        var globalCount = lines.Count(line => line[72] == 'G');
        var directoryCount = lines.Count(line => line[72] == 'D');
        lines.Insert(terminateIndex, IgesParameterRecord("128,garbage;", 1, parameterCount + 1));
        lines[terminateIndex + 1] = IgesRecord($"S{startCount}G{globalCount}D{directoryCount}P{parameterCount + 1}", 'T', 1);
        Assert.False(CncFileAdmissionValidator.IsValidIges(Encoding.ASCII.GetBytes(string.Join("\n", lines))));
    }

    [Theory]
    [InlineData("#26 = SURFACE_CURVE('',#27,(),.PCURVE_S1.);")]
    [InlineData("#26 = SURFACE_CURVE('',#27,(#31,#43),.BOGUS.);")]
    public void DeepInspection_RejectsMalformedOpenCascadeSurfaceCurve(string invalidRecord)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestAssets", "Cnc", "box-20x30x40.step");
        var valid = File.ReadAllText(path);
        var malformed = valid.Replace("#26 = SURFACE_CURVE('',#27,(#31,#43),.PCURVE_S1.);", invalidRecord, StringComparison.Ordinal);
        Assert.NotEqual(valid, malformed);
        Assert.False(CncFileAdmissionValidator.IsValidStep(Encoding.ASCII.GetBytes(malformed)));
    }

    [Fact]
    public void StructuralAdmission_RejectsConnectedBogusStepChainAndIncompleteTopology()
    {
        var step = Encoding.ASCII.GetBytes(
            "ISO-10303-21;HEADER;FILE_DESCRIPTION(('x'),'2;1');FILE_NAME('x','','','','','','');FILE_SCHEMA(('AP214'));ENDSEC;DATA;"
            + "#1=PRODUCT('p','p','',());#2=BOGUS(#1,#3);#3=SHAPE_REPRESENTATION('',(#4),#6);#4=BOGUS(#3,#5);"
            + "#5=MANIFOLD_SOLID_BREP('',#4);#6=GEOMETRIC_REPRESENTATION_CONTEXT(3);ENDSEC;END-ISO-10303-21;");
        Assert.False(CncFileAdmissionValidator.IsValidStep(step));
    }

    [Fact]
    public void Envelope_AdmitsRealCadWithoutClaimingDeepTopologyValidation()
    {
        var data = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestAssets", "Cnc", "box-20x30x40.step"));
        Assert.True(CncFileAdmissionValidator.HasValidStepEnvelope(data));
        Assert.False(CncFileAdmissionValidator.HasValidStepEnvelope(Encoding.ASCII.GetBytes("solid renamed STL")));
        Assert.False(CncFileAdmissionValidator.IsValidIges(Encoding.ASCII.GetBytes("not an iges fixed record")));
    }

    [Fact]
    public void CncStructuralAdmission_RejectsMarkerOnlyCraftedFiles()
    {
        byte[] step = Encoding.ASCII.GetBytes(
            "ISO-10303-21;\nHEADER;\nFILE_DESCRIPTION(('x'),'2;1');\nFILE_NAME('x','','','','','','');\nFILE_SCHEMA(('AP214'));\nENDSEC;\nDATA;\nENDSEC;\nEND-ISO-10303-21;");
        byte[] iges = Encoding.ASCII.GetBytes(string.Join("\n", new[]
        {
                IgesRecord("", 'S', 1),
                IgesRecord("", 'G', 1),
                IgesRecord("", 'D', 1),
                IgesRecord("", 'D', 2),
                IgesRecord("", 'P', 1),
                IgesRecord("S1G1D2P1", 'T', 1),
            }));
        byte[] pdf = Encoding.ASCII.GetBytes("%PDF-1.7\n1 0 obj\n<< /Type /Catalog >>\nendobj\nstartxref\n9\n%%EOF");

        Assert.False(CncFileAdmissionValidator.HasValidStepEnvelope(step));
        Assert.False(CncFileAdmissionValidator.IsValidIges(iges));
        Assert.False(CncFileAdmissionValidator.IsValidPdf(pdf));
    }

    [Fact]
    public void CncStructuralAdmission_RejectsReviewerAdversarialShells()
    {
        byte[] incompleteStep = Encoding.ASCII.GetBytes(
            "ISO-10303-21;HEADER;FILE_DESCRIPTION(('x'),'2;1');FILE_NAME('x','','','','','','');FILE_SCHEMA(('AP214'));ENDSEC;DATA;#1=PRODUCT('p','p','',());#2=SHAPE_REPRESENTATION('s',(),$);ENDSEC;END-ISO-10303-21;");
        byte[] unknownIges = Encoding.ASCII.GetBytes(string.Join("\n", new[]
        {
                IgesRecord("", 'S', 1),
                IgesRecord(",;", 'G', 1),
                IgesDirectoryRecord(999, 1, 'D', 1),
                IgesDirectoryRecord(999, 1, 'D', 2, parameterLineCount: 1),
                IgesParameterRecord("999,garbage;", 1, 1),
                IgesRecord("S1G1D2P1", 'T', 1),
            }));
        byte[] emptyXrefPdf = BuildClassicPdf(
            new Dictionary<int, string>
            {
                [1] = "<< /Type /Catalog /Pages 2 0 R >>",
                [2] = "<< /Type /Pages /Count 1 /Kids [3 0 R] >>",
                [3] = "<< /Type /Page /Parent 2 0 R >>",
            },
            rootObject: 1,
            includeInUseXrefEntries: false);

        Assert.False(CncFileAdmissionValidator.IsValidStep(incompleteStep));
        Assert.False(CncFileAdmissionValidator.IsValidIges(unknownIges));
        Assert.False(CncFileAdmissionValidator.IsValidPdf(emptyXrefPdf));
    }

    [Fact]
    public void CncStructuralAdmission_RejectsEmptyIgesSurfaceAtEntityValidation()
    {
        byte[] iges = Encoding.ASCII.GetBytes(string.Join("\n", new[]
        {
                IgesRecord("", 'S', 1),
                IgesRecord("", 'S', 2),
                IgesRecord(",;", 'G', 1),
                IgesDirectoryRecord(128, 1, 'D', 1),
                IgesDirectoryRecord(128, 1, 'D', 2, parameterLineCount: 1),
                IgesParameterRecord("128;", 1, 1),
                IgesRecord("S2G1D2P1", 'T', 1),
            }));

        Assert.False(CncFileAdmissionValidator.IsValidIges(iges));
    }

    [Fact]
    public void CncStructuralAdmission_AcceptsCompactStepAndRecursivePagesPdf()
    {
        byte[] compactStep = Encoding.ASCII.GetBytes(
            " ISO-10303-21 ; HEADER ; FILE_DESCRIPTION ( ( 'x' ) , '2;1' ) ; FILE_NAME ( 'x' , '' , ( ) , ( ) , '' , '' , '' ) ; FILE_SCHEMA ( ( 'AP214' ) ) ; ENDSEC ; DATA ; #1 = PRODUCT ( 'p' , 'p' , '' , ( ) ) ; #2 = PRODUCT_DEFINITION_FORMATION ( '' , '' , #1 ) ; #3 = PRODUCT_DEFINITION ( '' , '' , #2 , #4 ) ; #4 = PRODUCT_DEFINITION_CONTEXT ( '' , #5 , '' ) ; #5 = APPLICATION_CONTEXT ( '' ) ; #6 = PRODUCT_DEFINITION_SHAPE ( '' , '' , #3 ) ; #7 = MANIFOLD_SOLID_BREP ( '' , #8 ) ; #8 = CLOSED_SHELL ( '' , ( #12 ) ) ; #9 = ADVANCED_BREP_SHAPE_REPRESENTATION ( '' , ( #7 ) , #10 ) ; #10 = GEOMETRIC_REPRESENTATION_CONTEXT ( 3 ) ; #11 = SHAPE_DEFINITION_REPRESENTATION ( #6 , #9 ) ; #12 = ADVANCED_FACE ( '' , ( #18 ) , #13 , .T. ) ; #13 = PLANE ( '' , #14 ) ; #14 = AXIS2_PLACEMENT_3D ( '' , #15 , #16 , #17 ) ; #15 = CARTESIAN_POINT ( '' , ( 0. , 0. , 0. ) ) ; #16 = DIRECTION ( '' , ( 0. , 0. , 1. ) ) ; #17 = DIRECTION ( '' , ( 1. , 0. , 0. ) ) ; #18 = FACE_OUTER_BOUND ( '' , #19 , .T. ) ; #19 = EDGE_LOOP ( '' , ( #20 ) ) ; #20 = ORIENTED_EDGE ( '' , * , * , #21 , .T. ) ; #21 = EDGE_CURVE ( '' , #22 , #23 , #24 , .T. ) ; #22 = VERTEX_POINT ( '' , #25 ) ; #23 = VERTEX_POINT ( '' , #26 ) ; #24 = LINE ( '' , #25 , #27 ) ; #25 = CARTESIAN_POINT ( '' , ( 0. , 0. , 0. ) ) ; #26 = CARTESIAN_POINT ( '' , ( 1. , 0. , 0. ) ) ; #27 = VECTOR ( '' , #17 , 1. ) ; ENDSEC ; END-ISO-10303-21 ; ");
        byte[] nestedPagesPdf = BuildClassicPdf(
            new Dictionary<int, string>
            {
                [1] = "<< /Type /Catalog /Pages 2 0 R >>",
                [2] = "<< /Type /Pages /Count 1 /Kids [3 0 R] >>",
                [3] = "<< /Type /Pages /Parent 2 0 R /Count 1 /Kids [4 0 R] >>",
                [4] = "<< /Type /Page /Parent 3 0 R /MediaBox [0 0 10 10] >>",
            },
            rootObject: 1,
            includeInUseXrefEntries: true);

        Assert.True(CncFileAdmissionValidator.HasValidStepEnvelope(compactStep));
        Assert.True(CncFileAdmissionValidator.IsValidPdf(nestedPagesPdf));
    }

    [Fact]
    public void CncStructuralAdmission_AcceptsXrefStreamAndCompressedRecursivePageObjects()
    {
        byte[] pdf = BuildXrefAndObjectStreamPdf();

        Assert.True(CncFileAdmissionValidator.IsValidPdf(pdf));
    }

    [Fact]
    public void CncStructuralAdmission_MalformedNumericFieldsFailClosedWithoutThrowing()
    {
        byte[] iges = Encoding.ASCII.GetBytes(string.Join("\n", new[]
        {
                IgesRecord("", 'S', 1),
                IgesRecord(",;", 'G', 1),
                IgesDirectoryRecord(186, 1, 'D', 1),
                IgesDirectoryRecord(186, 1, 'D', 2, parameterLineCount: 1),
                IgesParameterRecord("186,1,1,0,0,1,1;", 1, 1),
                IgesRecord("S999999999999999999999999999999G1D2P1", 'T', 1),
            }));

        Assert.False(CncFileAdmissionValidator.IsValidIges(iges));

        byte[] step = Encoding.ASCII.GetBytes(
            "ISO-10303-21;HEADER;FILE_DESCRIPTION(('x'),'2;1');FILE_NAME('x','','','','','','');FILE_SCHEMA(('AP214'));ENDSEC;DATA;"
            + "#1=PRODUCT('p','p','',());#2=PRODUCT_DEFINITION_FORMATION('','',#999999999999999999999999999999);"
            + "#3=PRODUCT_DEFINITION('','',#2,#4);#4=PRODUCT_DEFINITION_CONTEXT('',#5,'');#5=APPLICATION_CONTEXT('');"
            + "#6=PRODUCT_DEFINITION_SHAPE('','',#3);#7=SHAPE_DEFINITION_REPRESENTATION(#6,#8);"
            + "#8=ADVANCED_BREP_SHAPE_REPRESENTATION('',(),#9);#9=GEOMETRIC_REPRESENTATION_CONTEXT(3);ENDSEC;END-ISO-10303-21;");
        byte[] pdf = BuildMalformedXrefStreamWithOversizedWidth();

        Assert.False(CncFileAdmissionValidator.IsValidStep(step));
        Assert.False(CncFileAdmissionValidator.IsValidPdf(pdf));
    }


    private static string IgesRecord(string data, char section, int sequence) =>
        (data ?? string.Empty).PadRight(72).Substring(0, 72) + section + sequence.ToString(CultureInfo.InvariantCulture).PadLeft(7);

    private static string IgesDirectoryRecord(int entityType, int parameterPointer, char section, int sequence, int parameterLineCount = 0)
    {
        string[] fields = new string[9];
        fields[0] = entityType.ToString(CultureInfo.InvariantCulture);
        if (sequence % 2 == 1)
        {
            fields[1] = parameterPointer.ToString(CultureInfo.InvariantCulture);
        }
        else
        {
            fields[3] = parameterLineCount.ToString(CultureInfo.InvariantCulture);
        }

        return IgesRecord(string.Concat(fields.Select(field => (field ?? string.Empty).PadLeft(8))), section, sequence);
    }

    private static string IgesParameterRecord(string data, int directoryPointer, int sequence) =>
        (data ?? string.Empty).PadRight(64).Substring(0, 64)
        + directoryPointer.ToString(CultureInfo.InvariantCulture).PadLeft(8)
        + 'P'
        + sequence.ToString(CultureInfo.InvariantCulture).PadLeft(7);

    private static byte[] BuildClassicPdf(
        IReadOnlyDictionary<int, string> objects,
        int rootObject,
        bool includeInUseXrefEntries)
    {
        var pdf = new StringBuilder("%PDF-1.7\n");
        var offsets = new Dictionary<int, int>();
        foreach (KeyValuePair<int, string> item in objects.OrderBy(item => item.Key))
        {
            offsets[item.Key] = pdf.Length;
            pdf.Append(item.Key).Append(" 0 obj\n").Append(item.Value).Append("\nendobj\n");
        }

        int xrefOffset = pdf.Length;
        int size = objects.Keys.Max() + 1;
        pdf.Append("xref\n0 ").Append(includeInUseXrefEntries ? size : 1).Append("\n0000000000 65535 f \n");
        if (includeInUseXrefEntries)
        {
            for (int number = 1; number < size; number++)
            {
                pdf.Append(offsets[number].ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
            }
        }

        pdf.Append("trailer\n<< /Size ").Append(size).Append(" /Root ").Append(rootObject)
            .Append(" 0 R >>\nstartxref\n").Append(xrefOffset.ToString(CultureInfo.InvariantCulture)).Append("\n%%EOF");
        return Encoding.ASCII.GetBytes(pdf.ToString());
    }

    private static byte[] BuildXrefAndObjectStreamPdf()
    {
        var output = new MemoryStream();
        void WriteAscii(string value)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(value);
            output.Write(bytes, 0, bytes.Length);
        }

        WriteAscii("%PDF-1.7\n");
        string[] compressedBodies =
        {
                "<< /Type /Catalog /Pages 2 0 R >>",
                "<< /Type /Pages /Count 1 /Kids [3 0 R] >>",
                "<< /Type /Pages /Parent 2 0 R /Count 1 /Kids [4 0 R] >>",
                "<< /Type /Page /Parent 3 0 R >>",
            };
        var objectBody = new StringBuilder();
        var relativeOffsets = new List<int>();
        foreach (string body in compressedBodies)
        {
            relativeOffsets.Add(objectBody.Length);
            objectBody.Append(body).Append(' ');
        }

        var objectHeader = new StringBuilder();
        for (int index = 0; index < compressedBodies.Length; index++)
        {
            objectHeader.Append(index + 1).Append(' ').Append(relativeOffsets[index]).Append(' ');
        }

        string objectStreamData = objectHeader + objectBody.ToString();
        int objectStreamOffset = (int)output.Position;
        WriteAscii($"5 0 obj\n<< /Type /ObjStm /N 4 /First {objectHeader.Length} /Length {objectStreamData.Length} >>\nstream\n{objectStreamData}\nendstream\nendobj\n");

        int xrefOffset = (int)output.Position;
        var xref = new MemoryStream();
        void WriteEntry(byte type, uint field2, ushort field3)
        {
            xref.WriteByte(type);
            xref.WriteByte((byte)(field2 >> 24));
            xref.WriteByte((byte)(field2 >> 16));
            xref.WriteByte((byte)(field2 >> 8));
            xref.WriteByte((byte)field2);
            xref.WriteByte((byte)(field3 >> 8));
            xref.WriteByte((byte)field3);
        }

        WriteEntry(0, 0, ushort.MaxValue);
        for (ushort index = 0; index < 4; index++)
        {
            WriteEntry(2, 5, index);
        }

        WriteEntry(1, (uint)objectStreamOffset, 0);
        WriteEntry(1, (uint)xrefOffset, 0);
        byte[] xrefBytes = xref.ToArray();
        WriteAscii($"6 0 obj\n<< /Type /XRef /Size 7 /Root 1 0 R /W [1 4 2] /Index [0 7] /Length {xrefBytes.Length} >>\nstream\n");
        output.Write(xrefBytes, 0, xrefBytes.Length);
        WriteAscii($"\nendstream\nendobj\nstartxref\n{xrefOffset}\n%%EOF");
        return output.ToArray();
    }

    private static byte[] BuildMalformedXrefStreamWithOversizedWidth()
    {
        var pdf = new StringBuilder("%PDF-1.7\n");
        int xrefOffset = pdf.Length;
        pdf.Append("1 0 obj\n<< /Type /XRef /Size 2 /Root 2 0 R /W [999999999999999999999 4 2] /Index [0 2] /Length 0 >>\n")
            .Append("stream\n\nendstream\nendobj\nstartxref\n")
            .Append(xrefOffset.ToString(CultureInfo.InvariantCulture))
            .Append("\n%%EOF");
        return Encoding.ASCII.GetBytes(pdf.ToString());
    }

}
