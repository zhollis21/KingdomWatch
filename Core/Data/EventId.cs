using System;
using System.Globalization;

namespace KingdomWatch.Core.Data
{
    /// <summary>
    /// Durable event identity. Never reused. Referenced by history, grievances,
    /// miracles, rumors, decision provenance and player bookmarks, all of which
    /// outlive the event itself.
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="EntityId"/> there is no kind: an event is an event.
    /// See docs/design/kingdom-watch-plan-v7.1.md section 5.
    /// </remarks>
    public readonly struct EventId : IEquatable<EventId>, IComparable<EventId>
    {
        /// <summary>The absence of an event. Equal to <c>default</c>.</summary>
        public static readonly EventId None = default;

        public EventId(ulong value)
        {
            Value = value;
        }

        public ulong Value { get; }

        public bool IsNone => Value == 0UL;

        public bool Equals(EventId other) => Value == other.Value;

        public override bool Equals(object? obj) => obj is EventId other && Equals(other);

        public override int GetHashCode() => Value.GetHashCode();

        public int CompareTo(EventId other) => Value.CompareTo(other.Value);

        public override string ToString() => IsNone
            ? "None"
            : "Event#" + Value.ToString(CultureInfo.InvariantCulture);

        public static bool operator ==(EventId left, EventId right) => left.Equals(right);

        public static bool operator !=(EventId left, EventId right) => !left.Equals(right);

        public static bool operator <(EventId left, EventId right) => left.CompareTo(right) < 0;

        public static bool operator >(EventId left, EventId right) => left.CompareTo(right) > 0;

        public static bool operator <=(EventId left, EventId right) => left.CompareTo(right) <= 0;

        public static bool operator >=(EventId left, EventId right) => left.CompareTo(right) >= 0;
    }
}
