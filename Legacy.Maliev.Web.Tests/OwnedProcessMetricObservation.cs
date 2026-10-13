using System.Diagnostics;
using Microsoft.Win32.SafeHandles;

namespace Legacy.Maliev.Web.Tests;

/// <summary>Observe metrics of the exact retained child; natural terminal observations need no signal.</summary>
internal static class OwnedProcessMetricObservation
{
    internal static bool TryRead(Process process, SafeProcessHandle ownedHandle, int ownedPid, Action readMetrics)
    {
        try
        {
            readMetrics();
            return true;
        }
        catch (InvalidOperationException) when (IsExactRetainedChildTerminal(process, ownedHandle, ownedPid))
        {
            return false;
        }
    }

    private static bool IsExactRetainedChildTerminal(Process process, SafeProcessHandle ownedHandle, int ownedPid)
    {
        try
        {
            // The retained handle identifies the original child even after OS PID reuse.
            // Disposed, replaced or unverifiable handles cannot qualify this exception.
            return !ownedHandle.IsClosed && !ownedHandle.IsInvalid && process.Id == ownedPid
                && ReferenceEquals(process.SafeHandle, ownedHandle) && process.HasExited;
        }
        catch
        {
            // A failed terminal recheck preserves the original metrics exception.
            return false;
        }
    }
}
