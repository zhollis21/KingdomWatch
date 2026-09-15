namespace KingdomWatch.Core.Traversal
{
    /// <summary>
    /// What a cell of the world is, as far as moving through it goes.
    /// </summary>
    /// <remarks>
    /// Only the distinctions traversal needs are here - what it costs to cross
    /// a cell and what it takes to be allowed to. Fertility, resources and
    /// biomes are separate data for the systems that read them (#54, #33);
    /// folding them into this enum would make every terrain kind a cross
    /// product of movement and yield.
    ///
    /// <see cref="SmallRiver"/> and <see cref="DeepWater"/> are the two water
    /// kinds section 12 names: a small river is a wall until a bridge rewrites
    /// the cell (#35), deep water needs a boat (#45). Both are expressed
    /// through <see cref="TerrainRules"/>, never by code that asks "is this
    /// water?".
    ///
    /// Values are explicit and must never be renumbered: they are written into
    /// saves. Append new kinds at the end.
    /// </remarks>
    public enum TerrainKind
    {
        /// <summary>Not a valid kind. Guards against a defaulted cell.</summary>
        None = 0,

        Plains = 1,
        Forest = 2,
        Hills = 3,

        /// <summary>Impassable to everyone until bridged (section 12).</summary>
        SmallRiver = 4,

        /// <summary>Large rivers and open water. Boats only (section 12).</summary>
        DeepWater = 5,
    }
}
