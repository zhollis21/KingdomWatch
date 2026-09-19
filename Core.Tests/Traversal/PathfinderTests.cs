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

        private static (bool Found, long Cost, List<WorldPosition> Route) Find(
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
        public void A_route_cost_beyond_an_int_is_summed_correctly()
        {
            // 220,000 cells of a max-cost kind in a strip: 219,999 steps at
            // 10 * 1000 is 2.2 billion, past int.MaxValue. The grid only bounds
            // its cell count, so the sum has to be a long.
            const int length = 220_000;
            var rules = new TerrainRules(
                (TerrainKind.Plains, new TerrainRule(TerrainRule.MaxCost, Transport.Foot)),
                (TerrainKind.Forest, new TerrainRule(20, Transport.Foot)),
                (TerrainKind.Hills, new TerrainRule(30, Transport.Foot)),
                (TerrainKind.SmallRiver, TerrainRule.Impassable),
                (TerrainKind.DeepWater, new TerrainRule(10, Transport.Boat)));
            var grid = new TerrainGrid(length, 1, TerrainKind.Plains);
            var route = new List<WorldPosition>();

            var found = new Pathfinder(grid, rules).TryFindRoute(
                Origin, new WorldPosition(length - 1, 0), Transport.Foot, route, out var cost);

            Assert.Multiple(() =>
            {
                Assert.That(found, Is.True);
                Assert.That(cost, Is.EqualTo((length - 1) * 10L * TerrainRule.MaxCost));
                Assert.That(cost, Is.GreaterThan(int.MaxValue));
                Assert.That(route, Has.Count.EqualTo(length));
            });
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
        public void A_route_is_priced_by_the_cells_it_enters_so_the_way_back_can_differ()
        {
            // Out: three plains and a forest entered, 500. Back: three plains
            // and the plains start cell, 400. The cost of a walk depends on
            // which end you start from.
            var grid = Map("....f");
            var finder = new Pathfinder(grid, TerrainRules.Default);
            var (found, cost, route) = Find(grid, Origin, new WorldPosition(4, 0));
            var back = new List<WorldPosition>(route);
            back.Reverse();

            Assert.Multiple(() =>
            {
                Assert.That(found, Is.True);
                Assert.That(finder.CostOfRoute(route, Transport.Foot), Is.EqualTo(cost), "pricing the route the search found gives the search's cost");
                Assert.That(cost, Is.EqualTo(500L));
                Assert.That(finder.CostOfRoute(back, Transport.Foot), Is.EqualTo(400L));
            });
        }

        [Test]
        public void A_diagonal_step_in_a_route_is_priced_at_fourteen()
        {
            var finder = new Pathfinder(Map("..", ".."), TerrainRules.Default);
            var route = new[] { Origin, new WorldPosition(1, 1) };

            Assert.That(finder.CostOfRoute(route, Transport.Foot), Is.EqualTo(140L));
        }

        [Test]
        public void A_route_of_one_cell_costs_nothing_to_walk()
        {
            var finder = new Pathfinder(Map("."), TerrainRules.Default);

            Assert.That(finder.CostOfRoute(new[] { Origin }, Transport.Foot), Is.Zero);
        }

        [Test]
        public void Pricing_refuses_a_route_that_is_not_a_walk()
        {
            var finder = new Pathfinder(Map("..~.", "...W", "..W."), TerrainRules.Default);

            Assert.Multiple(() =>
            {
                Assert.That(() => finder.CostOfRoute(null!, Transport.Foot), Throws.ArgumentNullException);
                Assert.That(() => finder.CostOfRoute(Array.Empty<WorldPosition>(), Transport.Foot), Throws.ArgumentException, "no cells");
                Assert.That(() => finder.CostOfRoute(new[] { Origin, new WorldPosition(3, 0) }, Transport.Foot), Throws.ArgumentException, "a jump");
                Assert.That(() => finder.CostOfRoute(new[] { Origin, Origin }, Transport.Foot), Throws.ArgumentException, "standing still is not a step");
                Assert.That(() => finder.CostOfRoute(new[] { new WorldPosition(1, 0), new WorldPosition(2, 0) }, Transport.Foot), Throws.ArgumentException, "into a river");
                Assert.That(() => finder.CostOfRoute(new[] { new WorldPosition(2, 0), new WorldPosition(3, 0) }, Transport.Foot), Throws.ArgumentException, "out of a river");
                Assert.That(() => finder.CostOfRoute(new[] { new WorldPosition(3, 1), new WorldPosition(2, 2) }, Transport.Boat), Throws.ArgumentException, "a diagonal cutting a corner past land, by boat");                Assert.That(() => finder.CostOfRoute(new[] { Origin, new WorldPosition(9, 0) }, Transport.Foot), Throws.TypeOf<ArgumentOutOfRangeException>(), "off the map");
                Assert.That(() => finder.CostOfRoute(new[] { Origin, new WorldPosition(1, 0) }, Transport.None), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => finder.CostOfRoute(new[] { Origin, new WorldPosition(1, 0) }, (Transport)4), Throws.TypeOf<ArgumentOutOfRangeException>());
            });
        }

        // A mask over TerrainKind: which cells count as "found".
        private static bool[] Wanting(params TerrainKind[] kinds)
        {
            var mask = new bool[(int)TerrainKind.DeepWater + 1];

            foreach (var kind in kinds)
            {
                mask[(int)kind] = true;
            }

            return mask;
        }

        private static (bool Found, long Cost, List<WorldPosition> Route) Nearest(
            Pathfinder finder, WorldPosition from, bool[] wanted, int radius = 16, Transport mover = Transport.Foot)
        {
            var route = new List<WorldPosition>();
            var found = finder.TryFindNearest(from, mover, wanted, radius, route, out var cost);
            return (found, cost, route);
        }

        [Test]
        public void The_nearest_site_is_the_cheapest_to_reach_not_the_fewest_cells_away()
        {
            // With hills at the maximum cost, the forest two cells away sits
            // behind a wall of them and costs 760 by the way round; the
            // forest three cells away costs 540 over plains. Rings would
            // pick the first; the search picks the second.
            var grid = Map(
                ".hf.",
                ".hh.",
                "...f");
            var rules = new TerrainRules(
                (TerrainKind.Plains, new TerrainRule(10, Transport.Foot)),
                (TerrainKind.Forest, new TerrainRule(20, Transport.Foot)),
                (TerrainKind.Hills, new TerrainRule(TerrainRule.MaxCost, Transport.Foot)),
                (TerrainKind.SmallRiver, TerrainRule.Impassable),
                (TerrainKind.DeepWater, TerrainRule.Impassable));
            var finder = new Pathfinder(grid, rules);

            var (found, cost, route) = Nearest(finder, Origin, Wanting(TerrainKind.Forest));

            Assert.Multiple(() =>
            {
                Assert.That(found, Is.True);
                Assert.That(route[^1], Is.EqualTo(new WorldPosition(3, 2)));
                Assert.That(cost, Is.EqualTo(540L));
                Assert.That(route[0], Is.EqualTo(Origin));
                Assert.That(finder.CostOfRoute(route, Transport.Foot), Is.EqualTo(cost), "the route is priced the way the search priced it");
            });
        }

        [Test]
        public void A_site_underfoot_is_a_route_of_one_cell_at_no_cost()
        {
            var finder = new Pathfinder(Map("f."), TerrainRules.Default);

            var (found, cost, route) = Nearest(finder, Origin, Wanting(TerrainKind.Forest));

            Assert.Multiple(() =>
            {
                Assert.That(found, Is.True);
                Assert.That(cost, Is.Zero);
                Assert.That(route, Is.EqualTo(new[] { Origin }));
            });
        }

        [Test]
        public void Equal_cost_sites_are_settled_by_cell_index()
        {
            // East and south are both one straight step onto forest; the
            // lower index - the row above - wins, on every platform.
            var finder = new Pathfinder(Map(".f", "f."), TerrainRules.Default);

            var (_, cost, route) = Nearest(finder, Origin, Wanting(TerrainKind.Forest));

            Assert.Multiple(() =>
            {
                Assert.That(cost, Is.EqualTo(200L));
                Assert.That(route[^1], Is.EqualTo(new WorldPosition(1, 0)));
            });
        }

        [Test]
        public void The_radius_bounds_the_search_as_a_box_around_the_origin()
        {
            var grid = new TerrainGrid(12, 3, TerrainKind.Plains);
            grid.Set(new WorldPosition(4, 1), TerrainKind.Forest);
            var finder = new Pathfinder(grid, TerrainRules.Default);
            var from = new WorldPosition(0, 1);

            Assert.Multiple(() =>
            {
                Assert.That(Nearest(finder, from, Wanting(TerrainKind.Forest), radius: 4).Found, Is.True, "on the edge of the box");
                Assert.That(Nearest(finder, from, Wanting(TerrainKind.Forest), radius: 3).Found, Is.False, "one past it");
                Assert.That(Nearest(finder, from, Wanting(TerrainKind.Forest), radius: 0).Found, Is.False, "only underfoot");
                Assert.That(Nearest(finder, from, Wanting(TerrainKind.Plains), radius: 0).Found, Is.True);
            });
        }

        [Test]
        public void A_site_nothing_connects_to_is_not_found_and_neither_is_one_from_nowhere()
        {
            var finder = new Pathfinder(Map("..~f", "..~.", "..~."), TerrainRules.Default);

            Assert.Multiple(() =>
            {
                var (found, cost, route) = Nearest(finder, Origin, Wanting(TerrainKind.Forest));
                Assert.That(found, Is.False, "across the river");
                Assert.That(cost, Is.Zero);
                Assert.That(route, Is.Empty);
                Assert.That(Nearest(finder, new WorldPosition(2, 0), Wanting(TerrainKind.Plains)).Found, Is.False, "standing in the river");
                Assert.That(Nearest(finder, Origin, Wanting(TerrainKind.Hills)).Found, Is.False, "nothing of the kind");
            });
        }

        [Test]
        public void The_nearest_search_refuses_what_it_cannot_search()
        {
            var finder = new Pathfinder(Map(".."), TerrainRules.Default);
            var route = new List<WorldPosition> { new WorldPosition(9, 9) };
            var forest = Wanting(TerrainKind.Forest);

            Assert.Multiple(() =>
            {
                Assert.That(() => finder.TryFindNearest(Origin, Transport.Foot, forest, 1, null!, out _), Throws.ArgumentNullException);
                Assert.That(() => finder.TryFindNearest(Origin, Transport.Foot, new bool[2], 1, route, out _), Throws.ArgumentException, "a mask too short to cover every kind");
                Assert.That(() => finder.TryFindNearest(Origin, Transport.Foot, forest, -1, route, out _), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => finder.TryFindNearest(new WorldPosition(5, 0), Transport.Foot, forest, 1, route, out _), Throws.TypeOf<ArgumentOutOfRangeException>(), "off the map");
                Assert.That(() => finder.TryFindNearest(Origin, Transport.None, forest, 1, route, out _), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => finder.TryFindNearest(Origin, (Transport)4, forest, 1, route, out _), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(finder.TryFindNearest(Origin, Transport.Foot, forest, 1, route, out _), Is.False);
                Assert.That(route, Is.Empty, "cleared even when nothing is found");
            });
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

        [Test]
        public void A_known_mask_restricts_what_counts_as_found()
        {
            // Two forests, the near one unknown: section 12's rule at the one
            // point that decides what a place-picking search may land on.
            var grid = Map(
                ".f..f",
                ".....");
            var finder = new Pathfinder(grid, TerrainRules.Default);
            var route = new List<WorldPosition>();
            var forest = TerrainMask(TerrainKind.Forest);
            var known = new bool[grid.CellCount];
            known[grid.IndexOf(new WorldPosition(4, 0))] = true;

            var found = finder.TryFindNearest(Origin, Transport.Foot, forest, known, 8, route, out _);

            Assert.Multiple(() =>
            {
                Assert.That(found, Is.True);
                Assert.That(route[route.Count - 1], Is.EqualTo(new WorldPosition(4, 0)), "the known one, not the near one");
            });
        }

        [Test]
        public void An_empty_known_mask_is_omniscient_and_matches_the_overload_without_one()
        {
            var grid = Map(
                ".f..f",
                ".....");
            var finder = new Pathfinder(grid, TerrainRules.Default);
            var withMask = new List<WorldPosition>();
            var without = new List<WorldPosition>();
            var forest = TerrainMask(TerrainKind.Forest);

            var a = finder.TryFindNearest(Origin, Transport.Foot, forest, default, 8, withMask, out var costA);
            var b = finder.TryFindNearest(Origin, Transport.Foot, forest, 8, without, out var costB);

            Assert.Multiple(() =>
            {
                Assert.That(a, Is.True);
                Assert.That(b, Is.True);
                Assert.That(costA, Is.EqualTo(costB));
                Assert.That(withMask, Is.EqualTo(without));
            });
        }

        [Test]
        public void Nothing_known_is_nothing_found_even_where_the_terrain_is_right()
        {
            var grid = Map(
                ".f..f",
                ".....");
            var finder = new Pathfinder(grid, TerrainRules.Default);
            var route = new List<WorldPosition>();

            var found = finder.TryFindNearest(
                Origin, Transport.Foot, TerrainMask(TerrainKind.Forest), new bool[grid.CellCount], 8, route, out _);

            Assert.Multiple(() =>
            {
                Assert.That(found, Is.False);
                Assert.That(route, Is.Empty);
            });
        }

        [Test]
        public void The_route_may_cross_unknown_ground_to_reach_a_known_site()
        {
            // Only the destination has to be known. A searcher that could not
            // path over unseen cells could not reach anywhere it had only
            // glimpsed the far side of, which is not what section 12 says.
            var grid = Map(
                "....f",
                ".....");
            var finder = new Pathfinder(grid, TerrainRules.Default);
            var route = new List<WorldPosition>();
            var known = new bool[grid.CellCount];
            known[grid.IndexOf(new WorldPosition(4, 0))] = true;

            var found = finder.TryFindNearest(
                Origin, Transport.Foot, TerrainMask(TerrainKind.Forest), known, 8, route, out _);

            Assert.Multiple(() =>
            {
                Assert.That(found, Is.True);
                Assert.That(route.Count, Is.GreaterThan(1), "it walked there");
                Assert.That(known[grid.IndexOf(route[1])], Is.False, "over a cell it does not know");
            });
        }

        [Test]
        public void A_known_mask_too_small_for_the_grid_is_refused()
        {
            // Silently accepting a short mask would restrict searches to the
            // wrong cells, which reads as a plausible result rather than a bug.
            var grid = Map(
                ".f...",
                ".....");
            var finder = new Pathfinder(grid, TerrainRules.Default);
            var route = new List<WorldPosition>();

            Assert.That(
                () => finder.TryFindNearest(
                    Origin, Transport.Foot, TerrainMask(TerrainKind.Forest), new bool[grid.CellCount - 1], 8, route, out _),
                Throws.ArgumentException);
        }

        private static bool[] TerrainMask(TerrainKind kind)
        {
            var mask = new bool[Enum.GetValues(typeof(TerrainKind)).Length];
            mask[(int)kind] = true;
            return mask;
        }
    }
}
