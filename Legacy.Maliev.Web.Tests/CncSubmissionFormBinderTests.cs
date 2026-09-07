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

    private static FormCollection Form(Dictionary<string, StringValues> values) => new(values);
}
