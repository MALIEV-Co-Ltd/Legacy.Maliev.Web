using Legacy.Maliev.Web.Infrastructure;
using Microsoft.Extensions.Logging;
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

    [Theory]
    [InlineData(null, "submit", "test-project", "test-site-key")]
    [InlineData(" \t ", "submit", "test-project", "test-site-key")]
    [InlineData("browser-token", " \t ", "test-project", "test-site-key")]
    [InlineData("browser-token", "submit", "", "test-site-key")]
    [InlineData("browser-token", "submit", " \t ", "test-site-key")]
    [InlineData("browser-token", "submit", "test-project", "")]
    [InlineData("browser-token", "submit", "test-project", " \t ")]
    public async Task Verify_BlankInputOrIdentifier_FailsClosedBeforeAssessment(
        string? token,
        string action,
        string projectId,
        string siteKey)
    {
        var client = new StubAssessmentClient(new RecaptchaAssessment(true, "submit", 1));
        var verifier = new RecaptchaEnterpriseVerifier(
            client,
            Options.Create(new RecaptchaEnterpriseOptions { ProjectId = projectId, SiteKey = siteKey }),
            NullLogger<RecaptchaEnterpriseVerifier>.Instance);

        Assert.False(await verifier.VerifyAsync(token, action, CancellationToken.None));
        Assert.Equal(0, client.CallCount);
    }

    [Fact]
    public async Task Verify_ConfiguredInput_ForwardsExactAssessmentIntent()
    {
        var client = new StubAssessmentClient(new RecaptchaAssessment(true, "submit", 0.9f));
        var verifier = new RecaptchaEnterpriseVerifier(
            client,
            Options.Create(ValidOptions()),
            NullLogger<RecaptchaEnterpriseVerifier>.Instance);

        Assert.True(await verifier.VerifyAsync("browser-token", "submit", CancellationToken.None));
        Assert.Equal(("test-project", "test-site-key", "browser-token", "submit"), client.LastIntent);
        Assert.Equal(1, client.CallCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Verify_ProviderFailure_DoesNotEmitUserBoundExceptionPayload(bool providerCancellation)
    {
        const string payload = "token=sample-user-token; email=customer@example.invalid; phone=+66800000000";
        Exception failure = providerCancellation ? new OperationCanceledException(payload) : new InvalidOperationException(payload);
        var logger = new CapturingLogger();
        var verifier = new RecaptchaEnterpriseVerifier(new ThrowingAssessmentClient(failure), Options.Create(ValidOptions()), logger);

        Assert.False(await verifier.VerifyAsync("sample-user-token", "submit", CancellationToken.None));

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        var emitted = entry.Message + entry.Exception;
        Assert.DoesNotContain("sample-user-token", emitted, StringComparison.Ordinal);
        Assert.DoesNotContain("customer@example.invalid", emitted, StringComparison.Ordinal);
        Assert.DoesNotContain("+66800000000", emitted, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Verify_CallerCancellation_PropagatesWithoutLogging()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var logger = new CapturingLogger();
        var verifier = new RecaptchaEnterpriseVerifier(
            new ThrowingAssessmentClient(new OperationCanceledException(cancellation.Token)),
            Options.Create(ValidOptions()),
            logger);

        await Assert.ThrowsAsync<OperationCanceledException>(() => verifier.VerifyAsync("browser-token", "submit", cancellation.Token));
        Assert.Empty(logger.Entries);
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

        public (string ProjectId, string SiteKey, string Token, string Action)? LastIntent { get; private set; }

        public Task<RecaptchaAssessment> AssessAsync(
            string projectId,
            string siteKey,
            string token,
            string expectedAction,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastIntent = (projectId, siteKey, token, expectedAction);
            return Task.FromResult(assessment);
        }
    }

    private sealed class ThrowingAssessmentClient(Exception failure) : IRecaptchaAssessmentClient
    {
        public Task<RecaptchaAssessment> AssessAsync(string projectId, string siteKey, string token, string expectedAction, CancellationToken cancellationToken)
            => Task.FromException<RecaptchaAssessment>(failure);
    }

    private sealed class CapturingLogger : ILogger<RecaptchaEnterpriseVerifier>
    {
        public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception), exception));
    }
}
