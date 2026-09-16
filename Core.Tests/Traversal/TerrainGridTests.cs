using System;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Traversal;
using NUnit.Framework;

namespace KingdomWatch.Core.Tests.Traversal
{
    [TestFixture]
    public sealed class TerrainGridTests
    {
        [Test]
        public void A_new_grid_is_filled_with_one_kind()
        {
            var grid = new TerrainGrid(4, 3, TerrainKind.Forest);

            Assert.Multiple(() =>
            {
                Assert.That(grid.Width, Is.EqualTo(4));
                Assert.That(grid.Height, Is.EqualTo(3));
                Assert.That(grid.CellCount, Is.EqualTo(12));
                Assert.That(grid[new WorldPosition(0, 0)], Is.EqualTo(TerrainKind.Forest));
                Assert.That(grid[new WorldPosition(3, 2)], Is.EqualTo(TerrainKind.Forest));
            });
        }

        [Test]
        public void Set_rewrites_one_cell_and_nothing_else()
        {
            var grid = new TerrainGrid(3, 3, TerrainKind.Plains);

            grid.Set(new WorldPosition(1, 2), TerrainKind.SmallRiver);

            Assert.Multiple(() =>
            {
                Assert.That(grid[new WorldPosition(1, 2)], Is.EqualTo(TerrainKind.SmallRiver));
                Assert.That(grid[new WorldPosition(2, 1)], Is.EqualTo(TerrainKind.Plains));
                Assert.That(grid[new WorldPosition(1, 1)], Is.EqualTo(TerrainKind.Plains));
            });
        }

        [Test]
        public void Positions_off_the_map_are_refused_rather_than_wrapped()
        {
            // (-1, 0) and (4, 0) would alias real cells under a row-major
            // index if they were not caught; the far edges are the cases.
            var grid = new TerrainGrid(4, 3, TerrainKind.Plains);

            Assert.Multiple(() =>
            {
                Assert.That(grid.Contains(new WorldPosition(0, 0)), Is.True);
                Assert.That(grid.Contains(new WorldPosition(3, 2)), Is.True);
                Assert.That(grid.Contains(new WorldPosition(4, 0)), Is.False);
                Assert.That(grid.Contains(new WorldPosition(0, 3)), Is.False);
                Assert.That(grid.Contains(new WorldPosition(-1, 0)), Is.False);
                Assert.That(grid.Contains(new WorldPosition(0, -1)), Is.False);
                Assert.That(() => grid[new WorldPosition(4, 0)], Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => grid[new WorldPosition(-1, 2)], Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(
                    () => grid.Set(new WorldPosition(0, 3), TerrainKind.Plains),
                    Throws.TypeOf<ArgumentOutOfRangeException>());
            });
        }

        [Test]
        public void Index_and_position_are_inverses_in_row_major_order()
        {
            var grid = new TerrainGrid(4, 3, TerrainKind.Plains);

            Assert.Multiple(() =>
            {
                Assert.That(grid.IndexOf(new WorldPosition(0, 0)), Is.Zero);
                Assert.That(grid.IndexOf(new WorldPosition(3, 0)), Is.EqualTo(3));
                Assert.That(grid.IndexOf(new WorldPosition(0, 1)), Is.EqualTo(4));
                Assert.That(grid.IndexOf(new WorldPosition(3, 2)), Is.EqualTo(11));
                Assert.That(grid.PositionAt(11), Is.EqualTo(new WorldPosition(3, 2)));
                Assert.That(grid.PositionAt(4), Is.EqualTo(new WorldPosition(0, 1)));
                Assert.That(() => grid.PositionAt(12), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => grid.PositionAt(-1), Throws.TypeOf<ArgumentOutOfRangeException>());
            });

            for (var index = 0; index < grid.CellCount; index++)
            {
                Assert.That(grid.IndexOf(grid.PositionAt(index)), Is.EqualTo(index));
            }
        }

        [Test]
        public void Undefined_kinds_are_refused_everywhere_a_kind_goes_in()
        {
            var grid = new TerrainGrid(2, 2, TerrainKind.Plains);

            Assert.Multiple(() =>
            {
                Assert.That(() => new TerrainGrid(2, 2, TerrainKind.None), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => new TerrainGrid(2, 2, (TerrainKind)255), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => grid.Set(default, TerrainKind.None), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => grid.Set(default, (TerrainKind)6), Throws.TypeOf<ArgumentOutOfRangeException>());
            });
        }

        [Test]
        public void A_grid_needs_positive_dimensions()
        {
            Assert.Multiple(() =>
            {
                Assert.That(() => new TerrainGrid(0, 1, TerrainKind.Plains), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => new TerrainGrid(1, 0, TerrainKind.Plains), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => new TerrainGrid(-1, 1, TerrainKind.Plains), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => new TerrainGrid(1, 1, TerrainKind.Plains), Throws.Nothing);
            });
        }

        [Test]
        public void A_cell_is_one_byte()
        {
            // The grid is the largest thing in the world, and the dense
            // representation the remarks promise depends on the enum's width.
            Assert.That(Enum.GetUnderlyingType(typeof(TerrainKind)), Is.EqualTo(typeof(byte)));
        }

        [Test]
        public void A_cell_count_that_does_not_fit_an_int_is_refused_rather_than_wrapped()
        {
            // 65536 squared is exactly 2^32, which wraps to zero unchecked and
            // would build an empty grid that claims to contain every cell.
            Assert.Multiple(() =>
            {
                Assert.That(() => new TerrainGrid(65536, 65536, TerrainKind.Plains), Throws.TypeOf<OverflowException>());
                Assert.That(() => new TerrainGrid(int.MaxValue, 2, TerrainKind.Plains), Throws.TypeOf<OverflowException>());
            });
        }

        [Test]
        public void Positions_at_the_extremes_of_int_are_simply_off_the_map()
        {
            var grid = new TerrainGrid(4, 3, TerrainKind.Plains);

            Assert.Multiple(() =>
            {
                Assert.That(grid.Contains(new WorldPosition(int.MaxValue, 0)), Is.False);
                Assert.That(grid.Contains(new WorldPosition(0, int.MaxValue)), Is.False);
                Assert.That(grid.Contains(new WorldPosition(int.MinValue, 0)), Is.False);
                Assert.That(grid.Contains(new WorldPosition(0, int.MinValue)), Is.False);
                Assert.That(() => grid.IndexOf(new WorldPosition(int.MaxValue, int.MaxValue)), Throws.TypeOf<ArgumentOutOfRangeException>());
            });
        }
    }
}
