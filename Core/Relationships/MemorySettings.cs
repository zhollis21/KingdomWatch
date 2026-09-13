using System;

namespace KingdomWatch.Core.Relationships
{
    /// <summary>
    /// The thresholds that tier memories. Tuning, passed in: how long a
    /// grievance lasts is a design question, and not one this store answers.
    /// </summary>
    public readonly struct MemorySettings
    {
        public MemorySettings(
            int maxWitnesses,
            long oldAfterTicks,
            long forgetAfterTicks,
            int promoteWhenWitnessesAtLeast)
        {
            if (maxWitnesses < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxWitnesses), maxWitnesses, "A memory must be allowed at least one witness.");
            }

            if (oldAfterTicks < 0L)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(oldAfterTicks), oldAfterTicks, "A duration cannot be negative.");
            }

            if (forgetAfterTicks < oldAfterTicks)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(forgetAfterTicks),
                    forgetAfterTicks,
                    "A memory becomes old before it is forgotten, so this cannot be shorter than "
                    + nameof(oldAfterTicks) + " (" + oldAfterTicks + ").");
            }

            if (promoteWhenWitnessesAtLeast < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(promoteWhenWitnessesAtLeast),
                    promoteWhenWitnessesAtLeast,
                    "Promotion needs at least one witness, or every memory would be promoted.");
            }

            if (promoteWhenWitnessesAtLeast > maxWitnesses)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(promoteWhenWitnessesAtLeast),
                    promoteWhenWitnessesAtLeast,
                    "No memory could ever reach this: witness lists are capped at " + maxWitnesses + ".");
            }

            MaxWitnesses = maxWitnesses;
            OldAfterTicks = oldAfterTicks;
            ForgetAfterTicks = forgetAfterTicks;
            PromoteWhenWitnessesAtLeast = promoteWhenWitnessesAtLeast;
        }

        /// <summary>
        /// The most witnesses one memory tracks. Section 10 caps the list so a
        /// structure that grows for 500 years stays bounded; witnesses past
        /// the cap are not recorded.
        /// </summary>
        public int MaxWitnesses { get; }

        /// <summary>Age at which a Recent memory becomes Old.</summary>
        public long OldAfterTicks { get; }

        /// <summary>
        /// Age past which an Old memory with no witnesses left is forgotten.
        /// At least <see cref="OldAfterTicks"/>.
        /// </summary>
        public long ForgetAfterTicks { get; }

        /// <summary>
        /// The witness count at which a memory is promoted and kept forever.
        /// </summary>
        public int PromoteWhenWitnessesAtLeast { get; }
    }
}
