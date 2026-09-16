using System;
using System.Collections.Generic;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Rng;
using KingdomWatch.Core.Traversal;
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
            var grid = Generate();
            var counts = new Dictionary<TerrainKind, int>();

            for (var index = 0; index < grid.CellCount; index++)
            {
                var kind = grid[grid.PositionAt(index)];
                Assert.That(Enum.IsDefined(typeof(TerrainKind), kind) && kind != TerrainKind.None, Is.True);
                counts[kind] = counts.TryGetValue(kind, out var n) ? n + 1 : 1;
            }

            // Loose bounds: this checks the generator reads its own
            // proportions, not that the RNG is uniform.
            var cells = grid.CellCount;
            Assert.Multiple(() =>
            {
                Assert.That(counts[TerrainKind.Plains], Is.GreaterThan(cells / 2));
                Assert.That(counts[TerrainKind.Forest], Is.InRange(cells / 10, cells * 3 / 10));
                Assert.That(counts[TerrainKind.Hills], Is.InRange(1, cells / 10));
                Assert.That(counts[TerrainKind.SmallRiver], Is.EqualTo(Height));
                Assert.That(counts.ContainsKey(TerrainKind.DeepWater), Is.False);
            });
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
