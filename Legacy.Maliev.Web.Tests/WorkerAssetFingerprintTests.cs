using System.Reflection;
using System.Security.Cryptography;

namespace Legacy.Maliev.Web.Tests;

public sealed class WorkerAssetFingerprintTests
{
    [Fact]
    public void BuildIdentity_ContainsHashesForEveryServedWorkerSource()
    {
        string projectDirectory = BrowserHostIdentityVerifier.SourceProjectDirectory();
        string scriptRoot = Path.Combine(projectDirectory, "wwwroot", "src", "app", "js");
        string modelWorker = Path.Combine(scriptRoot, "model-viewer", "model-viewer.worker.js");
        string[] assets =
        [
            modelWorker,
            .. Directory.EnumerateFiles(Path.Combine(scriptRoot, "additive-quotation"), "*.js", SearchOption.AllDirectories),
            .. Directory.EnumerateFiles(Path.Combine(scriptRoot, "cnc-quotation"), "*.js", SearchOption.AllDirectories),
        ];

        var fingerprints = typeof(Program).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Where(attribute => attribute.Key.StartsWith("WorkerAsset:", StringComparison.Ordinal))
            .ToDictionary(attribute => attribute.Key, attribute => attribute.Value, StringComparer.Ordinal);

        Assert.Equal(assets.Length, fingerprints.Count);
        foreach (string asset in assets)
        {
            string relativePath = Path.GetRelativePath(projectDirectory, asset).Replace('\\', '/');
            string expectedHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(asset)));
            string key = "WorkerAsset:" + relativePath;

            Assert.True(fingerprints.TryGetValue(key, out string? actualHash), $"Missing build fingerprint for {relativePath}.");
            Assert.Equal(expectedHash, actualHash, ignoreCase: true);
        }
    }
}
