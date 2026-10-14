using System.Text.Json;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Checks the restored DataProtection chain used by the hosted test assembly.</summary>
public sealed class StartupDataProtectionMigrationTestsDependencyGraph
{
    [Fact]
    public void RestoredDataProtectionChain_UsesPatchedXmlAndPkcsVersions()
    {
        var assemblyName = typeof(StartupDataProtectionMigrationTestsDependencyGraph).Assembly.GetName().Name;
        var manifest = Path.Combine(AppContext.BaseDirectory, $"{assemblyName}.deps.json");
        Assert.True(File.Exists(manifest), "The compiled test dependency manifest must be retained.");
        using var dependencies = JsonDocument.Parse(File.ReadAllText(manifest));
        var libraries = dependencies.RootElement.GetProperty("libraries");
        foreach (var package in new[]
        {
            "Microsoft.AspNetCore.DataProtection.StackExchangeRedis",
            "Microsoft.AspNetCore.DataProtection",
            "System.Security.Cryptography.Xml",
            "System.Security.Cryptography.Pkcs",
        })
        {
            Assert.True(libraries.TryGetProperty($"{package}/10.0.13", out var library),
                $"The restored {package} dependency must resolve to the reviewed patched version 10.0.13.");
            Assert.Equal("package", library.GetProperty("type").GetString());
            Assert.False(libraries.TryGetProperty($"{package}/10.0.12", out _),
                $"The affected {package} version 10.0.12 must not remain in the restored graph.");
        }
    }
}
