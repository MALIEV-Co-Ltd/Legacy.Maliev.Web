using System.Globalization;
using System.Xml.Linq;
using Legacy.Maliev.Web.Application;
using Legacy.Maliev.Web.Components.Pages.InstantQuotation;

namespace Legacy.Maliev.Web.Tests;

public sealed class InstantQuotationAccessibilityContractTests
{
    [Fact]
    public void RepeatedWorkflowControlsHaveItemSpecificNamesAndSelectionState()
    {
        var workflow = ReadComponent("InstantQuotationWorkflow.razor");

        Assert.Contains("Cancel upload for {0}", workflow, StringComparison.Ordinal);
        Assert.Contains("Try upload again for {0}", workflow, StringComparison.Ordinal);
        Assert.Contains("View {0}", workflow, StringComparison.Ordinal);
        Assert.Contains("Remove {0}", workflow, StringComparison.Ordinal);
        Assert.Contains("aria-pressed=", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void WizardTransitionsMoveFocusAndUseActionGroupSemantics()
    {
        var workflow = ReadComponent("InstantQuotationWorkflow.razor");
        var codeBehind = ReadComponent("InstantQuotationWorkflow.razor.cs");
        var review = ReadComponent("InstantQuotationReview.razor");
        var customer = ReadComponent("InstantQuotationCustomerForm.razor");

        Assert.Contains("@ref=\"configurationSection\"", workflow, StringComparison.Ordinal);
        Assert.Contains("@ref=\"reviewSection\"", workflow, StringComparison.Ordinal);
        Assert.Contains("@ref=\"customerDetailsSection\"", workflow, StringComparison.Ordinal);
        Assert.Contains("tabindex=\"-1\"", workflow, StringComparison.Ordinal);
        Assert.Contains("FocusAsync", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("<nav", review, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<nav", customer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("role=\"group\"", review, StringComparison.Ordinal);
        Assert.Contains("role=\"group\"", customer, StringComparison.Ordinal);
    }

    [Fact]
    public void UploadConstraintsAndAuthoritativePriceChangesAreAnnounced()
    {
        var workflow = ReadComponent("InstantQuotationWorkflow.razor");

        Assert.Contains("Supported files: STL, OBJ, 3MF, GLB, GLTF, STP, STEP, IGS, and IGES. Maximum 100 files, 100 MB each.", workflow, StringComparison.Ordinal);
        Assert.Contains("<strong aria-live=\"polite\" aria-atomic=\"true\">@Money(OrderQuote?.FinalOrderPrice)</strong>", workflow, StringComparison.Ordinal);
        var priceTotal = Assert.Single(workflow.Split('\n'), line =>
            line.Contains("<strong", StringComparison.Ordinal)
            && line.Contains("@Money(OrderQuote?.FinalOrderPrice)", StringComparison.Ordinal));
        Assert.DoesNotContain("role=\"status\" aria-live=\"polite\"", priceTotal, StringComparison.Ordinal);
        Assert.Contains("<span role=\"status\" aria-live=\"polite\">@Localizer[\"{0} parts selected\", SelectedBulkCount]</span>", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void WallThicknessAdvisoryIsLocalizedKeyboardOperableAndDoesNotGateQuotation()
    {
        var workflow = ReadComponent("InstantQuotationWorkflow.razor");
        var code = ReadComponent("InstantQuotationWorkflow.razor.cs");
        var resource = XDocument.Load(Path.Combine(
            FindRepositoryRoot(),
            "Legacy.Maliev.Web",
            "Resources",
            "Components",
            "Pages",
            "InstantQuotation",
            "ThreeDimensionalPrintingEstimateContent.th.resx"));
        var keys = resource.Descendants("data")
            .Select(element => (string?)element.Attribute("name"))
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("instant-quote__thickness-toggle", workflow, StringComparison.Ordinal);
        Assert.Contains("aria-pressed=", workflow, StringComparison.Ordinal);
        Assert.Contains("disabled=\"@(SelectedThickness?.Available != true)\"", workflow, StringComparison.Ordinal);
        Assert.Contains("data-dfm-code=\"thin-wall\"", workflow, StringComparison.Ordinal);
        Assert.Contains("data-dfm-code=\"thickness-incomplete\"", workflow, StringComparison.Ordinal);
        Assert.Contains("role=\"status\"", workflow, StringComparison.Ordinal);
        Assert.Contains("ReportThicknessStateAsync", code, StringComparison.Ordinal);
        Assert.Contains("ToggleWallThicknessAsync", code, StringComparison.Ordinal);
        Assert.Contains("Wall thickness", keys);
        Assert.Contains("Thin walls may print with defects", keys);
        Assert.Contains("Thickness analysis is incomplete", keys);
        Assert.DoesNotContain("CanEnterReview && !ThicknessNeedsAttention", code, StringComparison.Ordinal);
    }

    [Fact]
    public void LocalizedDisplayLabelsPreserveCanonicalOptionValues()
    {
        var workflow = ReadComponent("InstantQuotationWorkflow.razor");
        var review = ReadComponent("InstantQuotationReview.razor");
        var customer = ReadComponent("InstantQuotationCustomerForm.razor");

        var selector = ReadComponent("InstantQuotationMaterialSelector.razor");
        Assert.Contains("SelectedMaterialKey=\"@part.Configuration.MaterialKey\"", workflow, StringComparison.Ordinal);
        Assert.Contains("ChangeMaterialAsync(part.PartId, new ChangeEventArgs { Value = key })", workflow, StringComparison.Ordinal);
        Assert.Contains("role=\"radiogroup\" aria-label=\"@Localizer[\"Material\"]\"", selector, StringComparison.Ordinal);
        Assert.Contains("role=\"radio\" aria-label=", selector, StringComparison.Ordinal);
        Assert.Contains("Localizer[material.Category]", selector, StringComparison.Ordinal);
        Assert.Contains("data-material-key=\"@material.Key\"", selector, StringComparison.Ordinal);
        Assert.Contains("aria-checked=\"@selected.ToString().ToLowerInvariant()\"", selector, StringComparison.Ordinal);
        Assert.Contains("tabindex=\"@(selected ? 0 : -1)\"", selector, StringComparison.Ordinal);
        Assert.Contains("SelectionChanged.InvokeAsync(material.Key)", selector, StringComparison.Ordinal);
        const string colorOption = "<option value=\"@color\">@Localizer[color == \"Any\" ? \"No preference\" : color]</option>";
        Assert.Contains(colorOption, workflow, StringComparison.Ordinal);
        Assert.Equal(3, workflow.Split('\n').Count(line => line.Contains(colorOption, StringComparison.Ordinal)));
        Assert.Contains("<option value=\"@material.Key\">@Localizer[material.DisplayName]</option>", review, StringComparison.Ordinal);
        Assert.Contains(colorOption, review, StringComparison.Ordinal);
        Assert.Single(review.Split('\n'), line => line.Contains(colorOption, StringComparison.Ordinal));
        var resource = XDocument.Load(Path.Combine(
            FindRepositoryRoot(), "Legacy.Maliev.Web", "Resources", "Components", "Pages", "InstantQuotation",
            "ThreeDimensionalPrintingEstimateContent.th.resx"));
        Assert.Equal("ไม่ระบุสี", Assert.Single(resource.Descendants("data"),
            element => (string?)element.Attribute("name") == "No preference").Element("value")?.Value);
        Assert.Contains("value=\"@country.Name\"", customer, StringComparison.Ordinal);
        Assert.Contains("@country.DisplayName", customer, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectedCustomerFieldsExposeControlledSummaryAndAssociations()
    {
        var customer = ReadComponent("InstantQuotationCustomerForm.razor");

        Assert.Contains("instant-quote__validation-summary", customer, StringComparison.Ordinal);
        Assert.Contains("role=\"alert\" tabindex=\"-1\" autofocus", customer, StringComparison.Ordinal);
        Assert.Contains("aria-invalid=", customer, StringComparison.Ordinal);
        Assert.Contains("aria-describedby=", customer, StringComparison.Ordinal);
        Assert.Contains("FieldError", customer, StringComparison.Ordinal);

        var fields = InstantQuotationPage.ControlledValidationFields(
            "[\"BillingBuilding\",\"Email\",\"FirstName\",\"ShippingBuilding\",\"Email\",\"RawServiceError\"]");

        Assert.Equal(["BillingBuilding", "Email", "FirstName", "ShippingBuilding"], fields);
    }

    [Theory]
    [InlineData("TH", "Thailand", "ไทย")]
    [InlineData("US", "United States", "สหรัฐอเมริกา")]
    [InlineData("DE", "Germany", "เยอรมนี")]
    public void CountryLabels_LocalizeThaiDisplayWithoutChangingCanonicalValue(
        string iso2,
        string canonicalName,
        string expectedThai)
    {
        var country = new Country(1, canonicalName, null, null, iso2, null, null, null);

        var thai = InstantQuotationCountryLabels.DisplayName(country, CultureInfo.GetCultureInfo("th-TH"));
        var english = InstantQuotationCountryLabels.DisplayName(country, CultureInfo.GetCultureInfo("en-US"));
        var option = new InstantQuotationCountryOption(canonicalName, thai);

        Assert.Equal(expectedThai, option.DisplayName);
        Assert.Equal(canonicalName, option.Name);
        Assert.Equal(canonicalName, english);
    }

    private static string ReadComponent(string name) => File.ReadAllText(Path.Combine(
        FindRepositoryRoot(),
        "Legacy.Maliev.Web",
        "Components",
        "Pages",
        "InstantQuotation",
        name));

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
