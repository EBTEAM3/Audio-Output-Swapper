using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AudioSwapper.Interop;

/// <summary>
/// Hands idle memory back to Windows.
///
/// A tray app spends almost all its life doing nothing, and the .NET runtime
/// has no reason to release the pages it touched during startup on its own. A
/// compact plus an explicit working-set trim pushes those pages out to the
/// standby list, where Windows can reclaim them under pressure and fault them
/// back in if this app ever needs them again.
///
/// This is a genuine reduction in resident memory, not a Task Manager trick:
/// the pages really do leave the working set. The cost is a small number of
/// soft faults the next time the app is used, which is unnoticeable next to the
/// interactions it is doing.
/// </summary>
internal static class MemoryTrim
{
    [DllImport("psapi.dll", SetLastError = true)]
    private static extern bool EmptyWorkingSet(IntPtr process);

    public static void TrimWorkingSet()
    {
        try
        {
            // Compact first, so the trim is not just pushing out garbage that
            // could have been collected.
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

            EmptyWorkingSet(Process.GetCurrentProcess().Handle);
        }
        catch (Exception)
        {
            // Purely an optimisation; never worth surfacing.
        }
    }
}
