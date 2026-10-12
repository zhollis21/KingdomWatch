using System;
using System.Collections.Generic;
using System.Linq;
using KingdomWatch.Core.Construction;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Events;
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

                if (lane.Count > 0 && Buildings.HasDoor(building.Kind))
                {
                    Assert.That(lane[0], Is.EqualTo(Buildings.DoorOf(building.Kind, building.Anchor)), building + "'s lane starts at its door");
                }
                else if (lane.Count > 0)
                {
                    Assert.That(Beside(building, lane[0]), Is.True, building + "'s lane starts beside one of its edges");
                }

                for (var i = 0; i < lane.Count; i++)
                {
                    Assert.That(w.Buildings.At(lane[i]), Is.Null, building + "'s lane at " + lane[i]);

                    if (i > 0)
                    {
                        Assert.That(Manhattan(lane[i], lane[i - 1]), Is.EqualTo(1), building + "'s lane shares an edge with its last cell at " + lane[i]);
                    }
                }
            }
        }

        [Test]
        public void Every_field_touches_a_road_along_an_edge()
        {
            var w = BuildVillage();
            var grid = w.World.Grid;
            var fields = w.Buildings.All.Where(b => b.Kind == BuildingKind.Field).ToList();
            Assert.That(fields, Has.Count.GreaterThanOrEqualTo(2), "fields to check");

            foreach (var field in fields)
            {
                var touching = Around(field).Where(at => grid.Contains(at) && grid.RoadAt(at) != RoadGrade.None).ToList();
                Assert.That(touching, Is.Not.Empty, field + " has a road along an edge");
                Assert.That(touching.Any(at => ReachesYard(grid, at)), Is.True, field + "'s road leads to the square");
            }
        }

        [Test]
        public void Fields_line_up_along_the_roads_already_there()
        {
            // Fields take the spots beside a road while there are any, and lay
            // a lane only once those near their barn are used up: a few of a
            // village's fields, not most. Without the pull toward roads half
            // of this village's fields lay lanes; with it, a fifth.
            var w = BuildVillage();
            var fields = w.Buildings.All.Where(b => b.Kind == BuildingKind.Field).ToList();
            Assert.That(fields, Has.Count.GreaterThanOrEqualTo(8), "fields to check");

            Assert.That(fields.Count(f => f.Lane.Count > 0) * 4, Is.LessThanOrEqualTo(fields.Count), "at most a quarter lay lanes");
        }

        [Test]
        public void A_field_with_no_road_beside_it_gets_a_lane_from_one_side()
        {
            // A house and a barn on open plains; then a strip of forest along
            // every road and the yard, so no plains spot for a field touches
            // a road and the first pass, plains only, has to lay a lane.
            var w = new BuildingsWorld();
            w.Wood(500);
            w.BuildNext();
            var barn = w.BuildNext();
            Assert.That(barn.Kind, Is.EqualTo(BuildingKind.Barn));
            var grid = w.World.Grid;
            var strip = new List<WorldPosition>();

            for (var y = 0; y < BuildingsWorld.Size; y++)
            {
                for (var x = 0; x < BuildingsWorld.Size; x++)
                {
                    var at = new WorldPosition(x, y);

                    if (grid[at] == TerrainKind.Plains && grid.RoadAt(at) == RoadGrade.None && w.Buildings.At(at) is null
                        && new[] { (1, 0), (-1, 0), (0, 1), (0, -1) }.Any(d => IsNetwork(grid, new WorldPosition(x + d.Item1, y + d.Item2))))
                    {
                        strip.Add(at);
                    }
                }
            }

            foreach (var at in strip)
            {
                grid.Set(at, TerrainKind.Forest);
            }

            w.Dawn();
            var field = w.Latest!;

            Assert.Multiple(() =>
            {
                Assert.That(field.Kind, Is.EqualTo(BuildingKind.Field));
                Assert.That(field.Lane, Is.Not.Empty, "a lane, since nothing it could touch is road");
                Assert.That(Beside(field, field.Lane[0]), Is.True, "starting beside one of its edges");
                Assert.That(field.ClearTicks, Is.GreaterThan(0L), "through the trees, priced in");
            });

            w.Finish(field);

            Assert.Multiple(() =>
            {
                Assert.That(Around(field).Any(at => grid.Contains(at) && grid.RoadAt(at) != RoadGrade.None), Is.True, "a road along an edge now");
                Assert.That(ReachesYard(grid, field.Lane[0]), Is.True);
            });
        }

        private static bool IsNetwork(TerrainGrid grid, WorldPosition at) =>
            grid.Contains(at) && (grid.RoadAt(at) != RoadGrade.None || Chebyshev(at, BuildingsWorld.Centre) <= Buildings.CampYardRadius);

        [Test]
        public void A_field_s_lane_leaves_from_its_cheapest_side_not_the_one_nearest_a_road()
        {
            // #169. The pocket's east side is four cells from the barn's door
            // as the crow flies, but walled off from it: its only way out is
            // eleven cells round to the house's lane. Its south side is five
            // from a road, straight down. The east side used to be tried first
            // and kept, its lane running down the field's own flank.
            var w = PocketWorld(PocketWest, PocketEastCorridor, PocketSouthCorridor);

            w.Dawn();
            var field = w.Latest!;

            Assert.Multiple(() =>
            {
                Assert.That(field.Kind, Is.EqualTo(BuildingKind.Field));
                Assert.That(field.Anchor, Is.EqualTo(PocketWest), "the only plains a field fits");
                Assert.That(field.Lane, Is.EqualTo(PocketSouthCorridor), "the short lane south");
            });
        }

        [Test]
        public void A_field_s_lane_leaves_from_its_cheapest_side_whichever_side_is_tried_first()
        {
            // Sides are tried south first; here the south lane winds thirteen
            // cells out to a road and the east one eleven, so the east wins.
            var w = PocketWorld(PocketWest, PocketEastCorridor, PocketSouthLongCorridor);

            w.Dawn();
            var field = w.Latest!;

            Assert.Multiple(() =>
            {
                Assert.That(field.Kind, Is.EqualTo(BuildingKind.Field));
                Assert.That(field.Anchor, Is.EqualTo(PocketWest));
                Assert.That(field.Lane, Is.EqualTo(PocketEastCorridor));
            });
        }

        [Test]
        public void A_field_goes_where_its_real_lane_is_short_not_where_a_road_only_looks_near()
        {
            // #169. Two pockets. The one by the barn scores better as the crow
            // flies - four cells from the barn's door - but its lane has to run
            // eleven cells round; the one in the corner looks six from a road
            // and its lane is eight. Joined and rescored, the corner wins.
            var w = PocketWorld(PocketWest, PocketEastCorridor, PocketCorner, PocketCornerCorridor);

            w.Dawn();
            var field = w.Latest!;

            Assert.Multiple(() =>
            {
                Assert.That(field.Kind, Is.EqualTo(BuildingKind.Field));
                Assert.That(field.Anchor, Is.EqualTo(PocketCorner));
                Assert.That(field.Lane, Is.EqualTo(PocketCornerCorridor));
            });
        }

        [Test]
        public void A_field_keeps_the_site_by_the_barn_when_the_other_s_lane_runs_further_still()
        {
            // The same two pockets, the corner's lane now thirteen cells: worse
            // than the eleven by the barn once both are rescored, so the site
            // that scored best as the crow flies keeps it.
            var w = PocketWorld(PocketWest, PocketEastCorridor, PocketCorner, PocketCornerLongCorridor);

            w.Dawn();
            var field = w.Latest!;

            Assert.Multiple(() =>
            {
                Assert.That(field.Kind, Is.EqualTo(BuildingKind.Field));
                Assert.That(field.Anchor, Is.EqualTo(PocketWest));
                Assert.That(field.Lane, Is.EqualTo(PocketEastCorridor));
            });
        }

        [Test]
        public void A_field_whose_only_site_needs_a_long_lane_is_still_laid_out()
        {
            // A long lane makes a site worse, never refused: a barn with only
            // such ground left still gets its field, and the village its grain.
            var w = PocketWorld(PocketWest, PocketEastCorridor);

            w.Dawn();
            var field = w.Latest!;

            Assert.Multiple(() =>
            {
                Assert.That(field.Kind, Is.EqualTo(BuildingKind.Field));
                Assert.That(field.Anchor, Is.EqualTo(PocketWest));
                Assert.That(field.Lane, Is.EqualTo(PocketEastCorridor));
            });
        }

        [Test]
        public void A_field_with_no_side_a_lane_can_leave_from_is_not_laid_out()
        {
            // The pocket alone, rocks on every side of it: nowhere for a lane
            // to start, however near the barn's door it looks.
            var w = PocketWorld(PocketWest);

            w.Dawn();

            Assert.That(w.Buildings.All.Where(b => b.Kind == BuildingKind.Field), Is.Empty);
        }

        [Test]
        public void A_field_s_lane_never_runs_along_another_of_its_sides()
        {
            // What a lane round its own field looks like from outside: cells
            // beside the field on more than the side it left from.
            var w = BuildVillage();
            var fields = w.Buildings.All.Where(b => b.Kind == BuildingKind.Field && b.Lane.Count > 0).ToList();
            Assert.That(fields, Is.Not.Empty, "fields with lanes to check");

            foreach (var field in fields)
            {
                var start = SideOf(field, field.Lane[0]);

                foreach (var at in field.Lane.Where(at => Beside(field, at)))
                {
                    Assert.That(SideOf(field, at), Is.EqualTo(start), field + "'s lane at " + at);
                }
            }
        }

        // Which side of a footprint a cell beside it is on: 0 north, 1 south,
        // 2 west, 3 east.
        private static int SideOf(Building building, WorldPosition at) =>
            at.Y < building.Anchor.Y ? 0
            : at.Y >= building.Anchor.Y + building.Height ? 1
            : at.X < building.Anchor.X ? 2
            : 3;

        // The pocket worlds (#169) start as the usual village's first house
        // and barn - the barn at (5, 4), its door's lane east and down to
        // the house's at (14, 9..17) - then turn every other cell to rocks,
        // which a field cannot stand on and a lane cannot cross, and carve
        // back the cells given: a 5x5 pocket at an anchor, or a corridor
        // ending on a road. Only a pocket fits a field.
        private static readonly WorldPosition PocketWest = new WorldPosition(2, 11);

        private static readonly WorldPosition PocketCorner = new WorldPosition(0, 0);

        // From the west pocket's east side round to the house's lane at (14, 17).
        private static readonly WorldPosition[] PocketEastCorridor = Line((7, 13), (7, 17)).Concat(Line((8, 17), (13, 17))).ToArray();

        // From the west pocket's south side straight down to a road at (4, 21).
        private static readonly WorldPosition[] PocketSouthCorridor = Line((4, 16), (4, 20)).ToArray();

        // From the west pocket's south side down and west to a road at (0, 25).
        private static readonly WorldPosition[] PocketSouthLongCorridor = Line((4, 16), (4, 22)).Concat(Line((3, 22), (0, 22))).Concat(Line((0, 23), (0, 24))).ToArray();

        // From the corner pocket's south side down and west to a road at (0, 11).
        private static readonly WorldPosition[] PocketCornerCorridor = Line((2, 5), (2, 9)).Concat(Line((1, 9), (0, 9))).Concat(Line((0, 10), (0, 10))).ToArray();

        // The same, carried on down the west edge to a road at (0, 16).
        private static readonly WorldPosition[] PocketCornerLongCorridor = Line((2, 5), (2, 9)).Concat(Line((1, 9), (0, 9))).Concat(Line((0, 10), (0, 15))).ToArray();

        private static BuildingsWorld PocketWorld(params object[] carve)
        {
            var w = new BuildingsWorld();
            w.Wood(500);
            w.BuildNext();
            var barn = w.BuildNext();
            Assert.That(barn.Kind, Is.EqualTo(BuildingKind.Barn));
            Assert.That(barn.Anchor, Is.EqualTo(new WorldPosition(5, 4)), "the barn the pockets are drawn round");
            var grid = w.World.Grid;

            for (var y = 0; y < BuildingsWorld.Size; y++)
            {
                for (var x = 0; x < BuildingsWorld.Size; x++)
                {
                    var at = new WorldPosition(x, y);

                    if (grid.RoadAt(at) == RoadGrade.None && w.Buildings.At(at) is null)
                    {
                        grid.Set(at, TerrainKind.Rocks);
                    }
                }
            }

            foreach (var piece in carve)
            {
                if (piece is WorldPosition anchor)
                {
                    for (var dy = 0; dy < 5; dy++)
                    {
                        for (var dx = 0; dx < 5; dx++)
                        {
                            grid.Set(new WorldPosition(anchor.X + dx, anchor.Y + dy), TerrainKind.Plains);
                        }
                    }
                }
                else
                {
                    var corridor = (WorldPosition[])piece;

                    foreach (var at in corridor)
                    {
                        grid.Set(at, TerrainKind.Plains);
                    }

                    // The road it ends on, one step on from its last cell,
                    // unless that is road already.
                    var last = corridor[corridor.Length - 1];
                    var before = corridor.Length > 1 ? corridor[corridor.Length - 2] : last;
                    var end = new WorldPosition(last.X + (last.X - before.X), last.Y + (last.Y - before.Y));

                    if (grid.RoadAt(end) == RoadGrade.None)
                    {
                        grid.Set(end, TerrainKind.Plains);
                        grid.SetRoad(end, RoadGrade.Track);
                    }
                }
            }

            return w;
        }

        // The cells from one end to the other along a row or a column.
        private static IEnumerable<WorldPosition> Line((int X, int Y) from, (int X, int Y) to)
        {
            var steps = Math.Max(Math.Abs(to.X - from.X), Math.Abs(to.Y - from.Y));

            for (var i = 0; i <= steps; i++)
            {
                yield return new WorldPosition(from.X + (Math.Sign(to.X - from.X) * i), from.Y + (Math.Sign(to.Y - from.Y) * i));
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
            // A bush and a tree in the yard, which only a building's work clears.
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
        public void The_next_building_clears_the_square_and_paves_it_when_cleared()
        {
            // A bush, a tree and an outcrop in the yard.
            var bush = new WorldPosition(BuildingsWorld.Centre.X + 1, BuildingsWorld.Centre.Y);
            var tree = new WorldPosition(BuildingsWorld.Centre.X, BuildingsWorld.Centre.Y + 2);
            var rock = new WorldPosition(BuildingsWorld.Centre.X - 2, BuildingsWorld.Centre.Y - 1);
            var w = new BuildingsWorld(paint: grid =>
            {
                grid.Set(bush, TerrainKind.Scrub);
                grid.Set(tree, TerrainKind.Forest);
                grid.Set(rock, TerrainKind.Rocks);
            });
            var grid = w.World.Grid;
            var cuts = w.World.Land.CutsLeft(tree);
            w.Wood(100);
            w.Dawn();

            var house = w.Buildings.All.Single();

            Assert.Multiple(() =>
            {
                Assert.That(house.Square, Is.EquivalentTo(new[] { bush, tree, rock }));
                Assert.That(
                    house.ClearTicks,
                    Is.EqualTo(Buildings.ClearTicksPerCell + (cuts * Buildings.TicksPerCut) + Buildings.TicksPerRock),
                    "on top of a plains footprint and lane");
                Assert.That(Buildings.TicksPerRock, Is.EqualTo(3 * PrimitiveTier.GatherStone.Duration), "three quarrying trips");
                Assert.That(house.ClearCuts, Is.EqualTo(cuts));
                Assert.That(house.ClearRocks, Is.EqualTo(1));
                Assert.That(new[] { bush, tree, rock }.All(w.Buildings.IsReserved), Is.True, "nobody picks, fells or quarries them meanwhile");
                Assert.That(grid.RoadAt(bush), Is.EqualTo(RoadGrade.None), "not paved before it is cleared");
                Assert.That(grid.RoadAt(rock), Is.EqualTo(RoadGrade.None), "nor the outcrop");
            });

            var stone = w.Stores.Available(ResourceKind.Stone);
            var wood = w.Stores.Available(ResourceKind.Wood);
            w.Finish(house);
            var stoneGained = w.Stores.Available(ResourceKind.Stone) - stone;
            var woodGained = w.Stores.Available(ResourceKind.Wood) - wood;
            w.Dawn();

            Assert.Multiple(() =>
            {
                Assert.That(stoneGained, Is.EqualTo(Buildings.StonePerRock));
                Assert.That(Buildings.StonePerRock, Is.EqualTo(3 * PrimitiveTier.GatherStone.Outputs[0].Quantity), "what three trips bring");
                Assert.That(woodGained, Is.EqualTo(cuts * Buildings.WoodPerCut));
                Assert.That(w.Buildings.All.Skip(1).All(b => b.Square.Count == 0), Is.True, "the square is taken on once");

                foreach (var at in new[] { bush, tree, rock })
                {
                    Assert.That(grid[at], Is.EqualTo(TerrainKind.Plains), at.ToString());
                    Assert.That(grid.RoadAt(at), Is.EqualTo(RoadGrade.Track), at.ToString());
                    Assert.That(w.Buildings.IsReserved(at), Is.False, at.ToString());
                }
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
        public void A_building_s_lane_and_square_cannot_be_changed_through_what_it_hands_out()
        {
            // Both are fixed at approval: reservations, clearing and the hash
            // all read them, so a downcast must not reach the cells.
            // A belt of forest round the camp, so the house's lane runs out
            // through it, and a bush in the yard for its share of the square.
            var bush = new WorldPosition(BuildingsWorld.Centre.X + 1, BuildingsWorld.Centre.Y);
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

                grid.Set(bush, TerrainKind.Scrub);
            });
            w.Wood(100);
            w.Dawn();
            var house = w.Buildings.All.Single();
            Assert.That(house.Lane, Is.Not.Empty);
            Assert.That(house.Square, Is.Not.Empty);

            Assert.Multiple(() =>
            {
                Assert.That(house.Lane, Is.Not.InstanceOf<WorldPosition[]>());
                Assert.That(house.Square, Is.Not.InstanceOf<WorldPosition[]>());
                Assert.That(((IList<WorldPosition>)house.Lane).IsReadOnly, Is.True);
                Assert.That(((IList<WorldPosition>)house.Square).IsReadOnly, Is.True);
            });
        }

        [Test]
        public void Whether_a_kind_has_a_door_refuses_what_is_not_a_kind()
        {
            Assert.Multiple(() =>
            {
                Assert.That(() => Buildings.HasDoor(BuildingKind.None), Throws.InstanceOf<ArgumentOutOfRangeException>());
                Assert.That(() => Buildings.HasDoor((BuildingKind)200), Throws.InstanceOf<ArgumentOutOfRangeException>());
                Assert.That(Buildings.HasDoor(BuildingKind.House), Is.True);
            });
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

        [Test]
        public void A_house_goes_further_out_only_when_nothing_fits_near_the_centre()
        {
            var w = new BuildingsWorld(size: 100, paint: PaintCrowdedCentre);
            w.Wood(100);

            w.Dawn();

            var house = w.Buildings.All.Single();
            Assert.Multiple(() =>
            {
                Assert.That(house.Kind, Is.EqualTo(BuildingKind.House));
                Assert.That(Chebyshev(house.Anchor, BuildingsWorld.Centre), Is.GreaterThan(CrowdedRadius), "past the rocks, where nothing stood in the way");
                Assert.That(Chebyshev(house.Anchor, BuildingsWorld.Centre), Is.GreaterThan(2 * Buildings.NearRadius), "past the box work's builders used to reach");
            });
        }

        [Test]
        public void Builders_walk_out_to_a_house_however_far_it_had_to_go()
        {
            var w = new BuildingsWorld(size: 100, paint: PaintCrowdedCentre);
            w.Wood(5000);
            w.Stores.Gather(ResourceKind.Food, 100000);
            w.Stores.Gather(ResourceKind.Stone, 1000);
            w.Dawn();
            var house = w.Buildings.All.Single();
            w.World.Jobs.Track(w.Settlement);

            // The first dawn: stores full, so the house is the only need.
            w.World.AdvanceTo(KingdomWatch.Core.Clock.SimulationTime.Zero.Plus(KingdomWatch.Core.Work.Jobs.Dawn + 1L));

            Assert.That(w.World.Jobs.OnDuty(w.Settlement, JobKind.Builder), Is.GreaterThan(0), "hands set out to build " + house);
        }

        // Rocks round the camp out to CrowdedRadius, which no footprint or
        // lane takes, with a track running east through them: the only room
        // for a house is past the rocks, joined to the yard along the track.
        private const int CrowdedRadius = 36;

        private static void PaintCrowdedCentre(TerrainGrid grid)
        {
            for (var y = 0; y < grid.Height; y++)
            {
                for (var x = 0; x < grid.Width; x++)
                {
                    var at = new WorldPosition(x, y);
                    var ring = Chebyshev(at, BuildingsWorld.Centre);

                    if (ring > Buildings.CampYardRadius && ring <= CrowdedRadius)
                    {
                        grid.Set(at, TerrainKind.Rocks);
                    }
                }
            }

            for (var x = BuildingsWorld.Centre.X + Buildings.CampYardRadius + 1; x <= BuildingsWorld.Centre.X + CrowdedRadius + 4; x++)
            {
                var at = new WorldPosition(x, BuildingsWorld.Centre.Y);
                grid.Set(at, TerrainKind.Plains);
                grid.SetRoad(at, RoadGrade.Track);
            }
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

        // Whether road cells lead from a door to the camp yard, each sharing
        // an edge with the next: a strip, not cells meeting at corners.
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

                foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
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

            return false;
        }

        // The cells along a footprint's edges, outside it: north, south,
        // east and west of it, not its corners.
        private static IEnumerable<WorldPosition> Around(Building building)
        {
            for (var dx = 0; dx < building.Width; dx++)
            {
                yield return new WorldPosition(building.Anchor.X + dx, building.Anchor.Y - 1);
                yield return new WorldPosition(building.Anchor.X + dx, building.Anchor.Y + building.Height);
            }

            for (var dy = 0; dy < building.Height; dy++)
            {
                yield return new WorldPosition(building.Anchor.X - 1, building.Anchor.Y + dy);
                yield return new WorldPosition(building.Anchor.X + building.Width, building.Anchor.Y + dy);
            }
        }

        private static bool Beside(Building building, WorldPosition at) => Around(building).Contains(at);

        private static int Manhattan(WorldPosition a, WorldPosition b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);

        private static int Chebyshev(WorldPosition a, WorldPosition b) => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
    }
}
