using System;
using System.Collections.Generic;
using System.Linq;
using KingdomWatch.Core.Construction;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Traversal;
using NUnit.Framework;

namespace KingdomWatch.Core.Tests.Construction
{
    /// <summary>
    /// The town planner (#23): where buildings go, and the lanes that join
    /// their doors to the village's roads.
    /// </summary>
    [TestFixture]
    public sealed class TownPlannerTests
    {
        [Test]
        public void Every_door_is_joined_to_the_yard_by_road()
        {
            var w = BuildVillage();
            var grid = w.World.Grid;

            foreach (var building in w.Buildings.All.Where(b => Buildings.HasDoor(b.Kind)))
            {
                Assert.That(ReachesYard(grid, Buildings.DoorOf(building.Kind, building.Anchor)), Is.True, building + "'s door");
            }
        }

        [Test]
        public void A_lane_is_a_walk_from_the_door_under_no_building()
        {
            var w = BuildVillage();

            foreach (var building in w.Buildings.All)
            {
                var lane = building.Lane;

                if (!Buildings.HasDoor(building.Kind))
                {
                    Assert.That(lane, Is.Empty, building + " has no door");
                    continue;
                }

                if (lane.Count > 0)
                {
                    Assert.That(lane[0], Is.EqualTo(Buildings.DoorOf(building.Kind, building.Anchor)), building + "'s lane starts at its door");
                }

                for (var i = 0; i < lane.Count; i++)
                {
                    Assert.That(w.Buildings.At(lane[i]), Is.Null, building + "'s lane at " + lane[i]);

                    if (i > 0)
                    {
                        Assert.That(Chebyshev(lane[i], lane[i - 1]), Is.EqualTo(1), building + "'s lane steps at " + lane[i]);
                    }
                }
            }
        }

        [Test]
        public void Barns_stand_further_out_than_the_houses_before_them()
        {
            var w = BuildVillage();
            var furthestHouse = 0;

            foreach (var building in w.Buildings.All.Where(b => Buildings.HasDoor(b.Kind)))
            {
                var out_ = Chebyshev(Buildings.DoorOf(building.Kind, building.Anchor), BuildingsWorld.Centre);

                if (building.Kind == BuildingKind.House)
                {
                    furthestHouse = Math.Max(furthestHouse, out_);
                }
                else if (building.Kind == BuildingKind.Barn)
                {
                    Assert.That(out_, Is.GreaterThan(furthestHouse), building + " is out past the houses");
                    Assert.That(out_, Is.GreaterThanOrEqualTo(Buildings.MinimumRing - 1), building + " is on the outskirts");
                }
            }

            Assert.That(w.Buildings.All.Any(b => b.Kind == BuildingKind.Barn), "the village built a barn");
        }

        [Test]
        public void Fields_lie_round_their_barn_and_off_every_road_and_lane()
        {
            var w = BuildVillage();
            var lanes = new HashSet<WorldPosition>(w.Buildings.All.SelectMany(b => b.Lane));
            var fields = w.Buildings.All.Where(b => b.Kind == BuildingKind.Field).ToList();
            Assert.That(fields, Is.Not.Empty, "the village laid out fields");

            foreach (var field in fields)
            {
                var barn = w.Buildings.All.Single(b => b.Id == field.Barn);
                Assert.That(Chebyshev(field.Anchor, barn.Anchor), Is.LessThanOrEqualTo(Buildings.FieldRadius), field + " is by " + barn);

                for (var dy = 0; dy < field.Height; dy++)
                {
                    for (var dx = 0; dx < field.Width; dx++)
                    {
                        var at = new WorldPosition(field.Anchor.X + dx, field.Anchor.Y + dy);
                        Assert.That(w.World.Grid.RoadAt(at), Is.EqualTo(RoadGrade.None), field + " at " + at);
                        Assert.That(lanes.Contains(at), Is.False, field + " at " + at);
                    }
                }
            }
        }

        [Test]
        public void A_lane_through_forest_is_priced_reserved_and_laid_as_road_when_cleared()
        {
            // A belt of forest round the camp: every house beyond it on open
            // plains, its lane back to the yard through the trees.
            var w = new BuildingsWorld(paint: grid =>
            {
                for (var y = 0; y < BuildingsWorld.Size; y++)
                {
                    for (var x = 0; x < BuildingsWorld.Size; x++)
                    {
                        var ring = Chebyshev(new WorldPosition(x, y), BuildingsWorld.Centre);

                        if (ring > Buildings.CampYardRadius && ring <= Buildings.CampYardRadius + 4)
                        {
                            grid.Set(new WorldPosition(x, y), TerrainKind.Forest);
                        }
                    }
                }
            });
            w.Wood(100);
            w.Dawn();

            var house = w.Buildings.All.Single();
            var trees = house.Lane.Where(at => w.World.Grid[at] == TerrainKind.Forest).ToList();
            Assert.That(trees, Is.Not.Empty, "the lane crosses the belt");
            var cuts = trees.Sum(at => w.World.Land.CutsLeft(at));

            Assert.Multiple(() =>
            {
                Assert.That(house.ClearTicks, Is.EqualTo(cuts * Buildings.TicksPerCut), "the footprint is plains; the lane's trees are the clearing");
                Assert.That(house.ClearCuts, Is.EqualTo(cuts));
                Assert.That(trees.All(w.Buildings.IsReserved), Is.True, "nobody fells the lane's trees meanwhile");
                Assert.That(house.Lane.All(at => w.World.Grid.RoadAt(at) == RoadGrade.None), Is.True, "not laid before it is cleared");
            });

            var wood = w.Stores.Available(ResourceKind.Wood);
            w.Finish(house);

            Assert.Multiple(() =>
            {
                Assert.That(w.Stores.Available(ResourceKind.Wood) - wood, Is.EqualTo(cuts * Buildings.WoodPerCut));

                foreach (var at in house.Lane)
                {
                    Assert.That(w.World.Grid.RoadAt(at), Is.EqualTo(RoadGrade.Track), at.ToString());
                    Assert.That(w.World.Grid[at], Is.EqualTo(TerrainKind.Plains), at.ToString());
                    Assert.That(w.Buildings.IsReserved(at), Is.False, at.ToString());
                }
            });
        }

        [Test]
        public void A_lane_over_open_ground_is_laid_at_approval()
        {
            // Nothing to clear, so nothing waits on the Builders.
            var w = new BuildingsWorld();
            w.Wood(100);
            w.Dawn();

            var house = w.Buildings.All.Single();

            Assert.Multiple(() =>
            {
                Assert.That(house.Cleared, Is.True);
                Assert.That(house.Lane.All(at => w.World.Grid.RoadAt(at) == RoadGrade.Track), Is.True);
                Assert.That(house.Lane.Any(w.Buildings.IsReserved), Is.False);
            });
        }

        [Test]
        public void The_yard_s_plains_are_paved_as_the_square_at_the_settlement_s_dawn()
        {
            // A bush and a tree in the yard, which nothing clears for free.
            var bush = new WorldPosition(BuildingsWorld.Centre.X + 1, BuildingsWorld.Centre.Y);
            var tree = new WorldPosition(BuildingsWorld.Centre.X, BuildingsWorld.Centre.Y + 2);
            var w = new BuildingsWorld(paint: grid =>
            {
                grid.Set(bush, TerrainKind.Scrub);
                grid.Set(tree, TerrainKind.Forest);
            });
            var grid = w.World.Grid;
            var radius = Buildings.CampYardRadius;

            Assert.That(grid.RoadAt(BuildingsWorld.Centre), Is.EqualTo(RoadGrade.None), "not before its first dawn");

            w.Dawn();
            var rewrites = grid.Rewrites;
            w.Dawn();

            Assert.Multiple(() =>
            {
                for (var y = BuildingsWorld.Centre.Y - radius; y <= BuildingsWorld.Centre.Y + radius; y++)
                {
                    for (var x = BuildingsWorld.Centre.X - radius; x <= BuildingsWorld.Centre.X + radius; x++)
                    {
                        var at = new WorldPosition(x, y);
                        var expected = at.Equals(bush) || at.Equals(tree) ? RoadGrade.None : RoadGrade.Track;
                        Assert.That(grid.RoadAt(at), Is.EqualTo(expected), at.ToString());
                    }
                }

                Assert.That(grid[bush], Is.EqualTo(TerrainKind.Scrub));
                Assert.That(grid[tree], Is.EqualTo(TerrainKind.Forest));
                Assert.That(grid.RoadAt(new WorldPosition(BuildingsWorld.Centre.X + radius + 1, BuildingsWorld.Centre.Y)), Is.EqualTo(RoadGrade.None), "the yard ends");
                Assert.That(grid.Rewrites, Is.EqualTo(rewrites), "a paved square is paved once");
            });
        }

        [Test]
        public void No_lane_to_the_roads_no_building()
        {
            // Rocks everywhere but the camp, a way from it to one patch of
            // scrub a house fits, and a strip under the patch for its door:
            // a door no lane can lead from, since lanes do not cross rocks.
            var w = new BuildingsWorld(paint: grid =>
            {
                for (var y = 0; y < BuildingsWorld.Size; y++)
                {
                    for (var x = 0; x < BuildingsWorld.Size; x++)
                    {
                        var patch = x >= 24 && x <= 28 && y >= 20 && y <= 25;
                        grid.Set(new WorldPosition(x, y), patch ? TerrainKind.Scrub : TerrainKind.Rocks);
                    }
                }

                grid.Set(BuildingsWorld.Centre, TerrainKind.Plains);

                for (var x = BuildingsWorld.Centre.X + 1; x < 24; x++)
                {
                    grid.Set(new WorldPosition(x, BuildingsWorld.Centre.Y), TerrainKind.Plains);
                }
            });
            w.Wood(100);

            w.Dawn();

            Assert.That(w.Buildings.All, Is.Empty);
        }

        [Test]
        public void A_door_is_the_middle_of_the_row_south_and_a_field_has_none()
        {
            var house = BuildingTable.Of(BuildingKind.House);
            var anchor = new WorldPosition(10, 4);

            Assert.Multiple(() =>
            {
                Assert.That(Buildings.DoorOf(BuildingKind.House, anchor), Is.EqualTo(new WorldPosition(10 + (house.Width / 2), 4 + house.Height)));
                Assert.That(Buildings.HasDoor(BuildingKind.Barn), Is.True);
                Assert.That(Buildings.HasDoor(BuildingKind.Field), Is.False);
                Assert.That(() => Buildings.DoorOf(BuildingKind.Field, anchor), Throws.InstanceOf<ArgumentOutOfRangeException>());
                Assert.That(() => Buildings.DoorOf(BuildingKind.None, anchor), Throws.InstanceOf<ArgumentOutOfRangeException>());
                Assert.That(() => Buildings.DoorOf((BuildingKind)200, anchor), Throws.InstanceOf<ArgumentOutOfRangeException>());
            });
        }

        [Test]
        public void The_same_village_is_planned_the_same_way_twice()
        {
            var one = BuildVillage();
            var two = BuildVillage();

            Assert.That(
                two.Buildings.All.Select(b => (b.Kind, b.Anchor, string.Join(";", b.Lane))),
                Is.EqualTo(one.Buildings.All.Select(b => (b.Kind, b.Anchor, string.Join(";", b.Lane)))));
        }

        // Forty people with wood to spare, built up as far as twenty
        // buildings: houses, a barn and fields.
        private static BuildingsWorld BuildVillage()
        {
            var w = new BuildingsWorld(people: 40);
            w.Wood(5000);

            for (var i = 0; i < 20; i++)
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

        // Whether road cells lead from a door to the camp yard.
        private static bool ReachesYard(TerrainGrid grid, WorldPosition door)
        {
            var seen = new HashSet<WorldPosition> { door };
            var open = new Queue<WorldPosition>();
            open.Enqueue(door);

            while (open.Count > 0)
            {
                var at = open.Dequeue();

                if (Chebyshev(at, BuildingsWorld.Centre) <= Buildings.CampYardRadius)
                {
                    return true;
                }

                for (var dy = -1; dy <= 1; dy++)
                {
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        var next = new WorldPosition(at.X + dx, at.Y + dy);

                        if (grid.Contains(next) && !seen.Contains(next)
                            && (grid.RoadAt(next) != RoadGrade.None || Chebyshev(next, BuildingsWorld.Centre) <= Buildings.CampYardRadius))
                        {
                            seen.Add(next);
                            open.Enqueue(next);
                        }
                    }
                }
            }

            return false;
        }

        private static int Chebyshev(WorldPosition a, WorldPosition b) => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
    }
}
