namespace KingdomWatch.Core.Clock
{
    /// <summary>
    /// The four seasons of the 120-day year, thirty days each, in the order
    /// they fall. Day 0 of the world is the first day of
    /// <see cref="Spring"/>.
    /// </summary>
    /// <remarks>
    /// A season is read off the clock (<see cref="SimulationTime.Season"/>),
    /// never stored: nothing turns a season over, so there is no state to
    /// save, hash or get out of step with the time it describes (#53).
    ///
    /// The world starts in spring so that a band's first lean season is
    /// three seasons away - long enough to provision for, which is what the
    /// look-ahead in <see cref="Work.Jobs"/> does. Values are the season's
    /// index in the year and are arithmetic, not labels: reorder them and
    /// the calendar changes.
    /// </remarks>
    public enum Season : byte
    {
        Spring = 0,
        Summer = 1,
        Autumn = 2,
        Winter = 3,
    }
}
