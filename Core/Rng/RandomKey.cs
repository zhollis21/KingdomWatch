using System;
using KingdomWatch.Core.Data;

namespace KingdomWatch.Core.Rng
{
    /// <summary>
    /// A random draw identified by what it is FOR rather than by a position in
    /// a stream. Build a key from the things that make the decision unique,
    /// then read a value off it.
    /// </summary>
    /// <remarks>
    /// This is the shape section 5 asks for. One stream per subsystem is not
    /// sufficient, because level of detail changes how many draws happen:
    /// detailed combat draws hit, damage, dodge, hit, damage, while compressed
    /// combat draws resolveBattle once. With a stream, zooming in would leave
    /// the subsystem at a different position and change every future battle.
    /// Keyed draws have no position to disturb.
    ///
    /// The struct carries a single ulong and allocates nothing, so building a
    /// key inside the tick loop is free.
    ///
    /// There is deliberately no Mix overload for PersonHandle or any other
    /// runtime handle. Handles carry a storage index and generation, both of
    /// which change when storage is compacted or a slot is recycled - keying a
    /// draw on one would make the result depend on storage layout rather than
    /// on identity. Key on the durable id instead.
    /// </remarks>
    public readonly struct RandomKey
    {
        // "EventId" in ASCII. Changing it changes every event-keyed draw in
        // every world, so it is as fixed as the mixer itself.
        private const ulong EventDiscriminator = 0x4576656E74496400UL;

        private readonly ulong _state;

        internal RandomKey(ulong state)
        {
            _state = state;
        }

        /// <summary>Folds another component into the key.</summary>
        public RandomKey Mix(ulong value) => new RandomKey(SplitMix64.Mix(_state ^ value));

        /// <summary>Folds a signed component into the key.</summary>
        public RandomKey Mix(long value) => Mix(unchecked((ulong)value));

        /// <summary>Folds a signed component into the key.</summary>
        public RandomKey Mix(int value) => Mix((long)value);

        /// <summary>
        /// Folds a durable entity id into the key. Kind and value are mixed
        /// separately so Person#1 and Settlement#1 key different draws.
        /// </summary>
        public RandomKey Mix(EntityId id) => Mix((ulong)id.Kind).Mix(id.Value);

        /// <summary>
        /// Folds a durable event id into the key. A discriminator is mixed
        /// ahead of the value so that Event#7 does not key the same draw as the
        /// bare number 7, matching how <see cref="Mix(EntityId)"/> mixes a kind
        /// ahead of its value. The constant is deliberately large and arbitrary
        /// - "EventId" in ASCII - because a caller passing counters and indices
        /// will never produce it by accident.
        /// </summary>
        public RandomKey Mix(EventId id) => Mix(EventDiscriminator).Mix(id.Value);

        /// <summary>The full 64-bit value for this key.</summary>
        public ulong NextUInt64() => Draw(0UL);

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
                    return draw % exclusiveMax;
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
            // merely overwhelmingly likely.
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
    }
}
