using KingdomWatch.Core.Data;

namespace KingdomWatch.Core.Construction
{
    /// <summary>
    /// A settlement's meals in store (<see cref="Needs.Hunger.MealsInStore"/>)
    /// on the first day of its last two years, so <see cref="Buildings.IsFoodShort"/>
    /// can tell a store that is being saved from one that is being eaten
    /// (#149). Read at the settlement's first dawn of each year.
    /// </summary>
    /// <remarks>
    /// The same day each year, so the comparison is never a full autumn
    /// store against an empty spring one. A plain class changed only by
    /// <see cref="Buildings"/>, for the reason <see cref="Building"/> is.
    /// </remarks>
    public sealed class StoreTrend
    {
        internal StoreTrend(EntityId settlement)
        {
            Settlement = settlement;
        }

        /// <summary>The settlement whose stores these are.</summary>
        public EntityId Settlement { get; }

        /// <summary>Meals in store on the first day of the year before, or -1 before there were two readings.</summary>
        public long LastYear { get; internal set; } = -1L;

        /// <summary>Meals in store on the first day of this year, or -1 before the first reading.</summary>
        public long ThisYear { get; internal set; } = -1L;

        /// <summary>The day of the last reading, or -1 before the first; so a second dawn that day reads nothing.</summary>
        public long ReadOn { get; internal set; } = -1L;
    }
}
