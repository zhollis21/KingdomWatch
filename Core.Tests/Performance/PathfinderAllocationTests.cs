using System.Collections.Generic;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Rng;
using KingdomWatch.Core.Traversal;
using KingdomWatch.Core.WorldGen;
using NUnit.Framework;

namespace KingdomWatch.Core.Tests.Performance
{
    /// <summary>
    /// Section 18's zero-allocation tick loop, pointed at
    /// <see cref="Pathfinder"/>: once built, route queries - found and not
    /// found, short and map-spanning - allocate nothing.
    /// </summary>
    [TestFixture]
    [Category("Performance")]
    public sealed class PathfinderAllocationTests
    {
        private const int Width = 64;
        private const int Height = 64;

        [Test]
        public void Route_queries_allocate_nothing()
        {
            var grid = PlaceholderMap.Generate(Width, Height, new DeterministicRng(16UL));
            var finder = new Pathfinder(grid, TerrainRules.Default);
            // Sized up front so growing it is never mistaken for the
            // pathfinder's own allocation; a route never exceeds the cell count.
            var route = new List<WorldPosition>(grid.CellCount);

            // Every query the measured span makes, made once already: the
            // first pass JIT-compiles the paths and the second is the one
            // that has to be free.
            Queries(finder, route);

            var allocated = Allocations.Measure(() => Queries(finder, route));

            Assert.That(allocated, Is.Zero);
        }

        private static void Queries(Pathfinder finder, List<WorldPosition> route)
        {
            var corner = new WorldPosition(0, 0);
            var farCorner = new WorldPosition(Width - 1, Height - 1);
            var middle = new WorldPosition(Width / 4, Height / 2);

            // The river splits the map, so the corner-to-corner query is the
            // expensive failure: it exhausts one whole bank before giving up.
            finder.TryFindRoute(corner, farCorner, Transport.Foot, route, out _);
            finder.TryFindRoute(corner, middle, Transport.Foot, route, out _);
            finder.TryFindRoute(middle, corner, Transport.Foot, route, out _);
            finder.TryFindRoute(middle, middle, Transport.Foot, route, out _);
            finder.TryFindRoute(farCorner, middle, Transport.Foot | Transport.Boat, route, out _);
        }
    }
}
