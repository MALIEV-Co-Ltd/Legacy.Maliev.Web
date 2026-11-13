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
    public void AccountCritiqueFixesKeepAccessibleFocusSubmitAndSignupProofContracts()
    {
        var web = Path.Combine(FindRepositoryRoot(), "Legacy.Maliev.Web");
        var css = File.ReadAllText(Path.Combine(web, "wwwroot", "src", "app", "css", "application-shell.css"));
        var js = File.ReadAllText(Path.Combine(web, "wwwroot", "src", "app", "js", "app.js"));
        var signup = File.ReadAllText(Path.Combine(web, "Components", "Pages", "Account", "SignupPage.razor"));
        var signupForm = File.ReadAllText(Path.Combine(web, "Components", "Pages", "Account", "SignupContent.razor"));
        var login = File.ReadAllText(Path.Combine(web, "Pages", "Account", "Login.cshtml"));

        Assert.Contains("outline: 3px solid var(--maliev-blue);", css, StringComparison.Ordinal);
        Assert.Contains(".maliev-page-header a:focus-visible", css, StringComparison.Ordinal);
        Assert.Contains("outline: 3px solid #9cc4ff;", css, StringComparison.Ordinal);
        Assert.DoesNotContain("outline: 3px solid #72a9ff;", css, StringComparison.Ordinal);
        Assert.Contains("GuardSingleSubmit(\"customer-login\")", login, StringComparison.Ordinal);
        Assert.Contains("querySelector('[data-submit-label]')", js, StringComparison.Ordinal);
        Assert.Contains("Six processes under one roof", signup, StringComparison.Ordinal);
        Assert.Contains("Nonthaburi", signup, StringComparison.Ordinal);
        Assert.Contains("/Legal/NonDisclosureAgreement", signup, StringComparison.Ordinal);
        Assert.Contains("By signing up, you agree to our", signupForm, StringComparison.Ordinal);
        Assert.DoesNotContain("you are agree", signupForm, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PrimaryPhoneNumberIsConsistentAndPrioritized()
    {
        var web = Path.Combine(FindRepositoryRoot(), "Legacy.Maliev.Web");
        var footer = File.ReadAllText(Path.Combine(web, "Components", "Layout", "PublicFooter.razor"));
        var contactUs = File.ReadAllText(Path.Combine(web, "Pages", "Shared", "_ContactUsSectionPartial.cshtml"));
        var contactUsThai = File.ReadAllText(Path.Combine(web, "Resources", "Pages", "Shared", "_ContactUsSectionPartial.th.resx"));
        var socialMedia = File.ReadAllText(Path.Combine(web, "Components", "Pages", "About", "SocialMediaContent.razor"));

        Assert.True(footer.IndexOf("+66(0)89-895-0690", StringComparison.Ordinal)
            < footer.IndexOf("+66(0)81-803-0404", StringComparison.Ordinal));
        Assert.Contains("+66(0)89-895-0690", contactUs, StringComparison.Ordinal);
        Assert.DoesNotContain("+66(0)81-803-0404", contactUs, StringComparison.Ordinal);
        Assert.Contains("row-cols-md-5", contactUs, StringComparison.Ordinal);
        Assert.Equal(5, contactUs.Split("<div class=\"col\">", StringSplitOptions.None).Length - 1);
        Assert.Contains("href=\"@Legacy.Maliev.Web.Application.SocialNetworks.WhatsApp\" target=\"_blank\" rel=\"noopener\"", contactUs, StringComparison.Ordinal);
        Assert.Equal(2, contactUs.Split("data-contact-placement=\"contact_section_whatsapp\"", StringSplitOptions.None).Length - 1);
        Assert.Equal(2, contactUs.Split("data-contact-placement=\"contact_section_line\"", StringSplitOptions.None).Length - 1);
        Assert.Equal(2, contactUs.Split("data-maliev-contact-destination=\"whatsapp_business\"", StringSplitOptions.None).Length - 1);
        Assert.Contains("aria-label=\"@Localizer[\"Contact MALIEV on WhatsApp\"]\"", contactUs, StringComparison.Ordinal);
        Assert.Contains("@Localizer[\"WhatsApp\"]", contactUs, StringComparison.Ordinal);
        Assert.Contains("<data name=\"Contact MALIEV on WhatsApp\" xml:space=\"preserve\">", contactUsThai, StringComparison.Ordinal);
        Assert.Contains("<value>ติดต่อ MALIEV ผ่าน WhatsApp</value>", contactUsThai, StringComparison.Ordinal);
        Assert.Contains("<data name=\"WhatsApp\" xml:space=\"preserve\">", contactUsThai, StringComparison.Ordinal);
        Assert.Contains("<value>WhatsApp</value>", contactUsThai, StringComparison.Ordinal);
        Assert.Contains("href=\"@Legacy.Maliev.Web.Application.SocialNetworks.Line\" target=\"_blank\" rel=\"noopener\" data-contact-placement=\"about_line\"", socialMedia, StringComparison.Ordinal);
    }

    [Fact]
    public void CustomerPasswordFormsEnforceTheCurrentEightCharacterMinimum()
    {
        var web = Path.Combine(FindRepositoryRoot(), "Legacy.Maliev.Web");
        var memberChange = File.ReadAllText(Path.Combine(web, "Components", "Pages", "Member", "MemberChangePasswordContent.razor"));
        var memberCreate = File.ReadAllText(Path.Combine(web, "Components", "Pages", "Member", "MemberCreatePasswordContent.razor"));
        var initial = File.ReadAllText(Path.Combine(web, "Components", "Pages", "Account", "SetInitialPasswordContent.razor"));
        var memberChangeHandler = File.ReadAllText(Path.Combine(web, "Areas", "Member", "Pages", "Account", "Manage", "ChangePassword.cshtml.cs"));
        var memberCreateHandler = File.ReadAllText(Path.Combine(web, "Areas", "Member", "Pages", "Account", "Manage", "CreatePassword.cshtml.cs"));
        var initialHandler = File.ReadAllText(Path.Combine(web, "Pages", "Account", "SetInitialPassword.cshtml.cs"));

        Assert.All(new[] { memberChange, memberCreate, initial }, source => Assert.Contains("minlength=\"8\"", source, StringComparison.Ordinal));
        Assert.All(new[] { memberChangeHandler, memberCreateHandler, initialHandler }, source => Assert.Contains("MinimumLength = 8", source, StringComparison.Ordinal));
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
