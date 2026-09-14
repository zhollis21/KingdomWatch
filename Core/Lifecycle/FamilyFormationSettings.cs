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
        /// A year of days. A placeholder in the <see cref="Data.PrimitiveTier"/>
        /// sense: plausible, not tuned, and not yet the year the seasons
        /// (#53) will define.
        /// </summary>
        public const long DefaultMourningTicks = 365L * SimulationTime.TicksPerDay;

        /// <summary>
        /// Remarriage after a year, first cousins refused. The conservative
        /// reading of section 6, for a world with no culture to loosen it.
        /// </summary>
        public static readonly FamilyFormationSettings Default =
            new FamilyFormationSettings(DefaultMourningTicks, firstCousinsPermitted: false);

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
