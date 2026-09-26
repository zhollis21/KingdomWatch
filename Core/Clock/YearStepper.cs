using System;

namespace KingdomWatch.Core.Clock
{
    /// <summary>
    /// Turns steps of whatever size a driver has to hand - a frame's worth of
    /// real time - into clock targets that never cross a year boundary, so a
    /// driver lands exactly on every boundary the harness hashes on (#72).
    /// </summary>
    /// <remarks>
    /// Where a step would cross a boundary it stops on the boundary and
    /// carries the rest into the next call. A step that spans several years
    /// therefore takes several calls, one per boundary, rather than being
    /// dropped.
    ///
    /// Not world state. It only chooses where to stop, and the world at a
    /// given time does not depend on how the run was sliced (AdvanceTo drains
    /// everything due on or before its target), so it is not hashed or saved.
    /// </remarks>
    public sealed class YearStepper
    {
        /// <summary>Ticks given but not yet stepped, held back by a boundary.</summary>
        public long Carried { get; private set; }

        /// <summary>
        /// Where to advance to from <paramref name="now"/>, given
        /// <paramref name="ticks"/> more to spend on top of whatever is carried:
        /// as far as that goes, but never past the next year boundary.
        /// </summary>
        public SimulationTime Next(SimulationTime now, long ticks)
        {
            if (ticks < 0L)
            {
                throw new ArgumentOutOfRangeException(nameof(ticks), ticks, "Cannot step backwards.");
            }

            if (ticks > long.MaxValue - Carried)
            {
                throw new ArgumentOutOfRangeException(nameof(ticks), ticks, "That many ticks on top of what is carried would overflow.");
            }

            var total = Carried + ticks;
            var step = Math.Min(total, TicksToNextBoundary(now));
            Carried = total - step;
            return now.Plus(step);
        }

        // In the last partial year of time there is no next boundary, so the
        // end of time stands in for it.
        private static long TicksToNextBoundary(SimulationTime now)
        {
            var lastYear = long.MaxValue / SimulationTime.TicksPerYear;
            return now.YearNumber < lastYear
                ? now.TicksUntil(SimulationTime.FromYears(now.YearNumber + 1L))
                : long.MaxValue - now.Ticks;
        }
    }
}
