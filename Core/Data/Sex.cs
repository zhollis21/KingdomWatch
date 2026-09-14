namespace KingdomWatch.Core.Data
{
    /// <summary>
    /// A person's sex. Fixed at birth, like race, and read by the two things
    /// that need it: partner eligibility (section 6 pairs widows with widowers
    /// and children with a mother and a father) and conception
    /// (<see cref="Lifecycle.Fertility"/>).
    /// </summary>
    /// <remarks>
    /// Values are explicit and must never be renumbered: they are written into
    /// saves.
    /// </remarks>
    public enum Sex : byte
    {
        /// <summary>Not a valid sex. Guards against a defaulted field.</summary>
        None = 0,

        Female = 1,

        Male = 2,
    }
}
