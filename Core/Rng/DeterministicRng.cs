using System;

namespace KingdomWatch.Core.Rng
{
    /// <summary>
    /// The world's only source of randomness. Holds the world seed and starts
    /// keys from it.
    /// </summary>
    /// <remarks>
    /// Never UnityEngine.Random, and never a shared mutable stream. Every draw
    /// is derived from the seed plus the identity of the decision being made,
    /// so adding a draw in one system cannot shift the random future of
    /// another. See docs/design/kingdom-watch-plan-v7.1.md section 5.
    ///
    /// Typical use reads as a sentence:
    ///
    ///     rng.Key(RandomDomain.Conception).Mix(householdId).Mix(attempt).Chance(1, 12)
    ///
    /// Presentation randomness - idle animations, ambient sound, particle
    /// jitter - is unconstrained and must never come from here, because it
    /// never touches Core.
    /// </remarks>
    public sealed class DeterministicRng
    {
        private static readonly bool[] DefinedDomains = EnumGuard.BuildMask(typeof(RandomDomain));

        public DeterministicRng(ulong worldSeed)
        {
            WorldSeed = worldSeed;
        }

        /// <summary>The seed every draw in this world descends from.</summary>
        public ulong WorldSeed { get; }

        /// <summary>
        /// Starts a key in the given domain. Mix in whatever identifies the
        /// decision, then read a value.
        /// </summary>
        public RandomKey Key(RandomDomain domain)
        {
            if (!EnumGuard.IsDefined(DefinedDomains, (int)domain))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(domain),
                    domain,
                    "Not a defined RandomDomain. An unrecognised domain would key a "
                    + "subsystem's draws under something nobody declared.");
            }

            if (domain == RandomDomain.None)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(domain), domain, "A draw must belong to a real domain.");
            }

            return new RandomKey(SplitMix64.Mix(WorldSeed)).Mix((ulong)domain);
        }
    }
}
