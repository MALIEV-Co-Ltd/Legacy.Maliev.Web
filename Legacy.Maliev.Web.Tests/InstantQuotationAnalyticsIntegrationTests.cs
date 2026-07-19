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
    public void DefaultRuntimeRegistration_KeepsPendingEventsInactive()
    {
        using var scope = factory.Services.CreateScope();

        var tracker = scope.ServiceProvider.GetRequiredService<IInstantQuotationAnalyticsTracker>();

        Assert.Same(NoOpInstantQuotationAnalyticsTracker.Instance, tracker);
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
}
