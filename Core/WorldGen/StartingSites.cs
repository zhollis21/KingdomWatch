using System;
using System.Collections.Generic;
using KingdomWatch.Core.Construction;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Traversal;
using KingdomWatch.Core.Work;

namespace KingdomWatch.Core.WorldGen
{
    /// <summary>
    /// Where a world's starting villages stand (#146): the spots with the
    /// most wood in reach, each one a village could live on, spread out, and
    /// as many on each side of the river.
    /// </summary>
    /// <remarks>
    /// **Wood ranks, food gates.** Wood in reach is what bounds how many
    /// hearths a village can keep, so how many households it can house
    /// (<see cref="Buildings.CanFuelAnotherHearth"/>), and every barn and
    /// house is built from it; once its fields are in, a village eats from
    /// them more than from its bushes. So a site is ranked by
    /// <see cref="Buildings.WoodInReach(WorldPosition, ReadOnlySpan{bool})"/>.
    /// Stone is not scored: nothing spends it yet.
    ///
    /// Bushes gate instead. A site's bushes have to cover a whole year of
    /// the village's eating (<see cref="Buildings.YearlyNeed"/>), and the site
    /// has to pass the founding check
    /// (<see cref="Buildings.CanSettle(WorldPosition, ReadOnlySpan{bool}, int)"/>)
    /// for its yard and barn ground. The founding check's own food bar is not
    /// enough here: it is set for a band that chose where to stop, and the
    /// sites with the most wood are the ones that only just clear it. In the
    /// #146 kickoff runs, villages founded there with ten days' food ran out
    /// before their first harvest - eight of thirty-two, over eight seeds,
    /// were dead within sixty days - and with the year's floor none were.
    ///
    /// **Read as if everything were known.** Nobody exists to have seen the
    /// land yet, and a village's searches reach
    /// <see cref="Jobs.MaxSiteRadius"/>, well inside the
    /// <see cref="Nomadic.NomadicBands.RevealRadius"/> it is given at
    /// founding, so a whole-map view counts exactly what the village will.
    ///
    /// **Deterministic.** Candidates are every <see cref="Lattice"/>-th cell
    /// each way, ranked by wood with ties to the lower row, then the lower
    /// column; the walk down that ranking takes a site when its side still
    /// has room, it is at least the spacing from every site already taken,
    /// its bushes would feed the village a year, and it passes the founding
    /// check. No draws, so the sites are a pure
    /// function of the map.
    ///
    /// Two sides because the placeholder map has one river, a wall until
    /// bridges (section 12). Section 15's homelands and sanity check are #33's,
    /// and replace this: a world that cannot place its villages throws here
    /// rather than starting with fewer.
    /// </remarks>
    public static class StartingSites
    {
        /// <summary>Cells between candidate sites, each way.</summary>
        public const int Lattice = 8;

        /// <summary>
        /// The sites, in the order to found them: the west side's in rank
        /// order, then the east's. Must be called before anything is built,
        /// at the time the villages will be founded, since the founding check
        /// reads the season. Throws when either side has fewer than
        /// <paramref name="perSide"/> sites that fit.
        /// </summary>
        public static IReadOnlyList<WorldPosition> Pick(World world, int perSide, int villageSize, int spacing)
        {
            if (world is null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            if (perSide < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(perSide), perSide, "Each side has at least one village.");
            }

            if (villageSize < 2)
            {
                throw new ArgumentOutOfRangeException(nameof(villageSize), villageSize, "A village is at least a couple.");
            }

            if (spacing < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(spacing), spacing, "Villages cannot be closer than on top of each other.");
            }

            // Empty is the whole map, as the Pathfinder reads a known mask.
            var grid = world.Grid;
            var known = ReadOnlySpan<bool>.Empty;

            var candidates = new List<Candidate>();

            for (var y = Lattice / 2; y < grid.Height; y += Lattice)
            {
                var river = RiverColumn(grid, y);

                for (var x = Lattice / 2; x < grid.Width; x += Lattice)
                {
                    var at = new WorldPosition(x, y);

                    if (x == river || !world.Jobs.CanStandAt(at))
                    {
                        continue;
                    }

                    candidates.Add(new Candidate(at, world.Buildings.WoodInReach(at, known), x < river));
                }
            }

            // A total order - wood, then row, then column - so the unstable
            // sort gives the same ranking on every platform.
            candidates.Sort(static (a, b) =>
                a.Wood != b.Wood ? b.Wood.CompareTo(a.Wood)
                : a.At.Y != b.At.Y ? a.At.Y.CompareTo(b.At.Y)
                : a.At.X.CompareTo(b.At.X));

            var west = new List<WorldPosition>(perSide);
            var east = new List<WorldPosition>(perSide);
            var spacingSquared = (long)spacing * spacing;
            var need = Buildings.YearlyNeed(villageSize);

            for (var i = 0; i < candidates.Count && (west.Count < perSide || east.Count < perSide); i++)
            {
                var candidate = candidates[i];
                var side = candidate.West ? west : east;

                // The cheap tests first: the founding check places a barn.
                if (side.Count >= perSide
                    || IsCrowded(candidate.At, west, spacingSquared)
                    || IsCrowded(candidate.At, east, spacingSquared)
                    || world.Buildings.ForageInReach(candidate.At, known) < need
                    || !world.Buildings.CanSettle(candidate.At, known, villageSize))
                {
                    continue;
                }

                side.Add(candidate.At);
            }

            if (west.Count < perSide || east.Count < perSide)
            {
                throw new InvalidOperationException(
                    "Seed " + world.Seed + " has room for " + west.Count + " villages west of the river and " + east.Count
                    + " east, of the " + perSide + " each side starts with.");
            }

            var sites = new List<WorldPosition>(perSide * 2);
            sites.AddRange(west);
            sites.AddRange(east);
            return sites.AsReadOnly();
        }

        /// <summary>
        /// The river's column in a row. Throws for a row with none: this is
        /// not a placeholder map.
        /// </summary>
        public static int RiverColumn(TerrainGrid grid, int row)
        {
            if (grid is null)
            {
                throw new ArgumentNullException(nameof(grid));
            }

            for (var x = 0; x < grid.Width; x++)
            {
                if (grid[new WorldPosition(x, row)] == TerrainKind.SmallRiver)
                {
                    return x;
                }
            }

            throw new InvalidOperationException("Row " + row + " has no river; this is not a placeholder map.");
        }

        private static bool IsCrowded(WorldPosition at, List<WorldPosition> taken, long spacingSquared)
        {
            for (var i = 0; i < taken.Count; i++)
            {
                long dx = at.X - taken[i].X;
                long dy = at.Y - taken[i].Y;

                if (dx * dx + dy * dy < spacingSquared)
                {
                    return true;
                }
            }

            return false;
        }

        private readonly struct Candidate
        {
            public Candidate(WorldPosition at, long wood, bool west)
            {
                At = at;
                Wood = wood;
                West = west;
            }

            public WorldPosition At { get; }

            public long Wood { get; }

            public bool West { get; }
        }
    }
}
