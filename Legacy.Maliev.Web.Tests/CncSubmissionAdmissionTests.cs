using System.Text;
using Legacy.Maliev.Web.Components.Pages.InstantQuotation;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using static Legacy.Maliev.Web.Components.Pages.InstantQuotation.CncSubmissionAdmission;

namespace Legacy.Maliev.Web.Tests;

public sealed class CncSubmissionAdmissionTests
{
    private const string Session = "11111111-1111-1111-1111-111111111111";
    private const string Form = "form-one";

    [Fact]
    public void MissingItemListNullItemAndFilename_AreRejectedBeforeComposition()
    {
        var bindings = Bindings();
        foreach (var items in new List<ItemDetail>[] { null!, [], [null!] })
        {
            var submission = new CncSubmission { OrderItems = items };
            Assert.False(IsCncRequestWithinBudgets(submission));
            var errors = new ModelStateDictionary();
            ValidateSubmittedOrderItems(submission, Session, Form, bindings, errors);
            Assert.False(errors.IsValid);
        }
        var item = Item(bindings);
        item.FileName = " ";
        Assert.False(Validate(bindings, item).IsValid);
    }

    [Theory]
    [InlineData("part.step", true)]
    [InlineData("part.stp", true)]
    [InlineData("part.iges", true)]
    [InlineData("part.igs", true)]
    [InlineData("part.STEP", true)]
    [InlineData("part.stl", false)]
    [InlineData("part.obj", false)]
    [InlineData("part.pdf", false)]
    public void ItemAdmission_AcceptsOnlyReceiptedSolidCad(string fileName, bool valid)
    {
        var bindings = Bindings();
        var item = Item(bindings, "item-1", fileName);
        Assert.Equal(valid, Validate(bindings, item).IsValid);
    }

    [Theory]
    [InlineData("none", "ISO 2768-m", false, true)]
    [InlineData("M6 through", "ISO 2768-m", false, true)]
    [InlineData("drawing", "ISO 2768-m", false, false)]
    [InlineData("none", "drawing", false, false)]
    [InlineData("drawing", "drawing", true, true)]
    [InlineData("none", "ISO 2768-m", true, true)]
    public void Requirements_DrawingRequiredAndOptionalPoliciesMatchSource(string threads, string tolerance, bool drawing, bool valid)
    {
        var bindings = Bindings();
        var item = Item(bindings);
        item.CncThreads = threads;
        item.CncTolerance = tolerance;
        if (drawing) Drawing(bindings, item);
        Assert.Equal(valid, Validate(bindings, item).IsValid);
    }

    [Theory]
    [InlineData("process")]
    [InlineData("quantity")]
    [InlineData("material")]
    [InlineData("snapshot")]
    [InlineData("roughness")]
    [InlineData("inspection")]
    [InlineData("certificate")]
    [InlineData("threads")]
    [InlineData("note")]
    public void InvalidRequirementOrSnapshot_IsRejected(string field)
    {
        var bindings = Bindings();
        var item = Item(bindings);
        switch (field)
        {
            case "process": item.Process = "FdmPrinting"; break;
            case "quantity": item.Quantity = "2"; break;
            case "material": item.Material = " "; break;
            case "snapshot": item.CncEstimateJson = "{"; break;
            case "roughness": item.CncRoughness = "unknown"; break;
            case "inspection": item.CncInspection = "unknown"; break;
            case "certificate": item.CncCertificate = "unknown"; break;
            case "threads": item.CncThreads = new string('x', 501); break;
            case "note": item.CncRequirementsNote = new string('x', 2001); break;
        }
        Assert.False(Validate(bindings, item).IsValid);
    }

    [Fact]
    public void ReceiptBindings_RejectCrossItemSessionFormRoleFilenamePathAndDuplicateNonce()
    {
        var bindings = Bindings();
        foreach (var field in new[] { "item", "session", "form", "role", "filename", "path", "missing" })
        {
            var item = Item(bindings);
            item.CncModelUploadReceipt = field == "missing" ? "" : bindings.CreateReceiptToken(
                field == "session" ? "other-session" : Session,
                field == "form" ? "other-form" : Form,
                field == "item" ? "other-item" : item.CncItemId,
                field == "role" ? "drawing" : "model",
                field == "filename" ? "other.step" : item.FileName,
                field == "path" ? "other-path" : item.StoragePath);
            Assert.False(Validate(bindings, item).IsValid);
        }
        var original = Item(bindings);
        Assert.False(Validate(bindings, original, original).IsValid);
        var first = Item(bindings, "first");
        var second = Item(bindings, "second");
        Drawing(bindings, first);
        second.CncDrawingFileName = first.CncDrawingFileName;
        second.CncDrawingStoragePath = first.CncDrawingStoragePath;
        second.CncDrawingUploadReceipt = first.CncDrawingUploadReceipt;
        Assert.False(Validate(bindings, first, second).IsValid);
    }

    [Fact]
    public void ExpiredReceipt_IsRejectedAndSameOriginalNamesRemainIndependentlyBound()
    {
        var clock = new Clock();
        var bindings = new CncProtectedUploadBindings(new EphemeralDataProtectionProvider(), clock);
        var first = Item(bindings, "first");
        var second = Item(bindings, "second");
        Drawing(bindings, first);
        Drawing(bindings, second);
        Assert.True(Validate(bindings, first, second).IsValid);
        Assert.NotEqual(first.StoragePath, second.StoragePath);
        clock.Now += TimeSpan.FromHours(3);
        Assert.False(Validate(bindings, first).IsValid);
    }

    [Fact]
    public void Budgets_EnforceCountUtf8AggregateAndGeneratedRecordLimits()
    {
        var bindings = Bindings();
        Assert.False(IsCncRequestWithinBudgets(new()));
        var request = new CncSubmission { OrderItems = Enumerable.Range(0, 20).Select(i => Item(bindings, $"item-{i}")).ToList() };
        Assert.True(IsCncRequestWithinBudgets(request));
        request.OrderItems.Add(Item(bindings, "extra"));
        Assert.False(IsCncRequestWithinBudgets(request));
        request.OrderItems.RemoveAt(20);
        foreach (var item in request.OrderItems)
        {
            var json = JObject.Parse(item.CncEstimateJson);
            json["geometrySummary"]!["category"] = new string('ก', 120);
            json["setupPlan"]!["category"] = new string('ข', 120);
            json["operationSummary"]!["category"] = new string('ค', 160);
            json["quantityPlan"]!["category"] = new string('ง', 120);
            item.CncEstimateJson = json.ToString(Formatting.None);
        }
        Assert.False(IsCncRequestWithinBudgets(request));
        var single = Item(bindings);
        single.CncEstimateJson = new string(' ', 40960);
        request.OrderItems = [single];
        Assert.True(IsCncRequestWithinBudgets(request));
        single.CncEstimateJson += "ก";
        Assert.False(IsCncRequestWithinBudgets(request));
        single.CncEstimateJson = CncSubmissionSnapshotTests.SnapshotJson();
        request.Description = new string('ก', 262144 / 3 + 1);
        Assert.False(IsCncRequestWithinBudgets(request));
    }

    [Fact]
    public void ReviewRecord_PreservesContactThaiTaxAddressItemsCanonicalUntrustedTextAndBoundaries()
    {
        var bindings = Bindings();
        var item = Item(bindings);
        Drawing(bindings, item);
        Assert.True(Validate(bindings, item).IsValid);
        var request = new CncSubmission
        {
            Mobile = "synthetic-mobile",
            Telephone = "synthetic-office",
            TaxNumber = "synthetic-tax",
            TaxBranch = "branch",
            TaxBranchCode = "00002",
            BillingBuilding = " อาคาร ",
            BillingStreet1 = " ถนน ",
            BillingCity = "เมือง",
            Country = "Thailand",
            Description = "<script>synthetic</script>\nsecond line",
            OrderItems = [item]
        };
        Assert.True(TryBuildReviewRecord(request, out var message));
        Assert.Contains("Tax number: synthetic-tax (สาขาที่ 00002)", message);
        Assert.Contains("Shipping address (same as billing)", message);
        Assert.Contains("1 - part.step", message);
        Assert.Contains("Technical drawing: drawing.pdf", message);
        Assert.Contains("Canonical untrusted snapshot:", message);
        Assert.Contains("<script>synthetic</script>", message); // Stored plain text, not HTML; email encoding is separate.
        Assert.Contains("engineering review is required before a binding quotation, order, invoice or payment.", message);
        request.Description = "";
        Assert.True(TryBuildReviewRecord(request, out var empty));
        var length = Encoding.UTF8.GetByteCount(empty);
        request.Description = new string('x', 262144 - length - Encoding.UTF8.GetByteCount("Description" + Environment.NewLine + Environment.NewLine + Environment.NewLine));
        Assert.True(TryBuildReviewRecord(request, out var atLimit));
        Assert.Equal(262144, Encoding.UTF8.GetByteCount(atLimit));
        request.Description += "ก";
        Assert.False(TryBuildReviewRecord(request, out _));
    }

    private static CncProtectedUploadBindings Bindings() => new(new EphemeralDataProtectionProvider(), new Clock());
    private static ModelStateDictionary Validate(CncProtectedUploadBindings bindings, params ItemDetail[] items)
    {
        var errors = new ModelStateDictionary();
        ValidateSubmittedOrderItems(new() { OrderItems = items.ToList() }, Session, Form, bindings, errors);
        return errors;
    }

    private static ItemDetail Item(CncProtectedUploadBindings bindings, string id = "item", string fileName = "part.step")
    {
        var item = new ItemDetail
        {
            CncItemId = id,
            FileName = fileName,
            StoragePath = $"2026-9-7/{Session}/{Guid.NewGuid():N}{Path.GetExtension(fileName)}",
            CncEstimateJson = CncSubmissionSnapshotTests.SnapshotJson(),
            Quantity = "1",
            Material = "6061",
            Process = "CncMilling",
            CncTolerance = "ISO 2768-m",
            CncThreads = "none",
            CncRoughness = "default",
            CncInspection = "standard",
            CncCertificate = "not-required"
        };
        item.CncModelUploadReceipt = bindings.CreateReceiptToken(Session, Form, id, "model", fileName, item.StoragePath);
        return item;
    }

    private static void Drawing(CncProtectedUploadBindings bindings, ItemDetail item)
    {
        item.CncDrawingFileName = "drawing.pdf";
        item.CncDrawingStoragePath = $"2026-9-7/{Session}/{Guid.NewGuid():N}.pdf";
        item.CncDrawingUploadReceipt = bindings.CreateReceiptToken(Session, Form, item.CncItemId, "drawing", item.CncDrawingFileName, item.CncDrawingStoragePath);
    }

    private sealed class Clock : TimeProvider
    {
        internal DateTimeOffset Now = new(2026, 9, 7, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
