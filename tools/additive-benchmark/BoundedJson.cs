using System.Text.Json;

namespace Maliev.AdditiveBenchmark;

internal static class BoundedJson
{
    internal const int MaximumBytes = 4 * 1024 * 1024;

    internal static byte[] Read(string path)
    {
        using FileStream stream = File.OpenRead(path);
        if (stream.Length > MaximumBytes)
        {
            throw new IOException("Input exceeds the JSON admission limit.");
        }

        using MemoryStream bytes = new();
        byte[] buffer = new byte[8192];
        int count;
        while ((count = stream.Read(buffer, 0, Math.Min(buffer.Length, MaximumBytes + 1 - (int)bytes.Length))) > 0)
        {
            bytes.Write(buffer, 0, count);
            if (bytes.Length > MaximumBytes)
            {
                throw new IOException("Input exceeds the JSON admission limit.");
            }
        }

        return bytes.ToArray();
    }

    internal static JsonDocument Parse(byte[] bytes)
    {
        JsonDocument document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 64 });
        try
        {
            CheckUniqueNames(document.RootElement);
            return document;
        }
        catch
        {
            document.Dispose();
            throw;
        }
    }

    private static void CheckUniqueNames(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            HashSet<string> names = new(StringComparer.Ordinal);
            foreach (JsonProperty property in value.EnumerateObject())
            {
                if (!names.Add(property.Name))
                {
                    throw new JsonException("Duplicate property.");
                }

                CheckUniqueNames(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in value.EnumerateArray())
            {
                CheckUniqueNames(item);
            }
        }
    }
}
