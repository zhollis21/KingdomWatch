using System;
using KingdomWatch.Core.Data;

namespace KingdomWatch.Core.Rng
{
    /// <summary>
    /// A random draw identified by what it is FOR and where it is taken from,
    /// rather than by a position in a stream. Build a key from the things that
    /// make the decision unique, then read a value off it.
    /// </summary>
    /// <remarks>
    /// This is the shape section 5 asks for. One stream per subsystem is not
    /// sufficient, because level of detail changes how many draws happen:
    /// detailed combat draws hit, damage, dodge, hit, damage, while compressed
    /// combat draws resolveBattle once. With a stream, zooming in would leave
    /// the subsystem at a different position and change every future battle.
    /// Keyed draws have no position to disturb.
    ///
    /// The struct allocates nothing, so building a key inside the tick loop is
    /// free. It carries its <see cref="RandomDomain"/> and
    /// <see cref="RandomSite"/> alongside the mixed state, which is what lets
    /// a draw tell an <see cref="IRandomDrawObserver"/> where it came from
    /// (#57); those two fields take no part in mixing after
    /// <see cref="DeterministicRng.Key"/> has folded them in, so they cost
    /// stack space and nothing else.
    ///
    /// **Order is part of a key.** Mix(a).Mix(b) and Mix(b).Mix(a) are
    /// different draws, which is why the domain and the site are folded in
    /// first and at a fixed depth: every key begins from a state unique to its
    /// call site, and the caller's own components build on that rather than
    /// competing with it.
    ///
    /// There is deliberately no Mix overload for PersonHandle or any other
    /// runtime handle. Handles carry a storage index and generation, both of
    /// which change when storage is compacted or a slot is recycled - keying a
    /// draw on one would make the result depend on storage layout rather than
    /// on identity. Key on the durable id instead.
    ///
    /// Durable ids follow one rule: mix a type tag, then mix the id's parts.
    /// Without the tag an id would be indistinguishable from the plain numbers
    /// it is built from - Event#7 would key exactly what the bare number 7
    /// keys, and Person#42 what the pair (1, 42) keys, since EntityKind.Person
    /// is 1. Small integers are precisely what counters and indices look like,
    /// so that collision was reachable by accident.
    ///
    /// Such a collision is the quiet kind. The world stays perfectly
    /// reproducible, no test fails, and the cross-platform hash still agrees;
    /// it surfaces much later as two things that should be independent moving
    /// in lockstep. Domain and site granularity is the other half of the
    /// protection - see the remarks on RandomDomain and RandomSite.
    /// </remarks>
    public readonly struct RandomKey
    {
        // Type tags, mixed ahead of a durable id's parts so that an id can
        // never key the same draw as the plain numbers it is built from.
        // The bytes spell "EntityId", and "EventId" padded with a trailing NUL
        // to fill eight - deliberately large and arbitrary, because a caller
        // folding in counters and indices will never produce one by accident.
        // Changing either changes every draw keyed on that type in every world,
        // so they are as fixed as the mixer itself.
        private const ulong EntityDiscriminator = 0x456E746974794964UL;
        private const ulong EventDiscriminator = 0x4576656E74496400UL;

        private readonly ulong _state;

        // Where this key came from. Carried for reporting only; the mixing is
        // already done by the time a key exists.
        private readonly RandomDomain _domain;
        private readonly RandomSite _site;
        private readonly IRandomDrawObserver? _observer;

        internal RandomKey(ulong state, RandomDomain domain, RandomSite site, IRandomDrawObserver? observer)
        {
            _state = state;
            _domain = domain;
            _site = site;
            _observer = observer;
        }

        /// <summary>Folds another component into the key.</summary>
        public RandomKey Mix(ulong value) =>
            new RandomKey(SplitMix64.Mix(_state ^ value), _domain, _site, _observer);

        /// <summary>Folds a signed component into the key.</summary>
        public RandomKey Mix(long value) => Mix(unchecked((ulong)value));

        /// <summary>Folds a signed component into the key.</summary>
        public RandomKey Mix(int value) => Mix((long)value);

        /// <summary>
        /// Folds a durable entity id into the key: its type tag, then its kind,
        /// then its value. Kind is mixed separately from value so Person#1 and
        /// Settlement#1 key different draws.
        /// </summary>
        public RandomKey Mix(EntityId id) =>
            Mix(EntityDiscriminator).Mix((ulong)id.Kind).Mix(id.Value);

        /// <summary>
        /// Folds a durable event id into the key: its type tag, then its value.
        /// </summary>
        public RandomKey Mix(EventId id) => Mix(EventDiscriminator).Mix(id.Value);

        /// <summary>The full 64-bit value for this key.</summary>
        public ulong NextUInt64() => Observe(Draw(0UL));

        /// <summary>
        /// A value in [0, exclusiveMax). Unbiased.
        /// </summary>
        /// <remarks>
        /// Taking the modulo of the whole 64-bit range would favour small
        /// results whenever exclusiveMax does not divide 2^64 evenly, so the
        /// short final block is rejected instead. Rejection re-draws with a
        /// different attempt number rather than advancing a stream, which is
        /// what keeps the result keyed.
        /// </remarks>
        public ulong Below(ulong exclusiveMax)
        {
            if (exclusiveMax == 0UL)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(exclusiveMax), exclusiveMax, "Upper bound must be greater than zero.");
            }

            // 2^64 mod exclusiveMax, computed with unsigned wraparound.
            var threshold = unchecked(0UL - exclusiveMax) % exclusiveMax;

            // Each attempt is rejected with probability below one half, so this
            // terminates quickly. It is not bounded by a fixed count because a
            // bound would have to bias the result to stay unbiased.
            for (var attempt = 0UL; ; attempt++)
            {
                var draw = Draw(attempt);

                if (draw >= threshold)
                {
                    // The accepted draw is what is reported, not each rejected
                    // attempt and not the bounded result: two sites that agree
                    // on this agree however either one bounds it.
                    return Observe(draw) % exclusiveMax;
                }
            }
        }

        /// <summary>A value in [minInclusive, maxExclusive).</summary>
        public int Range(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxExclusive),
                    maxExclusive,
                    "Upper bound must be greater than the lower bound (" + minInclusive + ").");
            }

            var span = (ulong)((long)maxExclusive - minInclusive);
            return (int)((long)minInclusive + (long)Below(span));
        }

        /// <summary>
        /// True with probability numerator/denominator. Odds are expressed as a
        /// ratio rather than a fraction because Core branches on the result,
        /// and section 5 requires integer arithmetic wherever the sim branches.
        /// </summary>
        public bool Chance(int numerator, int denominator)
        {
            if (denominator <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(denominator), denominator, "Denominator must be greater than zero.");
            }

            if (numerator < 0 || numerator > denominator)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(numerator),
                    numerator,
                    "Numerator must be between 0 and the denominator (" + denominator + ") inclusive.");
            }

            // Certainties resolve without a draw, so they are exact rather than
            // merely overwhelmingly likely. Nothing is drawn, so nothing is
            // reported: a certainty cannot collide with anything.
            if (numerator == 0)
            {
                return false;
            }

            if (numerator == denominator)
            {
                return true;
            }

            return Below((ulong)denominator) < (ulong)numerator;
        }

        private ulong Draw(ulong attempt) => SplitMix64.Mix(unchecked(_state + attempt));

        // One predictable branch on a field that is null everywhere except the
        // harness and the tests. Returns its argument so it can wrap a draw
        // without restructuring the caller.
        private ulong Observe(ulong value)
        {
            _observer?.Drew(_domain, _site, value);
            return value;
        }
    }
}
