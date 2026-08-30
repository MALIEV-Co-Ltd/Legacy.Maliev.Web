using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Legacy.Maliev.Web.Tests;

public sealed class AccountAuthParityTests : IClassFixture<TestingWebApplicationFactory>
{
    private readonly WebApplicationFactory<Program> factory;

    public AccountAuthParityTests(TestingWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task LoginRoute_PreservesSourceCurrentSeoAndHeadingHierarchy()
    {
        var source = await GetDocumentAsync("/account/login?culture=en");

        Assert.Contains("<title>Login | MALIEV</title>", source, StringComparison.Ordinal);
        Assert.Contains("Sign in to MALIEV to review quotations, orders, project files, and account details in your secure workspace.", source, StringComparison.Ordinal);
        Assert.Contains("<meta name=\"keywords\" content=\"member area, login\"", source, StringComparison.Ordinal);
        Assert.Contains("<section class=\"auth-card\" aria-labelledby=\"login-title\">", source, StringComparison.Ordinal);
        Assert.Contains("<h2 id=\"login-title\">Sign in</h2>", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SignupRoute_PreservesSourceCurrentSeoBenefitsAndFormGuidance()
    {
        var source = await GetDocumentAsync("/account/signup?culture=en");

        Assert.Contains("<title>Sign Up | MALIEV</title>", source, StringComparison.Ordinal);
        Assert.Contains("Create a MALIEV account to manage quotations, orders, project files, and manufacturing work online.", source, StringComparison.Ordinal);
        Assert.Contains("<section class=\"auth-intro\" aria-labelledby=\"signup-intro-title\">", source, StringComparison.Ordinal);
        Assert.Contains("<h1 id=\"signup-intro-title\">Manufacturing project management, in one place</h1>", source, StringComparison.Ordinal);
        Assert.Contains("Create an account to manage quotes, start an order or review previous projects and files.", source, StringComparison.Ordinal);
        Assert.Contains("class=\"auth-benefits\"", source, StringComparison.Ordinal);
        Assert.Contains("Six processes under one roof: CNC machining, 3D printing, 3D scanning, 3D design, silicone casting, and low-volume injection molding.", source, StringComparison.Ordinal);
        Assert.Contains("Thai and English support from our workshop in Nonthaburi, 09:00-18:00 Monday to Friday.", source, StringComparison.Ordinal);
        Assert.Contains("<h2 id=\"signup-title\">Create your account</h2>", source, StringComparison.Ordinal);
        Assert.Contains("At least 8 characters.", source, StringComparison.Ordinal);
        Assert.Contains("By signing up, you agree to our", source, StringComparison.Ordinal);
        Assert.Contains("data-submit-label>Sign Up</span>", source, StringComparison.Ordinal);
        Assert.Contains("<i class=\"fas fa-arrow-right\" aria-hidden=\"true\"></i>", source, StringComparison.Ordinal);
        Assert.Contains("data-recaptcha-action=\"submit\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("data-recaptcha-action=\"account_signup\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SignupFallback_UsesTheSameRecaptchaActionAsItsPostHandler()
    {
        var root = FindRepositoryRoot();
        var page = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Web", "Pages", "Account", "Signup.cshtml"));
        var handler = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Web", "Pages", "Account", "Signup.cshtml.cs"));

        Assert.Contains("\"submit\",", page, StringComparison.Ordinal);
        Assert.DoesNotContain("\"account_signup\"", page, StringComparison.Ordinal);
        Assert.Contains("VerifyAsync(RecaptchaToken, \"submit\"", handler, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ForgotPasswordRoute_PreservesSourceCurrentSeoAndHeadingHierarchy()
    {
        var source = await GetDocumentAsync("/account/forgotpassword?culture=en");

        Assert.Contains("<title>Forgot Password | MALIEV</title>", source, StringComparison.Ordinal);
        Assert.Contains("Recover your MALIEV account with a secure reset link and return to manage quotations and manufacturing projects.", source, StringComparison.Ordinal);
        Assert.Contains("<meta name=\"keywords\" content=\"MALIEV password reset, account recovery\"", source, StringComparison.Ordinal);
        Assert.Contains("<section class=\"auth-intro\" aria-labelledby=\"forgot-intro-title\">", source, StringComparison.Ordinal);
        Assert.Contains("<h1 id=\"forgot-intro-title\">Return to your workspace</h1>", source, StringComparison.Ordinal);
        Assert.Contains("Enter your confirmed email address and we will send you a secure password reset link.", source, StringComparison.Ordinal);
        Assert.Contains("<section class=\"auth-card\" aria-labelledby=\"forgot-title\">", source, StringComparison.Ordinal);
        Assert.Contains("<h2 id=\"forgot-title\">Forgot password</h2>", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResetPasswordRoute_PreservesSourceCurrentIntroAndCardSemantics()
    {
        var source = await GetDocumentAsync(
            "/account/resetpassword?culture=en&email=user%40example.com&token=abcdefghijklmnopqrstuvwxyz123456");

        Assert.Contains("<section class=\"auth-intro\" aria-labelledby=\"reset-intro-title\">", source, StringComparison.Ordinal);
        Assert.Contains("<h1 id=\"reset-intro-title\">Create a new password</h1>", source, StringComparison.Ordinal);
        Assert.Contains("Choose a strong password that you do not use for another account.", source, StringComparison.Ordinal);
        Assert.Contains("<section class=\"auth-card\" aria-labelledby=\"reset-title\">", source, StringComparison.Ordinal);
        Assert.Contains("<h2 id=\"reset-title\">Reset password</h2>", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EmailConfirmationRoute_PreservesSourceCurrentStatusCopyAndSemantics()
    {
        var source = await GetDocumentAsync(
            "/account/emailconfirmation?culture=en&email=customer%40example.com&token=invalid-token");

        Assert.Contains(
            "<section class=\"maliev-panel maliev-status\" aria-labelledby=\"email-confirmation-title\">",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "class=\"maliev-status__icon maliev-status__icon--danger\" aria-hidden=\"true\"><i class=\"fas fa-envelope-open-text\"></i>",
            source,
            StringComparison.Ordinal);
        Assert.Contains("<h1 id=\"email-confirmation-title\">We could not confirm your email</h1>", source, StringComparison.Ordinal);
        Assert.Contains("Please review the details below or request a new confirmation email.", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChangeEmailConfirmationRoute_PreservesSourceCurrentStatusSemantics()
    {
        var source = await GetDocumentAsync(
            "/account/changeemailconfirmation?culture=en&email=customer%40example.com&token=invalid-token");

        Assert.Contains(
            "<section class=\"maliev-panel maliev-status\" aria-labelledby=\"change-email-title\">",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "class=\"maliev-status__icon\" aria-hidden=\"true\"><i class=\"fas fa-envelope\"></i>",
            source,
            StringComparison.Ordinal);
        Assert.Contains("<h1 id=\"change-email-title\">Email change confirmation</h1>", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AccessDeniedRoute_PreservesSourceCurrentStatusSemantics()
    {
        var source = await GetDocumentAsync("/account/accessdenied?culture=en");

        Assert.Contains(
            "<section class=\"maliev-panel maliev-status\" aria-labelledby=\"access-denied-title\">",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "class=\"maliev-status__icon\" aria-hidden=\"true\"><i class=\"fas fa-shield-alt\"></i>",
            source,
            StringComparison.Ordinal);
        Assert.Contains("<h1 id=\"access-denied-title\">Access denied</h1>", source, StringComparison.Ordinal);
    }

    private async Task<string> GetDocumentAsync(string path)
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });
        using var response = await client.GetAsync(path);
        var source = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("<!DOCTYPE html>", source.TrimStart(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-migration-route-owner=\"blazor-static-ssr\"", source, StringComparison.Ordinal);
        return source;
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
