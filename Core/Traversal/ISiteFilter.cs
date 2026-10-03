namespace KingdomWatch.Core.Traversal
{
    /// <summary>
    /// A per-cell say in whether a nearest-site search may land on a cell,
    /// beyond its terrain and whether the searcher knows it: a berry bush
    /// stripped this season is still scrub, and still known, but nothing to
    /// pick (#26).
    /// </summary>
    /// <remarks>
    /// Asked only of cells the terrain mask and the known map have already
    /// accepted, in the order the search settles them, so an answer may read
    /// the clock but must not change anything.
    /// </remarks>
    public interface ISiteFilter
    {
        /// <summary>Whether the cell at this row-major index counts as a site.</summary>
        bool Accepts(int cell);
    }
}
