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
    ///     rng.Key(RandomDomain.Conception, RandomSite.ConceptionRoll)
    ///        .Mix(householdId).Mix(attempt).Chance(1, 12)
    ///
    /// A draw names both what it is for and where it is taken from, and there
    /// is no overload that takes a domain alone: the pair is what keeps two
    /// unrelated decisions in one domain from sharing a key, so forgetting it
    /// is a compile error rather than a silent correlation (#57).
    ///
    /// Presentation randomness - idle animations, ambient sound, particle
    /// jitter - is unconstrained and must never come from here, because it
    /// never touches Core.
    /// </remarks>
    public sealed class DeterministicRng
    {
        private static readonly bool[] DefinedDomains = EnumGuard.BuildMask(typeof(RandomDomain));
        private static readonly bool[] DefinedSites = EnumGuard.BuildMask(typeof(RandomSite));

        private readonly IRandomDrawObserver? _observer;

        public DeterministicRng(ulong worldSeed)
            : this(worldSeed, null)
        {
        }

        /// <summary>
        /// A world whose draws are watched. The observer sees every value
        /// handed out and is how the harness and the tests check that two
        /// call sites never collide; production passes none. It cannot change
        /// what is drawn - see <see cref="IRandomDrawObserver"/>.
        /// </summary>
        public DeterministicRng(ulong worldSeed, IRandomDrawObserver? observer)
        {
            WorldSeed = worldSeed;
            _observer = observer;
        }

        /// <summary>The seed every draw in this world descends from.</summary>
        public ulong WorldSeed { get; }

        /// <summary>
        /// Starts a key for one decision, taken at one place. Mix in whatever
        /// identifies the decision, then read a value.
        /// </summary>
        /// <remarks>
        /// The site is mixed straight after the domain and before any caller
        /// component, deliberately. At a fixed position two distinct sites
        /// give two distinct states to build on, so the components that follow
        /// would have to drive those apart states back together to collide.
        /// Mixed at a depth that varied by call site it would guarantee much
        /// less, because order is part of a key - see the remarks on
        /// <see cref="RandomKey"/>.
        /// </remarks>
        public RandomKey Key(RandomDomain domain, RandomSite site)
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

            if (!EnumGuard.IsDefined(DefinedSites, (int)site))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(site),
                    site,
                    "Not a defined RandomSite. An unrecognised site would key a draw "
                    + "under a call site that does not exist, which is exactly the "
                    + "collision declaring one is meant to prevent.");
            }

            return new RandomKey(SplitMix64.Mix(WorldSeed), domain, site, _observer)
                .Mix((ulong)domain)
                .Mix((ulong)site);
        }
    }
}
