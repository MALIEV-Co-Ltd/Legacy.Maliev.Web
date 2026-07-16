namespace Legacy.Maliev.Web.Tests;

public sealed class BlazorMigrationContractTests
{
    [Fact]
    public void LegalRoute_UsesStaticSsrComponentWithoutInteractiveServerInfrastructure()
    {
        var root = FindRepositoryRoot();
        var program = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Web", "Program.cs"));
        var legalPage = File.ReadAllText(Path.Combine(
            root,
            "Legacy.Maliev.Web",
            "Pages",
            "Legal",
            "Index.cshtml"));
        var legalComponent = File.ReadAllText(Path.Combine(
            root,
            "Legacy.Maliev.Web",
            "Components",
            "Pages",
            "Legal",
            "LegalContent.razor"));

        Assert.Contains("AddRazorComponents()", program, StringComparison.Ordinal);
        Assert.DoesNotContain("AddInteractiveServerComponents", program, StringComparison.Ordinal);
        Assert.DoesNotContain("MapBlazorHub", program, StringComparison.Ordinal);
        Assert.Contains("type=\"typeof(LegalContent)\"", legalPage, StringComparison.Ordinal);
        Assert.Contains("render-mode=\"Static\"", legalPage, StringComparison.Ordinal);
        Assert.Contains("data-migration-renderer=\"blazor-static-ssr\"", legalComponent, StringComparison.Ordinal);
        Assert.DoesNotContain("@rendermode", legalComponent, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Services/Index.cshtml", "Services/ServicesContent.razor", "ServicesContent")]
    [InlineData("About/SocialMedia.cshtml", "About/SocialMediaContent.razor", "SocialMediaContent")]
    [InlineData("About/Index.cshtml", "About/AboutContent.razor", "AboutContent")]
    [InlineData("Legal/PrivacyPolicy.cshtml", "Legal/PrivacyPolicyContent.razor", "PrivacyPolicyContent")]
    [InlineData("Legal/TermsConditions.cshtml", "Legal/TermsConditionsContent.razor", "TermsConditionsContent")]
    [InlineData("Legal/NonDisclosureAgreement.cshtml", "Legal/NonDisclosureAgreementContent.razor", "NonDisclosureAgreementContent")]
    public void ReadOnlyPublicRoute_UsesNonInteractiveStaticSsrComponent(
        string pagePath,
        string componentPath,
        string componentName)
    {
        var root = FindRepositoryRoot();
        var page = File.ReadAllText(Path.Combine(
            root,
            "Legacy.Maliev.Web",
            "Pages",
            pagePath.Replace('/', Path.DirectorySeparatorChar)));
        var component = File.ReadAllText(Path.Combine(
            root,
            "Legacy.Maliev.Web",
            "Components",
            "Pages",
            componentPath.Replace('/', Path.DirectorySeparatorChar)));

        Assert.Contains($"type=\"typeof({componentName})\"", page, StringComparison.Ordinal);
        Assert.Contains("render-mode=\"Static\"", page, StringComparison.Ordinal);
        Assert.Contains("data-migration-renderer=\"blazor-static-ssr\"", component, StringComparison.Ordinal);
        Assert.DoesNotContain("@rendermode", component, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Web.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the Legacy.Maliev.Web repository root.");
    }

    [Fact]
    public void CustomManufacturingRoute_UsesStaticBlazorBody()
    {
        var root = FindRepositoryRoot();
        var page = File.ReadAllText(Path.Combine(
            root,
            "Legacy.Maliev.Web",
            "Pages",
            "Services",
            "Custom-Manufacturing.cshtml"));

        Assert.Contains("type=\"typeof(CustomManufacturingContent)\"", page, StringComparison.Ordinal);
        Assert.Contains("render-mode=\"Static\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("<main class=\"service-page\">", page, StringComparison.Ordinal);
        Assert.DoesNotContain("<section class=\"service-hero\"", page, StringComparison.Ordinal);
        Assert.Contains("<partial name=\"_SchemaService\"", page, StringComparison.Ordinal);
        Assert.Contains("FAQPage", page, StringComparison.Ordinal);

        var body = File.ReadAllText(Path.Combine(
            root,
            "Legacy.Maliev.Web",
            "Components",
            "Pages",
            "Services",
            "CustomManufacturingContent.razor"));

        Assert.Contains("<ServiceBreadcrumb ServiceKey=\"Custom Manufacturing\" />", body, StringComparison.Ordinal);
        Assert.Contains("<ServiceLocation />", body, StringComparison.Ordinal);
    }

    [Fact]
    public void CncMachiningRoute_UsesStaticBlazorBody()
    {
        var root = FindRepositoryRoot();
        var page = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Web", "Pages", "Services", "CNC-Machining.cshtml"));

        Assert.Contains("type=\"typeof(CncMachiningContent)\"", page, StringComparison.Ordinal);
        Assert.Contains("render-mode=\"Static\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("<main class=\"service-page\">", page, StringComparison.Ordinal);
        Assert.Contains("<partial name=\"_SchemaService\"", page, StringComparison.Ordinal);
        Assert.Contains("FAQPage", page, StringComparison.Ordinal);

        var body = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Web", "Components", "Pages", "Services", "CncMachiningContent.razor"));
        Assert.Contains("<ServiceBreadcrumb ServiceKey=\"CNC Machining\" />", body, StringComparison.Ordinal);
        Assert.Contains("<ServiceLocation />", body, StringComparison.Ordinal);
    }

    [Fact]
    public void ThreeDimensionalPrintingRoute_UsesStaticBlazorBody()
    {
        var root = FindRepositoryRoot();
        var page = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Web", "Pages", "Services", "3D-Printing.cshtml"));
        Assert.Contains("type=\"typeof(ThreeDimensionalPrintingContent)\"", page, StringComparison.Ordinal);
        Assert.Contains("render-mode=\"Static\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("<main class=\"service-page\">", page, StringComparison.Ordinal);
        Assert.Contains("<partial name=\"_SchemaService\"", page, StringComparison.Ordinal);
        Assert.Contains("FAQPage", page, StringComparison.Ordinal);

        var body = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Web", "Components", "Pages", "Services", "ThreeDimensionalPrintingContent.razor"));
        Assert.Contains("<ServiceBreadcrumb ServiceKey=\"3D Printing\" />", body, StringComparison.Ordinal);
        Assert.Contains("<ServiceLocation />", body, StringComparison.Ordinal);
    }

    [Fact]
    public void ThreeDimensionalScanningRoute_UsesStaticBlazorBody()
    {
        var root = FindRepositoryRoot();
        var page = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Web", "Pages", "Services", "3D-Scanning.cshtml"));
        Assert.Contains("type=\"typeof(ThreeDimensionalScanningContent)\"", page, StringComparison.Ordinal);
        Assert.Contains("render-mode=\"Static\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("<main class=\"service-page\">", page, StringComparison.Ordinal);
        Assert.Contains("<partial name=\"_SchemaService\"", page, StringComparison.Ordinal);
        Assert.Contains("FAQPage", page, StringComparison.Ordinal);

        var body = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Web", "Components", "Pages", "Services", "ThreeDimensionalScanningContent.razor"));
        Assert.Contains("<ServiceBreadcrumb ServiceKey=\"3D Scanning\" />", body, StringComparison.Ordinal);
        Assert.Contains("<ServiceLocation />", body, StringComparison.Ordinal);
    }

    [Fact]
    public void HomeRoute_UsesStaticBlazorBodyAndComponentLocalization()
    {
        var root = FindRepositoryRoot();
        var page = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Web", "Pages", "Index.cshtml"));
        Assert.Contains("type=\"typeof(HomeContent)\"", page, StringComparison.Ordinal);
        Assert.Contains("render-mode=\"Static\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("landing-hero", page, StringComparison.Ordinal);

        var body = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Web", "Components", "Pages", "Home", "HomeContent.razor"));
        Assert.Contains("IStringLocalizer<HomeContent>", body, StringComparison.Ordinal);
        Assert.Contains("data-migration-component=\"home-content\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("asp-page", body, StringComparison.Ordinal);

        Assert.True(File.Exists(Path.Combine(root, "Legacy.Maliev.Web", "Resources", "Components", "Pages", "Home", "HomeContent.resx")));
        Assert.True(File.Exists(Path.Combine(root, "Legacy.Maliev.Web", "Resources", "Components", "Pages", "Home", "HomeContent.th.resx")));
        Assert.False(File.Exists(Path.Combine(root, "Legacy.Maliev.Web", "Resources", "Pages", "Index.resx")));
        Assert.False(File.Exists(Path.Combine(root, "Legacy.Maliev.Web", "Resources", "Pages", "Index.th.resx")));
    }

    [Fact]
    public void CareerListingRoute_UsesDisplayOnlyStaticSsrComponent()
    {
        var root = FindRepositoryRoot();
        var page = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Web", "Pages", "Career", "Index.cshtml"));
        var componentPath = Path.Combine(
            root,
            "Legacy.Maliev.Web",
            "Components",
            "Pages",
            "Career",
            "CareerIndexContent.razor");

        Assert.True(File.Exists(componentPath));
        var component = File.ReadAllText(componentPath);

        Assert.Contains("type=\"typeof(CareerIndexContent)\"", page, StringComparison.Ordinal);
        Assert.Contains("render-mode=\"Static\"", page, StringComparison.Ordinal);
        Assert.Contains("param-Model=\"Model.DisplayModel\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"get-careers\"", page, StringComparison.Ordinal);

        Assert.Contains("data-migration-component=\"career-index-content\"", component, StringComparison.Ordinal);
        Assert.Contains("IStringLocalizer<CareerIndexContent>", component, StringComparison.Ordinal);
        Assert.DoesNotContain("ICareerClient", component, StringComparison.Ordinal);
        Assert.DoesNotContain("@rendermode", component, StringComparison.Ordinal);

        Assert.True(File.Exists(Path.Combine(
            root,
            "Legacy.Maliev.Web",
            "Resources",
            "Components",
            "Pages",
            "Career",
            "CareerIndexContent.th.resx")));
        Assert.False(File.Exists(Path.Combine(
            root,
            "Legacy.Maliev.Web",
            "Resources",
            "Pages",
            "Career",
            "Index.th.resx")));
    }

    [Fact]
    public void CareerDetailRoute_UsesDisplayOnlyStaticSsrComponentAndMinimalPrintBridge()
    {
        var root = FindRepositoryRoot();
        var page = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Web", "Pages", "Career", "View.cshtml"));
        var componentPath = Path.Combine(
            root,
            "Legacy.Maliev.Web",
            "Components",
            "Pages",
            "Career",
            "CareerDetailContent.razor");

        Assert.True(File.Exists(componentPath));
        var component = File.ReadAllText(componentPath);

        Assert.Contains("type=\"typeof(CareerDetailContent)\"", page, StringComparison.Ordinal);
        Assert.Contains("render-mode=\"Static\"", page, StringComparison.Ordinal);
        Assert.Contains("param-Model=\"Model.DisplayModel\"", page, StringComparison.Ordinal);
        Assert.Contains("function PrintJobDescription()", page, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"job-description\"", page, StringComparison.Ordinal);

        Assert.Contains("data-migration-component=\"career-detail-content\"", component, StringComparison.Ordinal);
        Assert.Contains("IStringLocalizer<CareerDetailContent>", component, StringComparison.Ordinal);
        Assert.DoesNotContain("ICareerClient", component, StringComparison.Ordinal);
        Assert.DoesNotContain("MarkupString", component, StringComparison.Ordinal);
        Assert.DoesNotContain("@rendermode", component, StringComparison.Ordinal);

        Assert.True(File.Exists(Path.Combine(
            root,
            "Legacy.Maliev.Web",
            "Resources",
            "Components",
            "Pages",
            "Career",
            "CareerDetailContent.th.resx")));
        Assert.False(File.Exists(Path.Combine(
            root,
            "Legacy.Maliev.Web",
            "Resources",
            "Pages",
            "Career",
            "View.th.resx")));
    }

    [Fact]
    public void ContactRoute_UsesStaticSsrComponentsInsideTheRazorAntiforgeryBoundary()
    {
        var root = FindRepositoryRoot();
        var page = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Web", "Pages", "Contact", "Index.cshtml"));
        var componentRoot = Path.Combine(root, "Legacy.Maliev.Web", "Components", "Pages", "Contact");

        Assert.Contains("<form method=\"post\" asp-page-handler=\"SubmitRequest\" id=\"contact-form\">", page, StringComparison.Ordinal);
        Assert.Contains("type=\"typeof(ContactHeroContent)\"", page, StringComparison.Ordinal);
        Assert.Contains("type=\"typeof(ContactFormFields)\"", page, StringComparison.Ordinal);
        Assert.Contains("param-Model=\"Model.DisplayModel\"", page, StringComparison.Ordinal);
        Assert.Contains("type=\"typeof(ContactDetailsContent)\"", page, StringComparison.Ordinal);
        Assert.Equal(3, System.Text.RegularExpressions.Regex.Count(page, "render-mode=\"Static\""));
        Assert.DoesNotContain("asp-for=", page, StringComparison.Ordinal);
        Assert.DoesNotContain("name=\"g-recaptcha-response\"", page, StringComparison.Ordinal);

        foreach (var componentName in new[] { "ContactHeroContent", "ContactFormFields", "ContactDetailsContent" })
        {
            var component = File.ReadAllText(Path.Combine(componentRoot, $"{componentName}.razor"));
            Assert.Contains("IStringLocalizer<ContactContent>", component, StringComparison.Ordinal);
            Assert.DoesNotContain("IContactClient", component, StringComparison.Ordinal);
            Assert.DoesNotContain("IAntiBotVerifier", component, StringComparison.Ordinal);
            Assert.DoesNotContain("@rendermode", component, StringComparison.Ordinal);
        }

        Assert.True(File.Exists(Path.Combine(
            root,
            "Legacy.Maliev.Web",
            "Resources",
            "Components",
            "Pages",
            "Contact",
            "ContactContent.th.resx")));
        Assert.False(File.Exists(Path.Combine(root, "Legacy.Maliev.Web", "Resources", "Pages", "Contact", "Index.th.resx")));
    }

    [Fact]
    public void QuotationRoute_UsesStaticSsrComponentsInsideTheRazorMultipartBoundary()
    {
        var root = FindRepositoryRoot();
        var page = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Web", "Pages", "Quotation", "Index.cshtml"));
        var componentRoot = Path.Combine(root, "Legacy.Maliev.Web", "Components", "Pages", "Quotation");

        Assert.Contains("<form method=\"post\" enctype=\"multipart/form-data\" asp-page-handler=\"SubmitRequest\" id=\"quotation-form\">", page, StringComparison.Ordinal);
        Assert.Contains("type=\"typeof(QuotationHeroContent)\"", page, StringComparison.Ordinal);
        Assert.Contains("type=\"typeof(QuotationFormFields)\"", page, StringComparison.Ordinal);
        Assert.Contains("param-Model=\"Model.DisplayModel\"", page, StringComparison.Ordinal);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Count(page, "render-mode=\"Static\""));
        Assert.DoesNotContain("asp-for=", page, StringComparison.Ordinal);
        Assert.DoesNotContain("name=\"g-recaptcha-response\"", page, StringComparison.Ordinal);

        foreach (var componentName in new[] { "QuotationHeroContent", "QuotationFormFields" })
        {
            var component = File.ReadAllText(Path.Combine(componentRoot, $"{componentName}.razor"));
            Assert.Contains("IStringLocalizer<QuotationContent>", component, StringComparison.Ordinal);
            Assert.DoesNotContain("IQuotationClient", component, StringComparison.Ordinal);
            Assert.DoesNotContain("IQuotationFileClient", component, StringComparison.Ordinal);
            Assert.DoesNotContain("IAntiBotVerifier", component, StringComparison.Ordinal);
            Assert.DoesNotContain("@rendermode", component, StringComparison.Ordinal);
        }

        Assert.True(File.Exists(Path.Combine(
            root,
            "Legacy.Maliev.Web",
            "Resources",
            "Components",
            "Pages",
            "Quotation",
            "QuotationContent.th.resx")));
        Assert.False(File.Exists(Path.Combine(root, "Legacy.Maliev.Web", "Resources", "Pages", "Quotation", "Index.th.resx")));
    }

    [Fact]
    public void LoginRoute_UsesStaticSsrComponentInsideTheRazorSessionBoundary()
    {
        var root = FindRepositoryRoot();
        var page = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Web", "Pages", "Account", "Login.cshtml"));
        var componentPath = Path.Combine(
            root,
            "Legacy.Maliev.Web",
            "Components",
            "Pages",
            "Account",
            "LoginContent.razor");

        Assert.Contains("<form method=\"post\" class=\"maliev-form\">", page, StringComparison.Ordinal);
        Assert.Contains("type=\"typeof(LoginContent)\"", page, StringComparison.Ordinal);
        Assert.Contains("render-mode=\"Static\"", page, StringComparison.Ordinal);
        Assert.Contains("param-Model=\"Model.DisplayModel\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("asp-for=", page, StringComparison.Ordinal);
        Assert.True(
            page.IndexOf("</form>", StringComparison.Ordinal) < page.IndexOf("auth-card__footer", StringComparison.Ordinal),
            "Account recovery links must remain outside the credential form.");

        Assert.True(File.Exists(componentPath));
        var component = File.ReadAllText(componentPath);
        Assert.Contains("IStringLocalizer<LoginContent>", component, StringComparison.Ordinal);
        Assert.Contains("data-migration-component=\"login-content\"", component, StringComparison.Ordinal);
        Assert.DoesNotContain("IAccountSessionManager", component, StringComparison.Ordinal);
        var loginDisplayModel = File.ReadAllText(Path.Combine(
            root,
            "Legacy.Maliev.Web",
            "Components",
            "Pages",
            "Account",
            "LoginFormDisplayModel.cs"));
        Assert.DoesNotContain("string Password", loginDisplayModel, StringComparison.Ordinal);
        Assert.DoesNotContain("@rendermode", component, StringComparison.Ordinal);

        Assert.True(File.Exists(Path.Combine(
            root,
            "Legacy.Maliev.Web",
            "Resources",
            "Components",
            "Pages",
            "Account",
            "LoginContent.th.resx")));
        Assert.False(File.Exists(Path.Combine(root, "Legacy.Maliev.Web", "Resources", "Pages", "Account", "Login.th.resx")));
    }

    [Fact]
    public void ForgotPasswordRoute_UsesStaticSsrComponentWithoutExposingResetState()
    {
        var root = FindRepositoryRoot();
        var page = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Web", "Pages", "Account", "ForgotPassword.cshtml"));
        var componentPath = Path.Combine(root, "Legacy.Maliev.Web", "Components", "Pages", "Account", "ForgotPasswordContent.razor");

        Assert.Contains("<form method=\"post\" class=\"maliev-form\">", page, StringComparison.Ordinal);
        Assert.Contains("type=\"typeof(ForgotPasswordContent)\"", page, StringComparison.Ordinal);
        Assert.Contains("render-mode=\"Static\"", page, StringComparison.Ordinal);
        Assert.Contains("param-Model=\"Model.DisplayModel\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("asp-for=", page, StringComparison.Ordinal);
        Assert.True(page.IndexOf("</form>", StringComparison.Ordinal) < page.IndexOf("auth-card__footer", StringComparison.Ordinal));

        Assert.True(File.Exists(componentPath));
        var component = File.ReadAllText(componentPath);
        Assert.Contains("IStringLocalizer<ForgotPasswordContent>", component, StringComparison.Ordinal);
        Assert.Contains("data-migration-component=\"forgot-password-content\"", component, StringComparison.Ordinal);
        Assert.DoesNotContain("ICustomerAuthenticationClient", component, StringComparison.Ordinal);
        Assert.DoesNotContain("INotificationClient", component, StringComparison.Ordinal);
        Assert.DoesNotContain("Token", component, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("eligible", component, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("@rendermode", component, StringComparison.Ordinal);

        Assert.True(File.Exists(Path.Combine(root, "Legacy.Maliev.Web", "Resources", "Components", "Pages", "Account", "ForgotPasswordContent.th.resx")));
        Assert.False(File.Exists(Path.Combine(root, "Legacy.Maliev.Web", "Resources", "Pages", "Account", "ForgotPassword.th.resx")));
    }

    [Fact]
    public void ResetPasswordRoute_UsesStaticSsrComponentInsideTheServerChallengeBoundary()
    {
        var root = FindRepositoryRoot();
        var page = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Web", "Pages", "Account", "ResetPassword.cshtml"));
        var componentPath = Path.Combine(root, "Legacy.Maliev.Web", "Components", "Pages", "Account", "ResetPasswordContent.razor");

        Assert.Contains("<form method=\"post\" class=\"maliev-form\">", page, StringComparison.Ordinal);
        Assert.Contains("type=\"typeof(ResetPasswordContent)\"", page, StringComparison.Ordinal);
        Assert.Contains("render-mode=\"Static\"", page, StringComparison.Ordinal);
        Assert.Contains("param-Model=\"Model.DisplayModel\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("asp-for=", page, StringComparison.Ordinal);

        Assert.True(File.Exists(componentPath));
        var component = File.ReadAllText(componentPath);
        Assert.Contains("IStringLocalizer<ResetPasswordContent>", component, StringComparison.Ordinal);
        Assert.Contains("data-migration-component=\"reset-password-content\"", component, StringComparison.Ordinal);
        Assert.DoesNotContain("ICustomerAuthenticationClient", component, StringComparison.Ordinal);
        var resetDisplayModel = File.ReadAllText(Path.Combine(
            root,
            "Legacy.Maliev.Web",
            "Components",
            "Pages",
            "Account",
            "ResetPasswordFormDisplayModel.cs"));
        Assert.DoesNotContain("string Password", resetDisplayModel, StringComparison.Ordinal);
        Assert.DoesNotContain("string ConfirmPassword", resetDisplayModel, StringComparison.Ordinal);
        Assert.DoesNotContain("@rendermode", component, StringComparison.Ordinal);

        Assert.True(File.Exists(Path.Combine(root, "Legacy.Maliev.Web", "Resources", "Components", "Pages", "Account", "ResetPasswordContent.th.resx")));
        Assert.False(File.Exists(Path.Combine(root, "Legacy.Maliev.Web", "Resources", "Pages", "Account", "ResetPassword.th.resx")));
    }

    [Fact]
    public void SignupRoute_UsesStaticSsrComponentInsideTheServerRegistrationBoundary()
    {
        var root = FindRepositoryRoot();
        var page = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Web", "Pages", "Account", "Signup.cshtml"));
        var componentPath = Path.Combine(root, "Legacy.Maliev.Web", "Components", "Pages", "Account", "SignupContent.razor");

        Assert.Contains("<form id=\"customer-signup\" method=\"post\" class=\"maliev-form\">", page, StringComparison.Ordinal);
        Assert.Contains("type=\"typeof(SignupContent)\"", page, StringComparison.Ordinal);
        Assert.Contains("render-mode=\"Static\"", page, StringComparison.Ordinal);
        Assert.Contains("param-Model=\"Model.DisplayModel\"", page, StringComparison.Ordinal);
        Assert.Contains("document.getElementById('signup-recaptcha-response').value = token", page, StringComparison.Ordinal);
        Assert.DoesNotContain("asp-for=", page, StringComparison.Ordinal);

        Assert.True(File.Exists(componentPath));
        var component = File.ReadAllText(componentPath);
        Assert.Contains("IStringLocalizer<SignupContent>", component, StringComparison.Ordinal);
        Assert.Contains("data-migration-component=\"signup-content\"", component, StringComparison.Ordinal);
        Assert.Contains("name=\"g-recaptcha-response\"", component, StringComparison.Ordinal);
        Assert.DoesNotContain("ICustomerProfileClient", component, StringComparison.Ordinal);
        Assert.DoesNotContain("ICustomerAuthenticationClient", component, StringComparison.Ordinal);
        Assert.DoesNotContain("IAntiBotVerifier", component, StringComparison.Ordinal);
        Assert.DoesNotContain("@rendermode", component, StringComparison.Ordinal);

        var displayModel = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Web", "Components", "Pages", "Account", "SignupFormDisplayModel.cs"));
        Assert.DoesNotContain("string Password", displayModel, StringComparison.Ordinal);
        Assert.DoesNotContain("string ConfirmPassword", displayModel, StringComparison.Ordinal);
        Assert.DoesNotContain("RecaptchaToken", displayModel, StringComparison.Ordinal);

        Assert.True(File.Exists(Path.Combine(root, "Legacy.Maliev.Web", "Resources", "Components", "Pages", "Account", "SignupContent.th.resx")));
        Assert.False(File.Exists(Path.Combine(root, "Legacy.Maliev.Web", "Resources", "Pages", "Account", "Signup.th.resx")));
    }

    [Fact]
    public void AccountIndexRoute_UsesDisplayOnlyStaticSsrInsideTheServerLogoutBoundary()
    {
        var root = FindRepositoryRoot();
        var page = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Web", "Pages", "Account", "Index.cshtml"));
        var componentPath = Path.Combine(root, "Legacy.Maliev.Web", "Components", "Pages", "Account", "AccountIndexContent.razor");

        Assert.Contains("type=\"typeof(AccountIndexContent)\"", page, StringComparison.Ordinal);
        Assert.Contains("render-mode=\"Static\"", page, StringComparison.Ordinal);
        Assert.Contains("param-Model=\"Model.DisplayModel\"", page, StringComparison.Ordinal);
        Assert.Contains("<form method=\"post\" asp-page=\"/Account/Logout\">", page, StringComparison.Ordinal);
        Assert.True(
            page.IndexOf("type=\"typeof(AccountIndexContent)\"", StringComparison.Ordinal) <
            page.IndexOf("asp-page=\"/Account/Logout\"", StringComparison.Ordinal));

        Assert.True(File.Exists(componentPath));
        var component = File.ReadAllText(componentPath);
        Assert.Contains("IStringLocalizer<AccountIndexContent>", component, StringComparison.Ordinal);
        Assert.Contains("class=\"account-index-actions\"", component, StringComparison.Ordinal);
        Assert.Contains("data-migration-component=\"account-index-content\"", component, StringComparison.Ordinal);
        Assert.DoesNotContain("IAccountSessionManager", component, StringComparison.Ordinal);
        Assert.DoesNotContain("ICustomerAuthenticationClient", component, StringComparison.Ordinal);
        Assert.DoesNotContain("AccessToken", component, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RefreshToken", component, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("@rendermode", component, StringComparison.Ordinal);

        var displayModel = File.ReadAllText(Path.Combine(
            root,
            "Legacy.Maliev.Web",
            "Components",
            "Pages",
            "Account",
            "AccountIndexDisplayModel.cs"));
        Assert.Contains("bool IsAuthenticated", displayModel, StringComparison.Ordinal);
        Assert.Contains("string? DisplayName", displayModel, StringComparison.Ordinal);
        Assert.DoesNotContain("CustomerId", displayModel, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Token", displayModel, StringComparison.OrdinalIgnoreCase);

        var stylesheet = File.ReadAllText(Path.Combine(
            root,
            "Legacy.Maliev.Web",
            "wwwroot",
            "src",
            "app",
            "css",
            "application-shell.css"));
        Assert.Contains(".account-index-actions", stylesheet, StringComparison.Ordinal);
        Assert.Contains("display: contents;", stylesheet, StringComparison.Ordinal);

        Assert.True(File.Exists(Path.Combine(root, "Legacy.Maliev.Web", "Resources", "Components", "Pages", "Account", "AccountIndexContent.th.resx")));
        Assert.False(File.Exists(Path.Combine(root, "Legacy.Maliev.Web", "Resources", "Pages", "Account", "Index.th.resx")));
    }

    [Fact]
    public void AccessDeniedRoute_UsesLocalizedDisplayOnlyStaticSsr()
    {
        var root = FindRepositoryRoot();
        var page = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Web", "Pages", "Account", "AccessDenied.cshtml"));
        var componentPath = Path.Combine(root, "Legacy.Maliev.Web", "Components", "Pages", "Account", "AccessDeniedContent.razor");

        Assert.Contains("type=\"typeof(AccessDeniedContent)\"", page, StringComparison.Ordinal);
        Assert.Contains("render-mode=\"Static\"", page, StringComparison.Ordinal);
        Assert.Contains("ViewData[\"Robots\"] = \"noindex, nofollow\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("User.", page, StringComparison.Ordinal);

        Assert.True(File.Exists(componentPath));
        var component = File.ReadAllText(componentPath);
        Assert.Contains("IStringLocalizer<AccessDeniedContent>", component, StringComparison.Ordinal);
        Assert.Contains("data-migration-component=\"access-denied-content\"", component, StringComparison.Ordinal);
        Assert.Contains("href=\"/\"", component, StringComparison.Ordinal);
        Assert.Contains("href=\"/Contact\"", component, StringComparison.Ordinal);
        Assert.DoesNotContain("IAccountSessionManager", component, StringComparison.Ordinal);
        Assert.DoesNotContain("Token", component, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("@rendermode", component, StringComparison.Ordinal);

        Assert.True(File.Exists(Path.Combine(root, "Legacy.Maliev.Web", "Resources", "Components", "Pages", "Account", "AccessDeniedContent.th.resx")));
        Assert.False(File.Exists(Path.Combine(root, "Legacy.Maliev.Web", "Resources", "Pages", "Account", "AccessDenied.th.resx")));
    }
}
