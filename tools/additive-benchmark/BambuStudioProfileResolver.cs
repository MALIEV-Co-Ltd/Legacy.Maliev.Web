using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Maliev.AdditiveBenchmark;

/// <summary>
/// Resolves Bambu Studio preset inheritance into a deterministic complete JSON document.
/// </summary>
public static class BambuStudioProfileResolver
{
    /// <summary>
    /// Resolves one preset and all named <c>inherits</c>/<c>include</c> dependencies.
    /// </summary>
    /// <param name="profilePath">Leaf preset JSON path.</param>
    /// <param name="searchRoots">Directories containing candidate preset JSON files.</param>
    /// <returns>The complete settings and their provenance.</returns>
    public static ResolvedBambuProfile Resolve(string profilePath, IReadOnlyCollection<string> searchRoots)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profilePath);
        ArgumentNullException.ThrowIfNull(searchRoots);

        if (!File.Exists(profilePath))
        {
            throw new FileNotFoundException("Bambu Studio profile was not found.", profilePath);
        }

        Dictionary<string, string> profilesByName = BuildProfileIndex(searchRoots.Append(Path.GetDirectoryName(Path.GetFullPath(profilePath))!));
        var sourceNames = new List<string>();
        var resolving = new HashSet<string>(StringComparer.Ordinal);
        JsonObject settings = ResolveFile(Path.GetFullPath(profilePath), profilesByName, resolving, sourceNames);
        settings.Remove("include");

        RequireNonEmptyString(settings, "type");
        RequireNonEmptyString(settings, "name");
        RequireNonEmptyString(settings, "from");

        string json = settings.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        string sha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
        return new ResolvedBambuProfile(settings, sourceNames, sha256, json + Environment.NewLine);
    }

    private static Dictionary<string, string> BuildProfileIndex(IEnumerable<string> searchRoots)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string root in searchRoots.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (string path in Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories))
            {
                JsonObject? profile;
                try
                {
                    profile = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
                }
                catch (JsonException)
                {
                    continue;
                }

                string? name = ReadString(profile, "name");
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                if (result.TryGetValue(name, out string? existing)
                    && !string.Equals(existing, path, StringComparison.OrdinalIgnoreCase))
                {
                    if (FilesHaveSameContent(existing, path))
                    {
                        continue;
                    }

                    throw new InvalidDataException(
                        $"Bambu Studio profile name '{name}' is ambiguous between '{existing}' and '{path}'.");
                }

                result[name] = path;
            }
        }

        return result;
    }

    private static bool FilesHaveSameContent(string firstPath, string secondPath)
    {
        FileInfo first = new(firstPath);
        FileInfo second = new(secondPath);
        if (first.Length != second.Length)
        {
            return false;
        }

        return File.ReadAllBytes(firstPath).AsSpan().SequenceEqual(File.ReadAllBytes(secondPath));
    }

    private static JsonObject ResolveFile(
        string path,
        IReadOnlyDictionary<string, string> profilesByName,
        ISet<string> resolving,
        ICollection<string> sourceNames)
    {
        JsonObject current = JsonNode.Parse(File.ReadAllText(path)) as JsonObject
            ?? throw new InvalidDataException($"Bambu Studio profile '{path}' must contain a JSON object.");
        string name = ReadString(current, "name")
            ?? throw new InvalidDataException($"Bambu Studio profile '{path}' has no name.");

        if (!resolving.Add(name))
        {
            throw new InvalidDataException($"Bambu Studio profile inheritance contains a cycle at '{name}'.");
        }

        var resolved = new JsonObject();
        string? inheritedName = ReadString(current, "inherits");
        if (!string.IsNullOrWhiteSpace(inheritedName))
        {
            Merge(resolved, ResolveNamed(inheritedName, profilesByName, resolving, sourceNames));
        }

        if (current["include"] is JsonArray includes)
        {
            foreach (JsonNode? include in includes)
            {
                string? includeName = include?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(includeName))
                {
                    Merge(resolved, ResolveNamed(includeName, profilesByName, resolving, sourceNames));
                }
            }
        }

        Merge(resolved, current);
        resolved.Remove("include");
        sourceNames.Add(name);
        resolving.Remove(name);
        return resolved;
    }

    private static JsonObject ResolveNamed(
        string name,
        IReadOnlyDictionary<string, string> profilesByName,
        ISet<string> resolving,
        ICollection<string> sourceNames)
    {
        if (!profilesByName.TryGetValue(name, out string? path))
        {
            throw new InvalidDataException($"Bambu Studio inherited profile '{name}' was not found in the configured roots.");
        }

        return ResolveFile(path, profilesByName, resolving, sourceNames);
    }

    private static void Merge(JsonObject target, JsonObject source)
    {
        foreach ((string key, JsonNode? value) in source)
        {
            target[key] = value?.DeepClone();
        }
    }

    private static string? ReadString(JsonObject? value, string propertyName)
    {
        return value?[propertyName] is JsonValue property
            && property.TryGetValue(out string? result)
            ? result
            : null;
    }

    private static void RequireNonEmptyString(JsonObject value, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(ReadString(value, propertyName)))
        {
            throw new InvalidDataException($"Resolved Bambu Studio profile requires a non-empty '{propertyName}'.");
        }
    }
}

/// <summary>
/// A deterministic Bambu Studio preset with inheritance materialized.
/// </summary>
/// <param name="Settings">Resolved settings.</param>
/// <param name="SourceProfileNames">Profiles merged in precedence order.</param>
/// <param name="Sha256">Digest of the resolved JSON bytes before the trailing newline.</param>
/// <param name="Json">Resolved JSON suitable for a CLI input file.</param>
public sealed record ResolvedBambuProfile(
    JsonObject Settings,
    IReadOnlyList<string> SourceProfileNames,
    string Sha256,
    string Json);
