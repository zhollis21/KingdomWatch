using System;
using System.Collections.Generic;
using System.Linq;
using KingdomWatch.Core.Construction;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Traversal;
using NUnit.Framework;

namespace KingdomWatch.Core.Tests.Construction
{
    // #171: a village plans its streets, and closes the loops between
    // branches of its roads instead of growing them as a tree off the square.
    [TestFixture]
    public sealed class StreetPlanTests
    {
        private static readonly WorldPosition Camp = new WorldPosition(40, 40);

        // Grown once and only read: a hundred people on a map with room all
        // round, fed, so they build homes rather than farms, until they want
        // nothing more.
        private static readonly Lazy<BuildingsWorld> Town = new Lazy<BuildingsWorld>(GrowTown);

        [Test]
        public void The_street_plan_is_a_ring_and_four_spokes_out_of_the_yard()
        {
            var c = Camp;
            var ring = Buildings.StreetRing;
            var reach = Buildings.StreetReach;
            var yard = Buildings.CampYardRadius;

            Assert.Multiple(() =>
            {
                Assert.That(Buildings.IsStreet(c, c), Is.False, "the centre is the square");
                Assert.That(Buildings.IsStreet(new WorldPosition(c.X + yard, c.Y), c), Is.False, "the yard's edge is the square");
                Assert.That(Buildings.IsStreet(new WorldPosition(c.X + yard + 1, c.Y), c), Is.True, "a spoke leaves the yard");
                Assert.That(Buildings.IsStreet(new WorldPosition(c.X, c.Y - reach), c), Is.True, "a spoke's far end");
                Assert.That(Buildings.IsStreet(new WorldPosition(c.X, c.Y - reach - 1), c), Is.False, "past it");
                Assert.That(Buildings.IsStreet(new WorldPosition(c.X - ring, c.Y + 3), c), Is.True, "the ring's west side");
                Assert.That(Buildings.IsStreet(new WorldPosition(c.X + ring, c.Y + ring), c), Is.True, "the ring's corner");
                Assert.That(Buildings.IsStreet(new WorldPosition(c.X + 5, c.Y + 5), c), Is.False, "between ring and yard, off the axes");
                Assert.That(Buildings.IsStreet(new WorldPosition(c.X + ring + 1, c.Y + 2), c), Is.False, "just outside the ring");
            });
        }

        [Test]
        public void Nothing_is_built_on_its_own_streets()
        {
            var w = Town.Value;

            foreach (var building in w.Buildings.All)
            {
                var clearance = BuildingTable.Of(building.Kind).Clearance;

                for (var dy = -clearance; dy < building.Height; dy++)
                {
                    for (var dx = 0; dx < building.Width; dx++)
                    {
                        var at = new WorldPosition(building.Anchor.X + dx, building.Anchor.Y + dy);
                        Assert.That(Buildings.IsStreet(at, Camp), Is.False, building + " at " + at + (dy < 0 ? ", its roof" : string.Empty));
                    }
                }
            }
        }

        [Test]
        public void Doors_close_on_the_ground_are_close_by_road()
        {
            // The road distance between two doors over the distance between
            // them on the grid, averaged over every pair at least four cells
            // apart. Each lane only to the nearest road made it 1.76 in this
            // town; streets and links bring it to 1.11.
            var w = Town.Value;
            var doors = w.Buildings.All.Where(b => Buildings.HasDoor(b.Kind)).Select(b => Buildings.DoorOf(b.Kind, b.Anchor)).ToList();
            Assert.That(doors, Has.Count.GreaterThanOrEqualTo(12), "doors to measure");

            var ratios = new List<double>();

            foreach (var door in doors)
            {
                var byRoad = RoadDistances(w.World.Grid, door);

                foreach (var other in doors.Where(d => !d.Equals(door)))
                {
                    var apart = Math.Abs(door.X - other.X) + Math.Abs(door.Y - other.Y);

                    if (apart >= 4)
                    {
                        Assert.That(byRoad.ContainsKey(other), Is.True, other + " is reached by road from " + door);
                        ratios.Add((double)byRoad[other] / apart);
                    }
                }
            }

            Assert.That(ratios.Average(), Is.LessThan(1.4));
        }

        [Test]
        public void No_two_roads_run_side_by_side()
        {
            // Zach's catch on the first build: a link run alongside a road,
            // or a street laid a cell over from a lane that got there first,
            // left a double-width road. Outside the square, no two by two
            // block of cells is all road.
            var w = Town.Value;
            var grid = w.World.Grid;

            for (var y = 0; y < grid.Height - 1; y++)
            {
                for (var x = 0; x < grid.Width - 1; x++)
                {
                    if (Math.Max(Math.Abs(x - Camp.X), Math.Abs(y - Camp.Y)) <= Buildings.CampYardRadius + 1)
                    {
                        continue;
                    }

                    var block = new[] { new WorldPosition(x, y), new WorldPosition(x + 1, y), new WorldPosition(x, y + 1), new WorldPosition(x + 1, y + 1) };
                    Assert.That(block.All(at => grid.RoadAt(at) != RoadGrade.None), Is.False, "road all over the block at " + block[0]);
                }
            }
        }

        [Test]
        public void Only_doors_get_links_and_a_link_joins_road_to_road()
        {
            // A lane's first strip is the walk from the door, and a street
            // after it; any other strip is a link, from road to road.
            var w = Town.Value;
            var grid = w.World.Grid;
            var links = 0;

            foreach (var building in w.Buildings.All)
            {
                var strips = Strips(building.Lane);

                foreach (var strip in strips.Skip(1))
                {
                    links++;
                    Assert.That(Buildings.HasDoor(building.Kind), Is.True, building + " has a link");
                    Assert.That(TouchesRoadBeyond(grid, strip, strip[0]), Is.True, building + "'s link leaves from a road at " + strip[0]);
                    Assert.That(TouchesRoadBeyond(grid, strip, strip[strip.Count - 1]), Is.True, building + "'s link joins a road at " + strip[strip.Count - 1]);
                }
            }

            Assert.That(links, Is.GreaterThan(0), "links to check");
        }

        [Test]
        public void No_link_runs_round_its_own_building()
        {
            // Zach's catch on #172: a house's link ran up its west side and
            // round its back, an alley to the road behind. A link may run
            // along the door's row, not beside the rest of its footprint or
            // the rows its roof stands over.
            var w = Town.Value;

            foreach (var building in w.Buildings.All.Where(b => Buildings.HasDoor(b.Kind)))
            {
                var clearance = BuildingTable.Of(building.Kind).Clearance;
                var top = building.Anchor.Y - clearance;
                var bottom = building.Anchor.Y + building.Height - 1;
                var left = building.Anchor.X;
                var right = building.Anchor.X + building.Width - 1;

                foreach (var at in Strips(building.Lane).Skip(1).SelectMany(strip => strip))
                {
                    var flanks = ((at.X == left - 1 || at.X == right + 1) && at.Y >= top && at.Y <= bottom)
                        || (at.Y == top - 1 && at.X >= left && at.X <= right);
                    Assert.That(flanks, Is.False, building + "'s link at " + at);
                }
            }
        }

        private static BuildingsWorld GrowTown()
        {
            var w = new BuildingsWorld(people: 100, size: 80, camp: Camp);
            w.Wood(20000);
            w.Stores.Gather(ResourceKind.Food, 100000);

            for (var i = 0; i < 60; i++)
            {
                var before = w.Buildings.All.Count;
                w.Dawn();

                if (w.Buildings.All.Count == before)
                {
                    break;
                }

                w.Finish(w.Buildings.All[before]);
            }

            return w;
        }

        // Breadth first along road and the yard from a cell.
        private static Dictionary<WorldPosition, int> RoadDistances(TerrainGrid grid, WorldPosition from)
        {
            var distance = new Dictionary<WorldPosition, int> { [from] = 0 };
            var open = new Queue<WorldPosition>();
            open.Enqueue(from);

            while (open.Count > 0)
            {
                var at = open.Dequeue();

                foreach (var next in Beside(at))
                {
                    if (!distance.ContainsKey(next) && IsNetwork(grid, next))
                    {
                        distance[next] = distance[at] + 1;
                        open.Enqueue(next);
                    }
                }
            }

            return distance;
        }

        private static bool IsNetwork(TerrainGrid grid, WorldPosition at) =>
            grid.Contains(at)
            && (grid.RoadAt(at) != RoadGrade.None
                || Math.Max(Math.Abs(at.X - Camp.X), Math.Abs(at.Y - Camp.Y)) <= Buildings.CampYardRadius);

        private static IEnumerable<WorldPosition> Beside(WorldPosition at) => new[]
        {
            new WorldPosition(at.X + 1, at.Y), new WorldPosition(at.X - 1, at.Y), new WorldPosition(at.X, at.Y + 1), new WorldPosition(at.X, at.Y - 1),
        };

        // A lane cut into its strips, wherever a cell shares no edge with the
        // one before it.
        private static List<List<WorldPosition>> Strips(IReadOnlyList<WorldPosition> lane)
        {
            var strips = new List<List<WorldPosition>>();

            for (var i = 0; i < lane.Count; i++)
            {
                if (i == 0 || Math.Abs(lane[i].X - lane[i - 1].X) + Math.Abs(lane[i].Y - lane[i - 1].Y) != 1)
                {
                    strips.Add(new List<WorldPosition>());
                }

                strips[strips.Count - 1].Add(lane[i]);
            }

            return strips;
        }

        private static bool TouchesRoadBeyond(TerrainGrid grid, List<WorldPosition> strip, WorldPosition at) =>
            Beside(at).Any(next => !strip.Contains(next) && IsNetwork(grid, next));
    }
}
