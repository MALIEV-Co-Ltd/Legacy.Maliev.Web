using System.Xml.Linq;

namespace Legacy.Maliev.Web.Tests;

public sealed class BankPaymentLocalizationTests
{
    [Fact]
    public void SupportedPayments_AdvertisesScbInsteadOfKasikornbank()
    {
        var root = FindRepositoryRoot();
        var markup = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Web", "Pages", "Shared", "_SupportedPaymentsPartial.cshtml"));

        Assert.Contains("sprite-payments-logo-scbbank", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("sprite-payments-logo-kasikornbank", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void MemberQuotation_LocalizesBankAndOmitsUnknownTransferMetadata()
    {
        var root = FindRepositoryRoot();
        var markup = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Web", "Components", "Pages", "Member", "MemberQuotationDetailContent.razor"));
        var resource = XDocument.Load(Path.Combine(root, "Legacy.Maliev.Web", "Resources", "Components", "Pages", "Member", "MemberQuotationDetailContent.th.resx"));

        Assert.Contains("@Localizer[account.Bank]", markup, StringComparison.Ordinal);
        Assert.Contains("@Localizer[\"Maliev Co., Ltd.\"]", markup, StringComparison.Ordinal);
        Assert.Contains("string.IsNullOrWhiteSpace(account.Branch)", markup, StringComparison.Ordinal);
        Assert.Contains("string.IsNullOrWhiteSpace(account.Swift)", markup, StringComparison.Ordinal);
        Assert.Equal("ธนาคารไทยพาณิชย์ (SCB)", ResourceValue(resource, "Siam Commercial Bank (SCB)"));
        Assert.Equal("บริษัท มาลีฟ จำกัด", ResourceValue(resource, "Maliev Co., Ltd."));
    }

    private static string ResourceValue(XDocument document, string name) => document.Root!
        .Elements("data")
        .Single(element => string.Equals((string?)element.Attribute("name"), name, StringComparison.Ordinal))
        .Element("value")!
        .Value;

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Web.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
