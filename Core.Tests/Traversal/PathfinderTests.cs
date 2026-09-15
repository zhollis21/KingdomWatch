using System;
using System.Collections.Generic;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Traversal;
using NUnit.Framework;

namespace KingdomWatch.Core.Tests.Traversal
{
    [TestFixture]
    public sealed class PathfinderTests
    {
        private static readonly WorldPosition Origin = new WorldPosition(0, 0);

        /// <summary>
        /// Builds a grid from rows of characters, top row first: '.' plains,
        /// 'f' forest, 'h' hills, '~' small river, 'W' deep water.
        /// </summary>
        private static TerrainGrid Map(params string[] rows)
        {
            var grid = new TerrainGrid(rows[0].Length, rows.Length, TerrainKind.Plains);

            for (var y = 0; y < rows.Length; y++)
            {
                for (var x = 0; x < rows[y].Length; x++)
                {
                    grid.Set(new WorldPosition(x, y), rows[y][x] switch
                    {
                        '.' => TerrainKind.Plains,
                        'f' => TerrainKind.Forest,
                        'h' => TerrainKind.Hills,
                        '~' => TerrainKind.SmallRiver,
                        'W' => TerrainKind.DeepWater,
                        _ => throw new ArgumentException("Unknown cell " + rows[y][x], nameof(rows)),
                    });
                }
            }

            return grid;
        }

        private static (bool Found, int Cost, List<WorldPosition> Route) Find(
            TerrainGrid grid, WorldPosition from, WorldPosition to, Transport mover = Transport.Foot)
        {
            var route = new List<WorldPosition>();
            var found = new Pathfinder(grid, TerrainRules.Default).TryFindRoute(from, to, mover, route, out var cost);
            return (found, cost, route);
        }

        [Test]
        public void A_straight_walk_over_plains_costs_ten_per_cell_entered()
        {
            var (found, cost, route) = Find(Map("....."), Origin, new WorldPosition(4, 0));

            Assert.Multiple(() =>
            {
                Assert.That(found, Is.True);
                Assert.That(cost, Is.EqualTo(4 * 10 * 10));
                Assert.That(route, Is.EqualTo(new[]
                {
                    new WorldPosition(0, 0),
                    new WorldPosition(1, 0),
                    new WorldPosition(2, 0),
                    new WorldPosition(3, 0),
                    new WorldPosition(4, 0),
                }));
            });
        }

        [Test]
        public void A_diagonal_walk_costs_fourteen_per_cell_entered()
        {
            var (found, cost, route) = Find(Map("...", "...", "..."), Origin, new WorldPosition(2, 2));

            Assert.Multiple(() =>
            {
                Assert.That(found, Is.True);
                Assert.That(cost, Is.EqualTo(2 * 14 * 10));
                Assert.That(route, Is.EqualTo(new[]
                {
                    new WorldPosition(0, 0),
                    new WorldPosition(1, 1),
                    new WorldPosition(2, 2),
                }));
            });
        }

        [Test]
        public void From_equals_to_is_a_route_of_one_cell_at_no_cost()
        {
            var (found, cost, route) = Find(Map("..."), new WorldPosition(1, 0), new WorldPosition(1, 0));

            Assert.Multiple(() =>
            {
                Assert.That(found, Is.True);
                Assert.That(cost, Is.Zero);
                Assert.That(route, Is.EqualTo(new[] { new WorldPosition(1, 0) }));
            });
        }

        [Test]
        public void The_cheaper_route_wins_over_the_shorter_one()
        {
            // Straight through forest is 3 cells at 20 = 600; around it over
            // plains is a diagonal, two straights and a diagonal = 140 + 100
            // + 100 + 140 = 480. The cost table decides, not the cell count.
            var grid = Map(
                ".....",
                ".fff.",
                ".....");

            var (found, cost, route) = Find(grid, new WorldPosition(0, 1), new WorldPosition(4, 1));

            Assert.Multiple(() =>
            {
                Assert.That(found, Is.True);
                Assert.That(cost, Is.EqualTo(480));
                Assert.That(route, Has.None.Matches<WorldPosition>(p => grid[p] == TerrainKind.Forest));
            });
        }

        [Test]
        public void A_cell_opened_by_a_worse_path_is_improved_when_a_better_one_arrives()
        {
            // (1,1) is popped before (1,0) - it is diagonal-close to the goal -
            // and opens the hill at 140 + 14 * 30 = 560. Then (1,0) pops and
            // reaches the same hill at 200 + 10 * 30 = 500. Without the
            // decrease-key the goal keeps its first, worse cost.
            var grid = Map(
                ".fh",
                "...");

            var (found, cost, route) = Find(grid, Origin, new WorldPosition(2, 0));

            Assert.Multiple(() =>
            {
                Assert.That(found, Is.True);
                Assert.That(cost, Is.EqualTo(500));
                Assert.That(route, Is.EqualTo(new[]
                {
                    new WorldPosition(0, 0),
                    new WorldPosition(1, 0),
                    new WorldPosition(2, 0),
                }));
            });
        }

        [Test]
        public void A_river_is_walked_around_when_it_has_an_end()
        {
            var grid = Map(
                "..~..",
                "..~..",
                ".....");

            var (found, cost, route) = Find(grid, new WorldPosition(0, 0), new WorldPosition(4, 0));

            Assert.Multiple(() =>
            {
                Assert.That(found, Is.True);
                Assert.That(route, Has.None.Matches<WorldPosition>(p => grid[p] == TerrainKind.SmallRiver));
                Assert.That(route, Does.Contain(new WorldPosition(2, 2)));
                // The corner rule forbids the diagonal past the river's end, so the
                // walk drops to the bottom row: 14 + 10 + 10 + 10 + 14 + 10 at 10.
                Assert.That(cost, Is.EqualTo(680));
            });
        }

        [Test]
        public void An_unbroken_river_cannot_be_crossed_on_foot()
        {
            var grid = Map(
                "..~..",
                "..~..",
                "..~..");

            var (found, cost, route) = Find(grid, Origin, new WorldPosition(4, 2));

            Assert.Multiple(() =>
            {
                Assert.That(found, Is.False);
                Assert.That(cost, Is.Zero);
                Assert.That(route, Is.Empty);
            });
        }

        [Test]
        public void A_diagonal_river_cannot_be_cut_across_at_its_corners()
        {
            // The river runs (2,0) (1,1) (0,2). Without the corner rule the
            // walk (2,1) -> (1,0) squeezes between two river cells.
            var grid = Map(
                "..~",
                ".~.",
                "~..");

            var (found, _, _) = Find(grid, new WorldPosition(2, 2), Origin);

            Assert.That(found, Is.False);
        }

        [Test]
        public void A_diagonal_step_needs_both_cells_it_passes_between()
        {
            // One of the two cells beside the diagonal is a river; the step
            // (0,0) -> (1,1) is refused and the route goes around it.
            var grid = Map(
                ".~",
                "..");

            var (found, cost, route) = Find(grid, Origin, new WorldPosition(1, 1));

            Assert.Multiple(() =>
            {
                Assert.That(found, Is.True);
                Assert.That(route, Is.EqualTo(new[]
                {
                    new WorldPosition(0, 0),
                    new WorldPosition(0, 1),
                    new WorldPosition(1, 1),
                }));
                Assert.That(cost, Is.EqualTo(200));
            });
        }

        [Test]
        public void Deep_water_needs_a_boat()
        {
            var grid = Map(".W.");
            var from = Origin;
            var to = new WorldPosition(2, 0);

            var onFoot = Find(grid, from, to, Transport.Foot);
            var byBoat = Find(grid, new WorldPosition(1, 0), new WorldPosition(1, 0), Transport.Boat);
            var amphibious = Find(grid, from, to, Transport.Foot | Transport.Boat);

            Assert.Multiple(() =>
            {
                Assert.That(onFoot.Found, Is.False);
                Assert.That(byBoat.Found, Is.True);
                Assert.That(amphibious.Found, Is.True);
                Assert.That(amphibious.Cost, Is.EqualTo(200));
            });
        }

        [Test]
        public void An_impassable_endpoint_is_no_route_not_an_error()
        {
            var grid = Map(".~.");

            Assert.Multiple(() =>
            {
                Assert.That(Find(grid, Origin, new WorldPosition(1, 0)).Found, Is.False);
                Assert.That(Find(grid, new WorldPosition(1, 0), Origin).Found, Is.False);
            });
        }

        [Test]
        public void A_bridge_is_a_cell_rewrite_and_the_next_query_uses_it()
        {
            // Section 12: a bridge turns an impassable water edge into a cheap
            // land edge. The grid is mutable and the pathfinder holds no
            // cache, so that is the whole mechanism.
            var grid = Map(
                "..~..",
                "..~..",
                "..~..");
            var finder = new Pathfinder(grid, TerrainRules.Default);
            var route = new List<WorldPosition>();

            var before = finder.TryFindRoute(Origin, new WorldPosition(4, 0), Transport.Foot, route, out _);
            grid.Set(new WorldPosition(2, 1), TerrainKind.Plains);
            var after = finder.TryFindRoute(Origin, new WorldPosition(4, 0), Transport.Foot, route, out var cost);

            Assert.Multiple(() =>
            {
                Assert.That(before, Is.False);
                Assert.That(after, Is.True);
                Assert.That(route, Does.Contain(new WorldPosition(2, 1)));
                Assert.That(cost, Is.EqualTo(14 * 10 + 10 * 10 + 14 * 10 + 10 * 10));
            });
        }

        [Test]
        public void The_same_query_gives_the_same_route_every_time()
        {
            // Open plains is the worst case for ties: every route of the
            // same length costs the same, and the choice among them has to be
            // a property of the inputs rather than of heap history.
            var grid = new TerrainGrid(16, 16, TerrainKind.Plains);
            var finder = new Pathfinder(grid, TerrainRules.Default);
            var first = new List<WorldPosition>();
            var second = new List<WorldPosition>();
            var from = new WorldPosition(1, 14);
            var to = new WorldPosition(13, 2);

            finder.TryFindRoute(from, to, Transport.Foot, first, out var firstCost);
            finder.TryFindRoute(to, from, Transport.Foot, new List<WorldPosition>(), out _);
            finder.TryFindRoute(from, to, Transport.Foot, second, out var secondCost);
            var fresh = new List<WorldPosition>();
            new Pathfinder(grid, TerrainRules.Default).TryFindRoute(from, to, Transport.Foot, fresh, out _);

            Assert.Multiple(() =>
            {
                Assert.That(second, Is.EqualTo(first));
                Assert.That(fresh, Is.EqualTo(first));
                Assert.That(secondCost, Is.EqualTo(firstCost));
                Assert.That(firstCost, Is.EqualTo(12 * 14 * 10));
            });
        }

        [Test]
        public void A_route_is_always_a_chain_of_adjacent_passable_cells()
        {
            var grid = Map(
                "..f..h..",
                ".~~~~~..",
                ".f....~.",
                "...hh.~.",
                ".~~~..~.",
                "........");
            var (found, _, route) = Find(grid, new WorldPosition(0, 0), new WorldPosition(7, 5));

            Assert.That(found, Is.True);

            for (var i = 1; i < route.Count; i++)
            {
                var dx = Math.Abs(route[i].X - route[i - 1].X);
                var dy = Math.Abs(route[i].Y - route[i - 1].Y);

                Assert.Multiple(() =>
                {
                    Assert.That(Math.Max(dx, dy), Is.EqualTo(1), "step " + i);
                    Assert.That(grid[route[i]], Is.Not.EqualTo(TerrainKind.SmallRiver), "step " + i);
                });
            }
        }

        [Test]
        public void The_route_list_is_cleared_before_it_is_written()
        {
            var grid = Map("...");
            var route = new List<WorldPosition> { new WorldPosition(9, 9) };

            new Pathfinder(grid, TerrainRules.Default).TryFindRoute(
                Origin, new WorldPosition(2, 0), Transport.Foot, route, out _);

            Assert.That(route, Does.Not.Contain(new WorldPosition(9, 9)));
        }

        [Test]
        public void Is_passable_reads_the_table_for_a_cell()
        {
            var finder = new Pathfinder(Map(".~W"), TerrainRules.Default);

            Assert.Multiple(() =>
            {
                Assert.That(finder.IsPassable(new WorldPosition(0, 0), Transport.Foot), Is.True);
                Assert.That(finder.IsPassable(new WorldPosition(1, 0), Transport.Foot), Is.False);
                Assert.That(finder.IsPassable(new WorldPosition(2, 0), Transport.Foot), Is.False);
                Assert.That(finder.IsPassable(new WorldPosition(2, 0), Transport.Boat), Is.True);
                Assert.That(
                    () => finder.IsPassable(new WorldPosition(3, 0), Transport.Foot),
                    Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(
                    () => finder.IsPassable(Origin, Transport.None),
                    Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(
                    () => finder.IsPassable(Origin, (Transport)4),
                    Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(
                    () => finder.IsPassable(Origin, (Transport)(-1)),
                    Throws.TypeOf<ArgumentOutOfRangeException>());
            });
        }

        [Test]
        public void Off_map_endpoints_and_undefined_movers_are_errors()
        {
            var finder = new Pathfinder(Map("..."), TerrainRules.Default);
            var route = new List<WorldPosition>();

            Assert.Multiple(() =>
            {
                Assert.That(
                    () => finder.TryFindRoute(new WorldPosition(3, 0), Origin, Transport.Foot, route, out _),
                    Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(
                    () => finder.TryFindRoute(Origin, new WorldPosition(0, -1), Transport.Foot, route, out _),
                    Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(
                    () => finder.TryFindRoute(Origin, Origin, Transport.None, route, out _),
                    Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(
                    () => finder.TryFindRoute(Origin, Origin, (Transport)4, route, out _),
                    Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(
                    () => finder.TryFindRoute(Origin, Origin, (Transport)(-1), route, out _),
                    Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(
                    () => finder.TryFindRoute(Origin, Origin, Transport.Foot, null!, out _),
                    Throws.ArgumentNullException);
            });
        }

        [Test]
        public void A_pathfinder_needs_a_grid_and_a_table()
        {
            Assert.Multiple(() =>
            {
                Assert.That(() => new Pathfinder(null!, TerrainRules.Default), Throws.ArgumentNullException);
                Assert.That(() => new Pathfinder(Map("."), null!), Throws.ArgumentNullException);
            });
        }
    }
}
