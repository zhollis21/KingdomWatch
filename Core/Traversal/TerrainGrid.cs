using System;
using System.Collections.Generic;
using KingdomWatch.Core.Data;

namespace KingdomWatch.Core.Traversal
{
    /// <summary>
    /// The world as a rectangle of <see cref="TerrainKind"/> cells, addressed
    /// by <see cref="WorldPosition"/>. What every route is found across.
    /// </summary>
    /// <remarks>
    /// A dense grid rather than an edge graph because section 12 asks for
    /// grid A* locally, and because the things that will sit on the world
    /// later - trees, buildings, roads - are cell-shaped. One byte-sized enum per
    /// cell is the whole representation; the meaning of a kind lives in
    /// <see cref="TerrainRules"/>. A second byte per cell holds the
    /// <see cref="RoadGrade"/> laid over it (#23): a road sits on its ground
    /// rather than replacing it, so the ground is still there when the road
    /// goes, and a grade is one rule whatever it crosses.
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
        private static readonly bool[] DefinedGrades = EnumGuard.BuildMask(typeof(RoadGrade));

        private readonly TerrainKind[] _cells;
        private readonly RoadGrade[] _roads;

        // The index of each recent rewrite, at its number modulo the capacity.
        private int[]? _changes;

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
            _roads = new RoadGrade[_cells.Length];
        }

        public int Width { get; }

        public int Height { get; }

        /// <summary>Cells in the grid: <see cref="Width"/> times <see cref="Height"/>.</summary>
        public int CellCount => _cells.Length;

        /// <summary>The kind at a position. Throws when the position is off the map.</summary>
        public TerrainKind this[WorldPosition position] => _cells[IndexOf(position)];

        /// <summary>The road over a position. Throws when the position is off the map.</summary>
        public RoadGrade RoadAt(WorldPosition position) => _roads[IndexOf(position)];

        public bool Contains(WorldPosition position) =>
            position.X >= 0 && position.X < Width && position.Y >= 0 && position.Y < Height;

        /// <summary>
        /// Rewrites the change log keeps: a reader further behind than this
        /// rereads the whole map (<see cref="TryChangesSince"/>).
        /// </summary>
        public const int ChangeLogCapacity = 4096;

        /// <summary>Rewrites a cell. Throws when off the map or the kind is undefined.</summary>
        public void Set(WorldPosition position, TerrainKind kind)
        {
            RequireKind(kind, nameof(kind));
            var index = IndexOf(position);
            _cells[index] = kind;
            Log(index);
        }

        /// <summary>
        /// Lays a road over a cell, or lifts it with <see cref="RoadGrade.None"/>.
        /// The ground under it is unchanged. A rewrite like <see cref="Set"/>:
        /// counted in <see cref="Rewrites"/> and kept in the change log.
        /// Throws when off the map or the grade is undefined.
        /// </summary>
        public void SetRoad(WorldPosition position, RoadGrade grade)
        {
            if (!EnumGuard.IsDefined(DefinedGrades, (int)grade))
            {
                throw new ArgumentOutOfRangeException(nameof(grade), grade, "Not a defined RoadGrade.");
            }

            var index = IndexOf(position);
            _roads[index] = grade;
            Log(index);
        }

        private void Log(int index)
        {
            // Allocated on the first rewrite: most grids, every test's
            // included, are never rewritten.
            _changes ??= new int[ChangeLogCapacity];
            _changes[Rewrites % ChangeLogCapacity] = index;
            Rewrites++;
        }

        /// <summary>
        /// Fills <paramref name="into"/> with the index of every cell
        /// rewritten since <see cref="Rewrites"/> was <paramref name="seen"/>,
        /// oldest first, so a reader redraws what changed rather than the
        /// whole map (#150). False, with the list empty, when more than
        /// <see cref="ChangeLogCapacity"/> rewrites have happened since: the
        /// oldest are gone, and the reader must reread the whole map.
        /// </summary>
        /// <remarks>
        /// A record of what happened to the map, not part of it: the cells
        /// say everything the world hash and saves need, so neither reads it.
        /// </remarks>
        /// <param name="seen">What <see cref="Rewrites"/> was when the reader last looked.</param>
        /// <param name="into">The list to fill. Cleared before use.</param>
        public bool TryChangesSince(long seen, List<int> into)
        {
            if (into is null)
            {
                throw new ArgumentNullException(nameof(into));
            }

            // Cleared before the guards, so a reused list never carries the
            // previous answer through a refusal.
            into.Clear();

            if (seen < 0L || seen > Rewrites)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(seen), seen, "Not a count the grid has passed: it is at " + Rewrites + ".");
            }

            if (Rewrites - seen > ChangeLogCapacity)
            {
                return false;
            }

            // Null only while nothing has been rewritten, when there is
            // nothing to add.
            if (_changes is int[] changes)
            {
                for (var n = seen; n < Rewrites; n++)
                {
                    into.Add(changes[n % ChangeLogCapacity]);
                }
            }

            return true;
        }

        /// <summary>
        /// How many times a cell has been rewritten, its ground or its road: unchanged means the map
        /// is too, so what was worked out from it still holds (the world hash
        /// keeps its terrain fold this way, #130).
        /// </summary>
        public long Rewrites { get; private set; }

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

        /// <summary>The road at a row-major index, for the pathfinder's inner loop.</summary>
        internal RoadGrade RoadAt(int index) => _roads[index];

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
