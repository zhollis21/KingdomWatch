using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Land;
using KingdomWatch.Core.Traversal;
using KingdomWatch.Core.Work;
using NUnit.Framework;

namespace KingdomWatch.Core.Tests.Construction
{
    // The two primitives buildings stand on (#100): clearing a cell for good,
    // and counting what a community could reach.
    [TestFixture]
    public sealed class ClearingTests
    {
        private static LandCover Land(TerrainGrid grid) => new LandCover(grid, new SimulationClock(new IdAllocator()));

        [Test]
        public void Clearing_a_standing_tree_fells_it_and_leaves_plains_that_never_regrow()
        {
            var grid = new TerrainGrid(4, 4, TerrainKind.Forest);
            var land = Land(grid);
            var at = new WorldPosition(1, 1);
            land.Take(at);
            var rewrites = grid.Rewrites;

            Assert.Multiple(() =>
            {
                Assert.That(land.Clear(at), Is.True, "still standing after one cut");
                Assert.That(grid[at], Is.EqualTo(TerrainKind.Plains));
                Assert.That(land.StateAt(grid.IndexOf(at)), Is.Zero);
                Assert.That(grid.Rewrites, Is.EqualTo(rewrites + 1));
                Assert.That(land.IsWorkable(at), Is.True, "plains are open ground");
            });
        }

        [Test]
        public void Clearing_a_stump_or_a_bush_fells_nothing()
        {
            var grid = new TerrainGrid(4, 4, TerrainKind.Forest);
            grid.Set(new WorldPosition(2, 2), TerrainKind.Scrub);
            var land = Land(grid);
            land.TreeCuts = 1;
            var stump = new WorldPosition(1, 1);
            land.Take(stump);

            Assert.Multiple(() =>
            {
                Assert.That(land.StageOf(stump), Is.EqualTo(TreeStage.Stump));
                Assert.That(land.Clear(stump), Is.False);
                Assert.That(land.Clear(new WorldPosition(2, 2)), Is.False);
                Assert.That(grid[new WorldPosition(2, 2)], Is.EqualTo(TerrainKind.Plains));
            });
        }

        [Test]
        public void Only_scrub_and_forest_are_cleared()
        {
            var grid = new TerrainGrid(4, 4, TerrainKind.Plains);
            grid.Set(new WorldPosition(1, 1), TerrainKind.Rocks);
            var land = Land(grid);

            Assert.Multiple(() =>
            {
                Assert.That(() => land.Clear(new WorldPosition(0, 0)), Throws.ArgumentException);
                Assert.That(() => land.Clear(new WorldPosition(1, 1)), Throws.ArgumentException);
            });
        }

        [Test]
        public void Counting_reachable_cells_sees_only_what_is_known_and_reachable_inside_the_box()
        {
            // Scrub at x 2, 4 and 8 along row 0; a river down column 6.
            var grid = new TerrainGrid(10, 3, TerrainKind.Plains);
            grid.Set(new WorldPosition(2, 0), TerrainKind.Scrub);
            grid.Set(new WorldPosition(4, 0), TerrainKind.Scrub);
            grid.Set(new WorldPosition(8, 0), TerrainKind.Scrub);

            for (var y = 0; y < 3; y++)
            {
                grid.Set(new WorldPosition(6, y), TerrainKind.SmallRiver);
            }

            var pathfinder = new Pathfinder(grid, TerrainRules.Default);
            var scrub = JobTable.Terrain(JobKind.Forager).ToArray();
            var from = new WorldPosition(0, 1);
            var known = new bool[grid.CellCount];
            known[grid.IndexOf(new WorldPosition(4, 0))] = true;

            Assert.Multiple(() =>
            {
                Assert.That(pathfinder.CountReachable(from, Jobs.Mover, scrub, default, 9), Is.EqualTo(2), "the river cuts off x 8");
                Assert.That(pathfinder.CountReachable(from, Jobs.Mover, scrub, default, 3), Is.EqualTo(1), "x 4 is outside a box of 3");
                Assert.That(pathfinder.CountReachable(from, Jobs.Mover, scrub, known, 9), Is.EqualTo(1), "only x 4 is known");
                Assert.That(() => pathfinder.CountReachable(from, Jobs.Mover, scrub, default, -1), Throws.TypeOf<System.ArgumentOutOfRangeException>());
                Assert.That(() => pathfinder.CountReachable(from, Jobs.Mover, scrub, new bool[3], 9), Throws.ArgumentException, "a known mask too short");
                Assert.That(() => pathfinder.CountReachable(from, Jobs.Mover, new bool[1], default, 9), Throws.ArgumentException, "a terrain mask too short");
                Assert.That(() => pathfinder.CountReachable(new WorldPosition(10, 0), Jobs.Mover, scrub, default, 9), Throws.TypeOf<System.ArgumentOutOfRangeException>(), "off the map");
                Assert.That(pathfinder.CountReachable(new WorldPosition(6, 0), Jobs.Mover, scrub, default, 9), Is.Zero, "from the river");
            });
        }

        [Test]
        public void Builders_and_farmers_are_jobs_but_not_gathering()
        {
            Assert.Multiple(() =>
            {
                Assert.That(JobTable.IsJob(JobKind.Builder), Is.True);
                Assert.That(JobTable.IsJob(JobKind.Farmer), Is.True);
                Assert.That(JobTable.IsGathering(JobKind.Builder), Is.False);
                Assert.That(JobTable.IsGathering(JobKind.Farmer), Is.False);
                Assert.That(JobTable.IsGathering(JobKind.Forager), Is.True);
                Assert.That(() => JobTable.Recipe(JobKind.Builder), Throws.TypeOf<System.ArgumentOutOfRangeException>());
                Assert.That(() => JobTable.Terrain(JobKind.Farmer).ToArray(), Throws.TypeOf<System.ArgumentOutOfRangeException>());
            });
        }
    }
}
