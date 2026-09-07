using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Components.Pages.InstantQuotation;
using Newtonsoft.Json;
using static Legacy.Maliev.Web.Components.Pages.InstantQuotation.CncSubmissionAdmission;

namespace Legacy.Maliev.Web.Tests;

public sealed class CncNotificationComposerTests
{
    [Fact]
    public void ValidSubmission_PreservesSourceRecipientsChannelsAndCommercialWarnings()
    {
        CncSubmission submission = Submission();

        bool composed = CncNotificationComposer.TryCompose(
            submission,
            8675,
            [new CncNotificationItemLinks(new Uri("https://files.example/model?token=one"))],
            out CncNotificationPlan? plan);

        Assert.True(composed);
        Assert.NotNull(plan);
        Assert.Equal(NotificationChannel.Manufacturing, plan.CustomerChannel);
        Assert.Equal(NotificationChannel.Manufacturing, plan.ManufacturingChannel);
        Assert.Equal("customer@example.com", plan.Customer.To);
        Assert.Equal("Quotation Request #8675", plan.Customer.Subject);
        Assert.Null(plan.Customer.ReplyTo);
        Assert.Equal(["mail-tracking@maliev.com"], plan.Customer.Bcc);
        Assert.Equal("manufacturing@maliev.com", plan.Manufacturing.To);
        Assert.Equal("customer@example.com", plan.Manufacturing.ReplyTo);
        Assert.Null(plan.Manufacturing.Bcc);
        Assert.Contains("preliminary and untrusted", plan.Customer.Body, StringComparison.Ordinal);
        Assert.Contains("not an order confirmation or payment authorization", plan.Customer.Body, StringComparison.Ordinal);
        Assert.Contains("Engineering must issue or approve the binding commercial quotation", plan.Manufacturing.Body, StringComparison.Ordinal);
        Assert.Contains("Canonical untrusted snapshot:", plan.Manufacturing.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void CustomerControlledValues_AreHtmlEncodedAndLinksUseValidatedAbsoluteUris()
    {
        CncSubmission submission = Submission();
        submission.FirstName = "<script>alert('name')</script>";
        submission.Company = "A&B <Factory>";
        submission.Description = "first <line>\r\nsecond & line";
        submission.OrderItems[0].FileName = "part'<one>.step";

        Assert.True(CncNotificationComposer.TryCompose(
            submission,
            1,
            [new CncNotificationItemLinks(new Uri("https://files.example/model?token=a%26b"))],
            out CncNotificationPlan? plan));

        string combined = plan!.Customer.Body + plan.Manufacturing.Body;
        Assert.DoesNotContain("<script>", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<Factory>", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("first <line>", combined, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", combined, StringComparison.Ordinal);
        Assert.Contains("A&amp;B &lt;Factory&gt;", combined, StringComparison.Ordinal);
        Assert.Contains("part&#39;&lt;one&gt;.step", combined, StringComparison.Ordinal);
        Assert.Contains("https://files.example/model?token=a%26b", combined, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("http://files.example/model")]
    [InlineData("https://user:password@files.example/model")]
    [InlineData("file:///c:/temp/model.step")]
    public void UnsafeModelLink_FailsClosed(string link)
    {
        Assert.False(CncNotificationComposer.TryCompose(
            Submission(),
            1,
            [new CncNotificationItemLinks(new Uri(link))],
            out CncNotificationPlan? plan));
        Assert.Null(plan);
    }

    [Fact]
    public void MissingOrUnexpectedDrawingLink_FailsClosed()
    {
        CncSubmission withDrawing = Submission();
        withDrawing.OrderItems[0].CncDrawingFileName = "drawing.pdf";
        withDrawing.OrderItems[0].CncDrawingStoragePath = "owned/drawing.pdf";
        Uri model = new Uri("https://files.example/model");

        Assert.False(CncNotificationComposer.TryCompose(
            withDrawing, 1, [new CncNotificationItemLinks(model)], out _));
        Assert.False(CncNotificationComposer.TryCompose(
            Submission(), 1, [new CncNotificationItemLinks(model, new Uri("https://files.example/drawing"))], out _));
        Assert.True(CncNotificationComposer.TryCompose(
            withDrawing,
            1,
            [new CncNotificationItemLinks(model, new Uri("https://files.example/drawing"))],
            out CncNotificationPlan? plan));
        Assert.Contains("Technical drawing:", plan!.Customer.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void IncompleteInputsAndOversizedProviderBody_FailClosed()
    {
        Assert.False(CncNotificationComposer.TryCompose(Submission(), 0, [], out _));
        Assert.False(CncNotificationComposer.TryCompose(Submission(), 1, [], out _));

        CncSubmission missingSnapshot = Submission();
        missingSnapshot.OrderItems[0].ValidatedCncEstimate = null!;
        Assert.False(CncNotificationComposer.TryCompose(
            missingSnapshot,
            1,
            [new CncNotificationItemLinks(new Uri("https://files.example/model"))],
            out _));

        CncSubmission oversized = Submission();
        oversized.Description = new string('x', 100_000);
        Assert.False(CncNotificationComposer.TryCompose(
            oversized,
            1,
            [new CncNotificationItemLinks(new Uri("https://files.example/model"))],
            out _));
    }

    private static CncSubmission Submission()
    {
        string json = CncSubmissionSnapshotTests.SnapshotJson();
        var snapshot = JsonConvert.DeserializeObject<CncEstimateSnapshot>(json)!;
        return new CncSubmission
        {
            FirstName = "Somsak",
            LastName = "Jaidee",
            Company = "MALIEV Customer",
            Email = "customer@example.com",
            Mobile = "+66810000000",
            Telephone = "+6620000000",
            TaxNumber = "0115562011815",
            Country = "Thailand",
            BillingStreet1 = "36/1 Moo 3",
            BillingCity = "Pak Kret",
            BillingProvince = "Nonthaburi",
            BillingPostalCode = "11120",
            ShipToBillingAddress = true,
            Description = "Please review the bore tolerance.",
            OrderItems =
            [
                new ItemDetail
                {
                    FileName = "part.step",
                    StoragePath = "owned/part.step",
                    Process = "CncMilling",
                    Material = "Aluminum 6061",
                    Quantity = "1",
                    CncTolerance = "ISO 2768-m",
                    CncThreads = "none",
                    CncRoughness = "default",
                    CncInspection = "standard",
                    CncCertificate = "not-required",
                    CncRequirementsNote = "Protect the sealing face.",
                    CncEstimateJson = json,
                    CanonicalCncEstimateJson = json,
                    ValidatedCncEstimate = snapshot,
                },
            ],
        };
    }
}
