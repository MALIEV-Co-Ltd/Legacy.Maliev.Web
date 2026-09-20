namespace Maliev.AdditiveBenchmark;

/// <summary>
/// Builds the pinned, non-shell Bambu Studio CLI invocation used for STL benchmark slices.
/// </summary>
public static class BambuStudioCli
{
    /// <summary>
    /// Builds arguments verified against Bambu Studio 02.08.02.61 on Windows.
    /// </summary>
    /// <param name="request">Slice request with complete profile paths.</param>
    /// <returns>Arguments to add individually to <see cref="System.Diagnostics.ProcessStartInfo.ArgumentList"/>.</returns>
    public static IReadOnlyList<string> BuildArguments(BambuStudioSliceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateFileName(request.OutputFileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.BedType);

        return
        [
            "--debug", "2",
            "--outputdir", request.OutputDirectory,
            "--orient", "1",
            "--arrange", "1",
            "--curr-bed-type", request.BedType,
            "--load-settings", $"{request.MachineProfilePath};{request.ProcessProfilePath}",
            "--load-filaments", request.FilamentProfilePath,
            "--slice", "0",
            "--export-3mf", request.OutputFileName,
            request.ModelPath,
        ];
    }

    private static void ValidateFileName(string outputFileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputFileName);
        if (!string.Equals(outputFileName, Path.GetFileName(outputFileName), StringComparison.Ordinal)
            || !string.Equals(Path.GetExtension(outputFileName), ".3mf", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Bambu Studio output must be a relative .3mf filename.", nameof(outputFileName));
        }
    }
}

/// <summary>
/// Inputs for one isolated Bambu Studio STL slice.
/// </summary>
/// <param name="ModelPath">Source STL path.</param>
/// <param name="MachineProfilePath">Complete machine profile path.</param>
/// <param name="ProcessProfilePath">Complete process profile path.</param>
/// <param name="FilamentProfilePath">Complete filament profile path.</param>
/// <param name="OutputDirectory">Dedicated job output directory.</param>
/// <param name="OutputFileName">Relative generated 3MF filename.</param>
/// <param name="BedType">Approved Bambu Studio build plate preset.</param>
public sealed record BambuStudioSliceRequest(
    string ModelPath,
    string MachineProfilePath,
    string ProcessProfilePath,
    string FilamentProfilePath,
    string OutputDirectory,
    string OutputFileName,
    string BedType);
