using System;
using System.Collections.Generic;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Traversal;
using KingdomWatch.Core.Work;

namespace KingdomWatch.Core.Construction
{
    /// <summary>
    /// The town planner (#23): where an approved building goes, and the lane
    /// that joins its door to the village's roads.
    /// </summary>
    /// <remarks>
    /// **A village grows its roads.** A settlement's centre is its camp
    /// yard, paved as its square: every plains cell of it is laid as
    /// <see cref="RoadGrade.Track"/> at each of its dawns, so the first
    /// building's lane already meets road. The yard's scrub, trees and rocks
    /// are cleared by the next building approved, priced into its clearing
    /// and reserved from gatherers until then - a rock as
    /// <see cref="TripsPerRock"/> quarrying trips, paying their Stone - and
    /// paved when that clearing is done (<see cref="Building.Square"/>). Its
    /// road network is that yard and every road cell. A house
    /// or barn has a door, the middle cell of the row south of its
    /// footprint, and is placed only where a lane can join that door to the
    /// network without crossing a footprint, a roof, someone's pending lane
    /// or another settlement's yard. The lane is the cheapest way from the
    /// door to the network stepping along edges only, so it is one strip
    /// rather than cells meeting at their corners, and it prefers open
    /// ground and existing road; it
    /// is laid as <see cref="RoadGrade.Track"/> when the building's ground is
    /// cleared, and its scrub and trees are cleared with the footprint's and
    /// priced into the same work. A field has no door, but it must touch a
    /// road along one of its edges: one already there, or a lane laid from
    /// the middle of one of its sides, the cheapest of the four. Roads are
    /// never built on, so a field keeps its way in however the village
    /// grows round it, buildings that block movement (#25) included.
    ///
    /// **Where, by score.** Every footprint the bounded search reaches from
    /// the centre (or, for a field, from its barn) that the old rules allow
    /// is scored, lowest best, ties to the lower cell index, all in integers:
    /// <list type="bullet">
    /// <item>a house near the centre, its door near a road;</item>
    /// <item>a barn on the outskirts: a ring <see cref="RingGap"/> beyond the
    /// furthest house, never nearer than <see cref="MinimumRing"/>, its door
    /// near a road but less so than a house's;</item>
    /// <item>a field near its barn and away from the centre.</item>
    /// </list>
    /// Open plains are still preferred outright to clearing, as they were
    /// (#100): only when no plains footprint scores at all is ground with
    /// scrub or trees on it considered. Each later kind brings its own row of
    /// preferences with the issue that adds it.
    ///
    /// The weights are placeholders in the <see cref="PrimitiveTier"/> sense.
    /// </remarks>
    public sealed partial class Buildings
    {
        /// <summary>Score per cell a door stands from the centre.</summary>
        public const int CentreWeight = 6;

        /// <summary>Score per cell a door stands from the nearest road: what keeps buildings on the streets.</summary>
        public const int LaneWeight = 6;

        /// <summary>Score per cell a barn's door stands off its ring.</summary>
        public const int RingWeight = 4;

        /// <summary>
        /// Score per cell a barn's door stands from the nearest road: less
        /// than <see cref="RingWeight"/>, so a farmstead takes a longer track
        /// out to the outskirts rather than crowding the nearest street.
        /// </summary>
        public const int BarnLaneWeight = 2;

        /// <summary>The nearest a barn's ring comes to the centre, in cells.</summary>
        public const int MinimumRing = 12;

        /// <summary>Cells beyond the furthest house a barn's ring lies.</summary>
        public const int RingGap = 4;

        /// <summary>Score per cell a field stands from its barn.</summary>
        public const int FieldBarnWeight = 4;

        /// <summary>Score taken off per cell a field stands from the centre: fields go to the outskirts.</summary>
        public const int FieldCentreWeight = 2;

        /// <summary>
        /// Score per cell a field's nearest side stands from a road: more
        /// than a step's worth of <see cref="FieldBarnWeight"/> and
        /// <see cref="FieldCentreWeight"/> together, so a field takes a spot
        /// along a road already there whenever one is free, and needs no lane.
        /// </summary>
        public const int FieldRoadWeight = 8;

        /// <summary>Score per footprint cell of scrub or forest, once plains have failed.</summary>
        public const int ClearingWeight = 2;

        /// <summary>How far a lane may run from a door, in cells.</summary>
        public const int LaneRadius = 2 * Jobs.MaxSiteRadius;

        // The best footprints a lane is tried for before placement gives up:
        // a door the search reached whose lane is walled off by buildings
        // is refused and the next best tried.
        private const int PlacementAttempts = 4;

        private readonly SiteScorer _scorer;
        private readonly LaneGate _laneGate;
        private readonly NetworkTarget _networkTarget;
        private readonly bool[] _anyKind;

        // Lane cells approved and not yet laid, to their building. Looked up,
        // never iterated.
        private readonly Dictionary<int, Building> _laneOf = new Dictionary<int, Building>();

        // Scratch for one placement: the network's cells near it, and the
        // lane found.
        private readonly List<WorldPosition> _network = new List<WorldPosition>();
        private readonly List<WorldPosition> _lane = new List<WorldPosition>();
        private readonly List<WorldPosition> _square = new List<WorldPosition>();
        private readonly List<WorldPosition> _laneRoute = new List<WorldPosition>();

        /// <summary>
        /// The door of a footprint anchored here: the middle cell of the row
        /// south of it. Throws for a kind with no door (<see cref="HasDoor"/>)
        /// and for one that is not a kind.
        /// </summary>
        public static WorldPosition DoorOf(BuildingKind kind, WorldPosition anchor)
        {
            var spec = BuildingTable.Of(kind);

            if (!HasDoor(kind))
            {
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "A " + kind + " has no door.");
            }

            return new WorldPosition(anchor.X + (spec.Width / 2), anchor.Y + spec.Height);
        }

        /// <summary>
        /// Whether a kind has a door its lane starts at: everything but a
        /// field, which is joined to the roads along an edge instead.
        /// </summary>
        public static bool HasDoor(BuildingKind kind) => kind != BuildingKind.Field;

        // Where a building of this kind goes, and its lane into _lane: the
        // best-scoring footprint the search reaches, trying plains alone
        // first. `from` is where the search starts - the centre, or a field's
        // barn - and `centre` the settlement's camp.
        private bool TryPlace(
            BuildingKind kind,
            WorldPosition from,
            WorldPosition centre,
            EntityId mapHolder,
            EntityId settlement,
            out WorldPosition anchor,
            Building? barn = null)
        {
            _lane.Clear();
            var spec = BuildingTable.Of(kind);
            var radius = barn is null ? Jobs.MaxSiteRadius : FieldRadius;

            _scorer.Begin(kind, spec, centre, barn, settlement);
            CollectNetwork(centre, radius);

            for (var attempt = 0; attempt < PlacementAttempts; attempt++)
            {
                if (!TryBest(from, radius, mapHolder, out anchor))
                {
                    return false;
                }

                if (TryJoin(kind, anchor, centre))
                {
                    return true;
                }

                _scorer.Refuse(_grid.IndexOf(anchor));
            }

            anchor = default;
            return false;
        }

        // The best footprint the search reaches: plains first, then any ground.
        private bool TryBest(WorldPosition from, int radius, EntityId mapHolder, out WorldPosition anchor)
        {
            var known = _knownMaps.For(mapHolder);

            for (var pass = 0; pass < 2; pass++)
            {
                _scorer.StartPass(plainsOnly: pass == 0);
                _pathfinder.TryFindNearest(from, Jobs.Mover, _ground, known, _scorer, radius, _scratchRoute, out _);

                if (_scorer.Best >= 0)
                {
                    anchor = _grid.PositionAt(_scorer.Best);
                    return true;
                }
            }

            anchor = default;
            return false;
        }

        // Joins a footprint to the network, its lane into _lane: a house or
        // barn from its door; a field not already touching a road from the
        // middle of one of its sides, nearest the network first.
        private bool TryJoin(BuildingKind kind, WorldPosition anchor, WorldPosition centre)
        {
            _lane.Clear();
            var spec = BuildingTable.Of(kind);

            if (HasDoor(kind))
            {
                return TryLane(spec, anchor, DoorOf(kind, anchor), centre);
            }

            if (TouchesNetwork(spec, anchor, centre))
            {
                return true;
            }

            // The four sides in order of their distance to the network, ties
            // in side order; each tried once.
            var tried = 0;

            for (var attempt = 0; attempt < Sides; attempt++)
            {
                var best = -1;
                var bestDistance = int.MaxValue;

                for (var side = 0; side < Sides; side++)
                {
                    var gate = SideOf(spec, anchor, side);

                    if ((tried & (1 << side)) != 0 || !_scorer.IsOpenGate(gate))
                    {
                        continue;
                    }

                    var distance = _scorer.ToNetwork(gate);

                    if (distance < bestDistance)
                    {
                        best = side;
                        bestDistance = distance;
                    }
                }

                if (best < 0)
                {
                    return false;
                }

                tried |= 1 << best;

                if (TryLane(spec, anchor, SideOf(spec, anchor, best), centre))
                {
                    return true;
                }
            }

            return false;
        }

        // Sides a field can be joined from.
        private const int Sides = 4;

        // The cell outside the middle of a footprint's side: south, east,
        // west, north.
        private static WorldPosition SideOf(BuildingSpec spec, WorldPosition anchor, int side)
        {
            switch (side)
            {
                case 0:
                    return new WorldPosition(anchor.X + (spec.Width / 2), anchor.Y + spec.Height);
                case 1:
                    return new WorldPosition(anchor.X + spec.Width, anchor.Y + (spec.Height / 2));
                case 2:
                    return new WorldPosition(anchor.X - 1, anchor.Y + (spec.Height / 2));
                default:
                    return new WorldPosition(anchor.X + (spec.Width / 2), anchor.Y - 1);
            }
        }

        // Whether a road, or the settlement's yard, lies along one of the
        // footprint's edges: a cell beside it, north, south, east or west.
        private bool TouchesNetwork(BuildingSpec spec, WorldPosition anchor, WorldPosition centre)
        {
            for (var dx = 0; dx < spec.Width; dx++)
            {
                if (IsNetwork(new WorldPosition(anchor.X + dx, anchor.Y - 1), centre)
                    || IsNetwork(new WorldPosition(anchor.X + dx, anchor.Y + spec.Height), centre))
                {
                    return true;
                }
            }

            for (var dy = 0; dy < spec.Height; dy++)
            {
                if (IsNetwork(new WorldPosition(anchor.X - 1, anchor.Y + dy), centre)
                    || IsNetwork(new WorldPosition(anchor.X + spec.Width, anchor.Y + dy), centre))
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsNetwork(WorldPosition at, WorldPosition centre) =>
            _grid.Contains(at) && (_grid.RoadAt(at) != RoadGrade.None || WithinYard(at, centre));

        // The cheapest way from a footprint's gate to the network along
        // edges, round everything a lane may not cross, into _lane without
        // the network cell it ends on.
        private bool TryLane(BuildingSpec spec, WorldPosition anchor, WorldPosition gate, WorldPosition centre)
        {
            _lane.Clear();
            _laneGate.Begin(spec, anchor, centre);
            _networkTarget.Centre = centre;

            if (!_pathfinder.TryFindNearest(gate, Jobs.Mover, _anyKind, default, _networkTarget, _laneGate, true, LaneRadius, _laneRoute, out _))
            {
                return false;
            }

            for (var i = 0; i < _laneRoute.Count - 1; i++)
            {
                _lane.Add(_laneRoute[i]);
            }

            return true;
        }

        // Every road cell near the centre, and the centre's yard: what a door's
        // distance to the network is measured against.
        private void CollectNetwork(WorldPosition centre, int radius)
        {
            _network.Clear();
            var reach = radius + LaneRadius;

            for (var y = Math.Max(0, centre.Y - reach); y <= Math.Min(_grid.Height - 1, centre.Y + reach); y++)
            {
                for (var x = Math.Max(0, centre.X - reach); x <= Math.Min(_grid.Width - 1, centre.X + reach); x++)
                {
                    var at = new WorldPosition(x, y);

                    if (WithinYard(at, centre) || _grid.RoadAt(at) != RoadGrade.None)
                    {
                        _network.Add(at);
                    }
                }
            }
        }

        // The yard's scrub, forest and rocks, into _square: what the building being
        // approved clears with its own. One building goes up at a time, so
        // none is still waiting on them.
        private void CollectSquare(WorldPosition centre)
        {
            _square.Clear();

            for (var y = Math.Max(0, centre.Y - CampYardRadius); y <= Math.Min(_grid.Height - 1, centre.Y + CampYardRadius); y++)
            {
                for (var x = Math.Max(0, centre.X - CampYardRadius); x <= Math.Min(_grid.Width - 1, centre.X + CampYardRadius); x++)
                {
                    var at = new WorldPosition(x, y);
                    var kind = _grid[at];

                    if (kind == TerrainKind.Scrub || kind == TerrainKind.Forest || kind == TerrainKind.Rocks)
                    {
                        _square.Add(at);
                    }
                }
            }
        }

        // Paves the plains of a settlement's yard that are not road yet: its
        // square. At every dawn, so it needs no state of its own and ground
        // cleared in the yard later is paved too.
        private void PaveSquare(WorldPosition centre)
        {
            for (var y = Math.Max(0, centre.Y - CampYardRadius); y <= Math.Min(_grid.Height - 1, centre.Y + CampYardRadius); y++)
            {
                for (var x = Math.Max(0, centre.X - CampYardRadius); x <= Math.Min(_grid.Width - 1, centre.X + CampYardRadius); x++)
                {
                    var at = new WorldPosition(x, y);

                    if (_grid[at] == TerrainKind.Plains && _grid.RoadAt(at) == RoadGrade.None)
                    {
                        _grid.SetRoad(at, RoadGrade.Track);
                    }
                }
            }
        }

        // Lays a building's lane and its share of the square as road, clearing
        // what grows on them. When its
        // ground is cleared, or at approval if there was nothing to clear.
        private void LayLane(Building building)
        {
            Lay(building.Lane);
            Lay(building.Square);
        }

        private void Lay(IReadOnlyList<WorldPosition> cells)
        {
            for (var i = 0; i < cells.Count; i++)
            {
                var at = cells[i];
                var kind = _grid[at];

                if (kind == TerrainKind.Scrub || kind == TerrainKind.Forest)
                {
                    _land.Clear(at);
                }
                else if (kind == TerrainKind.Rocks)
                {
                    // Quarried away; rocks hold no claims to forget.
                    _grid.Set(at, TerrainKind.Plains);
                }

                _grid.SetRoad(at, RoadGrade.Track);
                _laneOf.Remove(_grid.IndexOf(at));
            }
        }

        // Whether a footprint anchored here may stand: every cell on the map,
        // plains (or, unless plainsOnly, scrub or forest), under no building,
        // roof, road or pending lane, and out of every settlement's camp yard;
        // and its own roof's rows over no building, road, lane or yard (#150,
        // the #153 review). A roof may hang off the map.
        private bool Fits(BuildingSpec spec, WorldPosition anchor, WorldPosition keep, bool plainsOnly)
        {
            for (var dy = 0; dy < spec.Height; dy++)
            {
                for (var dx = 0; dx < spec.Width; dx++)
                {
                    var at = new WorldPosition(anchor.X + dx, anchor.Y + dy);

                    if (!_grid.Contains(at) || InAnyYard(at, keep))
                    {
                        return false;
                    }

                    var index = _grid.IndexOf(at);
                    var kind = _grid.KindAt(index);

                    if (!_ground[(int)kind] || (plainsOnly && kind != TerrainKind.Plains) || _onCell.ContainsKey(index)
                        || _underRoof.Contains(index) || IsRoadOrLane(index))
                    {
                        return false;
                    }
                }
            }

            for (var dy = 1; dy <= spec.Clearance; dy++)
            {
                for (var dx = 0; dx < spec.Width; dx++)
                {
                    var under = new WorldPosition(anchor.X + dx, anchor.Y - dy);

                    if (!_grid.Contains(under))
                    {
                        continue;
                    }

                    var index = _grid.IndexOf(under);

                    if (InAnyYard(under, keep) || _onCell.ContainsKey(index) || IsRoadOrLane(index))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private bool IsRoadOrLane(int index) => _grid.RoadAt(index) != RoadGrade.None || _laneOf.ContainsKey(index);

        // Within the camp yard of `keep` or of any settlement.
        private bool InAnyYard(WorldPosition at, WorldPosition keep)
        {
            if (WithinYard(at, keep))
            {
                return true;
            }

            for (var i = 0; i < _camps.Count; i++)
            {
                if (WithinYard(at, _camps[i].Position))
                {
                    return true;
                }
            }

            return false;
        }

        // Within the camp yard of any settlement other than the one at `own`.
        private bool InOtherYard(WorldPosition at, WorldPosition own)
        {
            for (var i = 0; i < _camps.Count; i++)
            {
                var camp = _camps[i].Position;

                if (!camp.Equals(own) && WithinYard(at, camp))
                {
                    return true;
                }
            }

            return false;
        }

        private static int Chebyshev(WorldPosition a, WorldPosition b) =>
            Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

        // The cell at the middle of a footprint.
        private static WorldPosition MiddleOf(BuildingSpec spec, WorldPosition anchor) =>
            new WorldPosition(anchor.X + (spec.Width / 2), anchor.Y + (spec.Height / 2));

        // Scores every footprint the search settles and keeps the best. Never
        // accepts one, so the search runs the whole box.
        private sealed class SiteScorer : ISiteFilter
        {
            private readonly Buildings _owner;
            private readonly int[] _refused = new int[PlacementAttempts];
            private int _refusedCount;

            private BuildingKind _kind;
            private BuildingSpec _spec;
            private WorldPosition _centre;
            private WorldPosition _barnMiddle;
            private int _ring;
            private bool _plainsOnly;
            private long _bestScore;

            public SiteScorer(Buildings owner) => _owner = owner;

            /// <summary>The best anchor's cell index this pass, or -1.</summary>
            public int Best { get; private set; }

            public void Begin(BuildingKind kind, BuildingSpec spec, WorldPosition centre, Building? barn, EntityId settlement)
            {
                _kind = kind;
                _spec = spec;
                _centre = centre;
                _barnMiddle = barn is null ? centre : MiddleOf(BuildingTable.Of(BuildingKind.Barn), barn.Anchor);
                _refusedCount = 0;
                _ring = kind == BuildingKind.Barn ? RingOf(settlement, centre) : 0;
            }

            public void StartPass(bool plainsOnly)
            {
                _plainsOnly = plainsOnly;
                Best = -1;
                _bestScore = long.MaxValue;
            }

            public void Refuse(int cell) => _refused[_refusedCount++] = cell;

            public bool Accepts(int cell)
            {
                for (var i = 0; i < _refusedCount; i++)
                {
                    if (_refused[i] == cell)
                    {
                        return false;
                    }
                }

                var grid = _owner._grid;
                var anchor = grid.PositionAt(cell);

                if (!_owner.Fits(_spec, anchor, _centre, _plainsOnly))
                {
                    return false;
                }

                long score;

                if (HasDoor(_kind))
                {
                    var door = DoorOf(_kind, anchor);

                    if (!DoorIsOpen(door))
                    {
                        return false;
                    }

                    var lane = (long)ToNetwork(door);
                    var fromCentre = Chebyshev(door, _centre);

                    score = _kind == BuildingKind.Barn
                        ? (RingWeight * (long)Math.Abs(fromCentre - _ring)) + (BarnLaneWeight * lane)
                        : (CentreWeight * (long)fromCentre) + (LaneWeight * lane);
                }
                else
                {
                    var middle = MiddleOf(_spec, anchor);
                    score = (FieldBarnWeight * (long)Chebyshev(middle, _barnMiddle)) - (FieldCentreWeight * (long)Chebyshev(middle, _centre))
                        + (FieldRoadWeight * (long)ToRoad(anchor));
                }

                if (!_plainsOnly)
                {
                    score += ClearingWeight * (long)Uncleared(anchor);
                }

                if (score < _bestScore || (score == _bestScore && cell < Best))
                {
                    _bestScore = score;
                    Best = cell;
                }

                return false;
            }

            // A door a lane can start from: on the map, ground a lane may run
            // on or road already, and under no building, roof or pending lane
            // of another, nor in another settlement's yard.
            // How far a field anchored here is from a road: none when one
            // lies along an edge, else from its nearest open side; a field
            // with no open side cannot be joined at all.
            private long ToRoad(WorldPosition anchor)
            {
                if (_owner.TouchesNetwork(_spec, anchor, _centre))
                {
                    return 0L;
                }

                var nearest = int.MaxValue;

                for (var side = 0; side < Sides; side++)
                {
                    var gate = SideOf(_spec, anchor, side);

                    if (IsOpenGate(gate))
                    {
                        nearest = Math.Min(nearest, ToNetwork(gate));
                    }
                }

                return nearest == int.MaxValue ? LaneRadius : nearest;
            }

            /// <summary>Whether a lane can start at this cell: <see cref="DoorIsOpen"/>.</summary>
            public bool IsOpenGate(WorldPosition gate) => DoorIsOpen(gate);

            private bool DoorIsOpen(WorldPosition door)
            {
                var grid = _owner._grid;

                if (!grid.Contains(door))
                {
                    return false;
                }

                var index = grid.IndexOf(door);

                return (_owner._ground[(int)grid.KindAt(index)] || grid.RoadAt(index) != RoadGrade.None)
                    && !_owner._onCell.ContainsKey(index)
                    && !_owner._underRoof.Contains(index)
                    && !_owner._laneOf.ContainsKey(index)
                    && !_owner.InOtherYard(door, _centre);
            }

            // Cells from the door to the nearest of the network: a lane's
            // length, as the crow flies.
            public int ToNetwork(WorldPosition door)
            {
                var network = _owner._network;
                var nearest = int.MaxValue;

                for (var i = 0; i < network.Count && nearest > 0; i++)
                {
                    nearest = Math.Min(nearest, Chebyshev(door, network[i]));
                }

                return nearest == int.MaxValue ? LaneRadius : nearest;
            }

            private int Uncleared(WorldPosition anchor)
            {
                var grid = _owner._grid;
                var count = 0;

                for (var dy = 0; dy < _spec.Height; dy++)
                {
                    for (var dx = 0; dx < _spec.Width; dx++)
                    {
                        count += grid[new WorldPosition(anchor.X + dx, anchor.Y + dy)] != TerrainKind.Plains ? 1 : 0;
                    }
                }

                return count;
            }

            // A barn's ring: RingGap beyond the settlement's furthest house
            // door, and never nearer than MinimumRing.
            private int RingOf(EntityId settlement, WorldPosition centre)
            {
                var furthest = 0;
                var all = _owner._all;

                for (var i = 0; i < all.Count; i++)
                {
                    var house = all[i];

                    if (house.Settlement == settlement && house.Kind == BuildingKind.House)
                    {
                        furthest = Math.Max(furthest, Chebyshev(DoorOf(BuildingKind.House, house.Anchor), centre));
                    }
                }

                return Math.Max(MinimumRing, furthest + RingGap);
            }
        }

        // Where a lane may run: ground that can be cleared to a track, or road
        // already; never under a building, a roof - the one being placed
        // included - or another building's pending lane, nor through
        // another settlement's yard. Its own yard, which it ends on, always.
        private sealed class LaneGate : ISiteFilter
        {
            private readonly Buildings _owner;
            private BuildingSpec _spec;
            private WorldPosition _anchor;
            private WorldPosition _centre;

            public LaneGate(Buildings owner) => _owner = owner;

            public void Begin(BuildingSpec spec, WorldPosition anchor, WorldPosition centre)
            {
                _spec = spec;
                _anchor = anchor;
                _centre = centre;
            }

            public bool Accepts(int cell)
            {
                var grid = _owner._grid;

                // The yard is where a lane ends, whatever its ground: the
                // search has already found it walkable.
                if (WithinYard(grid.PositionAt(cell), _centre))
                {
                    return true;
                }

                if (!(_owner._ground[(int)grid.KindAt(cell)] || grid.RoadAt(cell) != RoadGrade.None)
                    || _owner._onCell.ContainsKey(cell)
                    || _owner._underRoof.Contains(cell)
                    || _owner._laneOf.ContainsKey(cell))
                {
                    return false;
                }

                var at = grid.PositionAt(cell);

                // The building's own footprint and the rows its roof stands over.
                if (at.X >= _anchor.X && at.X < _anchor.X + _spec.Width
                    && at.Y >= _anchor.Y - _spec.Clearance && at.Y < _anchor.Y + _spec.Height)
                {
                    return false;
                }

                return !_owner.InOtherYard(at, _centre);
            }
        }

        // The network a lane ends on: any road, or the settlement's own yard.
        private sealed class NetworkTarget : ISiteFilter
        {
            private readonly TerrainGrid _grid;

            public NetworkTarget(TerrainGrid grid) => _grid = grid;

            public WorldPosition Centre { get; set; }

            public bool Accepts(int cell)
            {
                var at = _grid.PositionAt(cell);
                return _grid.RoadAt(cell) != RoadGrade.None || WithinYard(at, Centre);
            }
        }
    }
}
