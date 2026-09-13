namespace KingdomWatch.Core.Clock
{
    /// <summary>
    /// Receives each event the clock dispatches, in order.
    /// </summary>
    /// <remarks>
    /// The clock decides WHEN things happen and in what order; it has no
    /// opinion on what they mean. This is the seam where that changes hands.
    /// The world drives the clock with a <see cref="ScheduledEventRouter"/>,
    /// which hands each event to the system owning its kind; that system
    /// handles it and publishes whatever it meant - PersonDied, FamineStarted -
    /// through the <see cref="Events.DomainEventBus"/>. A scheduled event is a
    /// wake-up; a domain event is a fact. Tests and the harness soak implement
    /// this directly.
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
