using System;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;

namespace KingdomWatch.Core.Events
{
    /// <summary>
    /// Something meaningful that happened: what, to whom, when, and why.
    /// Published through <see cref="DomainEventBus"/>, recorded by
    /// <see cref="History.EventJournal"/>.
    /// </summary>
    /// <remarks>
    /// One fixed-size shape for every kind, mirroring
    /// <see cref="ScheduledEvent"/>, rather than a type per kind. Fixed size is
    /// what lets the journal hold events in one array, the determinism hash
    /// (#13) fold them without knowing their kind, and a save (#15) write them
    /// without a type registry - and it keeps publishing allocation-free. A
    /// subscriber switches on <see cref="Kind"/> and looks up any further
    /// detail - the father of a newborn, the cause of a death - in the stores.
    /// If a kind ever needs a third party on the event itself, append a field;
    /// do not start a second shape.
    ///
    /// <see cref="Id"/> comes from the same counter as scheduled events, so an
    /// id names exactly one thing across the queue, the journal and every
    /// grievance or bookmark that refers back to it. A domain event is NOT the
    /// scheduled event whose handler published it: a wake-up is not a fact,
    /// and one wake-up may publish several facts or none.
    ///
    /// There is no ordering here. Journal order is publish order, which the
    /// clock's dispatch order already makes deterministic. See
    /// docs/design/kingdom-watch-plan-v7.1.md section 5.
    /// </remarks>
    public readonly struct DomainEvent : IEquatable<DomainEvent>
    {
        private static readonly bool[] DefinedKinds = EnumGuard.BuildMask(typeof(DomainEventKind));

        public DomainEvent(
            EventId id,
            SimulationTime time,
            DomainEventKind kind,
            EntityId primaryEntity,
            EntityId secondaryEntity,
            Reasons reasons)
        {
            if (id.IsNone)
            {
                throw new ArgumentException(
                    "A domain event needs a durable id: history, grievances and bookmarks refer back to it.",
                    nameof(id));
            }

            if (!EnumGuard.IsDefined(DefinedKinds, (int)kind))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(kind), kind, "Not a defined DomainEventKind.");
            }

            if (kind == DomainEventKind.None)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(kind),
                    kind,
                    "DomainEventKind.None is the defaulted-field guard, not something that happens.");
            }

            Id = id;
            Time = time;
            Kind = kind;
            PrimaryEntity = primaryEntity;
            SecondaryEntity = secondaryEntity;
            Reasons = reasons;
        }

        /// <summary>Durable identity, shared with the scheduler's counter.</summary>
        public EventId Id { get; }

        /// <summary>When it happened - the clock's instant at publish.</summary>
        public SimulationTime Time { get; }

        public DomainEventKind Kind { get; }

        /// <summary>Who this is mainly about. May be <see cref="EntityId.None"/>.</summary>
        public EntityId PrimaryEntity { get; }

        /// <summary>The second party, if any. May be <see cref="EntityId.None"/>.</summary>
        public EntityId SecondaryEntity { get; }

        /// <summary>
        /// Why, when this was a decision. <see cref="Reasons.None"/> for
        /// anything that was not.
        /// </summary>
        public Reasons Reasons { get; }

        public bool Equals(DomainEvent other) =>
            Id == other.Id
            && Time == other.Time
            && Kind == other.Kind
            && PrimaryEntity == other.PrimaryEntity
            && SecondaryEntity == other.SecondaryEntity
            && Reasons == other.Reasons;

        public override bool Equals(object? obj) => obj is DomainEvent other && Equals(other);

        public override int GetHashCode() => Id.GetHashCode();

        public override string ToString() =>
            Id + " " + Kind + " " + PrimaryEntity + "->" + SecondaryEntity
            + " @ " + Time + (Reasons.Count == 0 ? string.Empty : " because " + Reasons);

        public static bool operator ==(DomainEvent left, DomainEvent right) => left.Equals(right);

        public static bool operator !=(DomainEvent left, DomainEvent right) => !left.Equals(right);
    }
}
