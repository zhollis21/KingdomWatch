using KingdomWatch.Core.Data;

namespace KingdomWatch.Core.Work
{
    /// <summary>
    /// What a band's last site search found for one job: whether anywhere
    /// was reachable, and if so where and at what cost there and back.
    /// </summary>
    /// <remarks>
    /// A read-only copy of what <see cref="Jobs"/> keeps between dawns, for
    /// the world hash. The search is refreshed at dawn and when the band
    /// moves, not when its known map grows, so the survey is state in its own
    /// right rather than something recomputable from the map (#104).
    ///
    /// <see cref="Destination"/>, <see cref="Cost"/> and
    /// <see cref="ReturnCost"/> are meaningful only while
    /// <see cref="Reachable"/>: a search that finds nowhere leaves the last
    /// ones where they were.
    /// </remarks>
    public readonly struct SiteSurvey
    {
        internal SiteSurvey(bool reachable, WorldPosition destination, long cost, long returnCost)
        {
            Reachable = reachable;
            Destination = destination;
            Cost = cost;
            ReturnCost = returnCost;
        }

        public bool Reachable { get; }

        public WorldPosition Destination { get; }

        /// <summary>Path cost out to the site, in the pathfinder's units.</summary>
        public long Cost { get; }

        /// <summary>Path cost back along the same route, reversed.</summary>
        public long ReturnCost { get; }
    }
}
