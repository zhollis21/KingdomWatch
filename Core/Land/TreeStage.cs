namespace KingdomWatch.Core.Land
{
    /// <summary>How far a tree has grown back since it was felled (#26).</summary>
    public enum TreeStage : byte
    {
        /// <summary>A tree that can be cut: never felled, or grown back.</summary>
        Standing = 0,

        /// <summary>Felled, for the first half of its regrowth.</summary>
        Stump = 1,

        /// <summary>Growing back, for the second half; not yet cut-able.</summary>
        Sapling = 2,
    }
}
