using System;
using System.Collections.Generic;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Rng;
using KingdomWatch.Core.Traversal;
using KingdomWatch.Core.Work;
using KingdomWatch.Core.WorldGen;
using NUnit.Framework;

namespace KingdomWatch.Core.Tests.Traversal
{
    [TestFixture]
    public sealed class PlaceholderMapTests
    {
        private const int Width = 48;
        private const int Height = 32;
        private const ulong Seed = 0x5EEDUL;
        private const int LargeSide = 256;

        private static TerrainGrid Generate(ulong seed = Seed) =>
            PlaceholderMap.Generate(Width, Height, new DeterministicRng(seed));

        [Test]
        public void The_same_seed_lays_down_the_same_map()
        {
            var first = Generate();
            var second = Generate();

            for (var index = 0; index < first.CellCount; index++)
            {
                var position = first.PositionAt(index);
                Assert.That(second[position], Is.EqualTo(first[position]), position.ToString());
            }
        }

        [Test]
        public void A_different_seed_lays_down_a_different_map()
        {
            var first = Generate(1UL);
            var second = Generate(2UL);
            var differences = 0;

            for (var index = 0; index < first.CellCount; index++)
            {
                if (first[first.PositionAt(index)] != second[second.PositionAt(index)])
                {
                    differences++;
                }
            }

            Assert.That(differences, Is.GreaterThan(first.CellCount / 10));
        }

        [Test]
        public void Every_cell_is_a_defined_kind_and_the_mix_is_roughly_as_advertised()
        {
            var grid = GenerateLarge();
            var counts = new Dictionary<TerrainKind, int>();

            for (var index = 0; index < grid.CellCount; index++)
            {
                var kind = grid[grid.PositionAt(index)];
                Assert.That(Enum.IsDefined(typeof(TerrainKind), kind) && kind != TerrainKind.None, Is.True);
                counts[kind] = counts.TryGetValue(kind, out var n) ? n + 1 : 1;
            }

            // Loose bounds: this checks the generator reads its own
            // proportions, not that the RNG is uniform. Patches are big, so
            // the map has to be too for its mix to settle.
            var cells = grid.CellCount;
            Assert.Multiple(() =>
            {
                Assert.That(counts[TerrainKind.Plains], Is.GreaterThan(cells / 2));
                Assert.That(counts[TerrainKind.Forest], Is.InRange(cells / 20, cells * 3 / 10));
                Assert.That(counts[TerrainKind.Rocks], Is.InRange(cells / 100, cells * 3 / 20));
                Assert.That(counts[TerrainKind.Scrub], Is.InRange(cells / 100, cells * 3 / 20));
                Assert.That(counts[TerrainKind.SmallRiver], Is.EqualTo(grid.Height));
                Assert.That(counts.ContainsKey(TerrainKind.DeepWater), Is.False);
            });
        }

        [TestCase(TerrainKind.Forest)]
        [TestCase(TerrainKind.Rocks)]
        [TestCase(TerrainKind.Scrub)]
        public void Each_resource_comes_in_patches_rather_than_scattered_cells(TerrainKind kind)
        {
            // #137: cells rolled one by one put a tree beside every camp. In a
            // patch most of a cell's neighbours are the same kind; scattered
            // at these proportions, few would be.
            var grid = GenerateLarge();
            var of = 0;
            var alike = 0;

            for (var y = 0; y < grid.Height - 1; y++)
            {
                for (var x = 0; x < grid.Width - 1; x++)
                {
                    if (grid[new WorldPosition(x, y)] != kind)
                    {
                        continue;
                    }

                    of += 2;
                    alike += grid[new WorldPosition(x + 1, y)] == kind ? 1 : 0;
                    alike += grid[new WorldPosition(x, y + 1)] == kind ? 1 : 0;
                }
            }

            Assert.That(alike, Is.GreaterThan(of * 6 / 10), kind + ": " + alike + " of " + of + " neighbours alike");
        }

        [TestCase(TerrainKind.Forest)]
        [TestCase(TerrainKind.Rocks)]
        [TestCase(TerrainKind.Scrub)]
        public void Somewhere_on_land_a_resource_is_out_of_a_workers_reach(TerrainKind kind)
        {
            // #137: a camp is a choice only if some camps lack something. Out
            // of reach here is Jobs.MaxSiteRadius in every direction, the box
            // a work-site search looks within.
            var grid = GenerateLarge();
            var lacking = 0;

            for (var y = 0; y < grid.Height; y += 4)
            {
                for (var x = 0; x < grid.Width; x += 4)
                {
                    if (grid[new WorldPosition(x, y)] != TerrainKind.SmallRiver && !WithinReach(grid, x, y, kind))
                    {
                        lacking++;
                    }
                }
            }

            Assert.That(lacking, Is.GreaterThan(0), kind.ToString());
        }

        [Test]
        public void The_river_runs_the_full_height_one_cell_per_row_and_never_touches_an_edge()
        {
            var grid = Generate();
            var previous = -1;

            for (var y = 0; y < Height; y++)
            {
                var column = -1;

                for (var x = 0; x < Width; x++)
                {
                    if (grid[new WorldPosition(x, y)] != TerrainKind.SmallRiver)
                    {
                        continue;
                    }

                    Assert.That(column, Is.EqualTo(-1), "two river cells in row " + y);
                    column = x;
                }

                Assert.Multiple(() =>
                {
                    Assert.That(column, Is.InRange(1, Width - 2), "row " + y);

                    if (previous >= 0)
                    {
                        Assert.That(Math.Abs(column - previous), Is.LessThanOrEqualTo(1), "row " + y);
                    }
                });
                previous = column;
            }
        }

        [Test]
        public void The_river_meanders_rather_than_running_straight()
        {
            // Each row's drift is its own keyed draw. One draw reused for every
            // row would give a river that only ever bends one way.
            var grid = Generate();
            var bentLeft = false;
            var bentRight = false;
            var previous = FindRiver(grid, 0).X;

            for (var y = 1; y < Height; y++)
            {
                var column = FindRiver(grid, y).X;
                bentLeft |= column < previous;
                bentRight |= column > previous;
                previous = column;
            }

            Assert.Multiple(() =>
            {
                Assert.That(bentLeft, Is.True);
                Assert.That(bentRight, Is.True);
            });
        }

        [Test]
        public void On_the_narrowest_map_the_river_is_pinned_to_the_middle_column()
        {
            // Three wide leaves exactly one column with a bank either side, so
            // the walk is clamped to it every row no matter what it draws.
            var grid = PlaceholderMap.Generate(3, 64, new DeterministicRng(Seed));

            for (var y = 0; y < 64; y++)
            {
                Assert.That(FindRiver(grid, y), Is.EqualTo(new WorldPosition(1, y)));
            }
        }

        [Test]
        public void The_two_banks_cannot_reach_each_other_until_a_cell_is_bridged()
        {
            // Section 12 and 15: pre-settlement rivers are absolute walls, and
            // a bridge is one cell rewritten. Both banks are plains-heavy, so
            // any two land cells on the same bank are connected.
            var grid = Generate();
            var finder = new Pathfinder(grid, TerrainRules.Default);
            var route = new List<WorldPosition>();
            var west = new WorldPosition(0, Height / 2);
            var east = new WorldPosition(Width - 1, Height / 2);
            var riverMidway = FindRiver(grid, Height / 2);

            var separated = !finder.TryFindRoute(west, east, Transport.Foot, route, out _);
            grid.Set(riverMidway, TerrainKind.Plains);
            var bridged = finder.TryFindRoute(west, east, Transport.Foot, route, out _);

            Assert.Multiple(() =>
            {
                Assert.That(separated, Is.True);
                Assert.That(bridged, Is.True);
                Assert.That(route, Does.Contain(riverMidway));
            });
        }

        [Test]
        public void Generation_refuses_a_map_too_narrow_for_a_river_with_banks()
        {
            var rng = new DeterministicRng(Seed);

            Assert.Multiple(() =>
            {
                Assert.That(() => PlaceholderMap.Generate(2, 4, rng), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => PlaceholderMap.Generate(3, 1, rng), Throws.Nothing);
                Assert.That(() => PlaceholderMap.Generate(3, 0, rng), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => PlaceholderMap.Generate(4, 4, null!), Throws.ArgumentNullException);
            });
        }

        // Several patches across, so a test sees patches and the gaps between
        // them rather than the inside of one.
        private static TerrainGrid GenerateLarge() =>
            PlaceholderMap.Generate(LargeSide, LargeSide, new DeterministicRng(Seed));

        private static bool WithinReach(TerrainGrid grid, int x, int y, TerrainKind kind)
        {
            var reach = Jobs.MaxSiteRadius;

            for (var cy = Math.Max(0, y - reach); cy <= Math.Min(grid.Height - 1, y + reach); cy++)
            {
                for (var cx = Math.Max(0, x - reach); cx <= Math.Min(grid.Width - 1, x + reach); cx++)
                {
                    if (grid[new WorldPosition(cx, cy)] == kind)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static WorldPosition FindRiver(TerrainGrid grid, int y)
        {
            for (var x = 0; x < grid.Width; x++)
            {
                var position = new WorldPosition(x, y);

                if (grid[position] == TerrainKind.SmallRiver)
                {
                    return position;
                }
            }

            throw new InvalidOperationException("No river in row " + y);
        }
    }
}
