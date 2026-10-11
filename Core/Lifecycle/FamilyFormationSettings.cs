using System;
using KingdomWatch.Core.Clock;

namespace KingdomWatch.Core.Lifecycle
{
    /// <summary>
    /// The two rules of family formation that section 6 hands to culture. No
    /// culture exists yet (#40), so they are settings; when it does, they
    /// become reads from it, and the checks that consume them do not change.
    /// </summary>
    public readonly struct FamilyFormationSettings
    {
        /// <summary>
        /// One year. A placeholder in the <see cref="Data.PrimitiveTier"/>
        /// sense: plausible, not tuned.
        /// </summary>
        public const long DefaultMourningTicks = SimulationTime.TicksPerYear;

        /// <summary>
        /// Remarriage after a year, first cousins permitted. Section 6 makes
        /// the cousin taboo culture's, loosened where the mating pool is
        /// strained; with no culture yet, every village is a closed pool of
        /// twenty, and refusing cousins sealed it within three generations
        /// while the barns stood full (#163). Culture (#40) takes it back.
        /// </summary>
        public static readonly FamilyFormationSettings Default =
            new FamilyFormationSettings(DefaultMourningTicks, firstCousinsPermitted: true);

        public FamilyFormationSettings(long mourningTicks, bool firstCousinsPermitted)
        {
            if (mourningTicks < 0L)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(mourningTicks), mourningTicks, "A duration cannot be negative.");
            }

            MourningTicks = mourningTicks;
            FirstCousinsPermitted = firstCousinsPermitted;
        }

        /// <summary>
        /// How long after a partnership ends before either party may form
        /// another. Section 6: widows and widowers may remarry after a
        /// mourning period, and culture can modulate its length.
        /// </summary>
        public long MourningTicks { get; }

        /// <summary>
        /// Whether first cousins may partner. Section 6: a culture taboo, not
        /// a rule - some settlements permit it, some do not, and it drifts.
        /// </summary>
        public bool FirstCousinsPermitted { get; }
    }
}
