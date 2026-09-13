namespace KingdomWatch.Core.Events
{
    /// <summary>
    /// Receives every event the <see cref="DomainEventBus"/> publishes, in
    /// publish order.
    /// </summary>
    /// <remarks>
    /// Every subscriber sees every event and ignores the kinds it does not
    /// care about. A per-kind filter would be a second registration surface
    /// for nothing: a switch on <see cref="DomainEvent.Kind"/> is cheaper than
    /// the lookup that would replace it, and the subscriber count is small.
    ///
    /// A subscriber is a listener, not a reactor. It may read state, record
    /// the event, and book a reaction on the clock into a later
    /// <see cref="Clock.SimulationPhase"/> at the same instant. It must not
    /// publish - the bus refuses that, see
    /// <see cref="DomainEventBus.Publish(DomainEventKind, Data.EntityId, Data.EntityId, Reasons)"/>
    /// - and it should not mutate simulation state directly. Nothing can
    /// enforce the last one; the reason to honour it is that two subscribers
    /// both mutating on the same event are ordered only by subscription
    /// order, which is the arbitrary-subscriber-order coupling section 4's
    /// phase model exists to remove.
    /// </remarks>
    public interface IDomainEventSubscriber
    {
        void On(in DomainEvent published);
    }
}
