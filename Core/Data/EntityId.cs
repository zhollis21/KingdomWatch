using System;
using System.Globalization;

namespace KingdomWatch.Core.Data
{
    /// <summary>
    /// Durable identity. Never changes, never reused, safe to store in history,
    /// genealogy, grievances and saves - anything that outlives the entity.
    /// </summary>
    /// <remarks>
    /// The kind is an explicit field rather than a tag packed into
    /// <see cref="Value"/>. Two plain fields are easier to read, test and print
    /// than masks and shifts, and they let the validator check that a durable
    /// reference points at the right KIND of entity rather than merely
    /// resolving to something. That costs 16 bytes instead of 8, which nothing
    /// measures as a problem at roughly 1,650 people - revisit if M2 profiling
    /// disagrees. See docs/design/kingdom-watch-plan-v7.1.md section 5.
    ///
    /// Contrast <see cref="PersonHandle"/>, which indexes into storage and IS
    /// reused once a slot is recycled. Never persist a handle.
    /// </remarks>
    public readonly struct EntityId : IEquatable<EntityId>, IComparable<EntityId>
    {
        /// <summary>The absence of an entity. Equal to <c>default</c>.</summary>
        public static readonly EntityId None = default;

        /// <summary>
        /// Builds a durable id. The <see cref="EntityKind.None"/> kind and the
        /// value 0 only ever occur together: neither is a usable id on its own,
        /// and both would otherwise be storable in history and saves.
        /// </summary>
        public EntityId(EntityKind kind, ulong value)
        {
            if (kind == EntityKind.None && value != 0UL)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    "EntityKind.None means no entity, so it cannot carry a value.");
            }

            if (kind != EntityKind.None && value == 0UL)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    "Durable ids count from 1, so 0 never names a real " + kind + ".");
            }

            Kind = kind;
            Value = value;
        }

        public EntityKind Kind { get; }

        public ulong Value { get; }

        public bool IsNone => Kind == EntityKind.None && Value == 0UL;

        public bool Equals(EntityId other) => Kind == other.Kind && Value == other.Value;

        public override bool Equals(object? obj) => obj is EntityId other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Kind, Value);

        /// <summary>
        /// Orders by kind, then by value. Total and stable, which is what the
        /// scheduler's deterministic event ordering needs from a tie-breaker.
        /// </summary>
        public int CompareTo(EntityId other)
        {
            var byKind = ((int)Kind).CompareTo((int)other.Kind);
            return byKind != 0 ? byKind : Value.CompareTo(other.Value);
        }

        public override string ToString() => IsNone
            ? "None"
            : Kind.ToString() + "#" + Value.ToString(CultureInfo.InvariantCulture);

        public static bool operator ==(EntityId left, EntityId right) => left.Equals(right);

        public static bool operator !=(EntityId left, EntityId right) => !left.Equals(right);

        public static bool operator <(EntityId left, EntityId right) => left.CompareTo(right) < 0;

        public static bool operator >(EntityId left, EntityId right) => left.CompareTo(right) > 0;

        public static bool operator <=(EntityId left, EntityId right) => left.CompareTo(right) <= 0;

        public static bool operator >=(EntityId left, EntityId right) => left.CompareTo(right) >= 0;
    }
}
