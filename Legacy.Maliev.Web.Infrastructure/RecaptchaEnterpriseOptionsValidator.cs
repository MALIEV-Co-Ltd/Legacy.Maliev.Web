using Microsoft.Extensions.Options;

namespace Legacy.Maliev.Web.Infrastructure;

internal sealed class RecaptchaEnterpriseOptionsValidator : IValidateOptions<RecaptchaEnterpriseOptions>
{
    public ValidateOptionsResult Validate(string? name, RecaptchaEnterpriseOptions options)
    {
        var failures = new List<string>();
        if (string.IsNullOrWhiteSpace(options.ProjectId))
        {
            failures.Add("Recaptcha:ProjectId is required.");
        }

        if (string.IsNullOrWhiteSpace(options.SiteKey))
        {
            failures.Add("Recaptcha:SiteKey is required.");
        }

        if (!string.IsNullOrWhiteSpace(options.CredentialsPath))
        {
            try
            {
                if (!Path.IsPathFullyQualified(options.CredentialsPath) || !File.Exists(options.CredentialsPath))
                {
                    failures.Add("Recaptcha:CredentialsPath must name an absolute readable file.");
                }
                else
                {
                    using var stream = File.OpenRead(options.CredentialsPath);
                    if (!stream.CanRead) failures.Add("Recaptcha:CredentialsPath must name an absolute readable file.");
                }
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException)
            {
                failures.Add("Recaptcha:CredentialsPath must name an absolute readable file.");
            }
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
