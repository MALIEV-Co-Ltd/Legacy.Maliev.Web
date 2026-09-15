using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Legacy.Maliev.Web.Tests;

public sealed class SourceParityThrough7c416ccTests
{
    private static readonly string[] ExpectedCommits =
    [
        "14a7f42608526b1773643e8050a2ec9c775e6723",
        "362308b605ff94878f684258ade46c67ae0b08ee",
        "e78ab85594e688aed223f54ef31c7b6df399a735",
        "99462c12c33fc62da281fc90fc61a8ee458d0d7a",
        "d957038a208b2cbdf1604010a6d82f1d74f1a06c",
        "2128395c31a1ebfe94f9d0ced846bdc861fe62f4",
        "05c42bcf7529e9d606803c271490baed59546bc1",
        "047a7b673c397c388d974b030d55e8eee5b88d69",
        "813f8ffdcd6b2cb1ed567f5264807133ddd482b7",
        "a15177b36ecb8a729e5eae61238ca9a073d10184",
        "a22cc927ef943e9a9f4ab9dbdd6337befdd06eab",
        "60d3677264e046fd73028ff3ccb75097d153bedb",
        "941cd2757246527108eb0dddce7582d1a47ea442",
        "2d126d1d55a3240e40678136e6aa621c7f10ed47",
        "cc5b3864b8752f5e5e421f01a68d8925c940021a",
        "9e9c7c5edfd2574ef721623951e3f50de1272a6a",
        "d15a44524698e315bc712139ef4dc2e8e35f3013",
        "3ce9936b6e39f41c000e542f005a18645f445a8a",
        "7c416cc8cfd27ef7440e7c046529011630276807"
    ];

    [Fact]
    public void Manifest_FreezesCurrentSourceDeltaWithoutGaps()
    {
        string path = Path.Combine(FindRepositoryRoot(), "docs", "source-parity-delta-through-7c416cc.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = document.RootElement;

        Assert.Equal("1.1", root.GetProperty("schemaVersion").GetString());
        Assert.Equal("5ac7d045c51194edd9e64d8564f1b726b001be34", root.GetProperty("historicalCheckpoint").GetString());
        Assert.Equal("7c416cc8cfd27ef7440e7c046529011630276807", root.GetProperty("sourceHead").GetString());
        JsonElement entries = root.GetProperty("entries");
        Assert.Equal(ExpectedCommits.Length, root.GetProperty("commitCount").GetInt32());
        Assert.Equal(ExpectedCommits.Length, entries.GetArrayLength());

        var actual = new List<string>(entries.GetArrayLength());
        var semantic = new StringBuilder();
        AddValue(semantic, "source-parity-semantic-stream-v1");
        AddValue(semantic, root.GetProperty("schemaVersion").GetString()!);
        AddValue(semantic, root.GetProperty("historicalCheckpoint").GetString()!);
        AddValue(semantic, root.GetProperty("sourceHead").GetString()!);
        AddValue(semantic, root.GetProperty("commitCount").GetInt32().ToString());
        AddArray(semantic, root.GetProperty("allowedOwners"));
        AddArray(semantic, root.GetProperty("allowedClassifications"));

        int sequence = 1;
        foreach (JsonElement entry in entries.EnumerateArray())
        {
            Assert.Equal(sequence, entry.GetProperty("sequence").GetInt32());
            string commit = entry.GetProperty("sourceCommit").GetString()!;
            Assert.Equal(ExpectedCommits[sequence - 1], commit);
            actual.Add(commit);

            string classification = entry.GetProperty("classification").GetString()!;
            Assert.DoesNotContain("pending", classification, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("gap", classification, StringComparison.OrdinalIgnoreCase);
            Assert.False(string.IsNullOrWhiteSpace(entry.GetProperty("validationEvidence").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(entry.GetProperty("classificationRationale").GetString()));

            JsonElement owners = entry.GetProperty("owners");
            JsonElement targets = entry.GetProperty("targetEvidence");
            Assert.Equal(owners.GetArrayLength(), targets.GetArrayLength());
            Assert.True(owners.GetArrayLength() > 0 || entry.GetProperty("retirements").GetArrayLength() > 0 || entry.GetProperty("exclusions").GetArrayLength() > 0);

            AddValue(semantic, sequence.ToString());
            AddValue(semantic, commit);
            AddValue(semantic, entry.GetProperty("subject").GetString()!);
            AddValue(semantic, classification);
            AddArray(semantic, owners);
            AddArray(semantic, entry.GetProperty("retirements"));
            AddArray(semantic, entry.GetProperty("exclusions"));
            AddArray(semantic, entry.GetProperty("sourceAreas"));
            AddValue(semantic, targets.GetArrayLength().ToString());
            foreach (JsonElement target in targets.EnumerateArray())
            {
                AddValue(semantic, target.GetProperty("repository").GetString()!);
                AddValue(semantic, target.GetProperty("commit").GetString()!);
            }

            AddValue(semantic, entry.GetProperty("validationEvidence").GetString()!);
            AddValue(semantic, entry.GetProperty("classificationRationale").GetString()!);
            sequence++;
        }

        Assert.Equal(ExpectedCommits, actual);
        string sequenceHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', actual) + "\n"))).ToLowerInvariant();
        Assert.Equal("169e7c130507a43a8eab5df9c3d907b6f6a6422126eb7a9946c4e12d85710fb8", sequenceHash);
        Assert.Equal(sequenceHash, root.GetProperty("sequenceSha256").GetString());
        string semanticHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(semantic.ToString()))).ToLowerInvariant();
        Assert.Equal("087ed874e3594dbb2822a21ede4eb0807aed1ef52f352debfe2441d6b2584b7a", semanticHash);
        Assert.Equal(semanticHash, root.GetProperty("semanticSha256").GetString());
    }

    private static void AddArray(StringBuilder builder, JsonElement values)
    {
        JsonElement[] items = [.. values.EnumerateArray()];
        AddValue(builder, items.Length.ToString());
        foreach (JsonElement item in items)
        {
            AddValue(builder, item.GetString()!);
        }
    }

    private static void AddValue(StringBuilder builder, string value) =>
        builder.Append(value.Length).Append(':').Append(value).Append('\n');

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Web.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Legacy.Maliev.Web repository root not found.");
    }
}
