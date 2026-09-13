using System;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;

namespace KingdomWatch.Core.Relationships
{
    /// <summary>
    /// Something a person or settlement remembers about a specific event:
    /// what it meant to them, and who was there. A grievance is a memory
    /// with negative <see cref="Valence"/>.
    /// </summary>
    /// <remarks>
    /// Keyed on the originating event rather than on a person, per section
    /// 11's insurance for the M5 unification of grievances, rumors and
    /// miracles into one HistoricalClaim: if all three key on the event that
    /// caused them, converging them later is a rename and not a migration.
    /// Subject is who or what it is about - the raider, the settlement, the
    /// god - and may be <see cref="EntityId.None"/> when the event speaks for
    /// itself.
    ///
    /// Strength is not stored: it is <see cref="WitnessCount"/>, the living
    /// witnesses plus the descendants taught it (section 10). The witnesses
    /// themselves live in a store-owned list this struct carries a reference
    /// to, which is what lets a holder's memories be handed out as one span
    /// while each keeps its own bounded witness list. Read them through
    /// <see cref="Memories.Witnesses"/>.
    /// </remarks>
    public readonly struct Memory
    {
        private readonly SpanList<EntityId>? _witnesses;

        internal Memory(
            EventId originEvent,
            EntityId holder,
            EntityId subject,
            sbyte valence,
            MemoryTier tier,
            SimulationTime formedAt,
            SpanList<EntityId> witnesses)
        {
            OriginEvent = originEvent;
            Holder = holder;
            Subject = subject;
            Valence = valence;
            Tier = tier;
            FormedAt = formedAt;
            _witnesses = witnesses;
        }

        public EventId OriginEvent { get; }

        /// <summary>Who holds the memory: a person, a settlement, any entity.</summary>
        public EntityId Holder { get; }

        /// <summary>Who or what it is about. May be <see cref="EntityId.None"/>.</summary>
        public EntityId Subject { get; }

        /// <summary>Negative is a grievance; positive, gratitude or reverence.</summary>
        public sbyte Valence { get; }

        public MemoryTier Tier { get; }

        public SimulationTime FormedAt { get; }

        public bool IsGrievance => Valence < 0;

        /// <summary>
        /// Living witnesses plus those taught it - the memory's strength.
        /// </summary>
        public int WitnessCount => _witnesses is null ? 0 : _witnesses.Count;

        // Only the store reaches this, and only on memories it built, so the
        // throw is the nullable-flow way of saying "never default here"
        // rather than a case anything handles.
        internal SpanList<EntityId> WitnessList =>
            _witnesses ?? throw new InvalidOperationException("A default Memory has no witness list.");

        internal Memory WithTier(MemoryTier tier) =>
            new Memory(OriginEvent, Holder, Subject, Valence, tier, FormedAt, WitnessList);

        public override string ToString() =>
            Holder + " remembers " + OriginEvent + " about " + Subject + " (valence " + Valence
            + ", " + Tier + ", " + WitnessCount + " witnesses)";
    }
}
