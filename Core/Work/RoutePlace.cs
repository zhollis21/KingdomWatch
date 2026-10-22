using KingdomWatch.Core.Data;

namespace KingdomWatch.Core.Work
{
    /// <summary>
    /// Where a task puts its worker at an instant, finer than a cell: on the
    /// step from <see cref="From"/> to <see cref="To"/>,
    /// <see cref="Along"/> of <see cref="Step"/> of the way. Standing still
    /// (at work, or a route of one cell) is a step from a cell to itself.
    /// </summary>
    /// <remarks>
    /// <see cref="Jobs.PlaceAt"/> hands it out for the view to draw a walk
    /// rather than a hop from cell to cell (#25). It is read, never kept:
    /// nothing in the simulation asks where between two cells someone is.
    /// <see cref="Along"/> and <see cref="Step"/> are in the pathfinder's
    /// cost units, so the fraction is integer arithmetic until the caller
    /// chooses otherwise.
    /// </remarks>
    public readonly struct RoutePlace
    {
        internal RoutePlace(WorldPosition from, WorldPosition to, long along, long step)
        {
            From = from;
            To = to;
            Along = along;
            Step = step;
        }

        /// <summary>The cell last stepped onto: <see cref="Jobs.PositionAt"/>.</summary>
        public WorldPosition From { get; }

        /// <summary>The cell being stepped onto; <see cref="From"/> when standing still.</summary>
        public WorldPosition To { get; }

        /// <summary>How far through the step, from zero up to but not including <see cref="Step"/>.</summary>
        public long Along { get; }

        /// <summary>The step's length in the pathfinder's cost units; always positive.</summary>
        public long Step { get; }

        internal static RoutePlace Standing(WorldPosition at) => new RoutePlace(at, at, 0L, 1L);
    }
}
