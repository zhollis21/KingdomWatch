using System;
using System.Threading;

namespace KingdomWatch.Core.Tests.Performance
{
    /// <summary>
    /// Counts the managed bytes a piece of work allocates on the calling
    /// thread. This is the seam for the zero-allocation tick loop section 18
    /// commits to (#59): any system that runs under
    /// <c>SimulationClock.AdvanceTo</c> gets a test that warms it up, then
    /// asserts <see cref="Measure"/> returns zero for a further span of work.
    /// </summary>
    /// <remarks>
    /// Warm up first. Heap growth, hash set buckets, and JIT compilation all
    /// happen on the first pass through a workload, and none of them are the
    /// per-tick allocations the target is about. Measure the second pass.
    /// </remarks>
    internal static class Allocations
    {
        internal static long Measure(Action work)
        {
            if (work is null)
            {
                throw new ArgumentNullException(nameof(work));
            }

            // A pause before measuring lets the runtime finish recompiling
            // the warmed-up methods in the background. With dynamic PGO that
            // recompilation landing inside the measured span reported a few
            // kilobytes that were the runtime's, not the code's - one full
            // run in four or so, in whichever test it happened to hit (#148;
            // gone with DOTNET_TieredPGO=0, and with a pause like this one).
            Thread.Sleep(200);
            var before = GC.GetAllocatedBytesForCurrentThread();
            work();
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }
    }
}
