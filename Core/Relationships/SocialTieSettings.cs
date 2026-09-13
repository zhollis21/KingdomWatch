using System;

namespace KingdomWatch.Core.Relationships
{
    /// <summary>
    /// The two numbers that bound social ties. Tuning, passed in, so that
    /// nothing in <see cref="SocialTies"/> has an opinion about how quickly
    /// an elf forgets an acquaintance.
    /// </summary>
    /// <remarks>
    /// One decay interval for all three values. Separate rates per value are
    /// a plausible tuning need and an additive change here if it comes;
    /// three knobs before anything has been tuned would be three guesses.
    /// </remarks>
    public readonly struct SocialTieSettings
    {
        public SocialTieSettings(int maxTiesPerPerson, long ticksPerDecayStep)
        {
            if (maxTiesPerPerson < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxTiesPerPerson), maxTiesPerPerson, "A person must be allowed at least one tie.");
            }

            if (ticksPerDecayStep < 1L)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(ticksPerDecayStep), ticksPerDecayStep, "Decay needs a positive interval.");
            }

            MaxTiesPerPerson = maxTiesPerPerson;
            TicksPerDecayStep = ticksPerDecayStep;
        }

        /// <summary>
        /// The most ties one person holds. Past it, the weakest is evicted to
        /// make room - the bound that keeps a 300-year life from accumulating
        /// everyone it ever met.
        /// </summary>
        public int MaxTiesPerPerson { get; }

        /// <summary>
        /// How many ticks it takes for each of a tie's values to move one unit
        /// toward zero.
        /// </summary>
        public long TicksPerDecayStep { get; }
    }
}
