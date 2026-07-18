using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Legacy.Maliev.Web.Tests;

public sealed class SharedNavigationMigrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient client;

    public SharedNavigationMigrationTests(WebApplicationFactory<Program> factory)
    {
        client = factory.WithWebHostBuilder(builder => builder.UseSetting("environment", "Testing"))
            .CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                BaseAddress = new Uri("https://localhost")
            });
    }

    [Fact]
    public void SharedLayouts_UseDisplayOnlyStaticNavigationComponent()
    {
        var root = FindRepositoryRoot();
        var web = Path.Combine(root, "Legacy.Maliev.Web");
        var layoutPaths = new[]
        {
            Path.Combine(web, "Pages", "Shared", "_Layout.cshtml"),
            Path.Combine(web, "Pages", "Shared", "_InstantQuotationLayout.cshtml"),
            Path.Combine(web, "Areas", "Knowledges", "Pages", "Shared", "_LayoutKnowledges.cshtml"),
            Path.Combine(web, "Areas", "Member", "Pages", "Shared", "_LayoutMember.cshtml")
        };

        foreach (var layoutPath in layoutPaths)
        {
            var layout = File.ReadAllText(layoutPath);
            Assert.Contains("type=\"typeof(PublicNavigation)\"", layout, StringComparison.Ordinal);
            Assert.Contains("render-mode=\"Static\"", layout, StringComparison.Ordinal);
            Assert.Contains("PublicNavigationDisplayModel.Create(Context", layout, StringComparison.Ordinal);
            Assert.DoesNotContain("_NavigationPartial", layout, StringComparison.Ordinal);
        }

        var componentPath = Path.Combine(web, "Components", "Layout", "PublicNavigation.razor");
        var modelPath = Path.Combine(web, "Components", "Layout", "PublicNavigationDisplayModel.cs");
        Assert.True(File.Exists(componentPath));
        Assert.True(File.Exists(modelPath));
        Assert.False(File.Exists(Path.Combine(web, "Pages", "Shared", "_NavigationPartial.cshtml")));
        Assert.False(File.Exists(Path.Combine(web, "Pages", "Shared", "_LoginPartial.cshtml")));
        Assert.False(File.Exists(Path.Combine(web, "Pages", "Shared", "_SelectLanguageNavbarPartial.cshtml")));

        var component = File.ReadAllText(componentPath);
        Assert.Contains("data-migration-component=\"public-navigation\"", component, StringComparison.Ordinal);
        Assert.Contains("Model.SuppressIdentityNavigation", component, StringComparison.Ordinal);
        Assert.Contains("Model.IsAuthenticated", component, StringComparison.Ordinal);
        Assert.Contains("Model.AntiforgeryFieldName", component, StringComparison.Ordinal);
        Assert.Contains("Model.AntiforgeryToken", component, StringComparison.Ordinal);
        Assert.DoesNotContain("HttpContext", component, StringComparison.Ordinal);
        Assert.DoesNotContain("@rendermode", component, StringComparison.Ordinal);

        var model = File.ReadAllText(modelPath);
        Assert.Contains("IAntiforgery", model, StringComparison.Ordinal);
        Assert.Contains("GetAndStoreTokens", model, StringComparison.Ordinal);
        Assert.Contains("RequestLocalizationOptions", model, StringComparison.Ordinal);
        Assert.DoesNotContain("AccessToken", model, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RefreshToken", model, StringComparison.OrdinalIgnoreCase);

        Assert.True(File.Exists(Path.Combine(web, "Resources", "Components", "Layout", "PublicNavigation.th.resx")));
        Assert.False(File.Exists(Path.Combine(web, "Resources", "Pages", "Shared", "_NavigationPartial.th.resx")));
        Assert.False(File.Exists(Path.Combine(web, "Resources", "Pages", "Shared", "_LoginPartial.th.resx")));

        var styleEntry = File.ReadAllText(Path.Combine(web, "assets", "site-entry.css"));
        Assert.Contains("public-navigation.css", styleEntry, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en", "Get a Quote", "Login", "EN")]
    [InlineData("th", "ขอใบเสนอราคา", "เข้าสู่ระบบ", "ไทย")]
    public async Task PublicNavigation_RendersLocalizedAnonymousStaticSsr(
        string culture,
        string quoteLabel,
        string loginLabel,
        string selectedLanguage)
    {
        using var response = await client.GetAsync($"/legal?culture={culture}");
        var source = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("data-migration-component=\"public-navigation\"", source, StringComparison.Ordinal);
        Assert.Contains($">{quoteLabel}<", source, StringComparison.Ordinal);
        Assert.Contains($">{loginLabel}<", source, StringComparison.Ordinal);
        Assert.Contains("href=\"/Account/Login\"", source, StringComparison.Ordinal);
        Assert.Contains("href=\"/Services/CNC-Machining\"", source, StringComparison.Ordinal);
        Assert.Contains("id=\"maliev-navbar-language\"", source, StringComparison.Ordinal);
        Assert.Matches($"<option[^>]*selected[^>]*>{selectedLanguage}</option>", source);
        Assert.Contains("name=\"__RequestVerificationToken\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("blazor.web.js", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LanguageSelector_PostsWithAntiforgeryAndReturnsLocally()
    {
        using var page = await client.GetAsync("/legal?culture=en");
        var source = WebUtility.HtmlDecode(await page.Content.ReadAsStringAsync());
        var form = Regex.Match(
            source,
            "<form[^>]*class=\"maliev-language-form\"[^>]*action=\"(?<action>[^\"]+)\"[\\s\\S]*?</form>",
            RegexOptions.CultureInvariant);

        Assert.True(form.Success);
        var token = Regex.Match(
            form.Value,
            "name=\"__RequestVerificationToken\"[^>]*value=\"(?<value>[^\"]+)\"",
            RegexOptions.CultureInvariant);
        Assert.True(token.Success);

        using var response = await client.PostAsync(
            form.Groups["action"].Value,
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["culture"] = "th",
                ["__RequestVerificationToken"] = token.Groups["value"].Value
            }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/legal", response.Headers.Location?.OriginalString);
        Assert.Contains(
            response.Headers.GetValues("Set-Cookie"),
            value => value.Contains(".AspNetCore.Culture=c%3Dth%7Cuic%3Dth", StringComparison.Ordinal));
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
