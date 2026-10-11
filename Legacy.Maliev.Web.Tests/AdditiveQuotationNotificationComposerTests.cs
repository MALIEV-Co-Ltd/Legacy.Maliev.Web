using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Application.Pricing;
using Legacy.Maliev.Web.Infrastructure;

namespace Legacy.Maliev.Web.Tests;

public sealed class AdditiveQuotationNotificationComposerTests
{
    [Fact]
    public void PreservesBothSourceRecipientsAndAuthoritativeSummary()
    {
        var plan = AdditiveQuotationNotificationComposer.Compose(Summary());
        Assert.Equal(NotificationChannel.Manufacturing, plan.Channel);
        Assert.Equal("customer@example.test", plan.Customer.To);
        Assert.Equal("Quotation Request #42", plan.Customer.Subject);
        Assert.Equal(["mail-tracking@maliev.com"], plan.Customer.Bcc);
        Assert.Null(plan.Customer.ReplyTo);
        Assert.Equal("manufacturing@maliev.com", plan.Manufacturing.To);
        Assert.Equal("customer@example.test", plan.Manufacturing.ReplyTo);
        Assert.Null(plan.Manufacturing.Bcc);
        Assert.Contains("Print time per unit: 12.5 minutes", plan.Manufacturing.Body, StringComparison.Ordinal);
        Assert.Contains("Total print time: 25 minutes", plan.Manufacturing.Body, StringComparison.Ordinal);
        Assert.Contains("Includes 7% VAT and estimated domestic shipping", plan.Customer.Body, StringComparison.Ordinal);
        Assert.Contains("International shipping is quoted separately", plan.Manufacturing.Body, StringComparison.Ordinal);
        Assert.Contains("(same as billing)", plan.Customer.Body, StringComparison.Ordinal);
        Assert.Contains("We will review all the files", plan.Customer.Body, StringComparison.Ordinal);
        Assert.Contains("printability tests", plan.Customer.Body, StringComparison.Ordinal);
        Assert.Contains("write us back", plan.Customer.Body, StringComparison.Ordinal);
        Assert.Contains("36/1 Moo 3", plan.Customer.Body, StringComparison.Ordinal);
        Assert.Contains("Nonthaburi 11120, Thailand", plan.Customer.Body, StringComparison.Ordinal);
        foreach (var body in new[] { plan.Customer.Body, plan.Manufacturing.Body })
        {
            Assert.Contains("part.stl", body, StringComparison.Ordinal);
            Assert.Contains("Quantity: 2 piece(s)", body, StringComparison.Ordinal);
            Assert.Contains("Price per unit: 123.45 THB", body, StringComparison.Ordinal);
            Assert.Contains("Subtotal: 246.90 THB", body, StringComparison.Ordinal);
            Assert.Contains("Estimated price: 345.67 THB", body, StringComparison.Ordinal);
            Assert.Contains("Lead time: up to 7 days", body, StringComparison.Ordinal);
            Assert.Contains("10 x 20 x 30 mm", body, StringComparison.Ordinal);
            Assert.Contains("staff review", body, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void EncodesCustomerItemAddressNotesAndSignedLinkAttributes()
    {
        var summary = Summary();
        var customer = summary.Customer with
        {
            FirstName = "<script>name</script>",
            LastName = "<last>",
            CompanyName = "<company>",
            TaxIdentification = "<tax>",
            Email = "user+tag@example.test",
            MobileNumber = "'<mobile>",
            TelephoneNumber = "<office>",
            BillingAddressLine1 = "<billing>",
            ShippingAddressLine1 = "<shipping>",
            ShipToBillingAddress = false,
            Description = "<note>\nsecond & line",
        };
        var item = summary.Items[0] with
        {
            FileName = "<file>",
            Material = "<material>",
            Color = "<color>",
            Dimensions = "<size>",
            GeometryWarning = "<warning>",
            DownloadUrl = new Uri("https://files.example/part?one=a&two=b"),
        };
        var plan = AdditiveQuotationNotificationComposer.Compose(summary with { Customer = customer, Items = [item] });
        foreach (var body in new[] { plan.Customer.Body, plan.Manufacturing.Body })
        {
            foreach (var text in new[] { "script", "last", "company", "tax", "mobile", "office", "billing", "shipping", "note", "file", "material", "color", "size", "warning" })
                Assert.DoesNotContain("<" + text + ">", body, StringComparison.Ordinal);
            Assert.Contains("&lt;billing&gt;", body, StringComparison.Ordinal);
            Assert.Contains("&lt;shipping&gt;", body, StringComparison.Ordinal);
            Assert.Contains("second &amp; line", body, StringComparison.Ordinal);
            Assert.Contains("mailto:user%2Btag%40example.test", body, StringComparison.Ordinal);
            Assert.Contains("https://files.example/part?one=a&amp;two=b", body, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(PrintProcess.Fdm, BuildPreference.Standard, "Standard")]
    [InlineData(PrintProcess.Fdm, BuildPreference.Quality, "Quality")]
    [InlineData(PrintProcess.Fdm, BuildPreference.Strength, "Strength")]
    [InlineData(PrintProcess.Resin, BuildPreference.Quality, "Standard resin")]
    [InlineData(PrintProcess.Resin, BuildPreference.Strength, "Standard resin")]
    public void ProcessControlsBuildLabels(PrintProcess process, BuildPreference preference, string label)
    {
        var summary = Summary();
        var item = summary.Items[0] with { Process = process, BuildPreference = preference };
        var plan = AdditiveQuotationNotificationComposer.Compose(summary with { Items = [item] });
        Assert.Contains("Build: " + label, plan.Customer.Body, StringComparison.Ordinal);
        Assert.Contains("Build: " + label, plan.Manufacturing.Body, StringComparison.Ordinal);
        if (process == PrintProcess.Resin)
        {
            Assert.Contains("wash and post-cure", plan.Manufacturing.Body, StringComparison.Ordinal);
            Assert.DoesNotContain("Build: Strength", plan.Customer.Body, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("http://files.example/part")]
    [InlineData("https://private@files.example/part")]
    public void RejectsUnsafeDownloadDestinations(string url)
    {
        var summary = Summary();
        var item = summary.Items[0] with { DownloadUrl = new Uri(url) };
        Assert.Throws<ArgumentException>(() => AdditiveQuotationNotificationComposer.Compose(summary with { Items = [item] }));
    }

    [Fact]
    public void InternationalEstimateDoesNotClaimShippingIsIncluded()
    {
        var plan = AdditiveQuotationNotificationComposer.Compose(Summary() with { DomesticShippingPriced = false, DestinationCountryCode = "JP" });
        foreach (var body in new[] { plan.Customer.Body, plan.Manufacturing.Body })
        {
            Assert.Contains("International shipping is excluded; quoted separately", body, StringComparison.Ordinal);
            Assert.DoesNotContain("estimated domestic shipping", body, StringComparison.Ordinal);
            Assert.Contains("Shipping to JP", body, StringComparison.Ordinal);
            Assert.Contains("VAT is calculated only on the included amount", body, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void PreservesPreparedTaxBranchAndAuthoritativeLeadTimeRange()
    {
        var summary = Summary();
        var plan = AdditiveQuotationNotificationComposer.Compose(summary with
        {
            Customer = summary.Customer with { TaxIdentification = "1234567890123 (สาขาที่ 00001)" },
            LeadTimeMinimumDays = 3,
        });
        foreach (var body in new[] { plan.Customer.Body, plan.Manufacturing.Body })
        {
            Assert.Contains(System.Net.WebUtility.HtmlEncode("1234567890123 (สาขาที่ 00001)"), body, StringComparison.Ordinal);
            Assert.Contains("Lead time: 3-7 days", body, StringComparison.Ordinal);
        }
    }

    private static AdditiveQuotationNotificationSummary Summary() => new(
        42, new("Thai", "Customer", "customer@example.test", null, "Thailand", null, null, "Review the sealing face"),
        [new("part.stl", "ABS", "Black", "10 x 20 x 30", null, 2, 123.45m, 246.90m,
            PrintProcess.Fdm, BuildPreference.Strength, new Uri("https://files.example/part?token=fixture"), 12.5m, 25m)],
        345.67m, 7, true, "TH", 7);
}
