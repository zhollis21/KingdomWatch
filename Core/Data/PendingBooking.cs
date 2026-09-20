using System;
using KingdomWatch.Core.Clock;

namespace KingdomWatch.Core.Data
{
    /// <summary>
    /// One entry in a system's "the state names the event it booked" record
    /// (#80): who it is for, what was booked, and of what kind.
    /// </summary>
    /// <remarks>
    /// Every periodic stream keeps one of these, because a stream that rebooks
    /// from inside its own handler and does not record what it booked lets the
    /// record and the queue disagree with nothing to notice. The systems keep
    /// them in their own shapes - a field on a tracked row, a dictionary keyed
    /// by household - so this is the common form they hand out in, for the
    /// validator to confirm against the queue and for the world hash to fold
    /// in.
    ///
    /// Ordered by owner, then kind, then id, so a caller that gathers
    /// bookings from several systems can sort them into one canonical
    /// sequence. The order is total: a stream books one event per owner per
    /// kind at a time, and <see cref="EventId"/> is unique regardless.
    /// </remarks>
    public readonly struct PendingBooking : IEquatable<PendingBooking>, IComparable<PendingBooking>
    {
        public PendingBooking(EntityId owner, ScheduledEventKind kind, EventId booked)
        {
            if (owner.IsNone)
            {
                throw new ArgumentException("A booking is for somebody.", nameof(owner));
            }

            if (kind == ScheduledEventKind.None)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(kind), kind, "A booking names the kind of event it booked.");
            }

            if (booked.IsNone)
            {
                throw new ArgumentException(
                    "A booking names an event; nothing booked is an absent entry, not an entry naming None.",
                    nameof(booked));
            }

            Owner = owner;
            Kind = kind;
            Booked = booked;
        }

        /// <summary>The community or person the stream runs for.</summary>
        public EntityId Owner { get; }

        /// <summary>What kind of event is booked.</summary>
        public ScheduledEventKind Kind { get; }

        /// <summary>The event the owner's state names.</summary>
        public EventId Booked { get; }

        public int CompareTo(PendingBooking other)
        {
            var byOwner = Owner.CompareTo(other.Owner);

            if (byOwner != 0)
            {
                return byOwner;
            }

            var byKind = ((int)Kind).CompareTo((int)other.Kind);
            return byKind != 0 ? byKind : Booked.Value.CompareTo(other.Booked.Value);
        }

        public bool Equals(PendingBooking other) =>
            Owner == other.Owner && Kind == other.Kind && Booked == other.Booked;

        public override bool Equals(object? obj) => obj is PendingBooking other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Owner, Kind, Booked);

        public override string ToString() => Owner + "'s " + Kind + " " + Booked;

        public static bool operator ==(PendingBooking left, PendingBooking right) => left.Equals(right);

        public static bool operator !=(PendingBooking left, PendingBooking right) => !left.Equals(right);
    }
}
