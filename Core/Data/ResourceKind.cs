namespace KingdomWatch.Core.Data
{
    /// <summary>
    /// A kind of bulk resource a <see cref="ResourceLedger"/> accounts for.
    /// </summary>
    /// <remarks>
    /// An enum rather than the data table section 9 sketches. The specific
    /// need for a data file - editing the resource set without a rebuild -
    /// does not exist yet, and per-resource data (spoilage for seasons,
    /// weight for hauling) can live in a table keyed by this enum when a
    /// system first needs one. Every mutation funnels through the ledger, so
    /// swapping this for a table id later is mechanical. Recipes ARE data;
    /// see <see cref="Recipe"/> and <see cref="PrimitiveTier"/>.
    ///
    /// M1 ships the three gathered resources below (#12). Stone tools and
    /// hide clothing are deliberately absent: tools may turn out to be
    /// personal property (section 6) rather than ledger stock, and hides have
    /// no source until hunting exists. The expansion toward ten resources is
    /// #37.
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
