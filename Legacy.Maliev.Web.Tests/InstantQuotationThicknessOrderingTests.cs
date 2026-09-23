using Legacy.Maliev.Web.Components.Pages.InstantQuotation;

namespace Legacy.Maliev.Web.Tests;

public sealed class InstantQuotationThicknessOrderingTests
{
    [Fact]
    public void DelayedReportsCannotResurrectRemovedPartsOrOverwriteNewerAdvisory()
    {
        Assert.True(InstantQuotationWorkflow.ShouldAcceptThicknessReport(true, null, 1));
        Assert.True(InstantQuotationWorkflow.ShouldAcceptThicknessReport(true, 1, 2));
        Assert.False(InstantQuotationWorkflow.ShouldAcceptThicknessReport(true, 2, 1));
        Assert.False(InstantQuotationWorkflow.ShouldAcceptThicknessReport(true, 2, 2));
        Assert.False(InstantQuotationWorkflow.ShouldAcceptThicknessReport(false, null, 3));
        Assert.False(InstantQuotationWorkflow.ShouldAcceptThicknessReport(true, null, 0));
    }
}
