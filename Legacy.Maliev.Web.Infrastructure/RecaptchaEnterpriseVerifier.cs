using Google.Api.Gax.ResourceNames;
using Google.Cloud.RecaptchaEnterprise.V1;
using Legacy.Maliev.Web.Application;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Legacy.Maliev.Web.Infrastructure;

public sealed class RecaptchaEnterpriseOptions
{
    public string SiteKey { get; set; } = string.Empty;

    public string ProjectId { get; set; } = string.Empty;

    public float MinimumScore { get; set; } = 0.5f;
}

internal sealed record RecaptchaAssessment(bool TokenValid, string? Action, float Score);

internal interface IRecaptchaAssessmentClient
{
    Task<RecaptchaAssessment> AssessAsync(
        string projectId,
        string siteKey,
        string token,
        string expectedAction,
        CancellationToken cancellationToken);
}

internal sealed class GoogleRecaptchaAssessmentClient : IRecaptchaAssessmentClient
{
    private readonly Lazy<Task<RecaptchaEnterpriseServiceClient>> client =
        new(() => RecaptchaEnterpriseServiceClient.CreateAsync());

    public async Task<RecaptchaAssessment> AssessAsync(
        string projectId,
        string siteKey,
        string token,
        string expectedAction,
        CancellationToken cancellationToken)
    {
        var serviceClient = await client.Value;
        var response = await serviceClient.CreateAssessmentAsync(
            new CreateAssessmentRequest
            {
                ParentAsProjectName = new ProjectName(projectId),
                Assessment = new Assessment
                {
                    Event = new Event
                    {
                        Token = token,
                        SiteKey = siteKey,
                        ExpectedAction = expectedAction
                    }
                }
            },
            cancellationToken);
        return new RecaptchaAssessment(
            response.TokenProperties.Valid,
            response.TokenProperties.Action,
            response.RiskAnalysis.Score);
    }
}

internal sealed class RecaptchaEnterpriseVerifier(
    IRecaptchaAssessmentClient assessmentClient,
    IOptions<RecaptchaEnterpriseOptions> options,
    ILogger<RecaptchaEnterpriseVerifier> logger) : IAntiBotVerifier
{
    public async Task<bool> VerifyAsync(
        string? token,
        string expectedAction,
        CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(token)
            || string.IsNullOrWhiteSpace(expectedAction)
            || string.IsNullOrWhiteSpace(settings.ProjectId)
            || string.IsNullOrWhiteSpace(settings.SiteKey))
        {
            logger.LogWarning("reCAPTCHA Enterprise verification is not configured or received no token.");
            return false;
        }

        try
        {
            var assessment = await assessmentClient.AssessAsync(
                settings.ProjectId,
                settings.SiteKey,
                token,
                expectedAction,
                cancellationToken);
            return assessment.TokenValid
                && string.Equals(assessment.Action, expectedAction, StringComparison.Ordinal)
                && assessment.Score >= settings.MinimumScore;
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "reCAPTCHA Enterprise assessment was unavailable.");
            return false;
        }
    }
}
