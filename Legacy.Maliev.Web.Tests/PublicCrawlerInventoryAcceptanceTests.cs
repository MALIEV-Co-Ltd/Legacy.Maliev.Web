using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Legacy.Maliev.Web.Application;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Source-pinned public discovery documents through the unchanged Web HTTP pipeline.</summary>
public sealed class PublicCrawlerInventoryAcceptanceTests(PublicCrawlerInventoryFixture factory)
    : IClassFixture<PublicCrawlerInventoryFixture>
{
    // acce1386acf4d1525ee061cf9da77260525031f7's literal inventory, not the target catalog.
    private static readonly string[] SourcePaths =
    [
        "/", "/services", "/services/custom-manufacturing", "/services/3d-design",
        "/services/silicone-casting", "/services/low-volume-injection-molding",
        "/services/cnc-machining", "/services/3d-printing", "/services/3d-scanning",
        "/services/finishing-and-color", "/about", "/about/socialmedia", "/contact", "/career",
        "/quotation", "/instantquotation/3d-printing", "/knowledges", "/knowledges/guidelines",
        "/knowledges/workflow", "/knowledges/specifications",
        "/knowledges/specifications/cnc-machining", "/knowledges/specifications/3d-printing",
        "/knowledges/specifications/3d-scanning", "/legal", "/legal/privacypolicy",
        "/legal/termsconditions", "/legal/nondisclosureagreement",
    ];

    public static IEnumerable<object[]> SourceInventoryCases() =>
        SourcePaths.SelectMany(path => new[] { new object[] { path, "en" }, new object[] { path, "th" } });

    public static IEnumerable<object[]> SourceBusinessIntentCases()
    {
        // Source acce commercial intent; custom Thai wording is updated by source 25db5545.
        (string Path, string English, string Thai)[] contracts =
        [
            ("/", "Custom Manufacturing Services", "รับผลิตชิ้นส่วน"),
            ("/services", "Custom part manufacturing", "บริการผลิตชิ้นส่วน"),
            ("/services/custom-manufacturing", "Custom Part Manufacturing", "รับผลิตชิ้นงานตามแบบ"),
            ("/services/3d-design", "3D Design", "รับออกแบบ 3 มิติ"),
            ("/services/silicone-casting", "Silicone Casting", "รับหล่อซิลิโคน"),
            ("/services/low-volume-injection-molding", "Low-Volume Injection Molding", "รับฉีดพลาสติก"),
            ("/services/cnc-machining", "CNC Machining", "รับงาน CNC"),
            ("/services/3d-printing", "3D Printing", "รับพิมพ์ 3D"),
            ("/services/3d-scanning", "3D Scanning", "รับสแกน 3D"),
            ("/services/finishing-and-color", "Finishing", "การเก็บผิว"),
        ];
        return contracts.SelectMany(contract => new[]
        {
            new object[] { contract.Path, "en", contract.English },
            new object[] { contract.Path, "th", contract.Thai },
        });
    }

    [Theory]
    [MemberData(nameof(SourceInventoryCases))]
    public async Task SourceInventory_ActualEnglishAndThaiDocumentsKeepTheirDiscoveryContract(string path, string culture)
    {
        using var client = Client(factory);
        using var response = await client.GetAsync($"{path}?culture={culture}&tracking=excluded");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        AssertDocument(await response.Content.ReadAsStringAsync(), response, path, culture);
    }

    [Theory]
    [InlineData("/", "Home", "en")]
    [InlineData("/", "Home", "th")]
    [InlineData("/instantquotation/3d-printing", "InstantQuotation", "en")]
    [InlineData("/instantquotation/3d-printing", "InstantQuotation", "th")]
    [InlineData("/knowledges/guidelines", "KnowledgesGuidelines", "en")]
    [InlineData("/knowledges/guidelines", "KnowledgesGuidelines", "th")]
    public async Task RetainedThreePublicLayouts_KeepTheSameHttpDiscoveryContract(string path, string routeFlag, string culture)
    {
        await using var retained = factory.WithWebHostBuilder(builder => builder.UseSetting($"BlazorRouting:{routeFlag}", "false"));
        using var client = Client(retained);
        using var response = await client.GetAsync($"{path}?culture={culture}&tracking=excluded");
        Assert.Equal(0, ((PublicCrawlerInventoryFixture.GetOnlySubmissionStore)retained.Services
            .GetRequiredService<IInstantQuotationSubmissionStore>()).Calls);
        Assert.True(response.StatusCode == HttpStatusCode.OK,
            $"Retained {path} {culture}: HTTP {(int)response.StatusCode}; captured exception categories: {string.Join("; ", factory.Exceptions)}");
        AssertDocument(await response.Content.ReadAsStringAsync(), response, path, culture);
    }

    [Theory]
    [MemberData(nameof(SourceBusinessIntentCases))]
    public async Task SourceCommercialPages_RenderIndependentIntentAndDirectQuotationAnchors(string path, string culture, string intent)
    {
        using var client = Client(factory);
        using var response = await client.GetAsync($"{path}?culture={culture}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = RemoveScripts(await response.Content.ReadAsStringAsync());
        Assert.Contains(intent, WebUtility.HtmlDecode(html), StringComparison.OrdinalIgnoreCase);
        if (!path.StartsWith("/services/", StringComparison.Ordinal)) return;
        var body = Regex.Match(html, "<body\\b[^>]*>(?<body>.*?)</body>", RegexOptions.Singleline | RegexOptions.IgnoreCase).Groups["body"].Value;
        var quotationAnchors = Tags(body, "a").Where(tag =>
        {
            var href = Attribute(tag, "href");
            return Uri.TryCreate(new Uri("https://www.maliev.com"), href, out var uri)
                && uri.Host == "www.maliev.com"
                && uri.AbsolutePath.Equals("/quotation", StringComparison.OrdinalIgnoreCase);
        });
        Assert.NotEmpty(quotationAnchors);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("th")]
    public async Task AdditiveNoWeaponsRoute_DoesNotReplaceAnyHistoricalInventoryDocument(string culture)
    {
        using var client = Client(factory);
        using var response = await client.GetAsync($"/no-weapons?culture={culture}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertDocument(await response.Content.ReadAsStringAsync(), response, "/no-weapons", culture);
    }

    [Theory]
    [InlineData("/account", "en", "noindex,follow")]
    [InlineData("/account", "th", "noindex,follow")]
    [InlineData("/account/forgotpassword", "en", "noindex,nofollow")]
    [InlineData("/account/forgotpassword", "th", "noindex,nofollow")]
    public async Task PublicInventoryAcceptance_DoesNotRelaxPrivateAndCredentialUtilities(string path, string culture, string expectedRobots)
    {
        using var client = Client(factory);
        using var response = await client.GetAsync($"{path}?culture={culture}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("noindex, follow", Assert.Single(response.Headers.GetValues("X-Robots-Tag")));
        var html = RemoveScripts(await response.Content.ReadAsStringAsync());
        Assert.Equal(expectedRobots, Attribute(Assert.Single(Meta(html, "name", "robots")), "content").Replace(" ", string.Empty, StringComparison.Ordinal));
    }

    private static void AssertDocument(string html, HttpResponseMessage response, string path, string culture)
    {
        // Strip script templates before counting document headings. Decode individual attributes,
        // never the whole JSON-LD document (source bf4c550c plus accepted encoded-type repair).
        var document = RemoveScripts(html);
        var bare = "https://www.maliev.com" + path;
        var english = bare + "?culture=en";
        var canonical = culture == "en" ? english : bare;
        var failures = new List<string>();
        void Require(bool condition, string contract)
        {
            if (!condition) failures.Add(contract);
        }

        Require(Attribute(Assert.Single(Tags(document, "html")), "lang") == culture, "resolved html language");
        var titles = Regex.Matches(document, "<title\\b[^>]*>(?<text>.*?)</title>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        Require(titles.Count == 1 && !string.IsNullOrWhiteSpace(titles[0].Groups["text"].Value), "one useful title");
        var descriptions = Meta(document, "name", "description");
        Require(descriptions.Length == 1 && Attribute(descriptions[0], "content").Trim().Length >= 40, "one description >=40 characters");
        var canonicals = Tags(document, "link").Where(tag => Attribute(tag, "rel") == "canonical").ToArray();
        Require(canonicals.Length == 1 && Attribute(canonicals[0], "href") == canonical, "one owned canonical with resolved UI culture");
        var alternates = Tags(document, "link").Where(tag => Attribute(tag, "rel") == "alternate" && Attribute(tag, "hreflang") != "").ToArray();
        Require(alternates.Length == 3, "exactly three localized alternates");
        foreach (var (language, href) in new[] { ("en", english), ("th", bare), ("x-default", bare) })
        {
            Require(alternates.Count(tag => Attribute(tag, "hreflang") == language && Attribute(tag, "href") == href) == 1, $"stable {language} alternate");
        }

        var headings = Regex.Matches(document, "<h1\\b[^>]*>(?<text>.*?)</h1>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        Require(headings.Count == 1 || (path == "/instantquotation/3d-printing" && headings.Count > 0), "source heading cardinality");
        Require(headings.Count > 0 && headings.Cast<Match>().All(match => !string.IsNullOrWhiteSpace(Regex.Replace(match.Groups["text"].Value, "<[^>]*>", ""))), "nonempty rendered H1");
        foreach (var property in new[] { "og:title", "og:description" })
        {
            var meta = Meta(document, "property", property);
            Require(meta.Length == 1 && !string.IsNullOrWhiteSpace(Attribute(meta[0], "content")), $"one useful {property}");
        }

        var robots = Meta(document, "name", "robots");
        if (path == "/quotation")
        {
            // Source d690 removes this route from discovery, not from the usable utility surface.
            Require(robots.Length == 1 && Attribute(robots[0], "content").Replace(" ", "", StringComparison.Ordinal) == "noindex,follow", "quotation utility noindex/follow");
            Require(response.Headers.TryGetValues("X-Robots-Tag", out var values) && values.SequenceEqual(new[] { "noindex, follow" }), "quotation original-folder header");
        }
        else
        {
            Require(robots.Length <= 1 && !robots.Any(tag => Regex.IsMatch(Attribute(tag, "content"), "\\b(noindex|nofollow)\\b", RegexOptions.IgnoreCase)), "indexable public metadata");
            Require(!response.Headers.TryGetValues("X-Robots-Tag", out var values) || !values.Any(value => Regex.IsMatch(value, "\\b(noindex|nofollow)\\b", RegexOptions.IgnoreCase)), "indexable public response header");
        }

        var nodes = new List<JsonElement>();
        foreach (Match script in Regex.Matches(html, "<script\\b(?<attributes>[^>]*)>(?<json>.*?)</script>", RegexOptions.Singleline | RegexOptions.IgnoreCase))
        {
            if (Attribute(script.Groups["attributes"].Value, "type") != "application/ld+json") continue;
            try
            {
                using var parsed = JsonDocument.Parse(script.Groups["json"].Value);
                Require(parsed.RootElement.ValueKind == JsonValueKind.Object && parsed.RootElement.TryGetProperty("@context", out var context) && context.ValueKind == JsonValueKind.String && context.GetString() == "https://schema.org", "valid owned schema.org JSON-LD context");
                nodes.Add(parsed.RootElement.Clone());
            }
            catch (JsonException)
            {
                failures.Add("malformed raw JSON-LD");
            }
        }

        if (!path.StartsWith("/knowledges", StringComparison.Ordinal)) Require(nodes.Count > 0, "source-required JSON-LD");
        if (path.StartsWith("/services/", StringComparison.Ordinal))
        {
            Require(nodes.Any(node => HasType(node, "Service")), "source-bed10 required Service JSON-LD");
        }
        Assert.True(failures.Count == 0, $"{path} {culture}: {string.Join("; ", failures)}");
    }

    private static bool HasType(JsonElement node, string expected)
    {
        if (node.ValueKind == JsonValueKind.Array) return node.EnumerateArray().Any(child => HasType(child, expected));
        if (node.ValueKind != JsonValueKind.Object) return false;
        if (node.TryGetProperty("@type", out var type) &&
            (type.ValueKind == JsonValueKind.String && type.GetString() == expected ||
             type.ValueKind == JsonValueKind.Array && type.EnumerateArray().Any(item => item.ValueKind == JsonValueKind.String && item.GetString() == expected))) return true;
        return node.EnumerateObject().Where(property => property.Name is not "@context" and not "@type")
            .Any(property => HasType(property.Value, expected));
    }

    private static string RemoveScripts(string html) => Regex.Replace(html, "<script\\b[^>]*>.*?</script>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
    private static string[] Tags(string html, string tag) => Regex.Matches(html, $"<{tag}\\b[^>]*>", RegexOptions.IgnoreCase).Select(match => match.Value).ToArray();
    private static string[] Meta(string html, string attribute, string value) => Tags(html, "meta").Where(tag => Attribute(tag, attribute).Equals(value, StringComparison.OrdinalIgnoreCase)).ToArray();
    private static string Attribute(string tag, string name) => WebUtility.HtmlDecode(Regex.Match(tag, $"\\b{Regex.Escape(name)}\\s*=\\s*[\"'](?<value>[^\"']*)[\"']", RegexOptions.IgnoreCase).Groups["value"].Value);
    private static HttpClient Client(WebApplicationFactory<Program> host) => host.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
        HandleCookies = false,
    });
}

/// <summary>Reuse the existing test host; control external reads and forbid checkpoint access.</summary>
public sealed class PublicCrawlerInventoryFixture : TestingWebApplicationFactory
{
    internal ConcurrentQueue<string> Exceptions { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<ICountryClient>();
            services.RemoveAll<ICareerClient>();
            services.RemoveAll<IInstantQuotationSubmissionStore>();
            services.AddSingleton<ICountryClient, Countries>();
            services.AddSingleton<ICareerClient, Careers>();
            services.AddSingleton<IInstantQuotationSubmissionStore, GetOnlySubmissionStore>();
            services.AddSingleton<ILoggerProvider>(new ExceptionCategories(Exceptions));
        });
    }

    internal sealed class GetOnlySubmissionStore : IInstantQuotationSubmissionStore
    {
        internal int Calls;

        public Task<IInstantQuotationSubmissionLease?> TryAcquireAsync(string submissionId, string ownerIdentity, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            throw new InvalidOperationException("Crawler GET must not access submission checkpoints.");
        }
    }

    private sealed class ExceptionCategories(ConcurrentQueue<string> exceptions) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new ExceptionLogger(exceptions);
        public void Dispose() { }

        private sealed class ExceptionLogger(ConcurrentQueue<string> exceptions) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (exception is null) return;
                // Constructor/render-mode messages contain type names, not request fields.
                var safeMessage = exception.Message.StartsWith("Multiple constructors", StringComparison.Ordinal)
                    || exception.Message.StartsWith("Unable to resolve service for type", StringComparison.Ordinal)
                    || exception.Message.StartsWith("Cannot render", StringComparison.Ordinal)
                    || exception.Message.StartsWith("A component of type", StringComparison.Ordinal)
                    ? exception.Message : "message omitted";
                exceptions.Enqueue($"{exception.GetType().Name} at {exception.TargetSite?.DeclaringType?.FullName}.{exception.TargetSite?.Name}: {safeMessage}");
            }
        }
    }

    private sealed class Countries : ICountryClient
    {
        public Task<ServiceResponse<IReadOnlyList<Country>>> GetCountriesAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new ServiceResponse<IReadOnlyList<Country>>([new Country(764, "Thailand", "Asia", "66", "TH", "THA", null, null)], true));
    }

    private sealed class Careers : ICareerClient
    {
        public Task<CareerListing> GetListingAsync(CareerSort sort, string? search, int pageIndex, int pageSize, CancellationToken cancellationToken) =>
            Task.FromResult(new CareerListing([], CareerOfferPage.Empty(pageIndex), true));
        public Task<ServiceResponse<CareerOffer>> GetOfferAsync(int offerId, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The public inventory has no career detail dependency.");
    }
}
