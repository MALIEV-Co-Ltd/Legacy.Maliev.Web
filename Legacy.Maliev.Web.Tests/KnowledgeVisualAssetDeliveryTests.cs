// <copyright file="KnowledgeVisualAssetDeliveryTests.cs" company="Maliev Company Limited">
// Copyright (c) Maliev Company Limited. All rights reserved.
// </copyright>

using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Playwright;
using Xunit.Abstractions;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Checks exact public visual bytes and decodes the full WebP response, not an HTML size hint.</summary>
/// <param name="output">The public, numerical measurement output.</param>
public sealed class KnowledgeVisualAssetDeliveryTests(ITestOutputHelper output)
{
    private const string DrawingFooter = "<text x=\"480\" y=\"457\" font-size=\"15\" text-anchor=\"middle\" fill=\"#53627b\">ILLUSTRATIVE EXAMPLE · NOT A COMPLETE PRODUCTION DRAWING</text>";
    private static readonly (string File, string Route, string Blob, int Bytes, string Sha256, string Marker)[] Source930 =
    [
        ("visual-hole-depths.svg", "/knowledges/cnc/holes-and-tolerances", "164d84c3ac22ccd0e45c3a1128a526f8e9973709", 4904, "4b3eef809f4e0df33c51e52880918c38ae5e08b770ec08df6a790f5794a99b27", "h₂"),
        ("visual-material-criteria.svg", "/knowledges/materials/selection", "292081cd3525db7ed62779483b0b3f9ac39f0efb", 4163, "28e15bb0fb6ba0e1b1a65426e3790a786f9a363e46ba834a4eee67d4d7e2bd1e", "Radial gap = c / 2"),
        ("visual-quotation-package.svg", "/knowledges/guidelines", "65e8bd788a08096ed0c47cffa316b7621daac877", 3156, "79a5880ea032c851ba278456ca846d3a0a8ca7e468208b32fa676c9e62f97a60", "Q = 4 · REV 02"),
        ("visual-review-loop.svg", "/knowledges/workflow", "f9c28311541b4a4251c7a0d6b8aa0a8555874e73", 3053, "ae34525d8ee6021ce2d3cf232b501ef26b993e5c12d5ae3f3123295a2f05f22b", "CHANGE → REVIEW AGAIN"),
        ("visual-service-inputs.svg", "/knowledges/specifications", "4a9275a0e0588269d964db9a30303ce24b97d48c", 2959, "bb0d02d940631a62fe0e83a2e38483d7f591f5fdecf83fb6239d0c4ae21f6e65", "60 ±0.10"),
    ];


    /// <summary>All source visuals retain their exact bytes, MIME types, and independently decoded dimensions.</summary>
    /// <returns>The delivery and Chromium decoding verification task.</returns>
    [Fact]
    public async Task KnowledgeVisuals_ExactHttpBytesAndDecodedDimensionsMatchFrozenSource()
    {
        string project = BrowserHostIdentityVerifier.SourceProjectDirectory();
        string root = Path.GetFullPath(Path.Combine(project, ".."));
        byte[] seedBytes = await File.ReadAllBytesAsync(Path.Combine(root, "docs/migration/knowledge-source-intake.v1.json"));
        Assert.Equal("5fd485eaecf9ef343e588ce2b737088d8bddd778", GitBlob(seedBytes));
        using var seed = JsonDocument.Parse(seedBytes);
        using var manifest = JsonDocument.Parse(await File.ReadAllBytesAsync(
            Path.Combine(root, "docs/migration/knowledge-assets.v1.json")));
        Assert.Equal("maliev-knowledge-assets.v1", manifest.RootElement.GetProperty("schema").GetString());
        Assert.Equal("770cb58032969ae4a0fe169686b53ab61a44fff8", manifest.RootElement.GetProperty("sourceCommit").GetString());
        Assert.Equal("a1ef9061734c2a57fd6b621b56559c3efa59168e", manifest.RootElement.GetProperty("sourceTree").GetString());
        var expected = seed.RootElement.GetProperty("rows").EnumerateArray().ToArray();
        var actual = manifest.RootElement.GetProperty("rows").EnumerateArray().ToArray();
        Assert.Equal(70, expected.Length);
        Assert.Equal(70, actual.Length);

        var factory = new TestingWebApplicationFactory(project);
        HttpClient? client = null;
        IPlaywright? playwright = null;
        IBrowser? browser = null;
        IBrowserContext? context = null;
        Exception? primary = null;
        var measurements = new List<object>();
        int photos = 0, diagrams = 0;
        try
        {
            client = factory.CreateClient(new() { AllowAutoRedirect = false });
            client.Timeout = TimeSpan.FromSeconds(30);
            playwright = await Playwright.CreateAsync();
            browser = await playwright.Chromium.LaunchAsync(new() { Headless = true, Timeout = 30_000 });
            context = await browser.NewContextAsync();
            await context.RouteAsync("**/*", route => route.AbortAsync());
            var page = await context.NewPageAsync();
            for (int index = 0; index < expected.Length; index++)
            {
                var frozen = expected[index];
                var measured = actual[index];
                string path = frozen.GetProperty("targetPath").GetString()!;
                string kind = frozen.GetProperty("kind").GetString()!;
                foreach (string key in new[] { "sourcePath", "targetPath", "blob", "kind" })
                {
                    Assert.Equal(frozen.GetProperty(key).GetString(), measured.GetProperty(key).GetString());
                }

                int size = frozen.GetProperty("bytes").GetInt32();
                Assert.Equal(size, measured.GetProperty("bytes").GetInt32());
                byte[] disk = await File.ReadAllBytesAsync(Path.Combine(root, path));
                var evolution = Source930.SingleOrDefault(row =>
                    path == "Legacy.Maliev.Web/wwwroot/src/images/knowledge/" + row.File);
                byte[] historical = disk;
                if (evolution.Blob is not null)
                {
                    // Preserve the immutable 770 inventory; prove the exact later five-file source edit separately.
                    string current = Encoding.UTF8.GetString(disk);
                    int footerPosition = current.LastIndexOf("\n</g>", StringComparison.Ordinal);
                    Assert.True(footerPosition >= 0);
                    historical = Encoding.UTF8.GetBytes(current.Insert(footerPosition, DrawingFooter));
                }
                Assert.Equal(size, historical.Length);
                Assert.Equal(frozen.GetProperty("blob").GetString(), GitBlob(historical));
                Assert.Equal(measured.GetProperty("sha256").GetString(),
                    Convert.ToHexStringLower(SHA256.HashData(historical)));
                string currentBlob = evolution.Blob ?? frozen.GetProperty("blob").GetString()!;
                size = evolution.Blob is null ? size : evolution.Bytes;
                Assert.Equal(size, disk.Length);
                Assert.Equal(currentBlob, GitBlob(disk));
                string sha256 = Convert.ToHexStringLower(SHA256.HashData(disk));
                Assert.Equal(evolution.Sha256 ?? measured.GetProperty("sha256").GetString(), sha256);
                if (kind == "provenance")
                {
                    continue;
                }

                const string prefix = "Legacy.Maliev.Web/wwwroot/";
                Assert.StartsWith(prefix, path);
                string url = "/" + path[prefix.Length..];
                using var response = await client.GetAsync(url);
                Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
                Assert.Equal(kind == "webp" ? "image/webp" : "image/svg+xml", response.Content.Headers.ContentType?.MediaType);
                byte[] bytes = await response.Content.ReadAsByteArrayAsync();
                Assert.Equal(size, bytes.Length);
                Assert.Equal(sha256, Convert.ToHexStringLower(SHA256.HashData(bytes)));
                Assert.Equal(currentBlob, GitBlob(bytes));
                if (kind == "webp")
                {
                    await using var decoded = await page.WaitForFunctionAsync(
                        """
                        async encoded => {
                            const bytes = Uint8Array.from(atob(encoded), character => character.charCodeAt(0));
                            const bitmap = await createImageBitmap(new Blob([bytes], { type: "image/webp" }));
                            try { return [bitmap.width, bitmap.height]; }
                            finally { bitmap.close(); }
                        }
                        """,
                        Convert.ToBase64String(bytes),
                        new() { Timeout = 30_000 });
                    int[] dimensions = await decoded.JsonValueAsync<int[]>();
                    bool small = path.EndsWith("-small.webp", StringComparison.Ordinal);
                    Assert.Equal(small ? new[] { 768, 512 } : new[] { 1536, 1024 }, dimensions);
                    measurements.Add(new { path, bytes = bytes.Length, sha256, width = dimensions[0], height = dimensions[1] });
                    photos++;
                }
                else
                {
                    diagrams++;
                }
            }

            Assert.Equal(42, photos);
            Assert.Equal(17, diagrams);
            var inherited = seed.RootElement.GetProperty("inherited").EnumerateArray().ToArray();
            Assert.Equal(2, inherited.Length);
            foreach (var frozen in inherited)
            {
                string path = frozen.GetProperty("targetPath").GetString()!;
                const string prefix = "Legacy.Maliev.Web/wwwroot/";
                Assert.StartsWith(prefix, path);
                byte[] disk = await File.ReadAllBytesAsync(Path.Combine(root, path));
                Assert.Equal(frozen.GetProperty("bytes").GetInt32(), disk.Length);
                Assert.Equal(frozen.GetProperty("blob").GetString(), GitBlob(disk));
                using var response = await client.GetAsync("/" + path[prefix.Length..]);
                Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
                Assert.Equal("image/svg+xml", response.Content.Headers.ContentType?.MediaType);
                byte[] bytes = await response.Content.ReadAsByteArrayAsync();
                Assert.Equal(disk.Length, bytes.Length);
                Assert.Equal(GitBlob(disk), GitBlob(bytes));
                Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(disk)),
                    Convert.ToHexStringLower(SHA256.HashData(bytes)));
            }
            // Public asset identities and numerical dimensions only; no application state or bodies.
            output.WriteLine(JsonSerializer.Serialize(new { schema = 1, photos, diagrams, measurements }));
        }
        catch (Exception exception)
        {
            primary = exception;
            throw;
        }
        finally
        {
            await MaterialPricingTestCleanup.ReleaseAllAsync(
                primary,
                () => context is null ? ValueTask.CompletedTask : context.DisposeAsync(),
                () => browser is null ? ValueTask.CompletedTask : browser.DisposeAsync(),
                () => { playwright?.Dispose(); return ValueTask.CompletedTask; },
                () => { client?.Dispose(); return ValueTask.CompletedTask; },
                () => factory.DisposeAsync());
        }
    }


    /// <summary>Both route owners deliver the exact evolved diagrams without deleting accessible technical content.</summary>
    [Theory]
    [InlineData("en", false)]
    [InlineData("th", false)]
    [InlineData("en", true)]
    [InlineData("th", true)]
    public async Task Source930_ActualArticleLinksDeliverTheLabelFreeTechnicalSvg(string culture, bool retained)
    {
        await using var parent = new TestingWebApplicationFactory();
        await using var application = parent.WithWebHostBuilder(builder =>
        {
            foreach (string feature in new[]
            {
                "BlazorRouting:KnowledgesIndex", "BlazorRouting:KnowledgesGuidelines",
                "BlazorRouting:KnowledgesWorkflow", "BlazorRouting:KnowledgesSpecifications",
            })
                builder.UseSetting(feature, retained ? "false" : "true");
        });
        using var client = application.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false,
        });
        foreach (var expected in Source930)
        {
            using var article = await client.GetAsync($"{expected.Route}?culture={culture}");
            Assert.Equal(HttpStatusCode.OK, article.StatusCode);
            string html = WebUtility.HtmlDecode(await article.Content.ReadAsStringAsync());
            Assert.Contains($"<html lang=\"{culture}\"", html, StringComparison.Ordinal);
            string url = "/src/images/knowledge/" + expected.File;
            var image = Regex.Match(html, "<img\\b[^>]*src=\"(" + Regex.Escape(url) + ")\"[^>]*>");
            Assert.True(image.Success);
            Assert.Contains("alt=\"", image.Value, StringComparison.Ordinal);
            Assert.Contains("width=\"960\"", image.Value, StringComparison.Ordinal);
            Assert.Contains("height=\"480\"", image.Value, StringComparison.Ordinal);
            Assert.Contains("href=\"" + url + "\" target=\"_blank\" rel=\"noopener\"", html, StringComparison.Ordinal);
            using var asset = await client.GetAsync(image.Groups[1].Value);
            Assert.Equal(HttpStatusCode.OK, asset.StatusCode);
            Assert.Equal("image/svg+xml", asset.Content.Headers.ContentType?.MediaType);
            byte[] bytes = await asset.Content.ReadAsByteArrayAsync();
            Assert.Equal(expected.Bytes, bytes.Length);
            Assert.Equal(expected.Blob, GitBlob(bytes));
            Assert.Equal(expected.Sha256, Convert.ToHexStringLower(SHA256.HashData(bytes)));
            var svg = XDocument.Parse(Encoding.UTF8.GetString(bytes));
            XNamespace ns = "http://www.w3.org/2000/svg";
            var element = Assert.IsType<XElement>(svg.Root);
            Assert.Equal(ns + "svg", element.Name);
            Assert.Equal("0 0 960 480", element.Attribute("viewBox")?.Value);
            Assert.Equal("img", element.Attribute("role")?.Value);
            Assert.Equal("title desc", element.Attribute("aria-labelledby")?.Value);
            Assert.False(string.IsNullOrWhiteSpace(element.Element(ns + "title")?.Value));
            Assert.False(string.IsNullOrWhiteSpace(element.Element(ns + "desc")?.Value));
            var labels = svg.Descendants(ns + "text").Select(node => node.Value).ToArray();
            Assert.Contains(expected.Marker, labels);
            Assert.DoesNotContain("ILLUSTRATIVE EXAMPLE · NOT A COMPLETE PRODUCTION DRAWING", labels);
            Assert.NotEmpty(svg.Descendants(ns + "path"));
        }
    }


    /// <summary>Both route owners deliver final figures while all twelve final source image URLs remain exact.</summary>
    [Theory]
    [InlineData("en", false)]
    [InlineData("th", false)]
    [InlineData("en", true)]
    [InlineData("th", true)]
    public async Task ProcessIllustrations_FinalSourceV3IsLocalizedAndActuallyServed(string culture, bool retained)
    {
        await using var parent = new TestingWebApplicationFactory();
        await using var application = parent.WithWebHostBuilder(builder =>
            builder.UseSetting("BlazorRouting:KnowledgesIndex", retained ? "false" : "true"));
        using var client = application.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false,
        });
        using var article = await client.GetAsync("/knowledges/manufacturing/process-selection?culture=" + culture);
        Assert.Equal(HttpStatusCode.OK, article.StatusCode);
        string html = WebUtility.HtmlDecode(await article.Content.ReadAsStringAsync());
        Assert.Contains($"<html lang=\"{culture}\"", html, StringComparison.Ordinal);
        var figures = Regex.Matches(html,
            "<figure class=\"docs-figure knowledge-diagram knowledge-process-illustration\">(?<body>.*?)</figure>",
            RegexOptions.Singleline).Cast<Match>().ToArray();
        Assert.Equal(3, figures.Length);
        var topics = new[] { "milling", "fdm", "air-bending" };
        var alt = culture == "th"
            ? new[] { "ดอกเอ็นมิลแนวตั้งกัดโพ็กเก็ตบนชิ้นอะลูมิเนียมที่จับยึดด้วยปากกา",
                "หัวฉีด FDM วางแนวพลาสติกสีน้ำเงินบนชั้นบนสุดของถาด โดยมีชั้นเดิมรองรับอยู่ด้านล่าง",
                "พันช์ของเครื่องพับกดแผ่นสีน้ำเงินที่ต่อเนื่องเป็นชิ้นเดียว โดยแผ่นวางบนบ่าทั้งสองด้านของแม่พิมพ์ตัววี" }
            : new[] { "A vertical end mill cuts an aluminum pocket while a vise grips the billet.",
                "An FDM nozzle deposits a blue bead onto the supported top layer of a printed tray.",
                "A press-brake punch bends a continuous blue sheet over the two shoulders of a V-die." };
        var labels = culture == "th"
            ? new[] { "การตัดเฉือน CNC", "พิมพ์ FDM แบบอัดรีดวัสดุ", "พับแผ่นด้วยเครื่องพับแบบ Air bending" }
            : new[] { "CNC machining", "FDM material extrusion", "Press-brake air bending" };
        for (int index = 0; index < figures.Length; index++)
        {
            string body = figures[index].Groups["body"].Value;
            string full = "/src/images/knowledge/diagrams/process-" + topics[index] + "-v3.webp";
            string small = full.Replace(".webp", "-small.webp", StringComparison.Ordinal);
            Assert.Contains($"src=\"{full}\"", body, StringComparison.Ordinal);
            Assert.Contains($"srcset=\"{small} 768w, {full} 1536w\"", body, StringComparison.Ordinal);
            Assert.Contains("sizes=\"(min-width: 48rem) 48rem, calc(100vw - 4rem)\"", body, StringComparison.Ordinal);
            Assert.Contains("width=\"1536\" height=\"1024\"", body, StringComparison.Ordinal);
            Assert.Contains($"alt=\"{alt[index]}\"", body, StringComparison.Ordinal);
            Assert.Contains($"aria-label=\"{alt[index]}\"", body, StringComparison.Ordinal);
            Assert.Contains("role=\"region\" tabindex=\"0\"", body, StringComparison.Ordinal);
            Assert.Contains($"<strong>{labels[index]}</strong>", body, StringComparison.Ordinal);
            Assert.Contains($"href=\"{full}\" target=\"_blank\" rel=\"noopener\"", body, StringComparison.Ordinal);
            Assert.DoesNotContain("-v2", body, StringComparison.Ordinal);
            Assert.DoesNotContain("visual-process-routes.svg", body, StringComparison.Ordinal);
        }

        (string File, string Blob, int Bytes)[] assets =
        [
            ("photos/process-air-bending-v2-small.webp", "0390d76d289086002011ea3c17318bbae9f4a118", 26554),
            ("photos/process-air-bending-v2.webp", "407e49f1bb15c2c7a56dfe597086c1a85876a18e", 112096),
            ("photos/process-fdm-v2-small.webp", "bbd1924ea3233b345f5e62710747daf0668ef1ef", 49418),
            ("photos/process-fdm-v2.webp", "40edf15d12e971f9b59647ad73ce496fec367741", 151828),
            ("photos/process-milling-v2-small.webp", "a5009177c9a971eb88cb1db74f7ef467fe31d17f", 54044),
            ("photos/process-milling-v2.webp", "20fe39f64b4eb6622efc77f83c2ac430e851f406", 228600),
            ("diagrams/process-air-bending-v3-small.webp", "03780d544f142bab1bf874f26253acbad50146cf", 23690),
            ("diagrams/process-air-bending-v3.webp", "f2745e40f83fc2f78d9a7a7b294cb8bcc4674ca0", 63098),
            ("diagrams/process-fdm-v3-small.webp", "3236aaf1c24b49ee94ac5b69966be5503bbd8870", 43088),
            ("diagrams/process-fdm-v3.webp", "88a82e1235d02a751673356299e1d99398ab308e", 116834),
            ("diagrams/process-milling-v3-small.webp", "bbb7b2982e306c119202915c8aefbbc1b60b70f5", 42138),
            ("diagrams/process-milling-v3.webp", "816b62e9755077c6a8e991d290560d3c7796a2cd", 106930),
        ];
        Assert.Equal(12, assets.Length);
        foreach (var expected in assets)
        {
            using var asset = await client.GetAsync("/src/images/knowledge/" + expected.File);
            Assert.Equal(HttpStatusCode.OK, asset.StatusCode);
            Assert.Equal("image/webp", asset.Content.Headers.ContentType?.MediaType);
            byte[] data = await asset.Content.ReadAsByteArrayAsync();
            Assert.Equal(expected.Bytes, data.Length);
            Assert.Equal(expected.Blob, GitBlob(data));
        }
    }

    private static string GitBlob(byte[] bytes)
    {
        byte[] header = Encoding.ASCII.GetBytes($"blob {bytes.Length}\0");
        byte[] framed = new byte[header.Length + bytes.Length];
        header.CopyTo(framed, 0);
        bytes.CopyTo(framed, header.Length);
        return Convert.ToHexStringLower(SHA1.HashData(framed));
    }
}
