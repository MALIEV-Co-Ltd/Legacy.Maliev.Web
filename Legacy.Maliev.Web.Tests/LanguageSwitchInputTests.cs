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
}
