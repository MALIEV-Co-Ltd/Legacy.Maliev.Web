namespace Maliev.AdditiveBenchmark;

/// <summary>Validates whether reference and simulator inputs are physically comparable.</summary>
public static class BenchmarkReferenceValidator
{
    /// <summary>Compares two row-major 4x4 transforms for a fixed-pose reference.</summary>
    /// <param name="requestedTransform">Transform supplied to the simulator.</param>
    /// <param name="referenceTransform">Transform observed in the sliced reference artifact.</param>
    /// <param name="orientationPolicy">Either <c>fixed</c> or <c>search</c>.</param>
    /// <returns>A stable comparability decision.</returns>
    public static BenchmarkComparisonResult Validate(
        IReadOnlyList<double> requestedTransform,
        IReadOnlyList<double> referenceTransform,
        string orientationPolicy)
    {
        ArgumentNullException.ThrowIfNull(requestedTransform);
        ArgumentNullException.ThrowIfNull(referenceTransform);
        if (!string.Equals(orientationPolicy, "fixed", StringComparison.OrdinalIgnoreCase))
        {
            return new BenchmarkComparisonResult(true, null);
        }

        if (requestedTransform.Count != 16 || referenceTransform.Count != 16)
        {
            return new BenchmarkComparisonResult(false, "reference_transform_invalid");
        }

        for (int index = 0; index < 16; index++)
        {
            if (!double.IsFinite(requestedTransform[index])
                || !double.IsFinite(referenceTransform[index])
                || Math.Abs(requestedTransform[index] - referenceTransform[index]) > 1e-8)
            {
                return new BenchmarkComparisonResult(false, "reference_pose_mismatch");
            }
        }

        return new BenchmarkComparisonResult(true, null);
    }
}

/// <summary>Result of checking whether one benchmark reference matches its requested inputs.</summary>
/// <param name="IsComparable">Whether the evidence may participate in certification metrics.</param>
/// <param name="ReasonCode">Stable rejection reason, or null when comparable.</param>
public sealed record BenchmarkComparisonResult(bool IsComparable, string? ReasonCode);
