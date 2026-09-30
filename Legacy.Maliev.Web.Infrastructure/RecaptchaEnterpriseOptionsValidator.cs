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

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
