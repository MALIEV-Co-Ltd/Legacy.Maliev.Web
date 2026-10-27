using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Maliev.AdditiveBenchmark;

/// <summary>Creates a deterministic anonymous inventory of a restricted CAD corpus.</summary>
public static class PrivateCorpusInventory
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".stl",
        ".obj",
        ".3mf",
    };

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    /// <summary>Hashes and selects unique restricted models without emitting paths or names.</summary>
    public static PrivateCorpusInventoryResult Create(string root, int limit, string consentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(consentId);
        if (limit < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }

        string fullRoot = Path.GetFullPath(root);
        if (!Directory.Exists(fullRoot))
        {
            throw new DirectoryNotFoundException("The restricted corpus root is unavailable.");
        }

        FileCandidate[] candidates = Directory.EnumerateFiles(fullRoot, "*", SearchOption.AllDirectories)
            .Select(path => new FileInfo(path))
            .Where(file => SupportedExtensions.Contains(file.Extension))
            .Select(file => new FileCandidate(file.FullName, file.Length, Bucket(file.Length), file.Extension.ToLowerInvariant()))
            .OrderBy(file => file.Bucket, StringComparer.Ordinal)
            .ThenBy(file => file.Bytes)
            .ThenBy(file => file.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var selected = new List<HashedCandidate>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        string[] buckets = ["small", "medium", "large", "very-large"];
        int targetPerBucket = Math.Max(1, (int)Math.Ceiling(limit / (double)buckets.Length));

        foreach (string bucket in buckets)
        {
            foreach (FileCandidate candidate in candidates.Where(item => item.Bucket == bucket))
            {
                HashedCandidate hashed = Hash(candidate);
                if (!seen.Add(hashed.Sha256))
                {
                    continue;
                }

                selected.Add(hashed);
                if (selected.Count(item => item.Bucket == bucket) >= targetPerBucket || selected.Count == limit)
                {
                    break;
                }
            }

            if (selected.Count == limit)
            {
                break;
            }
        }

        if (selected.Count < limit)
        {
            foreach (FileCandidate candidate in candidates)
            {
                HashedCandidate hashed = Hash(candidate);
                if (!seen.Add(hashed.Sha256))
                {
                    continue;
                }

                selected.Add(hashed);
                if (selected.Count == limit)
                {
                    break;
                }
            }
        }

        HashedCandidate[] ordered = selected
            .OrderBy(item => item.Sha256, StringComparer.Ordinal)
            .ToArray();
        int releaseCount = Math.Min(12, Math.Max(1, ordered.Length / 4));
        CorpusEntry[] entries = ordered.Select((item, index) => new CorpusEntry(
            $"private-{index + 1:D3}",
            item.Sha256,
            item.Bytes,
            item.Extension,
            item.Bucket,
            index < releaseCount ? "release-holdout" : "development",
            "unreviewed",
            "matched_reference_missing")).ToArray();
        var document = new CorpusDocument(
            "private-corpus.v1",
            "2026-09-20.1",
            consentId,
            "restricted-read-only",
            "sha256-sorted; exact-byte duplicates removed; release holdout frozen before tuning",
            entries.Length,
            entries);
        string json = JsonSerializer.Serialize(document, SerializerOptions) + Environment.NewLine;
        return new PrivateCorpusInventoryResult(json, entries.Length);
    }

    private static HashedCandidate Hash(FileCandidate candidate)
    {
        using FileStream stream = File.OpenRead(candidate.Path);
        string digest = Convert.ToHexString(SHA256.HashData(stream));
        return new HashedCandidate(digest, candidate.Bytes, candidate.Bucket, candidate.Extension);
    }

    private static string Bucket(long bytes) => bytes switch
    {
        <= 1_000_000 => "small",
        <= 10_000_000 => "medium",
        <= 100_000_000 => "large",
        _ => "very-large",
    };

    private sealed record FileCandidate(string Path, long Bytes, string Bucket, string Extension);

    private sealed record HashedCandidate(string Sha256, long Bytes, string Bucket, string Extension);

    private sealed record CorpusDocument(
        string SchemaVersion,
        string CorpusVersion,
        string ConsentId,
        string Availability,
        string SelectionPolicy,
        int EntryCount,
        IReadOnlyList<CorpusEntry> Entries);

    private sealed record CorpusEntry(
        string Id,
        string Sha256,
        long Bytes,
        string Format,
        string ByteSizeBucket,
        string Split,
        string GeometryFamily,
        string Availability);
}

/// <summary>Serialized private-corpus inventory and selected unique-entry count.</summary>
public sealed record PrivateCorpusInventoryResult(string Json, int EntryCount);
