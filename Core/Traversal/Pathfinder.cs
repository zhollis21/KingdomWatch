using System;
using System.Collections.Generic;
using KingdomWatch.Core.Data;

namespace KingdomWatch.Core.Traversal
{
    /// <summary>
    /// Finds the cheapest route across a <see cref="TerrainGrid"/> for a mover
    /// with a given <see cref="Transport"/>, under a <see cref="TerrainRules"/>
    /// table. Section 12's "grid A* locally".
    /// </summary>
    /// <remarks>
    /// **What comes out is a cost and a coarse route.** The cost is a long,
    /// summed without overflow for any grid that fits in memory (see
    /// <see cref="TerrainRule.MaxCost"/>), and is what a
    /// band or a task converts into travel time at its own speed (#54, #52);
    /// the route is the cell list section 4 wants kept in task state so the
    /// stepped simulation can place someone partway through a walk. Nothing
    /// here moves anything. A cost is directional - each step pays for the
    /// cell it enters - so the same route walked back is priced by
    /// <see cref="CostOfRoute"/> rather than assumed equal (#52).
    ///
    /// **Eight-way, 10 straight and 14 diagonal**, times the entered cell's
    /// <see cref="TerrainRule.Cost"/>. A diagonal step is allowed only when
    /// both cells it cuts between are passable to the mover - otherwise a
    /// river one cell wide, drawn diagonally, could be stepped across at its
    /// corners, and section 12 says small rivers are walls.
    ///
    /// **Deterministic.** A* is only as deterministic as its tie-breaking, so
    /// the open set is ordered by a total key - estimated total, then
    /// remaining estimate, then cell index - and neighbours are visited in a
    /// fixed order. Two queries with the same inputs produce the same route on
    /// every platform, which the cross-platform hash (#13) depends on.
    ///
    /// **Allocation-free after construction.** The per-cell arrays are sized
    /// to the grid once. A query resets only the cells the previous query
    /// touched, because clearing arrays the size of the world per route
    /// would dominate a query that visits a few hundred cells. The open set
    /// is a binary heap with decrease-key, so a cell is in it at most once
    /// and the heap never outgrows the grid.
    ///
    /// Section 12 also names a region graph and a per-tick request cap. Both
    /// are deferred to the issues with a caller for them (#23, #25), and
    /// they sit on top of this rather than replacing it.
    /// </remarks>
    public sealed class Pathfinder
    {
        private const int StraightCost = 10;

        // How many slots a terrain mask must have: one per value up to the last.
        private static readonly int DefinedTerrainKinds = EnumGuard.BuildMask(typeof(TerrainKind)).Length;
        private const int DiagonalCost = 14;

        // Orthogonal first, then diagonal; a fixed order is part of the
        // determinism contract. The step cost is the multiplier for that
        // direction.
        private static readonly (int Dx, int Dy, int StepCost)[] Neighbours =
        {
            (1, 0, StraightCost),
            (0, 1, StraightCost),
            (-1, 0, StraightCost),
            (0, -1, StraightCost),
            (1, 1, DiagonalCost),
            (-1, 1, DiagonalCost),
            (-1, -1, DiagonalCost),
            (1, -1, DiagonalCost),
        };

        private readonly TerrainGrid _grid;
        private readonly TerrainRules _rules;

        // Per-cell state. A cell is seen once it has a gScore, closed once
        // popped from the heap. Every cell marked seen is also recorded in
        // _touched so the next query can reset exactly those and no others.
        private readonly bool[] _seen;
        private readonly bool[] _closed;
        private readonly long[] _gScore;
        private readonly int[] _cameFrom;
        private readonly int[] _touched;
        private int _touchedCount;

        // Binary heap of open cells, plus each cell's slot in it (-1 when
        // not present) so a cheaper path found later can move it up.
        private readonly int[] _heap;
        private readonly int[] _heapSlot;
        private readonly long[] _fScore;
        private readonly long[] _hScore;
        private int _heapCount;

        public Pathfinder(TerrainGrid grid, TerrainRules rules)
        {
            _grid = grid ?? throw new ArgumentNullException(nameof(grid));
            _rules = rules ?? throw new ArgumentNullException(nameof(rules));

            var cells = grid.CellCount;
            _seen = new bool[cells];
            _closed = new bool[cells];
            _gScore = new long[cells];
            _cameFrom = new int[cells];
            _touched = new int[cells];
            _heap = new int[cells];
            _heapSlot = new int[cells];
            _fScore = new long[cells];
            _hScore = new long[cells];
            Array.Fill(_heapSlot, -1);
        }

        /// <summary>The grid routes are found on, for a caller choosing where to route to.</summary>
        public TerrainGrid Grid => _grid;

        /// <summary>
        /// What a mover pays to walk a given route, cell by cell: each step
        /// costs the cell it enters, times 10 straight or 14 diagonal, so a
        /// route walked backwards costs what its cells cost in that
        /// direction - three plains and a forest out, three plains and the
        /// start cell home. A route of one cell costs nothing.
        /// </summary>
        /// <exception cref="ArgumentException">
        /// The route is empty, or a step in it is not a walk: not to an
        /// adjacent cell, into or out of a cell the mover cannot stand on, or
        /// diagonally past one.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// A cell is off the map, or the mover has no defined transport.
        /// </exception>
        public long CostOfRoute(IReadOnlyList<WorldPosition> route, Transport mover)
        {
            if (route is null)
            {
                throw new ArgumentNullException(nameof(route));
            }

            TransportGuard.RequireMover(mover);

            if (route.Count == 0)
            {
                throw new ArgumentException("A route has at least the cell it starts on.", nameof(route));
            }

            var previous = route[0];

            if (!Passable(_grid.IndexOf(previous), mover))
            {
                throw new ArgumentException("The route starts on " + previous + ", which the mover cannot stand on.", nameof(route));
            }

            var cost = 0L;

            for (var i = 1; i < route.Count; i++)
            {
                var next = route[i];
                var nextIndex = _grid.IndexOf(next);
                var dx = next.X - previous.X;
                var dy = next.Y - previous.Y;

                if (dx == 0 && dy == 0 || Math.Abs(dx) > 1 || Math.Abs(dy) > 1)
                {
                    throw new ArgumentException(previous + " to " + next + " is not a step.", nameof(route));
                }

                if (!Passable(nextIndex, mover))
                {
                    throw new ArgumentException("The route enters " + next + ", which the mover cannot stand on.", nameof(route));
                }

                var diagonal = dx != 0 && dy != 0;

                if (diagonal
                    && (!Passable(_grid.IndexOf(new WorldPosition(next.X, previous.Y)), mover)
                        || !Passable(_grid.IndexOf(new WorldPosition(previous.X, next.Y)), mover)))
                {
                    throw new ArgumentException(previous + " to " + next + " cuts a corner the mover cannot pass.", nameof(route));
                }

                cost += (long)(diagonal ? DiagonalCost : StraightCost) * _rules[_grid.KindAt(nextIndex)].Cost;
                previous = next;
            }

            return cost;
        }

        /// <summary>Whether a mover with these transports may stand on this cell.</summary>
        public bool IsPassable(WorldPosition position, Transport mover)
        {
            TransportGuard.RequireMover(mover);
            return _rules.IsPassable(_grid[position], mover);
        }

        /// <summary>
        /// Finds the cheapest route from one cell to another. On success the
        /// route is written into <paramref name="route"/>, <paramref name="from"/>
        /// first and <paramref name="to"/> last, and <paramref name="cost"/> is
        /// its total in traversal units. When either end is impassable to the
        /// mover, or nothing connects them, returns false with the route
        /// cleared and the cost zero.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Either position is off the map, or the mover has no defined transport.
        /// </exception>
        public bool TryFindRoute(
            WorldPosition from, WorldPosition to, Transport mover, List<WorldPosition> route, out long cost)
        {
            if (route is null)
            {
                throw new ArgumentNullException(nameof(route));
            }

            TransportGuard.RequireMover(mover);
            var start = _grid.IndexOf(from);
            var goal = _grid.IndexOf(to);

            route.Clear();
            cost = 0;

            if (!Passable(start, mover) || !Passable(goal, mover))
            {
                return false;
            }

            BeginSearch();
            Open(start, 0, start, Heuristic(from, to));

            while (_heapCount > 0)
            {
                var current = PopCheapest();

                if (current == goal)
                {
                    cost = _gScore[goal];
                    WriteRoute(start, goal, route);
                    return true;
                }

                Expand(current, mover, to, from, int.MaxValue);
            }

            return false;
        }

        /// <summary>
        /// Finds the cheapest cell to reach, among those whose terrain the
        /// mask accepts, within a box <paramref name="radius"/> cells around
        /// <paramref name="from"/>. On success the route from
        /// <paramref name="from"/> to that cell is written into
        /// <paramref name="route"/> and <paramref name="cost"/> is its total;
        /// the origin itself counts, as a route of one cell at no cost. When
        /// the origin is impassable, or nothing accepted is reachable inside
        /// the box, returns false with the route cleared and the cost zero.
        /// </summary>
        /// <remarks>
        /// One search, however many candidates: Dijkstra from the origin
        /// settles cells in cost order, so the first settled cell the mask
        /// accepts is the cheapest one there is, and the search never runs
        /// past it. A ring scan with a route query per candidate would run a
        /// full failed search for every matching cell nothing connects to.
        /// Ties fall to the lower cell index, as every ordering here does.
        /// </remarks>
        /// <param name="acceptable">
        /// Indexed by <see cref="TerrainKind"/>; true for a kind that counts as
        /// found. Must cover every defined kind.
        /// </param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// The origin is off the map, the radius is negative, or the mover has
        /// no defined transport.
        /// </exception>
        public bool TryFindNearest(
            WorldPosition from,
            Transport mover,
            ReadOnlySpan<bool> acceptable,
            int radius,
            List<WorldPosition> route,
            out long cost) =>
            TryFindNearest(from, mover, acceptable, default, radius, route, out cost);

        /// <summary>
        /// <see cref="TryFindNearest(WorldPosition, Transport, ReadOnlySpan{bool}, int, List{WorldPosition}, out long)"/>,
        /// restricted to cells the searcher knows: a site only counts as found
        /// when its terrain is acceptable <i>and</i> <paramref name="known"/>
        /// says the searcher has seen it.
        /// </summary>
        /// <remarks>
        /// Section 12's bounded map knowledge, at the one point that decides
        /// what a place-picking search may land on. The route is still allowed
        /// to run through unknown cells - it is the destination that must be
        /// known, not the walking - because a searcher that could not path
        /// across unseen ground would be unable to reach anywhere it had only
        /// glimpsed the far side of.
        /// </remarks>
        /// <param name="known">
        /// Indexed by grid cell index; true for a cell the searcher knows. An
        /// empty span means omniscient, which is what the overload without it
        /// passes.
        /// </param>
        public bool TryFindNearest(
            WorldPosition from,
            Transport mover,
            ReadOnlySpan<bool> acceptable,
            ReadOnlySpan<bool> known,
            int radius,
            List<WorldPosition> route,
            out long cost)
        {
            if (route is null)
            {
                throw new ArgumentNullException(nameof(route));
            }

            if (!known.IsEmpty && known.Length < _grid.CellCount)
            {
                throw new ArgumentException(
                    "The known mask covers " + known.Length + " cells; the grid has " + _grid.CellCount + ".",
                    nameof(known));
            }

            if (acceptable.Length < DefinedTerrainKinds)
            {
                throw new ArgumentException(
                    "The mask covers " + acceptable.Length + " kinds; TerrainKind has " + DefinedTerrainKinds + ".",
                    nameof(acceptable));
            }

            if (radius < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(radius), radius, "A radius is not negative.");
            }

            TransportGuard.RequireMover(mover);
            var start = _grid.IndexOf(from);

            route.Clear();
            cost = 0;

            if (!Passable(start, mover))
            {
                return false;
            }

            BeginSearch();
            Open(start, 0, start, 0);

            while (_heapCount > 0)
            {
                var current = PopCheapest();

                if (acceptable[(int)_grid.KindAt(current)] && (known.IsEmpty || known[current]))
                {
                    cost = _gScore[current];
                    WriteRoute(start, current, route);
                    return true;
                }

                Expand(current, mover, null, from, radius);
            }

            return false;
        }

        // Closes a cell and opens its neighbours: eight-way, no cutting
        // corners, each priced by the cell it enters, with the heuristic
        // toward the goal when there is one - without, the search is
        // Dijkstra - and nothing opened outside the box.
        private void Expand(int current, Transport mover, WorldPosition? goal, WorldPosition origin, int radius)
        {
            _closed[current] = true;
            var position = _grid.PositionAt(current);

            for (var i = 0; i < Neighbours.Length; i++)
            {
                var (dx, dy, stepCost) = Neighbours[i];
                var next = new WorldPosition(position.X + dx, position.Y + dy);

                if (!_grid.Contains(next)
                    || Math.Abs(next.X - origin.X) > radius
                    || Math.Abs(next.Y - origin.Y) > radius)
                {
                    continue;
                }

                var nextIndex = _grid.IndexOf(next);

                if (_closed[nextIndex] || !Passable(nextIndex, mover))
                {
                    continue;
                }

                // No cutting corners: both cells a diagonal passes between
                // must be open, or a one-cell river is crossable at a bend.
                if (stepCost == DiagonalCost
                    && (!Passable(_grid.IndexOf(new WorldPosition(position.X + dx, position.Y)), mover)
                        || !Passable(_grid.IndexOf(new WorldPosition(position.X, position.Y + dy)), mover)))
                {
                    continue;
                }

                // Long, not int: a step is at most 14 * MaxCost and a route at
                // most CellCount steps, which an int cannot promise to hold.
                var tentative = _gScore[current] + ((long)stepCost * _rules[_grid.KindAt(nextIndex)].Cost);

                if (_seen[nextIndex] && tentative >= _gScore[nextIndex])
                {
                    continue;
                }

                Open(nextIndex, tentative, current, goal is WorldPosition target ? Heuristic(next, target) : 0L);
            }
        }


        // The mask directly rather than TerrainRule.Admits: the mover was
        // validated once at entry, and this runs for every neighbour of every
        // cell a query expands.
        private bool Passable(int index, Transport mover) => (_rules[_grid.KindAt(index)].Allowed & mover) != 0;

        /// <summary>
        /// Octile distance scaled by the cheapest cell in the table: the
        /// least any route could cost, so A* never overestimates.
        /// </summary>
        private long Heuristic(WorldPosition a, WorldPosition b)
        {
            var dx = Math.Abs(a.X - b.X);
            var dy = Math.Abs(a.Y - b.Y);
            var diagonal = Math.Min(dx, dy);
            var straight = Math.Max(dx, dy) - diagonal;

            return (((long)diagonal * DiagonalCost) + ((long)straight * StraightCost)) * _rules.CheapestCost;
        }

        private void BeginSearch()
        {
            for (var i = 0; i < _touchedCount; i++)
            {
                var cell = _touched[i];
                _seen[cell] = false;
                _closed[cell] = false;
                _heapSlot[cell] = -1;
            }

            _touchedCount = 0;
            _heapCount = 0;
        }

        private void Open(int cell, long gScore, int cameFrom, long heuristic)
        {
            _gScore[cell] = gScore;
            _cameFrom[cell] = cameFrom;
            _hScore[cell] = heuristic;
            _fScore[cell] = gScore + heuristic;

            if (_seen[cell])
            {
                // Already open with a worse path: move it up the heap.
                SiftUp(_heapSlot[cell]);
                return;
            }

            _seen[cell] = true;
            _touched[_touchedCount] = cell;
            _touchedCount++;
            _heap[_heapCount] = cell;
            _heapSlot[cell] = _heapCount;
            _heapCount++;
            SiftUp(_heapCount - 1);
        }

        private int PopCheapest()
        {
            var cheapest = _heap[0];
            _heapSlot[cheapest] = -1;
            _heapCount--;

            if (_heapCount > 0)
            {
                _heap[0] = _heap[_heapCount];
                _heapSlot[_heap[0]] = 0;
                SiftDown(0);
            }

            return cheapest;
        }

        /// <summary>
        /// The heap's total order: estimated total, then what remains, then
        /// the cell itself. No two open cells compare equal, so the order the
        /// heap pops them in is a property of the inputs alone.
        /// </summary>
        private bool Before(int a, int b)
        {
            if (_fScore[a] != _fScore[b])
            {
                return _fScore[a] < _fScore[b];
            }

            if (_hScore[a] != _hScore[b])
            {
                return _hScore[a] < _hScore[b];
            }

            return a < b;
        }

        private void SiftUp(int slot)
        {
            while (slot > 0)
            {
                var parent = (slot - 1) / 2;

                if (!Before(_heap[slot], _heap[parent]))
                {
                    return;
                }

                Swap(slot, parent);
                slot = parent;
            }
        }

        private void SiftDown(int slot)
        {
            while (true)
            {
                var left = (2 * slot) + 1;
                var right = left + 1;
                var smallest = slot;

                if (left < _heapCount && Before(_heap[left], _heap[smallest]))
                {
                    smallest = left;
                }

                if (right < _heapCount && Before(_heap[right], _heap[smallest]))
                {
                    smallest = right;
                }

                if (smallest == slot)
                {
                    return;
                }

                Swap(slot, smallest);
                slot = smallest;
            }
        }

        private void Swap(int a, int b)
        {
            var cellA = _heap[a];
            var cellB = _heap[b];
            _heap[a] = cellB;
            _heap[b] = cellA;
            _heapSlot[cellB] = a;
            _heapSlot[cellA] = b;
        }

        private void WriteRoute(int start, int goal, List<WorldPosition> route)
        {
            for (var cell = goal; cell != start; cell = _cameFrom[cell])
            {
                route.Add(_grid.PositionAt(cell));
            }

            route.Add(_grid.PositionAt(start));
            route.Reverse();
        }
    }
}
