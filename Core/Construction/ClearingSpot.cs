using KingdomWatch.Core.Data;

namespace KingdomWatch.Core.Construction
{
    /// <summary>
    /// One cell of a building's lane or square too far from its footprint
    /// to clear from there, with its own hours of clearing (#180): Builders
    /// walk out to it, so the walk counts, and it clears when its hours come
    /// home.
    /// </summary>
    /// <remarks>
    /// Its hours are part of the building's own <see cref="Building.ClearTicks"/>
    /// and <see cref="Building.LabourTicks"/>, and its claims and work part of
    /// <see cref="Building.Claimed"/> and <see cref="Building.Worked"/>: the
    /// building keeps the totals, the spot the share of them done out here.
    /// Only a cell with something on it gets one - a bare cell needs no trip.
    /// </remarks>
    public sealed class ClearingSpot
    {
        internal ClearingSpot(Building building, WorldPosition at, long ticks, int cuts, int rocks)
        {
            Building = building;
            At = at;
            Ticks = ticks;
            Cuts = cuts;
            Rocks = rocks;
        }

        /// <summary>The building whose lane or square it is on.</summary>
        public Building Building { get; }

        /// <summary>The cell, and the destination of a trip to clear it.</summary>
        public WorldPosition At { get; }

        /// <summary>Worker-ticks of clearing it was priced at when its building was approved.</summary>
        public long Ticks { get; }

        /// <summary>The cuts of standing timber <see cref="Ticks"/> was priced at, paid when it clears.</summary>
        public int Cuts { get; }

        /// <summary>1 for a rock, paid as Stone when it clears; otherwise 0.</summary>
        public int Rocks { get; }

        /// <summary>Worker-ticks claimed by Builders on their way or at work here.</summary>
        public long Claimed { get; internal set; }

        /// <summary>Worker-ticks done here.</summary>
        public long Worked { get; internal set; }

        /// <summary>Whether its hours are done and the cell cleared.</summary>
        public bool IsCleared => Worked == Ticks;

        /// <summary>Hours nobody has claimed yet.</summary>
        public long Left => Ticks - Worked - Claimed;

        public override string ToString() => "clearing at " + At + " for " + Building;
    }
}
