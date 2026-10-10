using Legacy.Maliev.Web.Components.Pages.InstantQuotation;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace Legacy.Maliev.Web.Tests;

public sealed class CncSubmissionFormBinderTests
{
    [Fact]
    public void Bind_PreservesTheRazorPageMultipartFieldContract()
    {
        var form = Form(new Dictionary<string, StringValues>
        {
            ["QuotationFormToken"] = "protected-form",
            ["FirstName"] = "Nat",
            ["LastName"] = "V",
            ["Email"] = "customer@example.test",
            ["Mobile"] = "0890000000",
            ["Telephone"] = "020000000",
            ["Company"] = "MALIEV",
            ["Country"] = "Thailand",
            ["BillingStreet1"] = "Road",
            ["BillingCity"] = "Bangkok",
            ["BillingProvince"] = "Bangkok",
            ["BillingPostalCode"] = "10110",
            ["ShipToBillingAddress"] = new StringValues(["true", "false"]),
            ["OrderItems[0].FileName"] = "part.step",
            ["OrderItems[0].StoragePath"] = "staging/part.step",
            ["OrderItems[0].Quantity"] = "2",
            ["OrderItems[0].Material"] = "Aluminum 6061",
            ["OrderItems[0].Process"] = "CncMilling",
            ["OrderItems[0].CncItemId"] = "part-0",
            ["OrderItems[0].CncModelUploadReceipt"] = "receipt",
        });

        Assert.True(CncSubmissionFormBinder.TryBind(form, out var token, out var submission));
        Assert.Equal("protected-form", token);
        Assert.True(submission.ShipToBillingAddress);
        var item = Assert.Single(submission.OrderItems);
        Assert.Equal("part.step", item.FileName);
        Assert.Equal("part-0", item.CncItemId);
    }

    [Fact]
    public void Bind_RejectsDuplicateScalarAndSparseItemIndexes()
    {
        var duplicate = Form(new Dictionary<string, StringValues>
        {
            ["QuotationFormToken"] = new StringValues(["one", "two"]),
        });
        Assert.False(CncSubmissionFormBinder.TryBind(duplicate, out _, out _));

        var sparse = Form(new Dictionary<string, StringValues>
        {
            ["QuotationFormToken"] = "one",
            ["OrderItems[1].FileName"] = "part.step",
        });
        Assert.False(CncSubmissionFormBinder.TryBind(sparse, out _, out _));
    }

    [Fact]
    public void Bind_RejectsMalformedItemKeysAndMoreThanTwentyItems()
    {
        var malformed = Form(new Dictionary<string, StringValues>
        {
            ["QuotationFormToken"] = "one",
            ["OrderItems[broken].FileName"] = "part.step",
        });
        Assert.False(CncSubmissionFormBinder.TryBind(malformed, out _, out _));

        var oversizedValues = new Dictionary<string, StringValues>
        {
            ["QuotationFormToken"] = "one",
        };
        for (var index = 0; index < 21; index++)
        {
            oversizedValues[$"OrderItems[{index}].FileName"] = $"part-{index}.step";
        }

        Assert.False(CncSubmissionFormBinder.TryBind(Form(oversizedValues), out _, out _));
    }

    [Theory]
    [InlineData("1", "3")]
    [InlineData("21", "100")]
    [InlineData("model-b", "model-a")]
    public void Bind_ExplicitIndexesPreserveBrowserOrderAndProtectedItemFields(string first, string second)
    {
        var values = new Dictionary<string, StringValues>
        {
            ["QuotationFormToken"] = "protected-form",
            ["OrderItems.Index"] = new StringValues([first, second]),
            [$"OrderItems[{second}].FileName"] = "second.step",
            [$"OrderItems[{first}].FileName"] = "first.step",
            [$"OrderItems[{first}].CncItemId"] = first,
            [$"OrderItems[{first}].CncEstimateJson"] = "{\"source\":\"browser\"}",
            [$"OrderItems[{first}].CncModelUploadReceipt"] = "protected-model-receipt",
            [$"OrderItems[{first}].CncDrawingUploadReceipt"] = "protected-drawing-receipt",
            [$"OrderItems[{first}].Color"] = "ignored-viewer-field",
        };

        Assert.True(CncSubmissionFormBinder.TryBind(Form(values), out _, out var submission));
        Assert.Collection(submission.OrderItems,
            item =>
            {
                Assert.Equal("first.step", item.FileName);
                Assert.Equal(first, item.CncItemId);
                Assert.Equal("{\"source\":\"browser\"}", item.CncEstimateJson);
                Assert.Equal("protected-model-receipt", item.CncModelUploadReceipt);
                Assert.Equal("protected-drawing-receipt", item.CncDrawingUploadReceipt);
            },
            item => Assert.Equal("second.step", item.FileName));
    }

    [Fact]
    public void Bind_FirstBrowserItemUsesExplicitIndexOne()
    {
        var values = new Dictionary<string, StringValues>
        {
            ["QuotationFormToken"] = "protected-form",
            ["OrderItems.Index"] = "1",
            ["OrderItems[1].FileName"] = "first.step",
        };

        Assert.True(CncSubmissionFormBinder.TryBind(Form(values), out _, out var submission));
        Assert.Equal("first.step", Assert.Single(submission.OrderItems).FileName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("part.one")]
    [InlineData("part]other")]
    [InlineData("part one")]
    public void Bind_RejectsInvalidExplicitIndexTokens(string index)
    {
        var values = new Dictionary<string, StringValues>
        {
            ["QuotationFormToken"] = "protected-form",
            ["OrderItems.Index"] = index,
            [$"OrderItems[{index}].FileName"] = "part.step",
        };

        Assert.False(CncSubmissionFormBinder.TryBind(Form(values), out _, out _));
    }

    [Fact]
    public void Bind_ExplicitIndexesRejectAmbiguousOrUnlistedFields()
    {
        var values = new Dictionary<string, StringValues>
        {
            ["QuotationFormToken"] = "protected-form",
            ["OrderItems.Index"] = new StringValues(["part", "part"]),
            ["OrderItems[part].FileName"] = "part.step",
        };
        Assert.False(CncSubmissionFormBinder.TryBind(Form(values), out _, out _));

        values["OrderItems.Index"] = "other";
        Assert.False(CncSubmissionFormBinder.TryBind(Form(values), out _, out _));

        values["OrderItems.Index"] = new StringValues(["part", "missing"]);
        Assert.False(CncSubmissionFormBinder.TryBind(Form(values), out _, out _));

        values["OrderItems.Index"] = "part";
        values["OrderItems[part].FileName"] = new StringValues(["one.step", "two.step"]);
        Assert.False(CncSubmissionFormBinder.TryBind(Form(values), out _, out _));
    }

    [Fact]
    public void Bind_ExplicitIndexesKeepItemCountAndTokenLengthLimits()
    {
        var indexes = Enumerable.Range(1, 21).Select(index => $"part-{index}").ToArray();
        var values = new Dictionary<string, StringValues>
        {
            ["QuotationFormToken"] = "protected-form",
            ["OrderItems.Index"] = new StringValues(indexes),
        };
        foreach (string index in indexes)
        {
            values[$"OrderItems[{index}].FileName"] = "part.step";
        }

        Assert.False(CncSubmissionFormBinder.TryBind(Form(values), out _, out _));
        values.Remove($"OrderItems[{indexes[20]}].FileName");
        values["OrderItems.Index"] = new StringValues(indexes[..20]);
        Assert.True(CncSubmissionFormBinder.TryBind(Form(values), out _, out var submission));
        Assert.Equal(20, submission.OrderItems.Count);

        string tooLong = new('a', 65);
        Assert.False(CncSubmissionFormBinder.TryBind(Form(new Dictionary<string, StringValues>
        {
            ["QuotationFormToken"] = "protected-form",
            ["OrderItems.Index"] = tooLong,
            [$"OrderItems[{tooLong}].FileName"] = "part.step",
        }), out _, out _));
    }

    private static FormCollection Form(Dictionary<string, StringValues> values) => new(values);
}
