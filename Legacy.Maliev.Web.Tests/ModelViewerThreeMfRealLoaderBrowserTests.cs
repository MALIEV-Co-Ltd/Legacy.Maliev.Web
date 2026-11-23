using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.Playwright;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Exercises the shipped browser loader with an in-memory synthetic package.</summary>
public sealed class ModelViewerThreeMfRealLoaderBrowserTests
{
    [Fact]
    public async Task SyntheticThreeMf_DecodesInWindowAndTransfersCopiesForAnalysis()
    {
        var root = BrowserHostIdentityVerifier.SourceProjectDirectory();
        string Read(string path) => File.ReadAllText(Path.Combine(root, "wwwroot", path));
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        await using var context = await browser.NewContextAsync();
        await context.RouteAsync("**/*", route => route.AbortAsync());
        var page = await context.NewPageAsync();
        await page.SetContentAsync("<!doctype html><html><head></head><body></body></html>");
        await page.AddScriptTagAsync(new() { Content = Read("src/vendor/three/three.js") });
        await page.AddScriptTagAsync(new() { Content = Read("lib/three/fflate.min.js") });
        await page.AddScriptTagAsync(new() { Content = Read("lib/three/3MFLoader.js") });
        var viewer = Read("src/app/js/model-viewer/model-viewer.js");
        await page.AddScriptTagAsync(new() { Content = viewer });

        var result = await page.EvaluateAsync<JsonElement>("""
            async ({ viewer, packageBase64 }) => {
                const start = viewer.indexOf('    this.ParseFile = function (file, onReady, options) {');
                const end = viewer.indexOf('    // Displays the given (already-parsed) object3D', start);
                if (start < 0 || end <= start) throw new Error('Parser closure not found');
                const bytes = Uint8Array.from(atob(packageBase64), character => character.charCodeAt(0));
                let analysis, transferCount = 0, parseWorkerCalled = false;
                const read = (file, ready, fail) => file.arrayBuffer().then(ready, fail);
                const analysisWorker = { Submit(message, transfers) {
                    transferCount = transfers.length;
                    analysis = structuredClone(message, { transfer: transfers });
                    return { jobId: 'synthetic-analysis', promise: Promise.resolve({ modelInfo: { bodyCount: 1 } }) };
                }, Cancel() {} };
                const parseWorker = { Submit() { parseWorkerCalled = true; throw new Error('Unexpected parse worker'); }, Cancel() {} };
                const parser = new Function('ReadArrayBuffer', 'ModelAnalysisWorkerManager', 'ModelParseWorkerManager',
                    'AddCadPreviewEdges', viewer.slice(start, end) + '\nreturn this;')
                    .call({}, read, analysisWorker, parseWorker, () => {});
                return await new Promise((resolve, reject) => {
                    parser.ParseFile(new File([bytes], 'synthetic.3mf'), (info, object, error) => {
                        if (error) { reject(new Error(error)); return; }
                        let preview;
                        object.traverse(child => { if (child.isMesh) preview = child.geometry; });
                        resolve({ sourceFormat: info.sourceFormat, parseWorkerCalled, transferCount,
                            action: analysis.action, meshes: analysis.meshes.length,
                            positions: Array.from(analysis.meshes[0].position),
                            previewPositionBytes: preview.attributes.position.array.byteLength });
                    });
                });
            }
            """, new { viewer, packageBase64 = Convert.ToBase64String(SyntheticPackage()) })
            .WaitAsync(TimeSpan.FromSeconds(15));

        Assert.Equal("3mf", result.GetProperty("sourceFormat").GetString());
        Assert.False(result.GetProperty("parseWorkerCalled").GetBoolean());
        Assert.Equal("analyze", result.GetProperty("action").GetString());
        Assert.Equal(1, result.GetProperty("meshes").GetInt32());
        Assert.InRange(result.GetProperty("transferCount").GetInt32(), 2, 3);
        Assert.Equal(new[] { 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f, 0f },
            result.GetProperty("positions").EnumerateArray().Select(value => value.GetSingle()).ToArray());
        Assert.True(result.GetProperty("previewPositionBytes").GetInt32() > 0);
    }

    private static byte[] SyntheticPackage()
    {
        using var bytes = new MemoryStream();
        using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string path, string text)
            {
                using var writer = new StreamWriter(zip.CreateEntry(path).Open(), new UTF8Encoding(false));
                writer.Write(text);
            }

            Add("[Content_Types].xml", "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"model\" ContentType=\"application/vnd.ms-package.3dmanufacturing-3dmodel+xml\"/></Types>");
            Add("_rels/.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"r0\" Type=\"http://schemas.microsoft.com/3dmanufacturing/2013/01/3dmodel\" Target=\"/3D/3dmodel.model\"/></Relationships>");
            Add("3D/3dmodel.model", "<model unit=\"millimeter\" xmlns=\"http://schemas.microsoft.com/3dmanufacturing/core/2015/02\"><resources><object id=\"1\" type=\"model\"><mesh><vertices><vertex x=\"0\" y=\"0\" z=\"0\"/><vertex x=\"1\" y=\"0\" z=\"0\"/><vertex x=\"0\" y=\"1\" z=\"0\"/></vertices><triangles><triangle v1=\"0\" v2=\"1\" v3=\"2\"/></triangles></mesh></object></resources><build><item objectid=\"1\"/></build></model>");
        }
        return bytes.ToArray();
    }
}
