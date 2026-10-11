namespace KingdomWatch.Core.Traversal
{
    /// <summary>
    /// What a cell found by <see cref="Pathfinder.WeighReachable"/> counts
    /// for, given what its route costs: a far bush worth less than a near one.
    /// </summary>
    /// <remarks>
    /// Asked once per counted cell, in the order the search settles them, so
    /// an answer must not change anything.
    /// </remarks>
    public interface ICostWeight
    {
        /// <summary>The weight of a cell whose route from the origin costs this much.</summary>
        long WeightOf(long cost);
    }
}
