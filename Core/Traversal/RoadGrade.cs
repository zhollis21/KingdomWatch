namespace KingdomWatch.Core.Traversal
{
    /// <summary>
    /// What way runs over a cell, on top of its <see cref="TerrainKind"/>
    /// (#23). A road is laid over ground rather than replacing it, so a
    /// cell keeps what it is under the road, and a grade is one row of
    /// <see cref="TerrainRules"/> however many kinds of ground it crosses.
    /// </summary>
    /// <remarks>
    /// One byte per cell, beside the terrain in <see cref="TerrainGrid"/>.
    /// Values are explicit and must never be renumbered: they are written
    /// into saves and the world hash. Append new grades at the end - a
    /// paved road, a bridge over a small river (#35).
    /// </remarks>
    public enum RoadGrade : byte
    {
        /// <summary>No road: the ground's own rule alone.</summary>
        None = 0,

        /// <summary>A trodden lane of bare earth, laid by the town planner.</summary>
        Track = 1,
    }
}
