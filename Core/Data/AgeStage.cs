namespace KingdomWatch.Core.Data
{
    /// <summary>
    /// Where a person is in life. The five stages section 6 names, and the
    /// vocabulary that partner eligibility, dependent-child adoption and food
    /// priority are written in.
    /// </summary>
    /// <remarks>
    /// Coarse on purpose: a stage, not an age in days. Section 6 wants
    /// children to visibly exist without a childhood simulator, and the
    /// systems that read this - who may marry, who counts as a dependent, who
    /// eats first when food runs short - all branch on the stage and nothing
    /// finer. Moving people between stages is #22's; what a stage means to
    /// each system is that system's, and the meanings that exist so far are
    /// on <see cref="AgeStages"/>.
    ///
    /// Values are explicit and must never be renumbered or reordered: they are
    /// written into saves, and the ordering - younger is smaller - is what
    /// <see cref="AgeStages"/> relies on. Append new stages at the end.
    /// </remarks>
    public enum AgeStage : byte
    {
        /// <summary>Not a valid stage. Guards against a defaulted field.</summary>
        None = 0,

        /// <summary>Mostly home, invisible.</summary>
        Infant = 1,

        /// <summary>Play, chores, socialization.</summary>
        Child = 2,

        /// <summary>Apprenticeship and work assistance. Not yet an adult.</summary>
        Adolescent = 3,

        /// <summary>Full participation.</summary>
        Adult = 4,

        /// <summary>Reduced work, high skill, social weight.</summary>
        Elder = 5,
    }

    /// <summary>
    /// What the stages mean to the systems that exist so far.
    /// </summary>
    public static class AgeStages
    {
        /// <summary>
        /// Whether a person in this stage is grown: may form a partnership, may
        /// head a household, is not a dependent. Adult and Elder.
        /// </summary>
        public static bool IsAdult(AgeStage stage) => stage >= AgeStage.Adult;

        /// <summary>
        /// Whether a person in this stage is a dependent: someone who is
        /// adopted by kin when their household has no adult left. Everyone
        /// below Adult, adolescents included - section 6 puts full
        /// participation at Adult, and an adolescent left alone in a house is
        /// a child alone in a house.
        /// </summary>
        public static bool IsDependent(AgeStage stage) => stage != AgeStage.None && stage < AgeStage.Adult;
    }
}
