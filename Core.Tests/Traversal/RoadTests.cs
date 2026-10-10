using System;
using System.Collections.Generic;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Traversal;
using NUnit.Framework;

namespace KingdomWatch.Core.Tests.Traversal
{
    /// <summary>
    /// Roads (#23): a grade laid over a cell's ground, with its own rule, that
    /// the pathfinder takes where it is cheaper.
    /// </summary>
    [TestFixture]
    public sealed class RoadTests
    {
        [Test]
        public void A_road_is_laid_over_ground_and_counts_as_a_rewrite()
        {
            var grid = new TerrainGrid(4, 4, TerrainKind.Forest);
            var at = new WorldPosition(2, 1);
            var changes = new List<int>();

            grid.SetRoad(at, RoadGrade.Track);

            Assert.Multiple(() =>
            {
                Assert.That(grid.RoadAt(at), Is.EqualTo(RoadGrade.Track));
                Assert.That(grid[at], Is.EqualTo(TerrainKind.Forest), "the ground is still there");
                Assert.That(grid.RoadAt(new WorldPosition(1, 1)), Is.EqualTo(RoadGrade.None));
                Assert.That(grid.Rewrites, Is.EqualTo(1L));
                Assert.That(grid.TryChangesSince(0L, changes), Is.True);
                Assert.That(changes, Is.EqualTo(new[] { grid.IndexOf(at) }));
            });

            grid.SetRoad(at, RoadGrade.None);
            Assert.That(grid.RoadAt(at), Is.EqualTo(RoadGrade.None), "lifted");
        }

        [Test]
        public void A_road_refuses_what_is_not_a_grade_or_a_cell()
        {
            var grid = new TerrainGrid(4, 4, TerrainKind.Plains);

            Assert.Multiple(() =>
            {
                Assert.That(() => grid.SetRoad(new WorldPosition(1, 1), (RoadGrade)200), Throws.InstanceOf<ArgumentOutOfRangeException>());
                Assert.That(() => grid.SetRoad(new WorldPosition(4, 1), RoadGrade.Track), Throws.InstanceOf<ArgumentOutOfRangeException>());
                Assert.That(() => grid.RoadAt(new WorldPosition(-1, 0)), Throws.InstanceOf<ArgumentOutOfRangeException>());
            });
        }

        [Test]
        public void Road_rules_are_looked_up_by_grade_and_none_adds_nothing()
        {
            var rules = TerrainRules.Default;

            Assert.Multiple(() =>
            {
                Assert.That(rules[RoadGrade.None].IsImpassable, Is.True);
                Assert.That(rules[RoadGrade.Track].Cost, Is.LessThan(rules[TerrainKind.Plains].Cost), "a track is quicker than plains");
                Assert.That(rules.CheapestCost, Is.EqualTo(rules[RoadGrade.Track].Cost), "the heuristic's floor counts roads");
                Assert.That(() => rules[(RoadGrade)200], Throws.InstanceOf<ArgumentOutOfRangeException>());
            });
        }

        [Test]
        public void A_table_without_road_rules_lets_roads_add_nothing()
        {
            var rules = new TerrainRules(Ground());

            Assert.Multiple(() =>
            {
                Assert.That(rules[RoadGrade.Track].IsImpassable, Is.True);
                Assert.That(rules.CheapestCost, Is.EqualTo(10));
            });
        }

        [Test]
        public void A_road_table_refuses_none_an_undefined_grade_and_a_grade_twice()
        {
            var track = new TerrainRule(6, Transport.Foot);

            Assert.Multiple(() =>
            {
                Assert.That(() => new TerrainRules(Ground(), null!), Throws.ArgumentNullException);
                Assert.That(() => new TerrainRules(Ground(), new[] { (RoadGrade.None, track) }), Throws.InstanceOf<ArgumentOutOfRangeException>());
                Assert.That(() => new TerrainRules(Ground(), new[] { ((RoadGrade)200, track) }), Throws.InstanceOf<ArgumentOutOfRangeException>());
                Assert.That(() => new TerrainRules(null!, Array.Empty<(RoadGrade, TerrainRule)>()), Throws.ArgumentNullException);
                Assert.That(() => new TerrainRules(Ground(), new[] { (RoadGrade.Track, track), (RoadGrade.Track, track) }), Throws.ArgumentException);
                Assert.That(
                    () => new TerrainRules(Ground(), new[] { (RoadGrade.Track, TerrainRule.Impassable), (RoadGrade.Track, TerrainRule.Impassable) }),
                    Throws.ArgumentException,
                    "twice is twice, whatever the rule");
            });
        }

        [Test]
        public void A_route_takes_a_road_where_it_is_cheaper_and_pays_the_road()
        {
            // Plains, with a track along the bottom row: walking the row
            // straight costs 10 a step; the track, one row down, 6.
            var grid = new TerrainGrid(9, 3, TerrainKind.Plains);

            for (var x = 0; x < 9; x++)
            {
                grid.SetRoad(new WorldPosition(x, 2), RoadGrade.Track);
            }

            var finder = new Pathfinder(grid, TerrainRules.Default);
            var route = new List<WorldPosition>();

            Assert.That(finder.TryFindRoute(new WorldPosition(0, 1), new WorldPosition(8, 1), Transport.Foot, route, out var cost), Is.True);

            Assert.Multiple(() =>
            {
                // Down onto the track, along it, and up: 14*6 + 6*10*6 + 14*10 = 584,
                // against 8*10*10 = 800 straight across.
                Assert.That(cost, Is.EqualTo(584L));
                Assert.That(finder.CostOfRoute(route, Transport.Foot), Is.EqualTo(cost));
                Assert.That(route.FindAll(at => at.Y == 2), Has.Count.EqualTo(7));
            });
        }

        [Test]
        public void A_road_over_a_river_opens_it_to_whoever_the_road_admits()
        {
            // What a bridge will be (#35): a grade whose rule admits feet,
            // laid over a river nobody can otherwise cross.
            var grid = new TerrainGrid(5, 1, TerrainKind.Plains);
            grid.Set(new WorldPosition(2, 0), TerrainKind.SmallRiver);
            var rules = new TerrainRules(Ground(), new[] { (RoadGrade.Track, new TerrainRule(10, Transport.Foot)) });
            var finder = new Pathfinder(grid, rules);
            var route = new List<WorldPosition>();

            Assert.That(finder.TryFindRoute(new WorldPosition(0, 0), new WorldPosition(4, 0), Transport.Foot, route, out _), Is.False);

            grid.SetRoad(new WorldPosition(2, 0), RoadGrade.Track);

            Assert.Multiple(() =>
            {
                Assert.That(finder.IsPassable(new WorldPosition(2, 0), Transport.Foot), Is.True);
                Assert.That(finder.TryFindRoute(new WorldPosition(0, 0), new WorldPosition(4, 0), Transport.Foot, route, out var cost), Is.True);
                Assert.That(cost, Is.EqualTo(400L));
                Assert.That(finder.IsPassable(new WorldPosition(2, 0), Transport.Boat), Is.False, "a road admits only what its rule does");
            });
        }

        [Test]
        public void With_roads_the_route_is_still_the_cheapest_there_is()
        {
            // The heuristic must not overestimate now that a step can cost
            // less than plains: A* to every cell agrees with Dijkstra's cost
            // to it, on a map with roads scattered over forest and plains.
            var grid = new TerrainGrid(12, 12, TerrainKind.Plains);

            for (var y = 0; y < 12; y++)
            {
                for (var x = 0; x < 12; x++)
                {
                    var at = new WorldPosition(x, y);

                    if ((x * 7 + y * 3) % 5 == 0)
                    {
                        grid.Set(at, TerrainKind.Forest);
                    }

                    if ((x + (2 * y)) % 4 == 0)
                    {
                        grid.SetRoad(at, RoadGrade.Track);
                    }
                }
            }

            var finder = new Pathfinder(grid, TerrainRules.Default);
            var route = new List<WorldPosition>();
            var mask = new bool[Enum.GetValues(typeof(TerrainKind)).Length];
            Array.Fill(mask, true);
            var from = new WorldPosition(0, 0);

            for (var cell = 0; cell < grid.CellCount; cell++)
            {
                Assert.That(finder.TryFindRoute(from, grid.PositionAt(cell), Transport.Foot, route, out var astar), Is.True);
                Assert.That(finder.TryFindNearest(from, Transport.Foot, mask, default, new Only(cell), 20, route, out var dijkstra), Is.True);
                Assert.That(astar, Is.EqualTo(dijkstra), grid.PositionAt(cell).ToString());
            }
        }

        [Test]
        public void A_gate_keeps_a_search_out_of_the_cells_it_refuses()
        {
            // A wall of refused cells down the middle, open at the bottom.
            var grid = new TerrainGrid(5, 5, TerrainKind.Plains);
            var finder = new Pathfinder(grid, TerrainRules.Default);
            var route = new List<WorldPosition>();
            var mask = new bool[Enum.GetValues(typeof(TerrainKind)).Length];
            Array.Fill(mask, true);
            var target = grid.IndexOf(new WorldPosition(4, 0));
            var wall = new Wall(grid);

            Assert.That(finder.TryFindNearest(new WorldPosition(0, 0), Transport.Foot, mask, default, new Only(target), wall, 10, route, out _), Is.True);

            Assert.Multiple(() =>
            {
                Assert.That(route.Exists(at => at.X == 2 && at.Y < 4), Is.False, "never through the wall");
                Assert.That(route, Has.Member(new WorldPosition(2, 4)));
            });
        }

        private static (TerrainKind, TerrainRule)[] Ground() => new[]
        {
            (TerrainKind.Plains, new TerrainRule(10, Transport.Foot)),
            (TerrainKind.Forest, new TerrainRule(20, Transport.Foot)),
            (TerrainKind.Rocks, new TerrainRule(30, Transport.Foot)),
            (TerrainKind.SmallRiver, TerrainRule.Impassable),
            (TerrainKind.DeepWater, new TerrainRule(10, Transport.Boat)),
            (TerrainKind.Scrub, new TerrainRule(15, Transport.Foot)),
        };

        private sealed class Only : ISiteFilter
        {
            private readonly int _cell;

            public Only(int cell) => _cell = cell;

            public bool Accepts(int cell) => cell == _cell;
        }

        private sealed class Wall : ISiteFilter
        {
            private readonly TerrainGrid _grid;

            public Wall(TerrainGrid grid) => _grid = grid;

            public bool Accepts(int cell)
            {
                var at = _grid.PositionAt(cell);
                return at.X != 2 || at.Y == 4;
            }
        }
    }
}
