using System;

namespace KingdomWatch.Core.Traversal
{
    /// <summary>
    /// How something moves. A mover carries the set it has; a terrain kind
    /// carries the set it admits (<see cref="TerrainRule.Allowed"/>); a cell
    /// is passable when the two overlap.
    /// </summary>
    /// <remarks>
    /// This is the "optional transport requirement" section 12 attaches to
    /// every movement, and it is what keeps naval additive: water is a kind of
    /// terrain that admits <see cref="Boat"/>, not a special case in every
    /// mover. A band on foot and an army with boats query the same pathfinder
    /// with different flags and get different answers.
    ///
    /// Values are explicit bits and must never be renumbered: they are written
    /// into saves. Append new transports at the next free bit.
    /// </remarks>
    [Flags]
    public enum Transport
    {
        /// <summary>No way to move. As a mover, invalid; as a rule, impassable.</summary>
        None = 0,

        Foot = 1,

        /// <summary>Crosses <see cref="TerrainKind.DeepWater"/>. Nothing carries it until boats arrive (#45).</summary>
        Boat = 2,
    }
}
