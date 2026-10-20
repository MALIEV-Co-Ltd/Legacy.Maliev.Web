using Legacy.Maliev.Web.Pages;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Legacy.Maliev.Web.Tests;

public sealed class LanguageSwitchInputTests
{
    [Theory]
    [InlineData("en", "en")]
    [InlineData("EN", "en")]
    [InlineData("th", "th")]
    [InlineData("TH", "th")]
    [InlineData("en-US", "th")]
    [InlineData("not-a-culture", "th")]
    [InlineData(null, "th")]
    [InlineData("", "th")]
    public void NormalizeSupportedCulture_RejectsUnsupportedInput(string? culture, string expected)
    {
        Assert.Equal(expected, CanonicalUrlPolicy.NormalizeSupportedCulture(culture));
    }

    [Theory]
    [InlineData("en", "~/services?tracking=excluded", "~/services?culture=en")]
    [InlineData("th", "/services?culture=en&tracking=excluded", "/services")]
    [InlineData("en", "https://attacker.example/path", "~/?culture=en")]
    [InlineData("th", "//attacker.example/path", "~/")]
    [InlineData("en", "/\\attacker.example/path", "~/?culture=en")]
    [InlineData("th", "~//attacker.example/path", "~/")]
    [InlineData("en", null, "~/?culture=en")]
    public void GetLocalizedReturnUrl_RebuildsTheSupportedLocalCultureContract(
        string culture,
        string? returnUrl,
        string expected)
    {
        Assert.Equal(expected, CanonicalUrlPolicy.GetLocalizedReturnUrl(returnUrl, culture));
    }

    [Fact]
    public void PublicLanguageSwitch_DefaultsUnsupportedCultureAndRedirects()
    {
        DefaultHttpContext httpContext = new();
        IndexModel model = new()
        {
            PageContext = new PageContext
            {
                HttpContext = httpContext,
            },
        };

        IActionResult result = model.OnPostSetLanguage("not-a-culture", "~/services");

        LocalRedirectResult redirect = Assert.IsType<LocalRedirectResult>(result);
        Assert.Equal("~/services", redirect.Url);
        Assert.Contains(
            httpContext.Response.Headers.SetCookie,
            value => value?.Contains(".AspNetCore.Culture=c%3Dth%7Cuic%3Dth", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void PublicLanguageSwitch_PreservesEnglishWithCanonicalQuery()
    {
        DefaultHttpContext httpContext = new();
        IndexModel model = new()
        {
            PageContext = new PageContext
            {
                HttpContext = httpContext,
            },
        };

        IActionResult result = model.OnPostSetLanguage("EN", "/services?tracking=excluded");

        LocalRedirectResult redirect = Assert.IsType<LocalRedirectResult>(result);
        Assert.Equal("/services?culture=en", redirect.Url);
        Assert.Contains(
            httpContext.Response.Headers.SetCookie,
            value => value?.Contains(".AspNetCore.Culture=c%3Den%7Cuic%3Den", StringComparison.Ordinal) == true);
    }

    [Theory]
    [InlineData("en", "/member?tracking=excluded", "en", "/member?culture=en")]
    [InlineData("EN", "/member?tracking=excluded", "en", "/member?culture=en")]
    [InlineData("th", "/member?culture=en&tracking=excluded", "th", "/member")]
    [InlineData("TH", "/member?culture=en&tracking=excluded", "th", "/member")]
    [InlineData("en-US", "/member", "th", "/member")]
    [InlineData("not-a-culture", "/member", "th", "/member")]
    [InlineData(null, "/member", "th", "/member")]
    [InlineData("", "/member", "th", "/member")]
    [InlineData("en", "https://attacker.example/path", "en", "~/?culture=en")]
    [InlineData("th", "//attacker.example/path", "th", "~/")]
    public void MemberLanguageSwitch_RetainedHandlerNormalizesCookieAndRedirect(
        string? culture, string returnUrl, string expectedCulture, string expectedReturn)
    {
        var context = new DefaultHttpContext();
        var model = new global::Legacy.Maliev.Web.Areas.Member.Pages.Index(null!, null!, null!, null!, null!)
        {
            PageContext = new PageContext { HttpContext = context },
        };
        // Retain a compilable baseline overlay when the legacy named handler is absent.
        var handler = model.GetType().GetMethod("OnPostSetLanguage", [typeof(string), typeof(string)]);
        Assert.NotNull(handler);

        var redirect = Assert.IsType<LocalRedirectResult>(handler.Invoke(model, [culture, returnUrl]));

        Assert.Equal(expectedReturn, redirect.Url);
        Assert.Contains(context.Response.Headers.SetCookie, value =>
            value?.Contains($".AspNetCore.Culture=c%3D{expectedCulture}%7Cuic%3D{expectedCulture}",
                StringComparison.Ordinal) == true);
    }
}
