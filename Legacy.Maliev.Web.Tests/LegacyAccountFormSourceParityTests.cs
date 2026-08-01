namespace Legacy.Maliev.Web.Tests;

public sealed class LegacyAccountFormSourceParityTests
{
    [Fact]
    public void SharedFormHardeningMatchesCurrentPublicContract()
    {
        var root = FindRepositoryRoot();
        var web = Path.Combine(root, "Legacy.Maliev.Web");
        var css = File.ReadAllText(Path.Combine(web, "wwwroot", "src", "app", "css", "application-shell.css"));
        var js = File.ReadAllText(Path.Combine(web, "wwwroot", "src", "app", "js", "app.js"));

        Assert.Contains("--maliev-container: min(92vw, 86rem)", css, StringComparison.Ordinal);
        Assert.Contains(".maliev-form button:disabled", css, StringComparison.Ordinal);
        Assert.Contains(".maliev-form[aria-busy=\"true\"] button[type=\"submit\"]", css, StringComparison.Ordinal);
        Assert.Contains(".auth-check__input", css, StringComparison.Ordinal);
        Assert.Contains(".grecaptcha-badge", css, StringComparison.Ordinal);
        Assert.Contains("querySelector('[data-submit-label]')", js, StringComparison.Ordinal);
        Assert.Contains("function GuardRecaptchaSubmit(", js, StringComparison.Ordinal);
        Assert.Contains("function GuardSingleSubmit(", js, StringComparison.Ordinal);
    }

    [Fact]
    public void AccountAndInquiryFormsKeepThaiSafeLabelsAndRecaptchaDisclosure()
    {
        var web = Path.Combine(FindRepositoryRoot(), "Legacy.Maliev.Web");
        var login = File.ReadAllText(Path.Combine(web, "Components", "Pages", "Account", "LoginContent.razor"));
        var signup = File.ReadAllText(Path.Combine(web, "Components", "Pages", "Account", "SignupContent.razor"));
        var contact = File.ReadAllText(Path.Combine(web, "Components", "Pages", "Contact", "ContactFormFields.razor"));
        var quotation = File.ReadAllText(Path.Combine(web, "Components", "Pages", "Quotation", "QuotationFormFields.razor"));

        Assert.Contains("class=\"auth-check__input\"", login, StringComparison.Ordinal);
        Assert.DoesNotContain("asp-for=\"RememberMe\"", login, StringComparison.Ordinal);
        Assert.Contains("name=\"RememberMe\" value=\"true\"", login, StringComparison.Ordinal);
        Assert.Contains("name=\"RememberMe\" value=\"false\"", login, StringComparison.Ordinal);
        Assert.Contains("minlength=\"8\"", signup, StringComparison.Ordinal);
        Assert.Contains("By signing up, you agree to our", signup, StringComparison.Ordinal);
        Assert.DoesNotContain("you are agree", signup, StringComparison.OrdinalIgnoreCase);

        foreach (var form in new[] { signup, contact, quotation })
        {
            Assert.Contains("This site is protected by reCAPTCHA and the Google", form, StringComparison.Ordinal);
            Assert.Contains("https://policies.google.com/privacy", form, StringComparison.Ordinal);
            Assert.Contains("https://policies.google.com/terms", form, StringComparison.Ordinal);
            Assert.Contains("data-recaptcha-error", form, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void LegacyFallbackFormsUseTheSameGuardedSubmitContract()
    {
        var web = Path.Combine(FindRepositoryRoot(), "Legacy.Maliev.Web");
        var login = File.ReadAllText(Path.Combine(web, "Pages", "Account", "Login.cshtml"));
        var signup = File.ReadAllText(Path.Combine(web, "Pages", "Account", "Signup.cshtml"));
        var contact = File.ReadAllText(Path.Combine(web, "Pages", "Contact", "Index.cshtml"));
        var quotation = File.ReadAllText(Path.Combine(web, "Pages", "Quotation", "Index.cshtml"));

        Assert.Contains("data-single-submit", login, StringComparison.Ordinal);
        Assert.Contains("GuardSingleSubmit(\"customer-login\")", login, StringComparison.Ordinal);
        Assert.Contains("GuardRecaptchaSubmit(\"customer-signup\"", signup, StringComparison.Ordinal);
        Assert.Contains("GuardRecaptchaSubmit(\"contact-us\"", contact, StringComparison.Ordinal);
        Assert.Contains("GuardRecaptchaSubmit(\"quotation-form\"", quotation, StringComparison.Ordinal);
        Assert.Contains("data-upload-analytics=\"true\"", quotation, StringComparison.Ordinal);
    }

    [Fact]
    public void PrimaryPhoneNumberIsConsistentAndPrioritized()
    {
        var web = Path.Combine(FindRepositoryRoot(), "Legacy.Maliev.Web");
        var footer = File.ReadAllText(Path.Combine(web, "Components", "Layout", "PublicFooter.razor"));
        var contactUs = File.ReadAllText(Path.Combine(web, "Pages", "Shared", "_ContactUsSectionPartial.cshtml"));

        Assert.True(footer.IndexOf("+66(0)89-895-0690", StringComparison.Ordinal)
            < footer.IndexOf("+66(0)81-803-0404", StringComparison.Ordinal));
        Assert.Contains("+66(0)89-895-0690", contactUs, StringComparison.Ordinal);
        Assert.DoesNotContain("+66(0)81-803-0404", contactUs, StringComparison.Ordinal);
    }

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
