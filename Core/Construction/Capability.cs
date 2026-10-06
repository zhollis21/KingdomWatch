namespace KingdomWatch.Core.Construction
{
    /// <summary>
    /// The economy ladder's eight capabilities (section 7): what a skill tier
    /// is a tier of, and what a building's minimum skill names.
    /// </summary>
    /// <remarks>
    /// Declared here because the building gate (#100) needs to name one
    /// before the skills that hold them exist; #22 stores skills against
    /// these. Append-only, for the reason <see cref="Data.JobKind"/> is.
    /// </remarks>
    public enum Capability : byte
    {
        /// <summary>Not a capability. Guards against a defaulted field.</summary>
        None = 0,

        Foraging = 1,
        Hunting = 2,
        Woodcraft = 3,
        Construction = 4,
        Farming = 5,
        Masonry = 6,
        Metalworking = 7,
        Soldiering = 8,
    }

    /// <summary>
    /// The plan's section 9 tiers, Novice to Master. Ordered, so a gate is a
    /// comparison. Append-only.
    /// </summary>
    /// <remarks>
    /// Not an apprenticeship: <c>SkillTier.Apprentice</c> is a level of
    /// skill, and being someone's apprentice is a relationship (#22).
    /// </remarks>
    public enum SkillTier : byte
    {
        /// <summary>Not a tier. Guards against a defaulted field.</summary>
        None = 0,

        Novice = 1,
        Apprentice = 2,
        Journeyman = 3,
        Expert = 4,
        Master = 5,
    }
}
