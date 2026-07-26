using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Legacy.Maliev.Web.Tests;

public sealed class AccountAuthParityTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public AccountAuthParityTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory.WithWebHostBuilder(builder => builder.UseSetting("environment", "Testing"));
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
        Assert.Contains(">Order Management<", source, StringComparison.Ordinal);
        Assert.Contains(">Account Management<", source, StringComparison.Ordinal);
        Assert.Contains(">Job Management<", source, StringComparison.Ordinal);
        Assert.Contains("<p class=\"maliev-eyebrow\">Get started - it's free</p>", source, StringComparison.Ordinal);
        Assert.Contains("<h2 id=\"signup-title\">Join now</h2>", source, StringComparison.Ordinal);
        Assert.Contains("At least 6 characters long, and must contain 6 unique characters.", source, StringComparison.Ordinal);
        Assert.Contains("By clicking sign up, you are agree to our", source, StringComparison.Ordinal);
        Assert.Contains("Sign Up <i class=\"fas fa-arrow-right\" aria-hidden=\"true\"></i>", source, StringComparison.Ordinal);
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
}
