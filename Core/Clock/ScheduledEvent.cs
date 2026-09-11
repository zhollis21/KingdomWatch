using System;
using KingdomWatch.Core.Data;

namespace KingdomWatch.Core.Clock
{
    /// <summary>
    /// One entry on the world clock: what happens, to whom, when, and in which
    /// phase of that instant. Ordering lives here, in <see cref="CompareTo"/>.
    /// </summary>
    /// <remarks>
    /// The ordering is
    /// <see cref="Time"/> then <see cref="Phase"/> then
    /// <see cref="PrimaryEntity"/> then <see cref="Kind"/> then
    /// <see cref="SecondaryEntity"/> then <see cref="Id"/>, and it must be
    /// TOTAL. If two entries can compare equal, which one runs first is decided
    /// by the queue's internal layout, and the simulation stops being
    /// reproducible the moment that layout changes for any reason.
    ///
    /// Section 4 stops at SecondaryEntityId, which is not quite enough. Two
    /// events of the same kind, at the same instant, between the same pair of
    /// entities are legitimate rather than exotic - two hauling trips finishing
    /// in the same simulated second, or a batch of birth checks scheduled "in
    /// thirty days" from a shared origin. A heap breaks such a tie on its array
    /// layout: identical on replay today, reordered the first time the queue is
    /// rebuilt from a save (#15) or compacted, and invisible to the
    /// cross-platform hash because desktop and IL2CPP execute the same
    /// operations and agree on the same wrong answer.
    ///
    /// <see cref="EventId"/> closes the order without adding a concept. It is
    /// already durable, monotonic, never reused and comparable, and a scheduled
    /// event needs one anyway for history, decision provenance (#8) and the
    /// validator's rule that no scheduled event targets a dead handle (#13).
    /// Because ids are handed out in increasing order, the tiebreak is
    /// scheduling order - pinned as stored data rather than left to the
    /// container.
    ///
    /// <see cref="PrimaryEntity"/> outranks <see cref="Kind"/> deliberately:
    /// everything happening to one person at an instant stays together, which
    /// is what makes a dispatch log readable. Either entity may be
    /// <see cref="EntityId.None"/>, for a world-level event or one with no
    /// second party. See docs/design/kingdom-watch-plan-v7.1.md section 4.
    /// </remarks>
    public readonly struct ScheduledEvent : IEquatable<ScheduledEvent>, IComparable<ScheduledEvent>
    {
        private static readonly bool[] DefinedPhases = EnumGuard.BuildMask(typeof(SimulationPhase));
        private static readonly bool[] DefinedKinds = EnumGuard.BuildMask(typeof(ScheduledEventKind));

        public ScheduledEvent(
            EventId id,
            SimulationTime time,
            SimulationPhase phase,
            ScheduledEventKind kind,
            EntityId primaryEntity,
            EntityId secondaryEntity)
        {
            if (id.IsNone)
            {
                throw new ArgumentException(
                    "A scheduled event needs a durable id: it is the last component of the ordering "
                    + "key, and without it two otherwise identical events have no defined order.",
                    nameof(id));
            }

            if (!EnumGuard.IsDefined(DefinedPhases, (int)phase))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(phase), phase, "Not a defined SimulationPhase.");
            }

            if (phase == SimulationPhase.None)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(phase),
                    phase,
                    "SimulationPhase.None is the defaulted-field guard, not a phase events run in.");
            }

            if (!EnumGuard.IsDefined(DefinedKinds, (int)kind))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(kind), kind, "Not a defined ScheduledEventKind.");
            }

            if (kind == ScheduledEventKind.None)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(kind),
                    kind,
                    "ScheduledEventKind.None is the defaulted-field guard, not something that happens.");
            }

            Id = id;
            Time = time;
            Phase = phase;
            Kind = kind;
            PrimaryEntity = primaryEntity;
            SecondaryEntity = secondaryEntity;
        }

        /// <summary>Durable identity, and the final ordering tiebreak.</summary>
        public EventId Id { get; }

        public SimulationTime Time { get; }

        public SimulationPhase Phase { get; }

        public ScheduledEventKind Kind { get; }

        /// <summary>Who this is mainly about. May be <see cref="EntityId.None"/>.</summary>
        public EntityId PrimaryEntity { get; }

        /// <summary>The second party, if any. May be <see cref="EntityId.None"/>.</summary>
        public EntityId SecondaryEntity { get; }

        /// <summary>
        /// The total order the scheduler dispatches in. Returns 0 only when
        /// both entries are the same event, because <see cref="Id"/> is unique.
        /// </summary>
        public int CompareTo(ScheduledEvent other)
        {
            var byPosition = ComparePositionTo(other);
            return byPosition != 0 ? byPosition : Id.CompareTo(other.Id);
        }

        /// <summary>
        /// Orders by everything except <see cref="Id"/> — WHERE an event sits
        /// in the instant, rather than WHICH event it is. Returns 0 for two
        /// distinct events occupying the same position.
        /// </summary>
        /// <remarks>
        /// Position and identity are different questions, and the scheduler
        /// needs both separately. <see cref="CompareTo"/> answers "which runs
        /// first", and must be total, so it falls back to
        /// <see cref="Id"/>. <see cref="SimulationClock.Schedule"/> asks "is
        /// this reaction ahead of the event that caused it", which is about
        /// position alone — a freshly allocated id is always the larger one, so
        /// a guard built on <see cref="CompareTo"/> can never reject a reaction
        /// that lands exactly where its own cause did, and a handler that
        /// reproduces itself there freezes the clock.
        /// </remarks>
        public int ComparePositionTo(ScheduledEvent other)
        {
            var byTime = Time.CompareTo(other.Time);

            if (byTime != 0)
            {
                return byTime;
            }

            var byPhase = ((int)Phase).CompareTo((int)other.Phase);

            if (byPhase != 0)
            {
                return byPhase;
            }

            var byPrimary = PrimaryEntity.CompareTo(other.PrimaryEntity);

            if (byPrimary != 0)
            {
                return byPrimary;
            }

            var byKind = ((int)Kind).CompareTo((int)other.Kind);

            if (byKind != 0)
            {
                return byKind;
            }

            return SecondaryEntity.CompareTo(other.SecondaryEntity);
        }

        public bool Equals(ScheduledEvent other) =>
            Id == other.Id
            && Time == other.Time
            && Phase == other.Phase
            && Kind == other.Kind
            && PrimaryEntity == other.PrimaryEntity
            && SecondaryEntity == other.SecondaryEntity;

        public override bool Equals(object? obj) => obj is ScheduledEvent other && Equals(other);

        public override int GetHashCode() => Id.GetHashCode();

        public override string ToString() =>
            Id + " " + Kind + " " + PrimaryEntity + "->" + SecondaryEntity
            + " " + Phase + " @ " + Time;

        public static bool operator ==(ScheduledEvent left, ScheduledEvent right) => left.Equals(right);

        public static bool operator !=(ScheduledEvent left, ScheduledEvent right) => !left.Equals(right);

        public static bool operator <(ScheduledEvent left, ScheduledEvent right) => left.CompareTo(right) < 0;

        public static bool operator >(ScheduledEvent left, ScheduledEvent right) => left.CompareTo(right) > 0;

        public static bool operator <=(ScheduledEvent left, ScheduledEvent right) => left.CompareTo(right) <= 0;

        public static bool operator >=(ScheduledEvent left, ScheduledEvent right) => left.CompareTo(right) >= 0;
    }
}
