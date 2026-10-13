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
    /// is scored, lowest best, ties to the lower cell index, all in integers
    /// (a door near an open cell of the settlement's planned streets counts
    /// as near a road, and no footprint or roof sits on one; the streets and
    /// the links a lane may carry are in Buildings.Streets.cs, #171):
    /// <list type="bullet">
    /// <item>a house near the centre, its door near a road;</item>
    /// <item>a barn on the outskirts: a ring <see cref="RingGap"/> beyond the
    /// furthest house, never nearer than <see cref="MinimumRing"/>, its door
    /// near a road but less so than a house's;</item>
    /// <item>a field near its barn and away from the centre, and near a
    /// road: as the crow flies to score, then, for the best few, by the
    /// lane it really gets, which can run much further round a barn or
    /// another field (#169).</item>
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

        /// <summary>
        /// How far from the centre, in cells each way, a house or barn is
        /// looked for first. Only when nothing fits there does the search
        /// widen, doubling, out to <see cref="BuildReach"/>: so a village
        /// stays compact while it can and a town grows as far as its
        /// buildings need.
        /// </summary>
        public const int NearRadius = 16;

        /// <summary>
        /// The most a building may cost to walk to from the centre, in route
        /// cost: as far as a Builder or Farmer goes for a share
        /// (<see cref="Jobs.ReachCost(long)"/>), so whatever is placed can be
        /// built and farmed.
        /// </summary>
        public static readonly long BuildReach = Jobs.ReachCost(ShareTicks);

        /// <summary>
        /// Cells that must lie between anything of one settlement - a
        /// building, a field, its camp yard - and anything of another, roads
        /// apart: placement keeps this clear, and a band does not settle
        /// where its yard would break it (#154). Villages start at least
        /// <see cref="World.M1VillageSpacing"/> apart, so two neighbours
        /// share the rest between them, first come first served.
        /// </summary>
        public const int VillageGap = 30;

        /// <summary>How far a lane may run from a door, in cells.</summary>
        public const int LaneRadius = 2 * NearRadius;

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
        private readonly List<WorldPosition> _sideLane = new List<WorldPosition>();
        private readonly List<WorldPosition> _keptLane = new List<WorldPosition>();
        private readonly List<WorldPosition> _square = new List<WorldPosition>();
        private readonly List<WorldPosition> _laneRoute = new List<WorldPosition>();
        private readonly List<(int MinX, int MinY, int MaxX, int MaxY)> _others = new List<(int MinX, int MinY, int MaxX, int MaxY)>();

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
        /// field, which is joined to the roads along an edge instead. Throws
        /// for <see cref="BuildingKind.None"/> and for a value that is not a kind.
        /// </summary>
        public static bool HasDoor(BuildingKind kind)
        {
            BuildingTable.Of(kind);
            return kind != BuildingKind.Field;
        }

        // Where a building of this kind goes, and its lane into _lane: the
        // best-scoring footprint the search reaches, trying plains alone
        // first. `from` is where the search starts - the centre, or a field's
        // barn - and `centre` the settlement's camp. A house or barn is
        // looked for within NearRadius first, then in boxes twice as wide
        // until one fits or the box holds all of BuildReach; a field within
        // FieldRadius of its barn, where a Farmer can reach it.
        private bool TryPlace(
            BuildingKind kind,
            WorldPosition from,
            WorldPosition centre,
            ReadOnlySpan<bool> known,
            EntityId settlement,
            out WorldPosition anchor,
            Building? barn = null)
        {
            CollectOthers(settlement);

            if (barn is object)
            {
                return TryPlaceWithin(kind, from, centre, known, settlement, barn, FieldRadius, out anchor);
            }

            var widest = _pathfinder.RadiusOf(BuildReach);

            for (var radius = NearRadius; ; radius = (int)Math.Min(widest, 2L * radius))
            {
                if (TryPlaceWithin(kind, from, centre, known, settlement, null, radius, out anchor))
                {
                    return true;
                }

                if (radius >= widest)
                {
                    return false;
                }
            }
        }

        private bool TryPlaceWithin(
            BuildingKind kind,
            WorldPosition from,
            WorldPosition centre,
            ReadOnlySpan<bool> known,
            EntityId settlement,
            Building? barn,
            int radius,
            out WorldPosition anchor)
        {
            _lane.Clear();
            var spec = BuildingTable.Of(kind);

            _scorer.Begin(kind, spec, centre, barn, settlement);
            CollectNetwork(centre, radius);

            if (barn is object)
            {
                return TryPlaceField(from, centre, known, radius, out anchor);
            }

            for (var attempt = 0; attempt < PlacementAttempts; attempt++)
            {
                if (!TryBest(from, radius, known, out anchor))
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

        // A field's best site by its real lane (#169). A site is scored by
        // how far its nearest side stands from a road as the crow flies,
        // and its lane can run much further round whatever stands in the
        // way. So each of the best few sites is joined and rescored with the
        // lane it actually got, the lowest kept, ties to the lower cell index
        // as in the scoring. All of them: the crow-flies distance is measured
        // against the network near the centre only, so it is no lower bound
        // on a lane, and no site can be passed over on its score alone. Open
        // plains stay preferred outright: once a plains site is kept, ground
        // to clear is not looked at. Into _lane.
        private bool TryPlaceField(WorldPosition from, WorldPosition centre, ReadOnlySpan<bool> known, int radius, out WorldPosition anchor)
        {
            var keptScore = long.MaxValue;
            var keptCell = -1;
            var keptPlains = false;
            anchor = default;

            for (var attempt = 0; attempt < PlacementAttempts; attempt++)
            {
                if (!TryBest(from, radius, known, out var site) || (keptPlains && !_scorer.PlainsOnly))
                {
                    break;
                }

                var cell = _grid.IndexOf(site);
                var estimate = _scorer.ToRoad(site);

                // A field is searched for from its barn, so it is asked
                // separately whether a Farmer could walk to it.
                if (_pathfinder.TryFindRoute(centre, site, Jobs.Mover, int.MaxValue, BuildReach, _scratchRoute, out _)
                    && TryJoin(BuildingKind.Field, site, centre))
                {
                    var score = _scorer.BestScore + (FieldRoadWeight * (_lane.Count - estimate));

                    if (score < keptScore || (score == keptScore && cell < keptCell))
                    {
                        keptScore = score;
                        keptCell = cell;
                        keptPlains = _scorer.PlainsOnly;
                        anchor = site;
                        _keptLane.Clear();
                        _keptLane.AddRange(_lane);
                    }
                }

                _scorer.Refuse(cell);
            }

            _lane.Clear();
            _lane.AddRange(_keptLane);
            _keptLane.Clear();
            return keptCell >= 0;
        }

        // The best footprint the search reaches: plains first, then any ground.
        private bool TryBest(WorldPosition from, int radius, ReadOnlySpan<bool> known, out WorldPosition anchor)
        {
            for (var pass = 0; pass < 2; pass++)
            {
                _scorer.StartPass(plainsOnly: pass == 0);
                _pathfinder.TryFindNearest(from, Jobs.Mover, _ground, known, _scorer, null, false, radius, BuildReach, _scratchRoute, out _);

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
        // middle of whichever of its sides gives the cheapest lane.
        private bool TryJoin(BuildingKind kind, WorldPosition anchor, WorldPosition centre)
        {
            _lane.Clear();
            var spec = BuildingTable.Of(kind);

            if (HasDoor(kind))
            {
                return TryLane(spec, anchor, DoorOf(kind, anchor), centre, out _);
            }

            if (TouchesNetwork(spec, anchor, centre))
            {
                return true;
            }

            // Every open side's lane, the cheapest kept, ties in side order
            // (#169): the side nearest the network as the crow flies can be
            // walled in, and its lane then runs right round the field.
            var bestCost = long.MaxValue;

            for (var side = 0; side < Sides; side++)
            {
                var gate = SideOf(spec, anchor, side);

                if (_scorer.IsOpenGate(gate) && TryLane(spec, anchor, gate, centre, out var cost) && cost < bestCost)
                {
                    bestCost = cost;
                    _sideLane.Clear();
                    _sideLane.AddRange(_lane);
                }
            }

            _lane.Clear();
            _lane.AddRange(_sideLane);
            _sideLane.Clear();
            return bestCost != long.MaxValue;
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
        private bool TryLane(BuildingSpec spec, WorldPosition anchor, WorldPosition gate, WorldPosition centre, out long cost)
        {
            _lane.Clear();
            _laneGate.Begin(spec, anchor, centre);
            _networkTarget.Centre = centre;

            _networkTarget.Streets = true;

            if (!_pathfinder.TryFindNearest(gate, Jobs.Mover, _anyKind, default, _networkTarget, _laneGate, true, LaneRadius, _laneRoute, out cost))
            {
                return false;
            }

            var end = _laneRoute[_laneRoute.Count - 1];

            for (var i = 0; i < _laneRoute.Count - 1; i++)
            {
                _lane.Add(_laneRoute[i]);
            }

            // Ended on a street not laid yet: the street, as far as it runs
            // to the network (#171). Where it does not, the lane joins the
            // network the way it would with no plan at all.
            if (IsNetwork(end, centre) || TryStreet(end, centre, ref cost))
            {
                return true;
            }

            _lane.Clear();
            _networkTarget.Streets = false;

            if (!_pathfinder.TryFindNearest(gate, Jobs.Mover, _anyKind, default, _networkTarget, _laneGate, true, LaneRadius, _laneRoute, out cost))
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

                    if (WithinYard(at, centre) || _grid.RoadAt(at) != RoadGrade.None || IsOpenStreet(_grid.IndexOf(at), centre))
                    {
                        _network.Add(at);
                    }
                }
            }
        }

        // The yard's scrub, forest and rocks, into _square: what the building
        // being approved clears with its own. One goes up at a time in a
        // settlement, a lane never enters another's yard, and VillageGap
        // keeps a yard far from every other settlement's buildings and so
        // from the lanes waiting at their doors (#154): nothing else waits
        // to clear these.
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
        // its own roof's rows over no building, road, lane or yard (#150,
        // the #153 review); and VillageGap from any other settlement (#154).
        // A roof may hang off the map.
        private bool Fits(BuildingSpec spec, WorldPosition anchor, WorldPosition keep, bool plainsOnly)
        {
            for (var dy = 0; dy < spec.Height; dy++)
            {
                for (var dx = 0; dx < spec.Width; dx++)
                {
                    var at = new WorldPosition(anchor.X + dx, anchor.Y + dy);

                    if (!_grid.Contains(at) || InAnyYard(at, keep) || IsStreet(at, keep))
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

                    if (InAnyYard(under, keep) || IsStreet(under, keep) || _onCell.ContainsKey(index) || IsRoadOrLane(index))
                    {
                        return false;
                    }
                }
            }

            // Its roof too: the rows it stands over are the house's as much as
            // its footprint is.
            return !NearOthers(new WorldPosition(anchor.X, anchor.Y - spec.Clearance), spec.Width, spec.Height + spec.Clearance);
        }

        // Every other settlement's buildings, roofs included, and camp yards,
        // each widened by VillageGap: what nothing of this one may overlap.
        // Gathered once per placement, for Fits to test each footprint and
        // roof against. EntityId.None for a camp not yet founded, to which
        // every settlement is another.
        private void CollectOthers(EntityId settlement)
        {
            _others.Clear();

            for (var i = 0; i < _all.Count; i++)
            {
                var building = _all[i];

                if (building.Settlement != settlement)
                {
                    var clearance = BuildingTable.Of(building.Kind).Clearance;
                    _others.Add(Widened(building.Anchor.X, building.Anchor.Y - clearance, building.Width, building.Height + clearance));
                }
            }

            for (var i = 0; i < _camps.Count; i++)
            {
                var camp = _camps[i];

                if (camp.Id != settlement)
                {
                    var side = (2 * CampYardRadius) + 1;
                    _others.Add(Widened(camp.Position.X - CampYardRadius, camp.Position.Y - CampYardRadius, side, side));
                }
            }
        }

        private static (int MinX, int MinY, int MaxX, int MaxY) Widened(int x, int y, int width, int height) =>
            (x - VillageGap, y - VillageGap, x + width - 1 + VillageGap, y + height - 1 + VillageGap);

        // Whether a rectangle anchored here overlaps anything CollectOthers
        // gathered: lies within VillageGap of another settlement.
        private bool NearOthers(WorldPosition anchor, int width, int height)
        {
            for (var i = 0; i < _others.Count; i++)
            {
                var other = _others[i];

                if (anchor.X <= other.MaxX && anchor.X + width - 1 >= other.MinX
                    && anchor.Y <= other.MaxY && anchor.Y + height - 1 >= other.MinY)
                {
                    return true;
                }
            }

            return false;
        }

        // Whether a camp founded here would keep VillageGap from every
        // settlement (#154).
        private bool YardIsApart(WorldPosition camp)
        {
            CollectOthers(EntityId.None);
            return !NearOthers(
                new WorldPosition(camp.X - CampYardRadius, camp.Y - CampYardRadius), (2 * CampYardRadius) + 1, (2 * CampYardRadius) + 1);
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

            /// <summary>The best anchor's score this pass.</summary>
            public long BestScore => _bestScore;

            /// <summary>Whether this pass takes open plains only.</summary>
            public bool PlainsOnly => _plainsOnly;

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
            public long ToRoad(WorldPosition anchor)
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
            private int ToNetwork(WorldPosition door)
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

                if (!(_owner.IsLaneGround(grid.KindAt(cell)) || grid.RoadAt(cell) != RoadGrade.None)
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

        // The network a lane ends on: any road, or the settlement's own yard;
        // and, with Streets, one of its planned streets still open (#171).
        private sealed class NetworkTarget : ISiteFilter
        {
            private readonly Buildings _owner;

            public NetworkTarget(Buildings owner) => _owner = owner;

            public WorldPosition Centre { get; set; }

            public bool Streets { get; set; }

            public bool Accepts(int cell)
            {
                var grid = _owner._grid;
                var at = grid.PositionAt(cell);
                return grid.RoadAt(cell) != RoadGrade.None || WithinYard(at, Centre) || (Streets && _owner.IsOpenStreet(cell, Centre));
            }
        }
    }
}
