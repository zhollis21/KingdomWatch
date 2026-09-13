namespace KingdomWatch.Core.Relationships
{
    /// <summary>
    /// How firmly a <see cref="Memory"/> is held - section 11's knowledge
    /// tiers, applied to what people and settlements remember.
    /// </summary>
    /// <remarks>
    /// Forgotten is not a tier: a forgotten memory is removed. The tiers are
    /// a one-way ladder - Recent becomes Old, and either becomes Promoted -
    /// and Promoted is permanent, which is the point of it: the Miracle of
    /// Oakshire outlives everyone who saw it.
    ///
    /// Values are explicit and will be persisted with the memory. Append.
    /// </remarks>
    public enum MemoryTier
    {
        /// <summary>Not a valid tier. Guards against a defaulted field.</summary>
        None = 0,

        /// <summary>Recently formed; held individually.</summary>
        Recent = 1,

        /// <summary>Past its recency; forgotten once nobody living witnessed it.</summary>
        Old = 2,

        /// <summary>Historically important. Never forgotten.</summary>
        Promoted = 3,
    }
}
