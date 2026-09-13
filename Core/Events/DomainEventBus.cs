using System;
using System.Collections.Generic;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;

namespace KingdomWatch.Core.Events
{
    /// <summary>
    /// Publishes <see cref="DomainEvent"/>s to every subscriber, in a fixed
    /// order, never recursively.
    /// </summary>
    /// <remarks>
    /// Too many systems react to one occurrence for direct calls to stay
    /// maintainable - a death touches households, jobs, inheritance,
    /// succession, the feed and history. This is the one mechanism they all
    /// hang off. It is a domain-event layer, not event sourcing: it notifies
    /// and forgets, and <see cref="History.EventJournal"/> is just the
    /// subscriber that remembers.
    ///
    /// **Notification is synchronous and in subscription order.** Subscription
    /// is sealed at the first publish, so that order is fixed by the code that
    /// builds the world and cannot drift between runs or across a load. Between
    /// the two, "who hears about it first" is a property of the wiring, not
    /// of anything that happens at run time.
    ///
    /// **Publishing from inside a subscriber is refused - the bus does not
    /// defer it, it throws.** Section 4 requires that a reaction to an event
    /// is queued rather than run recursively, otherwise PersonDied → household
    /// reacts → HouseholdEnded → settlement reacts → … runs subscribers of the
    /// second event in the middle of the first's. The queue that satisfies
    /// that requirement is the clock's, not this type's: the clock already
    /// enforces that a same-instant reaction lands in a LATER
    /// <see cref="SimulationPhase"/> (see <see cref="SimulationClock.Schedule"/>).
    /// So a subscriber that must cause more events schedules a clock event and
    /// publishes from that handler. A second queue here, drained after the
    /// current publish, would be a second ordering authority, would flatten
    /// the cascade into one phase, and would be half-state to snapshot when
    /// the phone suspends mid-cascade (#15). Refusing costs one
    /// <see cref="ScheduledEventKind"/> per reaction; that is the design's
    /// stated mechanism.
    ///
    /// That promise is only as good as the number of buses: a subscriber on
    /// one bus publishing through a second would nest without either
    /// noticing. So a clock has exactly one bus, and the constructor refuses
    /// a second (see <see cref="SimulationClock.ClaimBus"/>).
    ///
    /// Publishing outside a dispatch is fine - world generation publishes
    /// SettlementFounded and PersonBorn at T=0 before the clock has run.
    ///
    /// Not thread-safe, like everything else in the simulation.
    /// </remarks>
    public sealed class DomainEventBus
    {
        private readonly IdAllocator _ids;
        private readonly SimulationClock _clock;

        // A List indexed by position rather than an array, so Subscribe can
        // grow it; iteration is by index, which allocates nothing.
        private readonly List<IDomainEventSubscriber> _subscribers = new List<IDomainEventSubscriber>();

        private bool _sealed;
        private bool _publishing;

        /// <param name="clock">
        /// Stamps each event with the instant it happened, and supplies the
        /// allocator. Domain events draw from the clock's own event counter -
        /// there is no way to hand the bus a different one - so an id names
        /// exactly one thing across the queue and the journal.
        /// </param>
        public DomainEventBus(SimulationClock clock)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            clock.ClaimBus();
            _ids = clock.Ids;
        }

        public int SubscriberCount => _subscribers.Count;

        /// <summary>
        /// Adds a subscriber. Order of subscription is order of notification,
        /// so this is refused once anything has been published: a subscriber
        /// arriving mid-run would hear later events in a different position
        /// relative to the others than a subscriber wired at construction, and
        /// two runs of the same seed would disagree about who reacted first.
        /// </summary>
        public void Subscribe(IDomainEventSubscriber subscriber)
        {
            if (subscriber is null)
            {
                throw new ArgumentNullException(nameof(subscriber));
            }

            if (_sealed)
            {
                throw new InvalidOperationException(
                    "Cannot subscribe after the first event has been published. Subscription order is "
                    + "notification order, so every subscriber is wired before the world runs.");
            }

            // By identity, not Equals: the promise is that one INSTANCE hears
            // each event once, and a subscriber with value equality must not
            // be able to shadow a different one.
            for (var i = 0; i < _subscribers.Count; i++)
            {
                if (ReferenceEquals(_subscribers[i], subscriber))
                {
                    throw new ArgumentException(
                        "Already subscribed. A subscriber hears each event once.", nameof(subscriber));
                }
            }

            _subscribers.Add(subscriber);
        }

        /// <summary>
        /// Publishes an event that was not a decision - a birth, a natural
        /// death - and returns its durable id.
        /// </summary>
        public EventId Publish(DomainEventKind kind, EntityId primaryEntity, EntityId secondaryEntity) =>
            Publish(kind, primaryEntity, secondaryEntity, Reasons.None);

        /// <summary>
        /// Publishes an event, stamped with a fresh id and the clock's current
        /// instant, and notifies every subscriber before returning. Returns
        /// the id, which is what history and grievances refer back to.
        /// </summary>
        /// <remarks>
        /// Refused from inside a subscriber - see the type remarks. The
        /// reasons are recorded here, at the decision site, and nowhere else:
        /// the code that ran the calculation is the only code that knows what
        /// it weighed.
        ///
        /// A publish refused for an undefined kind has already consumed an id,
        /// which is harmless - ids need to be unique and increasing, not
        /// contiguous, exactly as with <see cref="SimulationClock.Schedule"/>.
        /// A subscriber that throws stops notification there: the subscribers
        /// after it never hear that event, the exception reaches the caller,
        /// and the bus is left ready for the next publish. Nothing here
        /// pretends the world is consistent at that point, because it is not.
        /// </remarks>
        public EventId Publish(
            DomainEventKind kind,
            EntityId primaryEntity,
            EntityId secondaryEntity,
            Reasons reasons)
        {
            if (_publishing)
            {
                throw new InvalidOperationException(
                    "Cannot publish " + kind + " from inside a subscriber: reactions are queued, never run "
                    + "recursively. Schedule a clock event into a later SimulationPhase and publish from there.");
            }

            var published = new DomainEvent(
                _ids.NextEvent(), _clock.Now, kind, primaryEntity, secondaryEntity, reasons);

            _sealed = true;
            _publishing = true;

            try
            {
                for (var i = 0; i < _subscribers.Count; i++)
                {
                    _subscribers[i].On(in published);
                }
            }
            finally
            {
                _publishing = false;
            }

            return published.Id;
        }
    }
}
