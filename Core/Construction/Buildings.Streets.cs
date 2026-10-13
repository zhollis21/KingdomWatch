using System;
using System.Collections.Generic;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Traversal;
using KingdomWatch.Core.Work;

namespace KingdomWatch.Core.Construction
{
    /// <summary>
    /// A village's planned streets, and the links that close its loops
    /// (#171).
    /// </summary>
    /// <remarks>
    /// Left to itself, every lane joins the nearest road and nothing ever
    /// joins two branches, so a village's roads grow as a tree off the square:
    /// two doors a few cells apart can be far apart by road. Three rules
    /// close it, all paid for by the building that needs them, as its own
    /// lane is.
    ///
    /// **Planned streets.** Every settlement has a street plan: a ring
    /// <see cref="StreetRing"/> cells out from its centre, and four spokes
    /// along its axes from the yard out to <see cref="StreetReach"/>. It is
    /// a function of the centre alone, so it has no state to save. Nothing is
    /// built on a planned cell of its own settlement, a door near one scores
    /// as near the network, and a lane may end on one. A lane that does pays
    /// for the street from there along the plan to the network, up to
    /// <see cref="StreetSection"/> cells of it, so the plan is laid outward a
    /// section at a time as the buildings along it go up. Where the plan is
    /// blocked (rocks, water, a lane that has not been laid yet), the lane
    /// joins the network as before. A street is never laid beside a road
    /// that is off the plan, so no two roads run side by side.
    ///
    /// **Links.** Once the lane of a building with a door is found, it may
    /// add short new tracks between parts of the network that are close on
    /// the ground but far apart by road, the lane itself included: one from
    /// its door
    /// (<see cref="DoorLinkReach"/>), then up to <see cref="ShortcutLinks"/>
    /// from anywhere in the network within <see cref="ShortcutArea"/> of it
    /// (<see cref="ShortcutReach"/>). Each is laid with the lane, cleared and
    /// priced with it, so a link is a part of <see cref="Building.Lane"/> like
    /// any other.
    ///
    /// The thresholds are placeholders in the <see cref="PrimitiveTier"/>
    /// sense, chosen by growing seeds 1-3 for 40 years.
    /// </remarks>
    public sealed partial class Buildings
    {
        /// <summary>How far from a settlement's centre, each way, its ring street runs.</summary>
        public const int StreetRing = 11;

        /// <summary>How far from a settlement's centre, each way, its spokes run.</summary>
        public const int StreetReach = 20;

        /// <summary>
        /// The most street, in cells, one building lays to reach the network:
        /// a plan is laid outward from the network a section at a time, never
        /// by a building out at the far end of a spoke paying for all of it.
        /// </summary>
        public const int StreetSection = 10;

        /// <summary>The longest a door's link may span, in cells as an ox walks (Manhattan).</summary>
        public const int DoorLinkReach = 10;

        /// <summary>How much shorter by road, in cells, a door's link must make the way it joins.</summary>
        public const int DoorLinkSaving = 8;

        /// <summary>The longest a shortcut may span, in cells as an ox walks (Manhattan).</summary>
        public const int ShortcutReach = 12;

        /// <summary>The most shortcuts one building adds.</summary>
        public const int ShortcutLinks = 2;

        /// <summary>How far from a building's middle, each way, its shortcuts may start.</summary>
        public const int ShortcutArea = 12;

        // The shortest a link may span: one cell of new track between two
        // that are already joined is no link at all.
        private const int LinkFloor = 2;

        // A shortcut joins two network cells m apart only when the way round
        // by road is at least (ShortcutRatioTenths * m / 10) + ShortcutSlack.
        private const int ShortcutRatioTenths = 18;
        private const int ShortcutSlack = 4;

        // A link whose track runs further than this over its span is a detour
        // of its own: steps * 5 <= span * 7 + 10, that is, 1.4 times the span
        // and two cells.
        private const int LinkDetourFifths = 7;
        private const int LinkDetourSlack = 10;

        // Road distances are counted out to here and no further: every
        // threshold above is at most this, so anything beyond is far enough.
        private static readonly int RoadDepthCap = Math.Max(
            DoorLinkReach + DoorLinkSaving, ((ShortcutRatioTenths * ShortcutReach) + 9) / 10 + ShortcutSlack);

        private static readonly int DepthWindow = (2 * RoadDepthCap) + 1;

        // Network cells this building's links are searched from, and the cells
        // of _lane, looked up as the lane grows. Reused, never iterated for
        // order: _seeds is filled in cell index order.
        private readonly List<int> _seeds = new List<int>();
        private readonly HashSet<int> _laneCells = new HashSet<int>();
        private readonly List<WorldPosition> _linkRoute = new List<WorldPosition>();
        private readonly List<WorldPosition> _bestLink = new List<WorldPosition>();

        // Road distance from the seed being tried, in a window around it,
        // stamped so a window is reset by moving the stamp.
        private readonly int[] _depth = new int[DepthWindow * DepthWindow];
        private readonly int[] _depthStamp = new int[DepthWindow * DepthWindow];
        private readonly int[] _queue = new int[DepthWindow * DepthWindow];
        private int _stamp;
        private WorldPosition _depthOrigin;

        // Open planned cells within StreetSection of the network along the
        // plan, each with its depth: the ones a site's door may score as
        // near. Reused; looked up, never iterated.
        private readonly Dictionary<int, int> _streetDepth = new Dictionary<int, int>();
        private readonly List<int> _streetQueue = new List<int>();

        private readonly StreetGate _streetGate;
        private readonly RealNetwork _realNetwork;
        private readonly LinkTarget _linkTarget;
        private readonly LinkGate _linkGate;

        /// <summary>
        /// Whether a cell lies on the street plan of the settlement centred
        /// at <paramref name="centre"/>: its ring, or one of its spokes out of
        /// the yard. The yard itself is the square, not a street.
        /// </summary>
        public static bool IsStreet(WorldPosition at, WorldPosition centre)
        {
            var dx = Math.Abs(at.X - centre.X);
            var dy = Math.Abs(at.Y - centre.Y);
            var away = Math.Max(dx, dy);

            if (away <= CampYardRadius)
            {
                return false;
            }

            return away == StreetRing || ((dx == 0 || dy == 0) && away <= StreetReach);
        }

        // A planned cell of this settlement a lane could end on: on the map,
        // ground a lane may run on, under nothing, and nobody's lane yet.
        private bool IsOpenStreet(int cell, WorldPosition centre)
        {
            var at = _grid.PositionAt(cell);

            return IsStreet(at, centre)
                && _grid.RoadAt(cell) == RoadGrade.None
                && IsLaneGround(_grid.KindAt(cell))
                && !_onCell.ContainsKey(cell)
                && !_underRoof.Contains(cell)
                && !_laneOf.ContainsKey(cell)
                && !InOtherYard(at, centre);
        }

        // The open planned cells a lane ending on could lay the street from
        // to the network, into _streetDepth: breadth first along the plan
        // from the open cells beside the network, StreetSection deep. A
        // door beside a stretch of plan further out than that scores as far
        // from the roads as it is, not as on them (#172).
        private void CollectStreets(WorldPosition centre)
        {
            _streetDepth.Clear();
            _streetQueue.Clear();

            for (var y = Math.Max(0, centre.Y - StreetReach); y <= Math.Min(_grid.Height - 1, centre.Y + StreetReach); y++)
            {
                for (var x = Math.Max(0, centre.X - StreetReach); x <= Math.Min(_grid.Width - 1, centre.X + StreetReach); x++)
                {
                    var at = new WorldPosition(x, y);
                    var cell = _grid.IndexOf(at);

                    if (IsOpenStreet(cell, centre) && BesideRealNetwork(at, centre))
                    {
                        _streetDepth[cell] = 1;
                        _streetQueue.Add(cell);
                    }
                }
            }

            for (var next = 0; next < _streetQueue.Count; next++)
            {
                var cell = _streetQueue[next];
                var depth = _streetDepth[cell];

                if (depth == StreetSection)
                {
                    continue;
                }

                var at = _grid.PositionAt(cell);

                for (var step = 0; step < 4; step++)
                {
                    var beside = Step(at, step);

                    if (!_grid.Contains(beside))
                    {
                        continue;
                    }

                    var index = _grid.IndexOf(beside);

                    if (!_streetDepth.ContainsKey(index) && IsOpenStreet(index, centre))
                    {
                        _streetDepth[index] = depth + 1;
                        _streetQueue.Add(index);
                    }
                }
            }
        }

        private bool BesideRealNetwork(WorldPosition at, WorldPosition centre)
        {
            for (var step = 0; step < 4; step++)
            {
                var beside = Step(at, step);

                if (_grid.Contains(beside) && IsRealNetwork(_grid.IndexOf(beside), centre))
                {
                    return true;
                }
            }

            return false;
        }

        // A lane that ended on a planned street not laid yet: the street from
        // there along the plan to the network (a road, the yard, or a lane
        // waiting to be laid), appended to _lane with the cell it started on.
        // False, with _lane as it was, when the plan does not run through
        // to the network within StreetSection cells.
        private bool TryStreet(WorldPosition from, WorldPosition centre, ref long cost)
        {
            _streetGate.Centre = centre;
            _realNetwork.Centre = centre;

            if (!_pathfinder.TryFindNearest(
                    from, Jobs.Mover, _anyKind, default, _realNetwork, _streetGate, true, StreetSection, long.MaxValue, _laneRoute, out var streetCost))
            {
                return false;
            }

            // The cell it started on and the street after it, without the
            // network cell it ends on.
            if (_laneRoute.Count - 1 > StreetSection)
            {
                return false;
            }

            for (var i = 0; i < _laneRoute.Count - 1; i++)
            {
                _lane.Add(_laneRoute[i]);
            }

            cost += streetCost;
            return true;
        }

        // The links a building approved with _lane adds to it: one from its
        // door, then shortcuts nearby. Each is appended to _lane.
        private void AddLinks(BuildingKind kind, WorldPosition anchor, WorldPosition centre)
        {
            // Links are for the doors people walk between. A field's way in
            // is any road along its edge, and it lies on the outskirts.
            if (!HasDoor(kind))
            {
                return;
            }

            var spec = BuildingTable.Of(kind);
            _laneGate.Begin(spec, anchor, centre);
            _linkTarget.Centre = centre;
            _linkGate.Centre = centre;

            _laneCells.Clear();

            for (var i = 0; i < _lane.Count; i++)
            {
                _laneCells.Add(_grid.IndexOf(_lane[i]));
            }

            var door = DoorOf(kind, anchor);

            // A door whose lane was nothing (it stands on a road) is already
            // the network; one with a lane starts it.
            if (_grid.Contains(door) && IsJoined(_grid.IndexOf(door), centre))
            {
                _seeds.Clear();
                _seeds.Add(_grid.IndexOf(door));

                if (TryBestLink(centre, DoorLinkReach, doorLink: true))
                {
                    LayLinkIntoLane();
                }
            }

            var middle = MiddleOf(spec, anchor);

            for (var link = 0; link < ShortcutLinks; link++)
            {
                CollectSeeds(middle, centre);

                if (!TryBestLink(centre, ShortcutReach, doorLink: false))
                {
                    return;
                }

                LayLinkIntoLane();
            }
        }

        private void LayLinkIntoLane()
        {
            // Both ends are the network already; only the track between is new.
            for (var i = 1; i < _bestLink.Count - 1; i++)
            {
                var at = _bestLink[i];

                if (_laneCells.Add(_grid.IndexOf(at)))
                {
                    _lane.Add(at);
                }
            }
        }

        // Every network cell within ShortcutArea of a building's middle, in
        // cell index order.
        private void CollectSeeds(WorldPosition middle, WorldPosition centre)
        {
            _seeds.Clear();

            for (var y = Math.Max(0, middle.Y - ShortcutArea); y <= Math.Min(_grid.Height - 1, middle.Y + ShortcutArea); y++)
            {
                for (var x = Math.Max(0, middle.X - ShortcutArea); x <= Math.Min(_grid.Width - 1, middle.X + ShortcutArea); x++)
                {
                    var cell = _grid.IndexOf(new WorldPosition(x, y));

                    if (IsJoined(cell, centre))
                    {
                        _seeds.Add(cell);
                    }
                }
            }
        }

        // Of the links from every seed, the one that saves the most by road,
        // into _bestLink: ties to the lower seed, then to the lower far end.
        private bool TryBestLink(WorldPosition centre, int reach, bool doorLink)
        {
            var bestSaving = int.MinValue;
            var bestSeed = -1;
            var bestEnd = -1;
            _bestLink.Clear();

            for (var s = 0; s < _seeds.Count; s++)
            {
                var seed = _seeds[s];
                var from = _grid.PositionAt(seed);

                // Most network cells have no open ground beside them for a
                // link to leave by: skip them before the searches.
                if (!CanLeave(from, centre))
                {
                    continue;
                }

                CountRoadDepths(from, centre);

                _linkTarget.Begin(from, reach, doorLink);

                // The route steps along edges, so it is never shorter than
                // its span: no far end saving more than the best so far
                // (which a later seed only ties at best), no search.
                if (_linkTarget.BestSaving() <= bestSaving)
                {
                    continue;
                }

                if (!_pathfinder.TryFindNearest(
                        from, Jobs.Mover, _anyKind, default, _linkTarget, _linkGate, true, reach, long.MaxValue, _linkRoute, out _))
                {
                    continue;
                }

                var end = _linkRoute[_linkRoute.Count - 1];
                var span = Manhattan(from, end);
                var steps = _linkRoute.Count - 1;

                if (steps * 5 > (span * LinkDetourFifths) + LinkDetourSlack)
                {
                    continue;
                }

                var saving = RoadDepth(end) - steps;
                var endCell = _grid.IndexOf(end);

                if (saving > bestSaving || (saving == bestSaving && (seed < bestSeed || (seed == bestSeed && endCell < bestEnd))))
                {
                    bestSaving = saving;
                    bestSeed = seed;
                    bestEnd = endCell;
                    _bestLink.Clear();
                    _bestLink.AddRange(_linkRoute);
                }
            }

            return bestSeed >= 0;
        }

        // Whether a link could take its first step from this network cell:
        // its first cell is never joined (a link spans at least LinkFloor),
        // and must pass the parts of LinkGate that do not depend on where
        // the link left from.
        private bool CanLeave(WorldPosition from, WorldPosition centre)
        {
            for (var step = 0; step < 4; step++)
            {
                var next = Step(from, step);

                if (!_grid.Contains(next))
                {
                    continue;
                }

                var cell = _grid.IndexOf(next);

                if (!IsJoined(cell, centre)
                    && _laneGate.Accepts(cell)
                    && !_laneGate.Flanks(next)
                    && !ClosesBlock(next, centre, links: true))
                {
                    return true;
                }
            }

            return false;
        }

        // Whether a cell is the network a link joins: a road, this
        // settlement's yard, a lane waiting to be laid, or the lane being
        // approved.
        private bool IsJoined(int cell, WorldPosition centre) =>
            _grid.RoadAt(cell) != RoadGrade.None
            || _laneOf.ContainsKey(cell)
            || _laneCells.Contains(cell)
            || WithinYard(_grid.PositionAt(cell), centre);

        // Whether new road here would make a two by two block of road with
        // three cells already joined (or, for a street, already the real
        // network) round one of its corners, outside the yard: a road two
        // wide where a link or street turns beside another (#172).
        private bool ClosesBlock(WorldPosition at, WorldPosition centre, bool links)
        {
            for (var corner = 0; corner < 4; corner++)
            {
                var sx = corner < 2 ? 1 : -1;
                var sy = corner % 2 == 0 ? 1 : -1;

                if (IsRoadOutsideYard(new WorldPosition(at.X + sx, at.Y), centre, links)
                    && IsRoadOutsideYard(new WorldPosition(at.X, at.Y + sy), centre, links)
                    && IsRoadOutsideYard(new WorldPosition(at.X + sx, at.Y + sy), centre, links))
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsRoadOutsideYard(WorldPosition at, WorldPosition centre, bool links)
        {
            if (!_grid.Contains(at) || WithinYard(at, centre))
            {
                return false;
            }

            var cell = _grid.IndexOf(at);
            return links ? IsJoined(cell, centre) : IsRealNetwork(cell, centre);
        }

        // Breadth first along the network from one cell, out to RoadDepthCap,
        // into the depth window centred on it.
        private void CountRoadDepths(WorldPosition from, WorldPosition centre)
        {
            _stamp++;
            _depthOrigin = from;
            var head = 0;
            var tail = 0;
            var start = WindowIndex(from);
            _depthStamp[start] = _stamp;
            _depth[start] = 0;
            _queue[tail++] = _grid.IndexOf(from);

            while (head < tail)
            {
                var cell = _queue[head++];
                var at = _grid.PositionAt(cell);
                var depth = _depth[WindowIndex(at)];

                if (depth == RoadDepthCap)
                {
                    continue;
                }

                for (var step = 0; step < 4; step++)
                {
                    var next = Step(at, step);

                    if (!_grid.Contains(next) || Manhattan(next, from) > RoadDepthCap)
                    {
                        continue;
                    }

                    var window = WindowIndex(next);
                    var nextCell = _grid.IndexOf(next);

                    if (_depthStamp[window] == _stamp || !IsJoined(nextCell, centre))
                    {
                        continue;
                    }

                    _depthStamp[window] = _stamp;
                    _depth[window] = depth + 1;
                    _queue[tail++] = nextCell;
                }
            }
        }

        // The road distance from the last CountRoadDepths origin, or one past
        // the cap when the network does not reach it within the cap.
        private int RoadDepth(WorldPosition at)
        {
            if (Math.Abs(at.X - _depthOrigin.X) > RoadDepthCap || Math.Abs(at.Y - _depthOrigin.Y) > RoadDepthCap)
            {
                return RoadDepthCap + 1;
            }

            var window = WindowIndex(at);
            return _depthStamp[window] == _stamp ? _depth[window] : RoadDepthCap + 1;
        }

        private int WindowIndex(WorldPosition at) =>
            ((at.Y - _depthOrigin.Y + RoadDepthCap) * DepthWindow) + (at.X - _depthOrigin.X + RoadDepthCap);

        private static int Manhattan(WorldPosition a, WorldPosition b) =>
            Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);

        // Where a street runs: along the plan, where a lane could, or onto the
        // network it ends on. Never alongside a road off the plan, which would
        // lay two roads side by side: a lane laid before its street, a cell
        // over, keeps the street from running beside it.
        private sealed class StreetGate : ISiteFilter
        {
            private readonly Buildings _owner;

            public StreetGate(Buildings owner) => _owner = owner;

            public WorldPosition Centre { get; set; }

            public bool Accepts(int cell)
            {
                if (_owner.IsRealNetwork(cell, Centre))
                {
                    return true;
                }

                var at = _owner._grid.PositionAt(cell);

                if (!IsStreet(at, Centre) || !_owner._laneGate.Accepts(cell))
                {
                    return false;
                }

                for (var step = 0; step < 4; step++)
                {
                    var beside = Step(at, step);

                    if (_owner._grid.Contains(beside)
                        && !IsStreet(beside, Centre)
                        && _owner.IsRealNetwork(_owner._grid.IndexOf(beside), Centre)
                        && !WithinYard(beside, Centre))
                    {
                        return false;
                    }
                }

                return !_owner.ClosesBlock(at, Centre, links: false);
            }
        }

        // The cell one step east, west, south or north.
        private static WorldPosition Step(WorldPosition at, int step) =>
            new WorldPosition(
                at.X + (step == 0 ? 1 : step == 1 ? -1 : 0),
                at.Y + (step == 2 ? 1 : step == 3 ? -1 : 0));

        // What a street ends on: a road, the yard, or a lane waiting to be laid.
        private sealed class RealNetwork : ISiteFilter
        {
            private readonly Buildings _owner;

            public RealNetwork(Buildings owner) => _owner = owner;

            public WorldPosition Centre { get; set; }

            public bool Accepts(int cell) => _owner.IsRealNetwork(cell, Centre);
        }

        private bool IsRealNetwork(int cell, WorldPosition centre) =>
            _grid.RoadAt(cell) != RoadGrade.None
            || _laneOf.ContainsKey(cell)
            || WithinYard(_grid.PositionAt(cell), centre);

        // A link's far end: the network, at least LinkFloor and at most the
        // reach from the seed as an ox walks, and far enough round by road.
        private sealed class LinkTarget : ISiteFilter
        {
            private readonly Buildings _owner;
            private WorldPosition _from;
            private int _reach;
            private bool _doorLink;

            public LinkTarget(Buildings owner) => _owner = owner;

            public WorldPosition Centre { get; set; }

            /// <summary>The network cell the link leaves from.</summary>
            public WorldPosition From => _from;

            public void Begin(WorldPosition from, int reach, bool doorLink)
            {
                _from = from;
                _reach = reach;
                _doorLink = doorLink;
            }

            /// <summary>
            /// The most any far end in reach could save by road over its
            /// span, or <see cref="int.MinValue"/> when none is in reach.
            /// </summary>
            public int BestSaving()
            {
                var grid = _owner._grid;
                var best = int.MinValue;

                for (var y = Math.Max(0, _from.Y - _reach); y <= Math.Min(grid.Height - 1, _from.Y + _reach); y++)
                {
                    for (var x = Math.Max(0, _from.X - _reach); x <= Math.Min(grid.Width - 1, _from.X + _reach); x++)
                    {
                        var at = new WorldPosition(x, y);
                        var cell = grid.IndexOf(at);

                        if (Accepts(cell))
                        {
                            best = Math.Max(best, _owner.RoadDepth(at) - Manhattan(_from, at));
                        }
                    }
                }

                return best;
            }

            public bool Accepts(int cell)
            {
                if (!_owner.IsJoined(cell, Centre))
                {
                    return false;
                }

                var at = _owner._grid.PositionAt(cell);
                var span = Manhattan(_from, at);

                if (span < LinkFloor || span > _reach)
                {
                    return false;
                }

                var byRoad = _owner.RoadDepth(at);

                return _doorLink
                    ? byRoad >= span + DoorLinkSaving
                    : byRoad * 10 >= (ShortcutRatioTenths * span) + (10 * ShortcutSlack);
            }
        }

        // Where a link runs: where a lane could, off the network, but onto
        // the network where it ends.
        private sealed class LinkGate : ISiteFilter
        {
            private readonly Buildings _owner;

            public LinkGate(Buildings owner) => _owner = owner;

            public WorldPosition Centre { get; set; }

            public bool Accepts(int cell)
            {
                if (_owner.IsJoined(cell, Centre))
                {
                    return _owner._linkTarget.Accepts(cell);
                }

                // A link is a way between roads, not an alley round the
                // sides or back of the building it came with.
                var at = _owner._grid.PositionAt(cell);

                if (!_owner._laneGate.Accepts(cell) || _owner._laneGate.Flanks(at))
                {
                    return false;
                }

                // New track touches the network only where it leaves it and
                // where it joins it, never running alongside a road.

                for (var step = 0; step < 4; step++)
                {
                    var beside = Step(at, step);

                    if (_owner._grid.Contains(beside))
                    {
                        var index = _owner._grid.IndexOf(beside);

                        if (_owner.IsJoined(index, Centre) && !beside.Equals(_owner._linkTarget.From) && !_owner._linkTarget.Accepts(index))
                        {
                            return false;
                        }
                    }
                }

                return !_owner.ClosesBlock(at, Centre, links: true);
            }
        }
    }
}
