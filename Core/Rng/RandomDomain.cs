namespace KingdomWatch.Core.Rng
{
    /// <summary>
    /// The subsystem a random draw belongs to. Part of every key, so a draw in
    /// one domain can never collide with a draw in another that happens to
    /// share entity ids.
    /// </summary>
    /// <remarks>
    /// Values are explicit and must never be renumbered or reordered.
    /// Renumbering a domain changes every roll derived from it, which rewrites
    /// the entire history of every existing world. Append new domains at the
    /// end.
    /// </remarks>
    public enum RandomDomain
    {
        /// <summary>Not a valid domain. Guards against a defaulted field.</summary>
        None = 0,

        Combat = 1,
        Conception = 2,
        Social = 3,
    }
}
