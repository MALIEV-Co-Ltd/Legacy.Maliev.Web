using System.Globalization;
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

        Assert.Contains("Supported files: STL, OBJ, 3MF, GLB, GLTF, STP, STEP, IGS, and IGES. Maximum 100 files, 200 MB each.", workflow, StringComparison.Ordinal);
        Assert.Contains("data-workflow-order-total aria-live=\"polite\" aria-atomic=\"true\"", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("role=\"status\" aria-live=\"polite\"", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void LocalizedDisplayLabelsPreserveCanonicalOptionValues()
    {
        var workflow = ReadComponent("InstantQuotationWorkflow.razor");
        var review = ReadComponent("InstantQuotationReview.razor");
        var customer = ReadComponent("InstantQuotationCustomerForm.razor");

        Assert.Contains("<option value=\"@material.Key\">@Localizer[material.DisplayName]</option>", workflow, StringComparison.Ordinal);
        Assert.Contains("<option value=\"@color\">@Localizer[color]</option>", workflow, StringComparison.Ordinal);
        Assert.Contains("@Localizer[MaterialName(part.Configuration.MaterialKey)]", review, StringComparison.Ordinal);
        Assert.Contains("@Localizer[part.Configuration.Color]", review, StringComparison.Ordinal);
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
            "[\"Email\",\"FirstName\",\"Email\",\"RawServiceError\"]");

        Assert.Equal(["Email", "FirstName"], fields);
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
