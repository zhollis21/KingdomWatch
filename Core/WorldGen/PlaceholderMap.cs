using System;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Rng;
using KingdomWatch.Core.Traversal;

namespace KingdomWatch.Core.WorldGen
{
    /// <summary>
    /// A stand-in world for the headless milestone: plains scattered with
    /// forest and hills, split top to bottom by one small river. Enough for
    /// two bands to walk around on, and for a route to have something to
    /// avoid.
    /// </summary>
    /// <remarks>
    /// This is not world generation. Section 15's archetypes, homelands,
    /// river classification and sanity check are #33, and that issue replaces
    /// this file. What it does share with the real thing is the contract: a
    /// pure function of the seed, with every draw keyed by cell (section 5),
    /// so the same seed lays down the same map on every platform and no cell
    /// depends on the order the others were filled in.
    ///
    /// The river is the one deliberate feature. Section 12 makes small rivers
    /// absolute walls before bridges exist, and section 15 wants the races'
    /// homelands separated by something physical - so the two halves are
    /// mutually unreachable from the first M1 run onward, until something
    /// rewrites a river cell into a crossing.
    /// </remarks>
    public static class PlaceholderMap
    {
        // Out of every 100 cells, roughly this many are forest and this many
        // hills. Placeholder proportions, in the TerrainRules.Default sense.
        private const int ForestPercent = 20;
        private const int HillsPercent = 5;

        // The river starts near the middle and drifts by at most one column
        // per row, so it is always contiguous and never leaves the map.
        private const int MinimumWidth = 3;

        // Key components so a cell's terrain draw and a row's river draw can
        // never be the same roll.
        private const int TerrainDraw = 1;
        private const int RiverDraw = 2;

        public static TerrainGrid Generate(int width, int height, DeterministicRng rng)
        {
            if (rng is null)
            {
                throw new ArgumentNullException(nameof(rng));
            }

            if (width < MinimumWidth)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(width), width, "The river needs land on both sides: width must be at least " + MinimumWidth + ".");
            }

            var grid = new TerrainGrid(width, height, TerrainKind.Plains);
            var domain = rng.Key(RandomDomain.WorldGen);

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var roll = domain.Mix(TerrainDraw).Mix(x).Mix(y).Range(0, 100);

                    if (roll < ForestPercent)
                    {
                        grid.Set(new WorldPosition(x, y), TerrainKind.Forest);
                    }
                    else if (roll < ForestPercent + HillsPercent)
                    {
                        grid.Set(new WorldPosition(x, y), TerrainKind.Hills);
                    }
                }
            }

            // The river's column at each row is a walk from the middle, clamped
            // so a column of land survives on either side. Each row's drift is
            // its own keyed draw; the walk is a running sum of them, which is
            // still a pure function of the seed.
            var column = width / 2;

            for (var y = 0; y < height; y++)
            {
                column += domain.Mix(RiverDraw).Mix(y).Range(-1, 2);
                column = Math.Max(1, Math.Min(width - 2, column));
                grid.Set(new WorldPosition(column, y), TerrainKind.SmallRiver);
            }

            return grid;
        }
    }
}
