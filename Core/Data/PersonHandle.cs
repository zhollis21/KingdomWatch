using System;
using System.Globalization;

namespace KingdomWatch.Core.Data
{
    /// <summary>
    /// Runtime handle into person storage. Indexes a slot and carries the
    /// generation that slot had when the handle was taken, so a handle to a
    /// recycled slot can be detected as stale rather than silently resolving to
    /// whoever moved in.
    /// </summary>
    /// <remarks>
    /// Handles are for the running simulation only. Anything that outlives the
    /// person - history, genealogy, grievances, saves - stores an
    /// <see cref="EntityId"/> instead. Persisting a handle is the latent
    /// history-corruption bug that the two types exist to prevent. See
    /// docs/design/kingdom-watch-plan-v7.1.md section 5.
    ///
    /// Generation 0 is reserved for <see cref="None"/>; a live slot starts at
    /// generation 1. Slot allocation itself belongs to PersonStore (issue #6),
    /// not here.
    /// </remarks>
    public readonly struct PersonHandle : IEquatable<PersonHandle>
    {
        /// <summary>No person. Equal to <c>default</c>.</summary>
        public static readonly PersonHandle None = default;

        public PersonHandle(int index, int generation)
        {
            if (index < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(index), index, "Storage index cannot be negative.");
            }

            if (generation < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(generation), generation, "Generation cannot be negative.");
            }

            Index = index;
            Generation = generation;
        }

        public int Index { get; }

        public int Generation { get; }

        /// <summary>True when this handle refers to no person at all.</summary>
        public bool IsNone => Generation == 0;

        public bool Equals(PersonHandle other) =>
            Index == other.Index && Generation == other.Generation;

        public override bool Equals(object? obj) => obj is PersonHandle other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Index, Generation);

        public override string ToString() => IsNone
            ? "None"
            : "Person[" + Index.ToString(CultureInfo.InvariantCulture)
              + ":" + Generation.ToString(CultureInfo.InvariantCulture) + "]";

        public static bool operator ==(PersonHandle left, PersonHandle right) => left.Equals(right);

        public static bool operator !=(PersonHandle left, PersonHandle right) => !left.Equals(right);
    }
}
