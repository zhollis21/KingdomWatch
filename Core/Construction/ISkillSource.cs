using System;
using KingdomWatch.Core.Data;

namespace KingdomWatch.Core.Construction
{
    /// <summary>
    /// The best skill a community has in a capability: what the first of the
    /// five build conditions reads (#100). A seam because skills are #22's,
    /// which plugs in here.
    /// </summary>
    public interface ISkillSource
    {
        /// <summary>The highest tier anyone in the community holds, or <see cref="SkillTier.None"/>.</summary>
        SkillTier BestIn(ICommunity community, Capability capability);
    }

    /// <summary>
    /// Everyone is a Novice at everything: true of a world with no skill
    /// system, where nobody has learned anything yet. Buildings that need
    /// Apprentice or better never qualify until #22 replaces this.
    /// </summary>
    public sealed class NoviceSkills : ISkillSource
    {
        private static readonly bool[] DefinedCapabilities = EnumGuard.BuildMask(typeof(Capability));

        /// <summary>
        /// Novice. Refuses a missing community, and <see cref="Capability.None"/>
        /// or an undefined value, so a corrupt gate cannot quietly pass.
        /// </summary>
        public SkillTier BestIn(ICommunity community, Capability capability)
        {
            if (community is null)
            {
                throw new ArgumentNullException(nameof(community));
            }

            if (capability == Capability.None || !EnumGuard.IsDefined(DefinedCapabilities, (int)capability))
            {
                throw new ArgumentOutOfRangeException(nameof(capability), capability, "Not a defined Capability, or None.");
            }

            return SkillTier.Novice;
        }
    }
}
