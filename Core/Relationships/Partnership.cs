using System;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;

namespace KingdomWatch.Core.Relationships
{
    /// <summary>
    /// One partnership between two people, from the event that formed it to
    /// the event that ended it. Never deleted: ended is a state, not an
    /// absence.
    /// </summary>
    /// <remarks>
    /// The two people are held in id order so a pair has exactly one canonical
    /// record, whichever way round a caller names them.
    ///
    /// Why it ended is not enumerated. The ending event is the provenance -
    /// the death cascade passes its PersonDied event, and anything that ends a
    /// partnership later passes its own - and the journal already says what
    /// that event was. A reason enum here would be a second copy of the same
    /// fact, free to disagree with the first.
    /// </remarks>
    public readonly struct Partnership : IEquatable<Partnership>
    {
        internal Partnership(
            EntityId first,
            EntityId second,
            EventId formedBy,
            SimulationTime formedAt,
            EventId endedBy,
            SimulationTime endedAt)
        {
            First = first;
            Second = second;
            FormedBy = formedBy;
            FormedAt = formedAt;
            EndedBy = endedBy;
            EndedAt = endedAt;
        }

        /// <summary>The lower of the two ids.</summary>
        public EntityId First { get; }

        /// <summary>The higher of the two ids.</summary>
        public EntityId Second { get; }

        public EventId FormedBy { get; }

        public SimulationTime FormedAt { get; }

        /// <summary><see cref="EventId.None"/> while the partnership is active.</summary>
        public EventId EndedBy { get; }

        /// <summary>Meaningless while <see cref="IsActive"/>.</summary>
        public SimulationTime EndedAt { get; }

        /// <summary>
        /// Formed and not yet ended. A <c>default</c> record was never formed
        /// and so is not active either.
        /// </summary>
        public bool IsActive => !FormedBy.IsNone && EndedBy.IsNone;

        public bool Involves(EntityId person) => person == First || person == Second;

        /// <summary>The other party. Throws for someone not in the partnership.</summary>
        public EntityId PartnerOf(EntityId person)
        {
            if (person == First)
            {
                return Second;
            }

            if (person == Second)
            {
                return First;
            }

            throw new ArgumentException(person + " is not in this partnership.", nameof(person));
        }

        internal Partnership Ended(EventId endedBy, SimulationTime endedAt) =>
            new Partnership(First, Second, FormedBy, FormedAt, endedBy, endedAt);

        public bool Equals(Partnership other) =>
            First == other.First
            && Second == other.Second
            && FormedBy == other.FormedBy
            && FormedAt == other.FormedAt
            && EndedBy == other.EndedBy
            && EndedAt == other.EndedAt;

        public override bool Equals(object? obj) => obj is Partnership other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(First, Second, FormedBy);

        public override string ToString() =>
            First + " and " + Second + (IsActive ? " since " : " ended ") + (IsActive ? FormedAt : EndedAt);

        public static bool operator ==(Partnership left, Partnership right) => left.Equals(right);

        public static bool operator !=(Partnership left, Partnership right) => !left.Equals(right);
    }
}
