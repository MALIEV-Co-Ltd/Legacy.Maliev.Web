// <copyright file="KnowledgeVisualAssetDeliveryTests.cs" company="Maliev Company Limited">
// Copyright (c) Maliev Company Limited. All rights reserved.
// </copyright>

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Playwright;
using Xunit.Abstractions;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Checks exact public visual bytes and decodes the full WebP response, not an HTML size hint.</summary>
/// <param name="output">The public, numerical measurement output.</param>
public sealed class KnowledgeVisualAssetDeliveryTests(ITestOutputHelper output)
{
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
                Assert.Equal(size, disk.Length);
                Assert.Equal(frozen.GetProperty("blob").GetString(), GitBlob(disk));
                string sha256 = Convert.ToHexStringLower(SHA256.HashData(disk));
                Assert.Equal(measured.GetProperty("sha256").GetString(), sha256);
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
                Assert.Equal(frozen.GetProperty("blob").GetString(), GitBlob(bytes));
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

    private static string GitBlob(byte[] bytes)
    {
        byte[] header = Encoding.ASCII.GetBytes($"blob {bytes.Length}\0");
        byte[] framed = new byte[header.Length + bytes.Length];
        header.CopyTo(framed, 0);
        bytes.CopyTo(framed, header.Length);
        return Convert.ToHexStringLower(SHA1.HashData(framed));
    }
}
