using System.Security.Cryptography;
using System.Text;

namespace Legacy.Maliev.Web.Tests;

public sealed class CncPlannerSourceBlobParityTests
{
    private static readonly IReadOnlyDictionary<string, string> SourceBlobIds =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["cnc-ball-rest.worker.js"] = "720a628c2c3c855fb762feb9736697c05c421028",
            ["cnc-cad-surfaces.worker.js"] = "f78d7dee0f3ac17f630b4c542c33ef834e230f1f",
            ["cnc-engine.js"] = "85240c0936ffed296b8a8efdb4e34fe3907fcd5e",
            ["cnc-feature-graph.worker.js"] = "123fb2855ec8dc5bd97f01292230cac55035e89d",
            ["cnc-fixture-catalog.js"] = "ca23ebaa6fdf5dec2d074d8b735450ff303adc6a",
            ["cnc-fixture-clearance.js"] = "fb052e4b6ec10064ef85f185dbb4eef73e6d2d3f",
            ["cnc-geometry.worker.js"] = "e8a5393e33efab878d7b596100d2df379f11f8bb",
            ["cnc-machine-capability.js"] = "ce2a6cc50078faba282cda566c518e17ace49088",
            ["cnc-manufacturing-evidence.worker.js"] = "3cad1aa4acfb3e7c053ae89cf936e28a8c2e3fff",
            ["cnc-material-catalog.js"] = "e74461e1aa4fbbcb1697fc6e8bf5e6d91ed0e9dc",
            ["cnc-plan-contracts.js"] = "1447d80983db5cb52c008bae00047268d8a1b7c7",
            ["cnc-planning.js"] = "07216dea763e9b32da1bf4df38cd30c23bc34d49",
            ["cnc-plan-validator.worker.js"] = "d417c4f36bc3e26f8d7cbbb373756d0045fccfb3",
            ["cnc-process-compiler.js"] = "012ba9f08df9a632834058961fd6e50516dedeb3",
            ["cnc-quotation-config.js"] = "857b7e459cd79eec1c0375bc2f5f367a333fadb2",
            ["cnc-quotation.worker.js"] = "e28eb03838920deae4abc45be99c7b56725dc59b",
            ["cnc-reach.js"] = "dfc69078387eef8c4fe4b48e372c9cc125dc1c36",
            ["cnc-setup-planner.js"] = "a04b4716626678edd1bf731b606ed7e5f2260bf3",
            ["cnc-spatial-field.worker.js"] = "55ff815130e80e44e99ae622f07a062a8acd480f",
            ["cnc-stock.js"] = "2799f4fff0e1288cc4bfcfa4dd9f929231345c0a",
            ["cnc-tool-library.js"] = "241e6cbe050b8bfc520298169f8e583e9c177142",
            ["cnc-topology.worker.js"] = "373d56c28a8cdffb068b003c0fcc7aac26ac06ec"
        };

    [Fact]
    public void PlannerAssets_MatchThePublishedSourceCheckpointExactly()
    {
        var assetDirectory = Path.Combine(
            FindRepositoryRoot(),
            "Legacy.Maliev.Web",
            "wwwroot",
            "src",
            "app",
            "js",
            "cnc-quotation");

        var actualFiles = Directory.EnumerateFiles(assetDirectory, "*.js")
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(SourceBlobIds.Keys.Order(StringComparer.Ordinal), actualFiles);

        foreach (var pair in SourceBlobIds)
        {
            var contents = File.ReadAllBytes(Path.Combine(assetDirectory, pair.Key));
            Assert.Equal(pair.Value, ComputeGitBlobId(contents));
        }
    }

    private static string ComputeGitBlobId(byte[] contents)
    {
        var header = Encoding.ASCII.GetBytes($"blob {contents.Length}\0");
        var input = new byte[header.Length + contents.Length];
        Buffer.BlockCopy(header, 0, input, 0, header.Length);
        Buffer.BlockCopy(contents, 0, input, header.Length, contents.Length);
        return Convert.ToHexStringLower(SHA1.HashData(input));
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
