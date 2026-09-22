namespace KingdomWatch.Core.Data
{
    /// <summary>
    /// A kind of bulk resource a <see cref="ResourceLedger"/> accounts for.
    /// </summary>
    /// <remarks>
    /// An enum rather than the data table section 9 sketches. The specific
    /// need for a data file - editing the resource set without a rebuild -
    /// does not exist yet, and per-resource data (weight for hauling) can
    /// live in a table keyed by this enum when a system first needs one.
    /// Every mutation funnels through the ledger, so swapping this for a
    /// table id later is mechanical. Recipes ARE data; see
    /// <see cref="Recipe"/> and <see cref="PrimitiveTier"/>. The economy
    /// ladder (docs/design/kingdom-watch-economy-ladder.md, #78) settles
    /// that nothing spoils; capacity is the only supply-side constraint.
    ///
    /// M1 ships the three gathered resources below (#12). Metal, and the
    /// Tools/Weapons/Armor the smithy forges from it, wait on #37 and #22;
    /// the economy ladder (#78) settles them as checked out from the
    /// settlement rather than personal property. Hide is not one of the
    /// seven the ladder settled on - considered and cut, not deferred.
    ///
    /// Values are explicit and must never be renumbered or reordered. They
    /// index the ledger's arrays and are written into saves and history, so
    /// changing one repoints every existing world's stock at the wrong thing.
    /// Append new kinds at the end.
    /// </remarks>
    public enum ResourceKind
    {
        /// <summary>Not a valid resource. Guards against a defaulted field.</summary>
        None = 0,

        Food = 1,
        Wood = 2,
        Stone = 3,
    }
}
