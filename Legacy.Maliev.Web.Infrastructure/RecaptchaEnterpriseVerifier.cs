using Google.Api.Gax.ResourceNames;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.RecaptchaEnterprise.V1;
using Legacy.Maliev.Web.Application;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Legacy.Maliev.Web.Infrastructure;

public sealed class RecaptchaEnterpriseOptions
{
    public string SiteKey { get; set; } = string.Empty;

    public string ProjectId { get; set; } = string.Empty;

    public string KeyId { get; set; } = string.Empty;

    public string? CredentialsPath { get; set; }

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

internal sealed class GoogleRecaptchaAssessmentClient(IOptions<RecaptchaEnterpriseOptions> options) : IRecaptchaAssessmentClient
{
    private readonly Lazy<Task<RecaptchaEnterpriseServiceClient>> client =
        new(() => CreateBuilder(options.Value).BuildAsync());

    internal static RecaptchaEnterpriseServiceClientBuilder CreateBuilder(
        RecaptchaEnterpriseOptions options, Func<string, GoogleCredential>? loadCredential = null)
    {
        var builder = new RecaptchaEnterpriseServiceClientBuilder();
        if (!string.IsNullOrWhiteSpace(options.CredentialsPath))
        {
            // This builder is created only by the lazy assessment client. Explicit
            // mounted credentials are service-account documents; ADC remains the
            // default for ambient workload identity when no path is configured.
            builder.GoogleCredential = loadCredential is null
                ? CredentialFactory.FromFile<ServiceAccountCredential>(options.CredentialsPath).ToGoogleCredential()
                : loadCredential(options.CredentialsPath);
        }
        return builder;
    }

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
            logger.LogWarning("reCAPTCHA Enterprise assessment was unavailable.");
            return false;
        }
    }
}
