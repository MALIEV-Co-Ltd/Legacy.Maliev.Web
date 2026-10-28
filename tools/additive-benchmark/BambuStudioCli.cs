namespace Maliev.AdditiveBenchmark;

/// <summary>
/// Builds the pinned, non-shell Bambu Studio CLI invocation used for STL benchmark slices.
/// </summary>
public static class BambuStudioCli
{
    /// <summary>
    /// Preserves the original Windows 02.08.02.61 argument contract; this builder does not run or verify the vendor executable.
    /// </summary>
    /// <param name="request">Slice request with complete profile paths.</param>
    /// <returns>Arguments to add individually to <see cref="System.Diagnostics.ProcessStartInfo.ArgumentList"/>.</returns>
    public static IReadOnlyList<string> BuildArguments(BambuStudioSliceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateFileName(request.OutputFileName);
        foreach (string field in new[] { request.ModelPath, request.MachineProfilePath, request.ProcessProfilePath,
                     request.FilamentProfilePath, request.OutputDirectory, request.BedType })
        {
            ValidateArgument(field);
        }
        if (request.MachineProfilePath.Contains(';') || request.ProcessProfilePath.Contains(';'))
        {
            throw new ArgumentException("Settings paths cannot contain the vendor bundle delimiter.", nameof(request));
        }
        if (request.ModelPath.StartsWith('-') || request.OrientationPolicy is not (BambuStudioOrientationPolicy.Fixed or BambuStudioOrientationPolicy.Search))
        {
            throw new ArgumentException("Model path or orientation policy is invalid.", nameof(request));
        }

        var arguments = new List<string>
        {
            "--debug", "2",
            "--outputdir", request.OutputDirectory,
            "--curr-bed-type", request.BedType,
            "--load-settings", $"{request.MachineProfilePath};{request.ProcessProfilePath}",
            "--load-filaments", request.FilamentProfilePath,
            "--slice", "0",
            "--export-3mf", request.OutputFileName,
        };

        if (request.OrientationPolicy == BambuStudioOrientationPolicy.Search)
        {
            arguments.Add("--orient");
            arguments.Add("1");
            arguments.Add("--arrange");
            arguments.Add("1");
        }
        else if (!string.Equals(Path.GetExtension(request.ModelPath), ".3mf", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Fixed-pose argument construction requires a prepared 3MF input; transform verification belongs to the caller.",
                nameof(request));
        }

        arguments.Add(request.ModelPath);
        return arguments;
    }

    private static void ValidateArgument(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Length > 16384 || System.Text.Encoding.UTF8.GetByteCount(value) > 16384
            || value.IndexOfAny(['\0', '\r', '\n']) >= 0)
        {
            throw new ArgumentException("Slice argument admission limit exceeded.");
        }
    }

    private static void ValidateFileName(string outputFileName)
    {
        ValidateArgument(outputFileName);
        if (outputFileName.IndexOfAny(['/', '\\', ':', '*', '?', '"', '<', '>', '|']) >= 0
            || outputFileName.StartsWith('-')
            || !string.Equals(outputFileName, Path.GetFileName(outputFileName), StringComparison.Ordinal)
            || !string.Equals(Path.GetExtension(outputFileName), ".3mf", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Bambu Studio output must be a relative .3mf filename.", nameof(outputFileName));
        }
    }
}

/// <summary>
/// Pure argument inputs for one Bambu Studio slice; no process is started by this builder.
/// </summary>
/// <param name="ModelPath">Source STL path.</param>
/// <param name="MachineProfilePath">Complete machine profile path.</param>
/// <param name="ProcessProfilePath">Complete process profile path.</param>
/// <param name="FilamentProfilePath">Complete filament profile path.</param>
/// <param name="OutputDirectory">Dedicated job output directory.</param>
/// <param name="OutputFileName">Relative generated 3MF filename.</param>
/// <param name="BedType">Approved Bambu Studio build plate preset.</param>
/// <param name="OrientationPolicy">Whether the reference preserves the prepared pose or permits a search.</param>
public sealed record BambuStudioSliceRequest(
    string ModelPath,
    string MachineProfilePath,
    string ProcessProfilePath,
    string FilamentProfilePath,
    string OutputDirectory,
    string OutputFileName,
    string BedType,
    BambuStudioOrientationPolicy OrientationPolicy = BambuStudioOrientationPolicy.Search);

/// <summary>Controls whether the offline reference oracle may change the supplied object pose.</summary>
public enum BambuStudioOrientationPolicy
{
    /// <summary>Use a prepared 3MF project and preserve its verified transform.</summary>
    Fixed,

    /// <summary>Allow Bambu Studio to search and arrange a pose.</summary>
    Search,
}
