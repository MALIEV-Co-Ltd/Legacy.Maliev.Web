using Legacy.Maliev.Web.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Legacy.Maliev.Web.Tests;

public sealed class RecaptchaEnterpriseVerifierTests
{
    [Theory]
    [InlineData(true, "submit", 0.5f, true)]
    [InlineData(true, "different", 0.9f, false)]
    [InlineData(true, "submit", 0.49f, false)]
    [InlineData(false, "submit", 0.9f, false)]
    public async Task Verify_RequiresValidMatchingActionAndMinimumRiskScore(
        bool tokenValid,
        string action,
        float score,
        bool expected)
    {
        var verifier = new RecaptchaEnterpriseVerifier(
            new StubAssessmentClient(new RecaptchaAssessment(tokenValid, action, score)),
            Options.Create(ValidOptions()),
            NullLogger<RecaptchaEnterpriseVerifier>.Instance);

        var result = await verifier.VerifyAsync("browser-token", "submit", CancellationToken.None);

        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task Verify_MissingConfigurationFailsClosedWithoutAssessment()
    {
        var client = new StubAssessmentClient(new RecaptchaAssessment(true, "submit", 1));
        var verifier = new RecaptchaEnterpriseVerifier(
            client,
            Options.Create(new RecaptchaEnterpriseOptions()),
            NullLogger<RecaptchaEnterpriseVerifier>.Instance);

        var result = await verifier.VerifyAsync("browser-token", "submit", CancellationToken.None);

        Assert.False(result);
        Assert.Equal(0, client.CallCount);
    }

    private static RecaptchaEnterpriseOptions ValidOptions() =>
        new()
        {
            SiteKey = "test-site-key",
            ProjectId = "test-project",
            MinimumScore = 0.5f
        };

    private sealed class StubAssessmentClient(RecaptchaAssessment assessment) : IRecaptchaAssessmentClient
    {
        public int CallCount { get; private set; }

        public Task<RecaptchaAssessment> AssessAsync(
            string projectId,
            string siteKey,
            string token,
            string expectedAction,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(assessment);
        }
    }
}
