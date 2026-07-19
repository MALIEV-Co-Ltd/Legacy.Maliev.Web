using Legacy.Maliev.Web.Components.Pages.InstantQuotation;

namespace Legacy.Maliev.Web.Tests;

public sealed class InstantQuotationReviewCustomerTests
{
    [Fact]
    public void Review_RendersOnlyAuthoritativePartAndOrderFields()
    {
        var review = ReadComponent("InstantQuotationReview.razor");

        foreach (var value in new[]
        {
            "DisplayFileName",
            "MaterialKey",
            "Color",
            "Quantity",
            "UnitPrice",
            "Subtotal",
            "ItemsSubtotal",
            "ShippingCost",
            "Vat",
            "FinalOrderPrice",
            "LeadTimeMinimumDays",
            "LeadTimeMaximumDays",
            "data-workflow-review",
            "Price and lead time are estimates",
        })
        {
            Assert.Contains(value, review, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("UploadReference", review, StringComparison.Ordinal);
        Assert.DoesNotContain("SessionId", review, StringComparison.Ordinal);
    }

    [Fact]
    public void CustomerForm_IsAPlainAntiforgeryProtectedPostWithExactPublicFields()
    {
        var form = ReadComponent("InstantQuotationCustomerForm.razor");

        Assert.Contains("method=\"post\"", form, StringComparison.Ordinal);
        Assert.Contains("action=\"/InstantQuotation/3D-Printing?handler=SubmitRequest\"", form, StringComparison.Ordinal);
        Assert.Contains("name=\"@Model.AntiforgeryFieldName\"", form, StringComparison.Ordinal);
        Assert.Contains("value=\"@Model.AntiforgeryRequestToken\"", form, StringComparison.Ordinal);

        foreach (var field in new[] { "FirstName", "LastName", "Email", "Telephone", "Country" })
        {
            Assert.Contains($"name=\"{field}\"", form, StringComparison.Ordinal);
            Assert.Contains($"id=\"instant-quote-{field.ToLowerInvariant()}\"", form, StringComparison.Ordinal);
            Assert.Contains("required", FieldTag(form, field), StringComparison.Ordinal);
        }

        foreach (var field in new[] { "Company", "TaxNumber" })
        {
            Assert.Contains($"name=\"{field}\"", form, StringComparison.Ordinal);
            Assert.DoesNotContain("required", FieldTag(form, field), StringComparison.Ordinal);
        }

        foreach (var field in new[] { "FirstName", "LastName", "Email", "Telephone", "Company", "TaxNumber" })
        {
            Assert.Contains("maxlength=\"50\"", FieldTag(form, field), StringComparison.Ordinal);
        }

        Assert.Contains("name=\"Description\"", form, StringComparison.Ordinal);
        Assert.Contains("maxlength=\"512\"", FieldTag(form, "Description"), StringComparison.Ordinal);
        Assert.DoesNotContain("SubmissionId", form, StringComparison.Ordinal);
        Assert.DoesNotContain("HttpContext", form, StringComparison.Ordinal);
    }

    [Fact]
    public void StaticPage_MintsBoundaryDataAndPassesOnlyDisplayModelToInteractiveIsland()
    {
        var page = ReadComponent("InstantQuotationPage.razor");
        var content = ReadComponent("ThreeDimensionalPrintingEstimateContent.razor");

        Assert.Contains("GetAndStoreTokens(context)", page, StringComparison.Ordinal);
        Assert.Contains("CreateLinkedTokenSource(context.RequestAborted)", page, StringComparison.Ordinal);
        Assert.Contains("GetCountriesAsync(timeout.Token).WaitAsync(timeout.Token)", page, StringComparison.Ordinal);
        Assert.Contains("GetCustomerDatabaseIdAsync(context", page, StringComparison.Ordinal);
        Assert.Contains("GetProfileAsync", page, StringComparison.Ordinal);
        Assert.Contains("InstantQuotationSubmissionStatus", page, StringComparison.Ordinal);
        Assert.Contains("InstantQuotationRequestReference", page, StringComparison.Ordinal);
        Assert.Contains("InstantQuotationProblemCategory", page, StringComparison.Ordinal);
        Assert.Contains("CustomerModel=", content, StringComparison.Ordinal);
        Assert.DoesNotContain("IHttpContextAccessor", ReadComponent("InstantQuotationWorkflow.razor.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void Workflow_ExposesEnabledThreeStepNavigationWithoutRepricing()
    {
        var markup = ReadComponent("InstantQuotationWorkflow.razor");
        var code = ReadComponent("InstantQuotationWorkflow.razor.cs");
        var coordinator = ReadComponent("InstantQuotationWorkflowCoordinator.cs");

        Assert.Contains("EnterReview", markup, StringComparison.Ordinal);
        Assert.Contains("EnterCustomerDetails", markup, StringComparison.Ordinal);
        Assert.Contains("ReturnToConfiguration", markup, StringComparison.Ordinal);
        Assert.Contains("ReturnToReview", markup, StringComparison.Ordinal);
        Assert.Contains("<InstantQuotationReview", markup, StringComparison.Ordinal);
        Assert.Contains("<InstantQuotationCustomerForm", markup, StringComparison.Ordinal);
        Assert.Contains(".EnterReview();", code, StringComparison.Ordinal);
        Assert.Contains(".EnterCustomerDetails();", code, StringComparison.Ordinal);
        Assert.DoesNotContain("pricingService.Quote", NavigationMethods(coordinator), StringComparison.Ordinal);
    }

    [Fact]
    public void Workflow_RendersUploadDerivedGeometryAndExactFiniteDfmCodes()
    {
        var markup = ReadComponent("InstantQuotationWorkflow.razor");

        foreach (var marker in new[]
        {
            "data-workflow-dfm-status",
            "data-workflow-dfm-part",
            "DimensionXmm",
            "DimensionYmm",
            "DimensionZmm",
            "VolumeMm3",
            "SurfaceAreaMm2",
            "MinThicknessMm",
            "FacetCount",
            "topology-not-checked",
            "non-watertight",
            "non-manifold",
            "multiple-bodies",
            "dimension-too-small",
            "dimension-too-large",
        })
        {
            Assert.Contains(marker, markup, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("Sha256", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("UploadReference", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void SubmissionOutcome_UsesControlledStableStatusAndPartialNoResubmitCopy()
    {
        var model = ReadComponent("InstantQuotationCustomerDisplayModel.cs");
        var markup = ReadComponent("InstantQuotationWorkflow.razor");

        Assert.Contains("completed", model, StringComparison.Ordinal);
        Assert.Contains("partial", model, StringComparison.Ordinal);
        Assert.Contains("rejected", model, StringComparison.Ordinal);
        Assert.Contains("do not resubmit", markup, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("href=\"/InstantQuotation/3D-Printing\"", markup, StringComparison.Ordinal);
        Assert.Contains("data-workflow-submitted", markup, StringComparison.Ordinal);
        Assert.Contains("RequestReference", markup, StringComparison.Ordinal);
    }

    private static string FieldTag(string source, string field)
    {
        var marker = $"name=\"{field}\"";
        var markerIndex = source.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(markerIndex >= 0, $"Field {field} was not found.");
        var start = source.LastIndexOf('<', markerIndex);
        var end = source.IndexOf('>', markerIndex);
        return source[start..(end + 1)];
    }

    private static string NavigationMethods(string source)
    {
        var start = source.IndexOf("public void EnterReview", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var end = source.IndexOf("public async ValueTask DisposeAsync", start, StringComparison.Ordinal);
        Assert.True(end > start);
        return source[start..end];
    }

    private static string ReadComponent(string fileName) => File.ReadAllText(Path.Combine(
        FindRepositoryRoot(),
        "Legacy.Maliev.Web",
        "Components",
        "Pages",
        "InstantQuotation",
        fileName));

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
