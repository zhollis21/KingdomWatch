namespace KingdomWatch.Core.Clock
{
    /// <summary>
    /// Receives each event the clock dispatches, in order.
    /// </summary>
    /// <remarks>
    /// The clock decides WHEN things happen and in what order; it has no
    /// opinion on what they mean. This is the seam where that changes hands.
    /// The domain-event layer (#8) is the real implementation - publishing
    /// PersonBorn, PersonDied and the rest to whichever systems subscribe, and
    /// carrying decision provenance. Until it exists, tests implement this
    /// directly.
    ///
    /// The clock passes itself so a handler can schedule the reactions to what
    /// it just handled. Those go through the same ordering rules as anything
    /// else, which is what keeps a cascade from becoming recursion - see
    /// <see cref="SimulationClock.Schedule"/>.
    /// </remarks>
    public interface IScheduledEventHandler
    {
        void Handle(ScheduledEvent scheduled, SimulationClock clock);
    }
}
