using System;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Rng;
using KingdomWatch.Core.Traversal;

namespace KingdomWatch.Core.WorldGen
{
    /// <summary>
    /// A stand-in world: plains with patches of forest, rock outcrops and
    /// berry scrub, split top to bottom by one small river. Enough for two
    /// bands to walk around on, for work to be a walk away, and for one camp
    /// to be better placed than another (#137).
    /// </summary>
    /// <remarks>
    /// This is not world generation. Section 15's archetypes, homelands,
    /// river classification and sanity check are #33, and that issue replaces
    /// this file. What it does share with the real thing is the contract: a
    /// pure function of the seed, with every draw keyed by cell (section 5),
    /// so the same seed lays down the same map on every platform and no cell
    /// depends on the order the others were filled in. Patches keep to it:
    /// each is value noise, keyed draws on a coarse lattice blended across
    /// the cells between them, so a cell reads only the lattice points
    /// around it, and all of it is integer arithmetic. Inside a rock or
    /// scrub patch, whether a cell is the kind is one more keyed draw on
    /// that cell.
    ///
    /// The river is the one deliberate feature. Section 12 makes small rivers
    /// absolute walls before bridges exist, and section 15 wants the races'
    /// homelands separated by something physical - so the two halves are
    /// mutually unreachable from the first M1 run onward, until something
    /// rewrites a river cell into a crossing.
    /// </remarks>
    public static class PlaceholderMap
    {
        // Each kind of patch is a smooth noise field, 0 to NoiseMax, and a
        // cell is that kind where the field reaches the threshold. Spacing
        // sets how big a patch is and how far apart; the threshold sets how
        // much of the map it covers. Placeholder numbers, in the
        // TerrainRules.Default sense: tuned so patches leave gaps wider than
        // a worker's reach (Jobs.MaxSiteRadius) and both homelands still
        // live and settle (WorldRunTests).
        private const int ForestSpacing = 40;
        private const int ForestThreshold = 670;
        private const int RockSpacing = 24;
        private const int RockThreshold = 800;
        private const int ScrubSpacing = 14;
        private const int ScrubThreshold = 740;

        // Forest is solid; an outcrop or a berry thicket is open ground with
        // one cell in this many a rock or a bush (#137 review).
        private const int RockFill = 4;
        private const int ScrubFill = 3;

        // Noise values run 0 to NoiseMax; smoothing weights run 0 to OneWeight.
        private const int NoiseMax = 1023;
        private const long OneWeight = 1024;

        // The fine layer is this many times smaller than the coarse, and adds
        // this share of the value, so patch edges are ragged, not round.
        private const int FineDivisor = 4;
        private const int FineShare = 4;

        // The river starts near the middle and drifts by at most one column
        // per row, so it is always contiguous and never leaves the map.
        private const int MinimumWidth = 3;

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

            // One site per kind of patch and one for the river, so no two
            // layers, and no layer and the river, can ever share a roll (#57).
            var forest = new Patches(rng.Key(RandomDomain.WorldGen, RandomSite.ForestPatches), width, height, ForestSpacing, ForestThreshold, 1);
            var rocks = new Patches(rng.Key(RandomDomain.WorldGen, RandomSite.RockPatches), width, height, RockSpacing, RockThreshold, RockFill);
            var scrub = new Patches(rng.Key(RandomDomain.WorldGen, RandomSite.ScrubPatches), width, height, ScrubSpacing, ScrubThreshold, ScrubFill);
            var river = rng.Key(RandomDomain.WorldGen, RandomSite.RiverDrift);

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    // Where patches overlap, forest wins over rocks and rocks
                    // over scrub; everything else is plains.
                    if (forest.Covers(x, y))
                    {
                        grid.Set(new WorldPosition(x, y), TerrainKind.Forest);
                    }
                    else if (rocks.Covers(x, y))
                    {
                        grid.Set(new WorldPosition(x, y), TerrainKind.Rocks);
                    }
                    else if (scrub.Covers(x, y))
                    {
                        grid.Set(new WorldPosition(x, y), TerrainKind.Scrub);
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
                column += river.Mix(y).Range(-1, 2);
                column = Math.Max(1, Math.Min(width - 2, column));
                grid.Set(new WorldPosition(column, y), TerrainKind.SmallRiver);
            }

            return grid;
        }

        // One kind of patch: a coarse lattice for the patches and a fine one
        // for their edges, the two blended by FineShare. Inside a patch, one
        // cell in `fill` is the kind, by a keyed draw per cell; a fill of one
        // is solid and draws nothing.
        private sealed class Patches
        {
            private readonly Lattice _coarse;
            private readonly Lattice _fine;
            private readonly RandomKey _fill;
            private readonly int _threshold;
            private readonly int _oneIn;

            public Patches(RandomKey key, int width, int height, int spacing, int threshold, int oneIn)
            {
                _coarse = new Lattice(key.Mix(0), width, height, spacing);
                _fine = new Lattice(key.Mix(1), width, height, Math.Max(1, spacing / FineDivisor));
                _fill = key.Mix(2);
                _threshold = threshold;
                _oneIn = oneIn;
            }

            public bool Covers(int x, int y) =>
                At(x, y) >= _threshold && _fill.Mix(x).Mix(y).Chance(1, _oneIn);

            private int At(int x, int y) =>
                (int)(((long)_coarse.At(x, y) * (FineShare - 1) + _fine.At(x, y)) / FineShare);
        }

        // Value noise: a keyed draw at every spacing-th cell each way, and
        // between them a smoothstep blend of the four around a cell. The
        // draws are taken once, up front, so reading a cell costs none.
        private sealed class Lattice
        {
            private readonly int[] _values;
            private readonly int _columns;
            private readonly int _spacing;

            public Lattice(RandomKey key, int width, int height, int spacing)
            {
                // One point past the last cell each way, so every cell has
                // four corners to blend.
                _spacing = spacing;
                _columns = (width / spacing) + 2;
                var rows = (height / spacing) + 2;
                _values = new int[_columns * rows];

                for (var ly = 0; ly < rows; ly++)
                {
                    for (var lx = 0; lx < _columns; lx++)
                    {
                        _values[(ly * _columns) + lx] = key.Mix(lx).Mix(ly).Range(0, NoiseMax + 1);
                    }
                }
            }

            public int At(int x, int y)
            {
                var lx = x / _spacing;
                var ly = y / _spacing;
                var sx = Smooth(x - (lx * _spacing), _spacing);
                var sy = Smooth(y - (ly * _spacing), _spacing);
                var at = (ly * _columns) + lx;

                var top = (_values[at] * (OneWeight - sx)) + (_values[at + 1] * sx);
                var bottom = (_values[at + _columns] * (OneWeight - sx)) + (_values[at + _columns + 1] * sx);
                return (int)(((top * (OneWeight - sy)) + (bottom * sy)) / (OneWeight * OneWeight));
            }

            // 3t^2 - 2t^3 in fixed point, so a patch has no creases along the
            // lattice lines.
            private static long Smooth(int offset, int spacing)
            {
                var t = offset * OneWeight / spacing;
                return t * t * ((3 * OneWeight) - (2 * t)) / (OneWeight * OneWeight);
            }
        }
    }
}
