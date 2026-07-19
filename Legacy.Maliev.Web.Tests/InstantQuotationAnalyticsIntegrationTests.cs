using Legacy.Maliev.Web.Components.Pages.InstantQuotation;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Legacy.Maliev.Web.Tests;

public sealed class InstantQuotationAnalyticsIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public InstantQuotationAnalyticsIntegrationTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory.WithWebHostBuilder(builder => builder.UseSetting("environment", "Testing"));
    }

    [Fact]
    public void DefaultRuntimeRegistration_ActivatesReviewedTrackerAndJsSinkPerCircuitScope()
    {
        using var firstScope = factory.Services.CreateScope();
        using var secondScope = factory.Services.CreateScope();

        var firstTracker = firstScope.ServiceProvider.GetRequiredService<IInstantQuotationAnalyticsTracker>();
        var secondTracker = secondScope.ServiceProvider.GetRequiredService<IInstantQuotationAnalyticsTracker>();
        var sink = firstScope.ServiceProvider.GetRequiredService<IInstantQuotationAnalyticsSink>();

        Assert.IsType<InstantQuotationAnalyticsTracker>(firstTracker);
        Assert.IsType<JsInstantQuotationAnalyticsSink>(sink);
        Assert.NotSame(firstTracker, secondTracker);
    }

    [Theory]
    [InlineData(true, false, true, false, 1, true, false)]
    [InlineData(false, true, true, true, 1, false, true)]
    [InlineData(true, false, false, true, 1, false, false)]
    [InlineData(false, true, true, false, 1, false, false)]
    [InlineData(true, false, true, true, 0, false, false)]
    [InlineData(false, true, true, true, 0, false, false)]
    [InlineData(false, false, true, true, 1, false, false)]
    public void VisibleMilestones_RequireCompleteCurrentAuthoritativeQuote(
        bool configurationVisible,
        bool reviewVisible,
        bool estimateComplete,
        bool reviewQuoteComplete,
        long revision,
        bool expectedEstimate,
        bool expectedReview)
    {
        var result = InstantQuotationWorkflow.GetVisibleAnalyticsMilestones(
            configurationVisible,
            reviewVisible,
            estimateComplete,
            reviewQuoteComplete,
            revision);

        Assert.Equal(expectedEstimate, result.EstimateShown);
        Assert.Equal(expectedReview, result.ReviewReached);
    }

    [Fact]
    public void UploadStart_IsEmittedOnlyAfterAUsableAnalyzedBatchAndBeforeReservation()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "Legacy.Maliev.Web",
            "Components",
            "Pages",
            "InstantQuotation",
            "InstantQuotationWorkflow.razor.cs"));
        var nonEmptyGate = source.IndexOf("if (analyzed.Count == 0)", StringComparison.Ordinal);
        var emission = source.IndexOf("analytics.RecordUploadStartAsync", StringComparison.Ordinal);
        var reservation = source.IndexOf("workflow.ReserveUploads(files)", StringComparison.Ordinal);

        Assert.True(nonEmptyGate >= 0 && nonEmptyGate < emission);
        Assert.True(emission < reservation);
        Assert.Contains("analyzed.Count", source[emission..reservation], StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Web.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}
