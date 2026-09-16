using System;
using KingdomWatch.Core.Data;

namespace KingdomWatch.Core.Traversal
{
    /// <summary>
    /// The world as a rectangle of <see cref="TerrainKind"/> cells, addressed
    /// by <see cref="WorldPosition"/>. What every route is found across.
    /// </summary>
    /// <remarks>
    /// A dense grid rather than an edge graph because section 12 asks for
    /// grid A* locally, and because the things that will sit on the world at
    /// M2 - trees, buildings, roads - are cell-shaped. One byte-sized enum per
    /// cell is the whole representation; the meaning of a kind lives in
    /// <see cref="TerrainRules"/>.
    ///
    /// Mutable, because section 12 needs edges to appear and disappear at run
    /// time - a bridge rewrites a river cell, the shape-terrain power rewrites
    /// anything. Nothing caches routes yet, so a <see cref="Set"/> has nothing
    /// to invalidate; the first cache brings its own invalidation with it.
    ///
    /// Positions run from (0, 0) to (Width - 1, Height - 1). Anything else is
    /// off the map and is refused rather than wrapped or clamped, because a
    /// route that silently wrapped would be wrong in a way no test would
    /// notice until something walked off one edge and appeared at the other.
    /// </remarks>
    public sealed class TerrainGrid
    {
        private static readonly bool[] DefinedKinds = EnumGuard.BuildMask(typeof(TerrainKind));

        private readonly TerrainKind[] _cells;

        public TerrainGrid(int width, int height, TerrainKind fill)
        {
            if (width <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width), width, "Width must be positive.");
            }

            if (height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(height), height, "Height must be positive.");
            }

            RequireKind(fill, nameof(fill));

            Width = width;
            Height = height;
            // Checked: a width and height that each fit in an int can still
            // multiply to something that does not, and an unchecked wrap would
            // size the array wrong rather than fail.
            _cells = new TerrainKind[checked(width * height)];
            Array.Fill(_cells, fill);
        }

        public int Width { get; }

        public int Height { get; }

        /// <summary>Cells in the grid: <see cref="Width"/> times <see cref="Height"/>.</summary>
        public int CellCount => _cells.Length;

        /// <summary>The kind at a position. Throws when the position is off the map.</summary>
        public TerrainKind this[WorldPosition position] => _cells[IndexOf(position)];

        public bool Contains(WorldPosition position) =>
            position.X >= 0 && position.X < Width && position.Y >= 0 && position.Y < Height;

        /// <summary>Rewrites a cell. Throws when off the map or the kind is undefined.</summary>
        public void Set(WorldPosition position, TerrainKind kind)
        {
            RequireKind(kind, nameof(kind));
            _cells[IndexOf(position)] = kind;
        }

        /// <summary>
        /// Row-major index of a position, for callers that keep per-cell
        /// arrays of their own alongside the grid. Throws when off the map.
        /// </summary>
        public int IndexOf(WorldPosition position)
        {
            if (!Contains(position))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(position), position, "Off the map: the grid is " + Width + " by " + Height + ".");
            }

            return (position.Y * Width) + position.X;
        }

        /// <summary>The position at a row-major index. The inverse of <see cref="IndexOf"/>.</summary>
        public WorldPosition PositionAt(int index)
        {
            if (index < 0 || index >= _cells.Length)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(index), index, "Not a cell index: the grid has " + _cells.Length + " cells.");
            }

            return new WorldPosition(index % Width, index / Width);
        }

        /// <summary>The kind at a row-major index, for the pathfinder's inner loop.</summary>
        internal TerrainKind KindAt(int index) => _cells[index];

        private static void RequireKind(TerrainKind kind, string parameterName)
        {
            if (!EnumGuard.IsDefined(DefinedKinds, (int)kind) || kind == TerrainKind.None)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName, kind, "Not a defined TerrainKind.");
            }
        }
    }
}
