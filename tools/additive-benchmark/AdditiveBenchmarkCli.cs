using System.Text;

namespace Maliev.AdditiveBenchmark;

/// <summary>
/// Command-line entry point for validating an additive benchmark manifest and writing its report.
/// </summary>
public static class AdditiveBenchmarkCli
{
    /// <summary>
    /// Runs the additive benchmark manifest validator.
    /// </summary>
    /// <param name="args">Command-line arguments.</param>
    /// <returns>Zero when ready, two when valid but blocked, and one when invalid.</returns>
    public static int Run(string[] args)
    {
        if (args.Length > 0 && string.Equals(args[0], "resolve-profile", StringComparison.Ordinal))
        {
            return RunResolveProfile(args[1..]);
        }

        if (args.Length > 0 && string.Equals(args[0], "inventory-corpus", StringComparison.Ordinal))
        {
            return RunInventoryCorpus(args[1..]);
        }

        if (!TryParseArguments(args, out string? manifestPath, out string? schemaPath, out string? reportPath))
        {
            Console.Error.WriteLine(
                "Usage: Maliev.AdditiveBenchmark --manifest <path> --schema <path> --report <path>");
            return 1;
        }

        try
        {
            BenchmarkRunResult result = AdditiveBenchmarkRunner.Run(manifestPath!, schemaPath!);
            string? directory = Path.GetDirectoryName(Path.GetFullPath(reportPath!));
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(reportPath!, result.ReportJson, new UTF8Encoding(false));
            Console.WriteLine(
                $"Additive benchmark manifest status: {result.Status}; report: {Path.GetFullPath(reportPath!)}");
            return result.ExitCode;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Unable to read or write benchmark files: {exception.Message}");
            return 1;
        }
    }

    private static int RunResolveProfile(string[] args)
    {
        string? profilePath = null;
        string? outputPath = null;
        var searchRoots = new List<string>();

        for (int index = 0; index < args.Length; index += 2)
        {
            if (index + 1 >= args.Length)
            {
                return WriteResolveProfileUsage();
            }

            switch (args[index])
            {
                case "--profile":
                    profilePath = args[index + 1];
                    break;
                case "--search-root":
                    searchRoots.Add(args[index + 1]);
                    break;
                case "--output":
                    outputPath = args[index + 1];
                    break;
                default:
                    return WriteResolveProfileUsage();
            }
        }

        if (string.IsNullOrWhiteSpace(profilePath)
            || string.IsNullOrWhiteSpace(outputPath)
            || searchRoots.Count == 0)
        {
            return WriteResolveProfileUsage();
        }

        try
        {
            ResolvedBambuProfile profile = BambuStudioProfileResolver.Resolve(profilePath, searchRoots);
            string fullOutputPath = Path.GetFullPath(outputPath);
            string? directory = Path.GetDirectoryName(fullOutputPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(fullOutputPath, profile.Json, new UTF8Encoding(false));
            Console.WriteLine($"Resolved Bambu Studio profile: {fullOutputPath}; sha256: {profile.Sha256}");
            return 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            Console.Error.WriteLine($"Unable to resolve Bambu Studio profile: {exception.Message}");
            return 1;
        }
    }

    private static int RunInventoryCorpus(string[] args)
    {
        string? root = null;
        string? output = null;
        string? consentId = null;
        int limit = 48;
        for (int index = 0; index < args.Length; index += 2)
        {
            if (index + 1 >= args.Length)
            {
                return WriteInventoryUsage();
            }

            switch (args[index])
            {
                case "--root": root = args[index + 1]; break;
                case "--output": output = args[index + 1]; break;
                case "--consent-id": consentId = args[index + 1]; break;
                case "--limit" when int.TryParse(args[index + 1], out int parsed): limit = parsed; break;
                default: return WriteInventoryUsage();
            }
        }

        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(output) || string.IsNullOrWhiteSpace(consentId))
        {
            return WriteInventoryUsage();
        }

        try
        {
            PrivateCorpusInventoryResult inventory = PrivateCorpusInventory.Create(root, limit, consentId);
            string fullOutput = Path.GetFullPath(output);
            Directory.CreateDirectory(Path.GetDirectoryName(fullOutput)!);
            File.WriteAllText(fullOutput, inventory.Json, new UTF8Encoding(false));
            Console.WriteLine($"Anonymous private corpus inventory: {inventory.EntryCount} unique entries; output: {fullOutput}");
            return inventory.EntryCount == limit ? 0 : 2;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Console.Error.WriteLine($"Unable to inventory restricted corpus: {exception.Message}");
            return 1;
        }
    }

    private static int WriteInventoryUsage()
    {
        Console.Error.WriteLine("Usage: Maliev.AdditiveBenchmark inventory-corpus --root <directory> --output <path> --consent-id <id> [--limit <count>]");
        return 1;
    }

    private static int WriteResolveProfileUsage()
    {
        Console.Error.WriteLine(
            "Usage: Maliev.AdditiveBenchmark resolve-profile --profile <path> --search-root <directory> [--search-root <directory>] --output <path>");
        return 1;
    }

    private static bool TryParseArguments(
        string[] args,
        out string? manifestPath,
        out string? schemaPath,
        out string? reportPath)
    {
        manifestPath = null;
        schemaPath = null;
        reportPath = null;

        for (int index = 0; index < args.Length; index += 2)
        {
            if (index + 1 >= args.Length)
            {
                return false;
            }

            switch (args[index])
            {
                case "--manifest":
                    manifestPath = args[index + 1];
                    break;
                case "--schema":
                    schemaPath = args[index + 1];
                    break;
                case "--report":
                    reportPath = args[index + 1];
                    break;
                default:
                    return false;
            }
        }

        return !string.IsNullOrWhiteSpace(manifestPath)
            && !string.IsNullOrWhiteSpace(schemaPath)
            && !string.IsNullOrWhiteSpace(reportPath);
    }
}
