using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Legacy.Maliev.Web.Tests;

[Collection(CncNativeBrowserCollection.Name)]
public sealed class BrowserHostIdentityTests(CncNativeBrowserFixture fixture)
{
    [Fact]
    public async Task QuotationResponse_UsesCurrentReferencedApplicationBuild()
    {
        using var client = new HttpClient();
        string html = await client.GetStringAsync(fixture.CncQuotationUrl);

        BrowserHostIdentityVerifier.EnsureCurrentBuild(html);

        Assert.Equal(
            BrowserHostIdentityVerifier.ExpectedModelWorkerUrl,
            BrowserHostIdentityVerifier.ReadWorkerUrl(html, "malievModelWorkerUrl"));
        Assert.Equal(
            BrowserHostIdentityVerifier.ExpectedCncWorkerUrl,
            BrowserHostIdentityVerifier.ReadWorkerUrl(html, "malievCncQuotationWorkerUrl"));
    }

    [Fact]
    public void QuotationResponse_RejectsStaleOrMissingBuildIdentity()
    {
        string stale = "window.malievModelWorkerUrl = \"/src/app/js/model-viewer/model-viewer.worker.js?v=0000000000000000\";"
            + "window.malievCncQuotationWorkerUrl = \"/src/app/js/cnc-quotation/cnc-quotation.worker.js?v=0000000000000000\";";

        Assert.Throws<InvalidOperationException>(() => BrowserHostIdentityVerifier.EnsureCurrentBuild(stale));
        Assert.Throws<InvalidOperationException>(() => BrowserHostIdentityVerifier.EnsureCurrentBuild(string.Empty));
    }

    [Fact]
    public async Task QuotationHost_ServesCurrentWorkspaceAssetBytes()
    {
        const string asset = "src/app/js/model-viewer/model-viewer.worker.js";
        byte[] expected = await File.ReadAllBytesAsync(
            Path.Combine(BrowserHostIdentityVerifier.SourceProjectDirectory(), "wwwroot", asset));
        using var client = new HttpClient();
        byte[] actual = await client.GetByteArrayAsync(new Uri(new Uri(fixture.CncQuotationUrl), "/" + asset));

        Assert.NotEmpty(expected);
        Assert.Equal(expected, actual);
    }
}

internal static partial class BrowserHostIdentityVerifier
{
    private static readonly string AssetVersion =
        typeof(Program).Assembly.ManifestModule.ModuleVersionId.ToString("N")[..16];

    internal static string ExpectedModelWorkerUrl =>
        "/src/app/js/model-viewer/model-viewer.worker.js?v=" + AssetVersion;

    internal static string ExpectedCncWorkerUrl =>
        "/src/app/js/cnc-quotation/cnc-quotation.worker.js?v=" + AssetVersion;

    internal static void EnsureCurrentBuild(string html)
    {
        string modelWorker = ReadWorkerUrl(html, "malievModelWorkerUrl");
        string cncWorker = ReadWorkerUrl(html, "malievCncQuotationWorkerUrl");
        if (!string.Equals(modelWorker, ExpectedModelWorkerUrl, StringComparison.Ordinal)
            || !string.Equals(cncWorker, ExpectedCncWorkerUrl, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The quotation browser host is not serving the currently referenced application build.");
        }
    }

    internal static string ReadWorkerUrl(string html, string variable)
    {
        Match match = WorkerAssignment().Match(html);
        while (match.Success)
        {
            if (string.Equals(match.Groups[1].Value, variable, StringComparison.Ordinal))
            {
                return match.Groups[3].Value;
            }

            match = match.NextMatch();
        }

        throw new InvalidOperationException($"The quotation response does not identify {variable}.");
    }

    internal static string SourceProjectDirectory([CallerFilePath] string testSourcePath = "")
    {
        string testDirectory = Path.GetDirectoryName(testSourcePath)
            ?? throw new InvalidOperationException("The compiled test source path has no directory.");
        string projectDirectory = Path.GetFullPath(Path.Combine(testDirectory, "..", "Legacy.Maliev.Web"));
        if (!File.Exists(Path.Combine(projectDirectory, "Legacy.Maliev.Web.csproj")))
        {
            throw new DirectoryNotFoundException(
                "The browser fixture must use the application project from the same workspace as its test source.");
        }

        return projectDirectory;
    }

    [GeneratedRegex("window\\.(maliev(?:Model|CncQuotation)WorkerUrl)\\s*=\\s*([\\\"'])([^\\\"']+)\\2\\s*;", RegexOptions.CultureInvariant)]
    private static partial Regex WorkerAssignment();
}
