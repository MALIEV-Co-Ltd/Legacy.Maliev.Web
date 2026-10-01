using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Legacy.Maliev.Web.Application;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Normal HTTP documents consumed by the actual offline SEO verifier, without provider requests.</summary>
public sealed class PublicDocumentVerifierQuotationHttpTests(TestingWebApplicationFactory factory)
    : IClassFixture<TestingWebApplicationFactory>
{
    [Theory]
    [InlineData(true, "en")]
    [InlineData(true, "th")]
    [InlineData(false, "en")]
    [InlineData(false, "th")]
    public async Task Quotation_RendererKeepsExactlyOneNoindexMetaAndFolderHeader(bool active, string culture)
    {
        await using var configured = Configure("Quotation", active, withCountries: true);
        using var client = Client(configured);
        using var response = await client.GetAsync($"/quotation?culture={culture}&item=3d-printing");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("noindex, follow", Assert.Single(response.Headers.GetValues("X-Robots-Tag")));
        Assert.Contains($"<html lang=\"{culture}\"", html, StringComparison.Ordinal);
        Assert.Contains("name=\"__RequestVerificationToken\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"quotation-form\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("AccessToken", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RefreshToken", html, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("noindex,follow", Attribute(Assert.Single(Meta(html, "name", "robots")), "content"));
    }

    [Theory]
    [InlineData(true, "en")]
    [InlineData(true, "th")]
    [InlineData(false, "en")]
    [InlineData(false, "th")]
    public async Task Legal_RendererPublishesOneUsefulLocalizedDescription(bool active, string culture)
    {
        await using var configured = Configure("Legal", active);
        using var client = Client(configured);
        using var response = await client.GetAsync($"/legal?culture={culture}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains($"<html lang=\"{culture}\"", html, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(html, "<h1\\b", RegexOptions.IgnoreCase));
        var description = Attribute(Assert.Single(Meta(html, "name", "description")), "content");
        Assert.True(description.Length >= 40, $"Legal {culture} renderer must expose a useful description; length was {description.Length}.");
        Assert.Equal(description, Attribute(Assert.Single(Meta(html, "property", "og:description")), "content"));
    }

    [Theory]
    [InlineData("en", "https://www.maliev.com/legal?culture=en")]
    [InlineData("th", "https://www.maliev.com/legal")]
    public async Task ActualLegalDocument_VerifierAcceptsItsLocalizedCanonical(string culture, string expectedCanonical)
    {
        using var client = Client(factory);
        using var response = await client.GetAsync($"/legal?culture={culture}&tracking=excluded");
        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expectedCanonical, Attribute(Assert.Single(Links(html, "canonical")), "href"));
        Assert.Equal(expectedCanonical, Attribute(Assert.Single(Meta(html, "property", "og:url")), "content"));

        using var verification = await Verify(html, expectedCanonical);
        var actual = Details(verification);
        Assert.True(actual.GetProperty("descriptionPassed").GetBoolean());
        Assert.True(actual.GetProperty("alternatePassed").GetBoolean());
        Assert.True(actual.GetProperty("canonicalPassed").GetBoolean(), "Actual localized Legal canonical must be accepted, including explicit English culture.");
        var scripts = JsonLdScripts(html);
        Assert.NotEmpty(scripts);
        foreach (var script in scripts)
        {
            using var json = JsonDocument.Parse(script.Groups["json"].Value);
            Assert.Equal("https://schema.org", json.RootElement.GetProperty("@context").GetString());
        }
        Assert.True(actual.GetProperty("jsonLdPassed").GetBoolean(),
            $"Actual valid Legal JSON-LD scripts: {scripts.Length}; raw type attributes: {string.Join(",", scripts.Select(script => Regex.Match(script.Groups["tag"].Value, "type=\"(?<value>[^\"]*)\"").Groups["value"].Value))}; verifier recognized: {actual.GetProperty("jsonLdCount").GetInt32()}.");
        Assert.True(verification.RootElement.GetProperty("Passed").GetBoolean());
    }

    [Theory]
    [InlineData("en", "missing")]
    [InlineData("th", "missing")]
    [InlineData("en", "duplicate")]
    [InlineData("th", "duplicate")]
    [InlineData("en", "opposite-culture")]
    [InlineData("th", "opposite-culture")]
    public async Task Verifier_RejectsIndependentlyCorruptedCanonicalFromActualDocument(string culture, string corruption)
    {
        using var client = Client(factory);
        using var response = await client.GetAsync($"/legal?culture={culture}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        var canonical = Assert.Single(Links(html, "canonical"));
        var opposite = culture == "en" ? "https://www.maliev.com/legal" : "https://www.maliev.com/legal?culture=en";
        var altered = corruption switch
        {
            "missing" => html.Replace(canonical, string.Empty, StringComparison.Ordinal),
            "duplicate" => html.Replace(canonical, canonical + canonical, StringComparison.Ordinal),
            "opposite-culture" => html.Replace(canonical, $"<link rel=\"canonical\" href=\"{opposite}\" />", StringComparison.Ordinal),
            _ => throw new InvalidOperationException("Unknown owned corruption control."),
        };
        using var verification = await Verify(altered, $"https://www.maliev.com/legal?culture={culture}");
        Assert.False(Details(verification).GetProperty("canonicalPassed").GetBoolean());
        Assert.False(verification.RootElement.GetProperty("Passed").GetBoolean());
    }

    [Theory]
    [InlineData("en", "noindex", "robotsPassed")]
    [InlineData("th", "noindex", "robotsPassed")]
    [InlineData("en", "duplicate-description", "descriptionPassed")]
    [InlineData("th", "duplicate-description", "descriptionPassed")]
    [InlineData("en", "malformed-jsonld", "jsonLdPassed")]
    [InlineData("th", "malformed-jsonld", "jsonLdPassed")]
    public async Task Verifier_RejectsTargetedMetadataCorruptionWithoutRelaxingOtherChecks(
        string culture, string corruption, string targetFlag)
    {
        using var client = Client(factory);
        using var response = await client.GetAsync($"/legal?culture={culture}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        var pageUri = $"https://www.maliev.com/legal?culture={culture}";
        var description = Assert.Single(Meta(html, "name", "description"));
        var jsonLd = JsonLdScripts(html);
        if (corruption == "malformed-jsonld")
        {
            Assert.NotEmpty(jsonLd);
            using var originalJson = JsonDocument.Parse(jsonLd[0].Groups["json"].Value);
            Assert.Equal("https://schema.org", originalJson.RootElement.GetProperty("@context").GetString());
        }
        else
        {
            using var original = await Verify(html, pageUri);
            Assert.True(Details(original).GetProperty(targetFlag).GetBoolean(), "The actual producer must first pass the targeted metadata control.");
        }
        var altered = corruption switch
        {
            "noindex" => html.Replace("</head>", "<meta name=\"robots\" content=\"noindex\" /></head>", StringComparison.Ordinal),
            "duplicate-description" => html.Replace(description, description + description, StringComparison.Ordinal),
            "malformed-jsonld" => html.Replace(jsonLd[0].Value, "<script type=\"application/ld+json\">{broken</script>", StringComparison.Ordinal),
            _ => throw new InvalidOperationException("Unknown owned corruption control."),
        };
        Assert.NotEqual(html, altered);
        using var verification = await Verify(altered, pageUri);
        Assert.False(Details(verification).GetProperty(targetFlag).GetBoolean());
        if (corruption == "malformed-jsonld")
        {
            Assert.True(Details(verification).GetProperty("jsonLdCount").GetInt32() >= 1);
            Assert.True(Details(verification).GetProperty("invalidJsonLd").GetInt32() >= 1,
                "Malformed JSON must be rejected as parsed invalid JSON, not merely an absent script.");
        }
        Assert.False(verification.RootElement.GetProperty("Passed").GetBoolean());
    }

    [Theory]
    [InlineData("/", "en", "hours", "LocalBusiness", "localBusinessCount")]
    [InlineData("/", "th", "hours", "LocalBusiness", "localBusinessCount")]
    [InlineData("/services/3d-printing", "en", "service", "Service", "serviceNodeCount")]
    [InlineData("/services/3d-printing", "th", "service", "Service", "serviceNodeCount")]
    public async Task RawHomeAndServiceJsonLd_ActualCompanionVerifierAcceptsEncodedType(
        string path, string culture, string contract, string schemaType, string countProperty)
    {
        using var client = Client(factory);
        using var response = await client.GetAsync($"{path}?culture={culture}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains($"<html lang=\"{culture}\"", html, StringComparison.Ordinal);
        var scripts = JsonLdScripts(html);
        Assert.NotEmpty(scripts);
        Assert.Contains(scripts, script => script.Groups["tag"].Value.Contains("application/ld&#x2B;json", StringComparison.Ordinal));
        var matched = false;
        foreach (var script in scripts)
        {
            using var json = JsonDocument.Parse(script.Groups["json"].Value);
            Assert.Equal("https://schema.org", json.RootElement.GetProperty("@context").GetString());
            if (json.RootElement.GetProperty("@type").GetString() != schemaType) continue;
            matched = true;
            if (contract != "hours") continue;
            var specifications = json.RootElement.GetProperty("openingHoursSpecification").EnumerateArray().ToArray();
            Assert.NotEmpty(specifications);
            Assert.All(specifications, specification =>
            {
                Assert.Equal("09:00", specification.GetProperty("opens").GetString());
                Assert.Equal("18:00", specification.GetProperty("closes").GetString());
            });
            var days = specifications.SelectMany(specification => specification.GetProperty("dayOfWeek").EnumerateArray())
                .Select(day => day.GetString()).OrderBy(day => day, StringComparer.Ordinal).ToArray();
            Assert.Equal(new[] { "Friday", "Monday", "Thursday", "Tuesday", "Wednesday" }, days);
        }
        Assert.True(matched, "The actual raw producer must first contain the independently parsed required schema.");
        using var verification = await Verify(html, "https://www.maliev.com/", contract);
        Assert.True(Details(verification).GetProperty(countProperty).GetInt32() > 0,
            $"Actual {schemaType} raw encoded-type JSON-LD must be recognized by the exported {contract} helper.");
        Assert.True(verification.RootElement.GetProperty("Passed").GetBoolean());
    }

    [Theory]
    [InlineData("", "th", "https://www.maliev.com/legal")]
    [InlineData("?tracking=excluded", "th", "https://www.maliev.com/legal")]
    [InlineData("?culture=th&tracking=excluded", "th", "https://www.maliev.com/legal")]
    [InlineData("?culture=TH", "th", "https://www.maliev.com/legal")]
    [InlineData("?culture=%74%68", "th", "https://www.maliev.com/legal")]
    [InlineData("?culture=en&tracking=excluded", "en", "https://www.maliev.com/legal?culture=en")]
    [InlineData("?culture=EN", "en", "https://www.maliev.com/legal?culture=en")]
    [InlineData("?culture=%65%6E", "en", "https://www.maliev.com/legal?culture=en")]
    [InlineData("?culture=fr", "th", "https://www.maliev.com/legal")]
    public async Task CanonicalCulture_KnownHttpLocalizationHasIndependentExpectedCanonical(
        string query, string expectedCulture, string expectedCanonical)
    {
        using var client = Client(factory);
        using var response = await client.GetAsync("/legal" + query);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains($"<html lang=\"{expectedCulture}\"", html, StringComparison.Ordinal);
        Assert.Equal(expectedCanonical, Attribute(Assert.Single(Links(html, "canonical")), "href"));
        Assert.Equal(expectedCanonical, Attribute(Assert.Single(Meta(html, "property", "og:url")), "content"));
        using var verification = await Verify(html, "https://www.maliev.com/legal" + query);
        Assert.True(Details(verification).GetProperty("canonicalPassed").GetBoolean());
        Assert.True(Details(verification).GetProperty("alternatePassed").GetBoolean());
    }

    [Theory]
    [InlineData("?ui-culture=en", "en", "en", "https://www.maliev.com/legal?culture=en", "https://www.maliev.com/legal?culture=en")]
    [InlineData("?culture=th&ui-culture=en", "th", "en", "https://www.maliev.com/legal?culture=en", "https://www.maliev.com/legal?culture=en")]
    [InlineData("?culture=en&ui-culture=th", "en", "th", "https://www.maliev.com/legal", "https://www.maliev.com/legal")]
    [InlineData("?culture=en&ui-culture=fr", "en", "th", "https://www.maliev.com/legal", "https://www.maliev.com/legal")]
    public async Task ResolvedUiCulture_AlignsDocumentLanguageAndMetadataWithoutChangingFormattingProvider(
        string query, string formattingCulture, string uiCulture, string expectedCanonical, string expectedOpenGraphUrl)
    {
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString(query);
        var providerResult = await new QueryStringRequestCultureProvider().DetermineProviderCultureResult(context);
        Assert.NotNull(providerResult);
        Assert.Equal(formattingCulture, Assert.Single(providerResult.Cultures).Value);
        using var client = Client(factory);
        using var response = await client.GetAsync("/legal" + query);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains($"<html lang=\"{uiCulture}\"", html, StringComparison.Ordinal);
        Assert.Equal(uiCulture, Attribute(Assert.Single(Meta(html, "name", "language")), "content"));
        Assert.Equal(expectedCanonical, Attribute(Assert.Single(Links(html, "canonical")), "href"));
        Assert.Equal(expectedOpenGraphUrl, Attribute(Assert.Single(Meta(html, "property", "og:url")), "content"));
        using var verification = await Verify(html, "https://www.maliev.com/legal" + query);
        Assert.True(Details(verification).GetProperty("canonicalPassed").GetBoolean());
        Assert.True(Details(verification).GetProperty("alternatePassed").GetBoolean());
        Assert.True(verification.RootElement.GetProperty("Passed").GetBoolean());
    }

    [Theory]
    [InlineData("?culture=th&ui-culture=en", "https://www.maliev.com/legal?culture=en", "https://www.maliev.com/legal?culture=en")]
    [InlineData("?culture=en&ui-culture=th", "https://www.maliev.com/legal", "https://www.maliev.com/legal")]
    public async Task MixedCulture_DesiredCanonicalAndOpenGraphDocumentConsistencyRemainsSeparateProducerRegression(
        string query, string expectedCanonical, string expectedOpenGraph)
    {
        using var client = Client(factory);
        using var response = await client.GetAsync("/legal" + query);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        var canonical = Attribute(Assert.Single(Links(html, "canonical")), "href");
        var openGraph = Attribute(Assert.Single(Meta(html, "property", "og:url")), "content");
        Assert.Equal(expectedCanonical, canonical);
        Assert.Equal(expectedOpenGraph, openGraph);
        Assert.Equal(canonical, openGraph);
    }

    [Theory]
    [InlineData(true, "", null, "th", "https://www.maliev.com/legal")]
    [InlineData(false, "", null, "th", "https://www.maliev.com/legal")]
    [InlineData(true, "?culture=th&ui-culture=en", null, "en", "https://www.maliev.com/legal?culture=en")]
    [InlineData(false, "?culture=th&ui-culture=en", null, "en", "https://www.maliev.com/legal?culture=en")]
    [InlineData(true, "", "c=th|uic=th", "th", "https://www.maliev.com/legal")]
    [InlineData(false, "", "c=th|uic=th", "th", "https://www.maliev.com/legal")]
    [InlineData(true, "?culture=th", "c=en|uic=en", "th", "https://www.maliev.com/legal")]
    [InlineData(false, "?culture=th", "c=en|uic=en", "th", "https://www.maliev.com/legal")]
    public async Task ActiveAndRetainedLegal_ResolvedUiMetadataPreservesDefaultAndCookieQueryBoundaries(
        bool active, string query, string? cultureCookie, string expectedCulture, string expectedCanonical)
    {
        await using var configured = Configure("Legal", active);
        using var client = Client(configured);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/legal" + query);
        if (cultureCookie is not null)
            request.Headers.Add("Cookie", CookieRequestCultureProvider.DefaultCookieName + "=" + Uri.EscapeDataString(cultureCookie));
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains($"<html lang=\"{expectedCulture}\"", html, StringComparison.Ordinal);
        Assert.Equal(expectedCulture, Attribute(Assert.Single(Meta(html, "name", "language")), "content"));
        Assert.Equal(expectedCanonical, Attribute(Assert.Single(Links(html, "canonical")), "href"));
        Assert.Equal(expectedCanonical, Attribute(Assert.Single(Meta(html, "property", "og:url")), "content"));
        Assert.Equal(expectedCulture == "en" ? "en_US" : "th_TH", Attribute(Assert.Single(Meta(html, "property", "og:locale")), "content"));
        var description = Attribute(Assert.Single(Meta(html, "name", "description")), "content");
        Assert.True(description.Length >= 40);
        Assert.Equal(description, Attribute(Assert.Single(Meta(html, "property", "og:description")), "content"));
        using var verification = await Verify(html, "https://www.maliev.com/legal" + query);
        Assert.True(verification.RootElement.GetProperty("Passed").GetBoolean());
    }

    [Theory]
    [InlineData("?culture=en&culture=th", "en,th")]
    [InlineData("?culture=th&culture=en", "th,en")]
    [InlineData("?culture=en&culture=en", "en,en")]
    public async Task AmbiguousCulture_CharacterizesUnsupportedCombinedHttpValueButVerifierFailsClosed(
        string query, string combinedCulture)
    {
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString(query);
        var providerResult = await new QueryStringRequestCultureProvider().DetermineProviderCultureResult(context);
        Assert.NotNull(providerResult);
        Assert.Equal(combinedCulture, Assert.Single(providerResult.Cultures).Value);
        using var client = Client(factory);
        using var response = await client.GetAsync("/legal" + query);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("<html lang=\"th\"", html, StringComparison.Ordinal);
        Assert.Equal("https://www.maliev.com/legal", Attribute(Assert.Single(Links(html, "canonical")), "href"));
        using var verification = await Verify(html, "https://www.maliev.com/legal" + query);
        Assert.False(Details(verification).GetProperty("canonicalPassed").GetBoolean(),
            "An audit URI with repeated culture keys is ambiguous even when normal HTTP falls back to its configured Thai default.");
    }

    [Theory]
    [InlineData("en", false)]
    [InlineData("th", false)]
    [InlineData("en", true)]
    [InlineData("th", true)]
    public async Task OrdinaryScriptMetadata_CannotAlterDocumentChecksOrSupplyMissingDescription(string culture, bool removeDescription)
    {
        using var client = Client(factory);
        using var response = await client.GetAsync($"/legal?culture={culture}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        var pageUri = $"https://www.maliev.com/legal?culture={culture}";
        using var original = await Verify(html, pageUri);
        Assert.True(Details(original).GetProperty("descriptionPassed").GetBoolean());
        var baseHtml = removeDescription ? html.Replace(Assert.Single(Meta(html, "name", "description")), "", StringComparison.Ordinal) : html;
        const string malicious = "<script type=\"text/template\"><meta name=\"description\" content=\"A forged description that is long enough to impersonate document metadata.\"><meta name=\"robots\" content=\"noindex,nofollow\"><link rel=\"canonical\" href=\"https://attacker.invalid/legal\"><h1>Forged heading</h1><title>Forged title</title></script>";
        var altered = baseHtml.Replace("</head>", malicious + "</head>", StringComparison.Ordinal);
        Assert.NotEqual(baseHtml, altered);
        using var verification = await Verify(altered, pageUri);
        Assert.Equal(!removeDescription, Details(verification).GetProperty("descriptionPassed").GetBoolean());
        foreach (var flag in new[] { "robotsPassed", "canonicalPassed", "alternatePassed", "titlePassed", "headingPassed" })
            Assert.Equal(Details(original).GetProperty(flag).GetBoolean(), Details(verification).GetProperty(flag).GetBoolean());
    }

    [Theory]
    [InlineData("en")]
    [InlineData("th")]
    public async Task EncodedJsonLdType_MalformedBodyMustBeRecognizedAndRejectedWithoutBodyDecoding(string culture)
    {
        using var client = Client(factory);
        using var response = await client.GetAsync($"/legal?culture={culture}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        var script = JsonLdScripts(html)[0];
        using var originalJson = JsonDocument.Parse(script.Groups["json"].Value);
        Assert.Equal("https://schema.org", originalJson.RootElement.GetProperty("@context").GetString());
        var altered = html.Replace(script.Value, script.Groups["tag"].Value + "{broken</script>", StringComparison.Ordinal);
        using var verification = await Verify(altered, $"https://www.maliev.com/legal?culture={culture}");
        Assert.True(Details(verification).GetProperty("invalidJsonLd").GetInt32() >= 1);
        Assert.False(Details(verification).GetProperty("jsonLdPassed").GetBoolean());
        Assert.False(verification.RootElement.GetProperty("Passed").GetBoolean());
    }

    [Theory]
    [InlineData("<script data-type='application/ld+json'>", false)]
    [InlineData("<script data-hint=\"type='application/ld+json'\">", false)]
    [InlineData("<script type='application/ld+json' type='text/javascript'>", false)]
    [InlineData("<script type='text/javascript' type='application/ld+json'>", false)]
    [InlineData("<script type='application/ld+json' type='application/ld+json'>", false)]
    [InlineData("<script>", false)]
    [InlineData("<script type='application/ld&#43;json'>", true)]
    [InlineData("<script TYPE=\"APPLICATION/LD+JSON\">", true)]
    [InlineData("<script type='application/ld+json'>", true)]
    [InlineData("<script type=application/ld+json>", true)]
    public async Task ScriptOpeningType_RequiresUniqueActualAttributeAndAcceptsValidHtmlForms(string opening, bool expectedJsonLd)
    {
        using var client = Client(factory);
        using var response = await client.GetAsync("/legal?culture=th");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        var scripts = JsonLdScripts(html);
        Assert.NotEmpty(scripts);
        var originalJson = scripts[0].Groups["json"].Value;
        using var json = JsonDocument.Parse(originalJson);
        Assert.Equal("https://schema.org", json.RootElement.GetProperty("@context").GetString());
        foreach (var script in scripts) html = html.Replace(script.Value, "", StringComparison.Ordinal);
        var altered = html.Replace("</head>", opening + originalJson + "</script></head>", StringComparison.Ordinal);
        Assert.NotEqual(html, altered);
        using var verification = await Verify(altered, "https://www.maliev.com/legal");
        Assert.Equal(expectedJsonLd ? 1 : 0, Details(verification).GetProperty("jsonLdCount").GetInt32());
        Assert.Equal(expectedJsonLd, Details(verification).GetProperty("jsonLdPassed").GetBoolean());
        Assert.Equal(expectedJsonLd, verification.RootElement.GetProperty("Passed").GetBoolean());
    }

    private WebApplicationFactory<Program> Configure(string route, bool active, bool withCountries = false) =>
        factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting($"BlazorRouting:{route}", active.ToString());
            if (withCountries)
            {
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<ICountryClient>();
                    services.AddSingleton<ICountryClient, OwnedCountries>();
                });
            }
        });

    private static HttpClient Client(WebApplicationFactory<Program> host) => host.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        BaseAddress = new Uri("https://localhost"),
    });

    private static string[] Meta(string html, string attribute, string value) => Regex.Matches(html, "<meta\\b[^>]*>", RegexOptions.IgnoreCase)
        .Select(match => match.Value).Where(tag => Attribute(tag, attribute).Equals(value, StringComparison.OrdinalIgnoreCase)).ToArray();

    private static string[] Links(string html, string rel) => Regex.Matches(html, "<link\\b[^>]*>", RegexOptions.IgnoreCase)
        .Select(match => match.Value).Where(tag => Attribute(tag, "rel").Equals(rel, StringComparison.OrdinalIgnoreCase)).ToArray();

    private static string Attribute(string tag, string name) => WebUtility.HtmlDecode(Regex.Match(
        tag, $"\\b{Regex.Escape(name)}=[\"'](?<value>[^\"']*)[\"']", RegexOptions.IgnoreCase).Groups["value"].Value);

    private static Match[] JsonLdScripts(string html) => Regex.Matches(html,
        "(?<tag><script\\b[^>]*>)(?<json>[\\s\\S]*?)</script>", RegexOptions.IgnoreCase)
        .Where(match => Attribute(match.Groups["tag"].Value, "type") == "application/ld+json").ToArray();

    private static JsonElement Details(JsonDocument verification)
    {
        using var details = JsonDocument.Parse(verification.RootElement.GetProperty("Actual").GetString()!);
        return details.RootElement.Clone();
    }

    private static async Task<JsonDocument> Verify(string html, string pageUri, string contract = "public")
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Web.slnx"))) directory = directory.Parent;
        Assert.NotNull(directory);
        var start = new ProcessStartInfo("pwsh")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-Command", "-" }) start.ArgumentList.Add(argument);
        start.Environment["MALIEV_TEST_VERIFIER_MODULE"] = Path.Combine(directory.FullName, "tests", "ProductionSeoVerifier.psm1");
        start.Environment["TERM"] = "dumb";
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(html));
        var invocation = contract switch
        {
            "public" => $"Test-PublicPageSeoContract -PageUri '{pageUri}' -Html $html -CanonicalOrigin 'https://www.maliev.com' -JsonLdRequired $true",
            "hours" => "Test-LocalBusinessHoursContract -Html $html",
            "service" => "Test-ServiceJsonLdContract -Html $html",
            _ => throw new InvalidOperationException("Unknown owned exported verifier contract."),
        };
        var script = "$ErrorActionPreference='Stop'; Import-Module $env:MALIEV_TEST_VERIFIER_MODULE -Force; "
            + $"$html=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{encoded}')); "
            + invocation + " | ConvertTo-Json -Depth 8 -Compress; exit 0\n";
        using var process = Process.Start(start);
        Assert.NotNull(process);
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try
        {
            await process.StandardInput.WriteAsync(script.AsMemory(), timeout.Token);
            process.StandardInput.Close();
            await process.WaitForExitAsync(timeout.Token);
            Assert.True(process.ExitCode == 0, $"Actual offline verifier process failed: {await stderr}");
            return JsonDocument.Parse(await stdout);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
    }

    private sealed class OwnedCountries : ICountryClient
    {
        public Task<ServiceResponse<IReadOnlyList<Country>>> GetCountriesAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new ServiceResponse<IReadOnlyList<Country>>(
                [new Country(764, "Thailand", "Asia", "66", "TH", "THA", null, null)], true));
    }
}
