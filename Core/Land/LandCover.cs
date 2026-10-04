using System;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Traversal;

namespace KingdomWatch.Core.Land
{
    /// <summary>
    /// What is growing on the land, cell by cell: whether a berry bush has
    /// fruit left this season and whether a tree is standing (#26). Gathering
    /// used to take nothing from the ground; now a bush gives
    /// <see cref="BushPicks"/> trips a season and a tree falls after
    /// <see cref="TreeCuts"/>.
    /// </summary>
    /// <remarks>
    /// **A spent cell is not a site.** Nothing yields less as it thins: a
    /// bush is ripe until its last trip is claimed, then bare, and the site
    /// search (<see cref="Ripe"/>) passes over it to the next nearest.
    /// Workers walk further as a thicket empties, and a band whose camp has
    /// nothing left in reach moves on at its next council.
    ///
    /// **Berries fruit by season.** Every bush is ripe again at the start of
    /// spring, summer and autumn, stripped or not, and bare all winter - the
    /// year cycle (#53) already makes winter foraging too thin to live on, and
    /// now there is nothing to pick at all. A felled tree is a stump and then
    /// a sapling, and stands again <see cref="RegrowDays"/> after it fell.
    /// Rocks are untouched: stone stays infinite until something decides
    /// otherwise.
    ///
    /// **A trip claims its harvest when it starts**, not when it ends, so the
    /// worker who takes a bush's last trip makes it bare for the next picker
    /// at once and a round of pickers spreads across a thicket rather than
    /// converging on one bush. A trip that never finishes gives its claim back
    /// (<see cref="Return"/>).
    ///
    /// **Lazy.** A cell holds the period its count belongs to - the season
    /// for a bush, the day of the last cut for a tree - and regrowth is read
    /// off the clock when the cell is next asked about. Nothing is polled and
    /// nothing is booked, so land offscreen costs nothing.
    ///
    /// One long per cell, dense, the stance <see cref="Knowledge.KnownMaps"/>
    /// takes: about nine megabytes on the M1 map, and a dense array has one
    /// canonical order for the world hash. Zero is untouched.
    ///
    /// The numbers are placeholders, set by the harness (#26).
    /// </remarks>
    public sealed class LandCover
    {
        /// <summary>Trips a berry bush gives in a fruiting season before it is bare, unless set otherwise.</summary>
        public const int DefaultBushPicks = 7;

        /// <summary>Woodcutting tasks that fell a tree, unless set otherwise.</summary>
        public const int DefaultTreeCuts = 5;

        /// <summary>Days from a tree's felling until it stands again: five years.</summary>
        public const long RegrowDays = 5L * SimulationTime.DaysPerYear;

        // A cell's state is (period + 1) << CountBits | count, so zero is a
        // cell nothing has touched and every touched cell is non-zero. A
        // day number at the end of time is under 2^47, so the shifted period
        // still fits a long.
        private const int CountBits = 16;
        private const int CountMask = (1 << CountBits) - 1;

        /// <summary>The most claims a cell can count: the largest picks or cuts allowed.</summary>
        public const int MaxClaims = CountMask;

        private readonly TerrainGrid _grid;
        private readonly SimulationClock _clock;
        private readonly long[] _cells;
        private int _bushPicks = DefaultBushPicks;
        private int _treeCuts = DefaultTreeCuts;

        public LandCover(TerrainGrid grid, SimulationClock clock)
        {
            _grid = grid ?? throw new ArgumentNullException(nameof(grid));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _cells = new long[grid.CellCount];
            Ripe = new RipeFilter(this);
            Promising = new PromisingFilter(this);
        }

        /// <summary>
        /// Trips a berry bush gives in a fruiting season before it is bare,
        /// 1 to <see cref="MaxClaims"/>. Settable so the harness can sweep it
        /// and fixtures about something else can make land that never runs
        /// out; set it before anyone works, since claims already counted are
        /// read against the new number.
        /// </summary>
        public int BushPicks
        {
            get => _bushPicks;
            set => _bushPicks = RequireClaims(value);
        }

        /// <summary>Woodcutting tasks that fell a tree, 1 to <see cref="MaxClaims"/>; as <see cref="BushPicks"/>.</summary>
        public int TreeCuts
        {
            get => _treeCuts;
            set => _treeCuts = RequireClaims(value);
        }

        /// <summary>
        /// Cells that can be worked now: a bush with fruit left this season, a
        /// standing tree, and anything else. What a work site must be.
        /// </summary>
        public ISiteFilter Ripe { get; }

        /// <summary>
        /// Cells worth camping by: <see cref="Ripe"/>, except that in winter
        /// every bush counts, since all of them fruit in spring. What a camp
        /// is chosen, kept or left by - a band waiting out the winter on its
        /// stores does not wander looking for berries nobody has.
        /// </summary>
        public ISiteFilter Promising { get; }

        /// <summary>Whether the cell at this position can be worked now.</summary>
        public bool IsWorkable(WorldPosition at) => IsWorkable(_grid.IndexOf(at), _clock.Now);

        /// <summary>
        /// Claims one harvest of the cell for a trip starting now. A bush's
        /// last trip leaves it bare; a tree's last cut fells it. Anything that
        /// is neither scrub nor forest gives without limit and records
        /// nothing. Throws if the cell cannot be worked.
        /// </summary>
        public void Take(WorldPosition at)
        {
            var cell = _grid.IndexOf(at);
            var now = _clock.Now;

            if (!IsWorkable(cell, now))
            {
                throw new InvalidOperationException(at + " has nothing left to gather.");
            }

            switch (_grid.KindAt(cell))
            {
                case TerrainKind.Scrub:
                    var season = SeasonIndex(now);
                    var picks = _cells[cell] != 0 && Period(cell) == season ? Count(cell) : 0;
                    _cells[cell] = Pack(season, picks + 1);
                    break;
                case TerrainKind.Forest:
                    // A tree that has stood again since it fell starts over.
                    var cuts = Count(cell) >= TreeCuts ? 0 : Count(cell);
                    _cells[cell] = Pack(now.DayNumber, cuts + 1);
                    break;
            }
        }

        /// <summary>
        /// Gives back a claim from a trip that never finished - its worker
        /// died on the way. A tree its claim had felled stands again.
        /// </summary>
        public void Return(WorldPosition at)
        {
            var cell = _grid.IndexOf(at);

            switch (_grid.KindAt(cell))
            {
                case TerrainKind.Scrub:
                case TerrainKind.Forest:
                    if (Count(cell) == 0)
                    {
                        throw new InvalidOperationException(at + " has no claim to give back.");
                    }

                    // A cell with no claim left is untouched again, tree or
                    // bush: a touched cell always holds a claim (#141 review).
                    var left = Count(cell) - 1;
                    _cells[cell] = left == 0 ? 0 : Pack(Period(cell), left);
                    Returns++;
                    break;
            }
        }

        /// <summary>
        /// Cuts a tree has left before it falls: <see cref="TreeCuts"/> for
        /// one untouched or grown back, fewer for one already being cut, none
        /// for a stump or a sapling. What clearing it costs and gives (#100).
        /// Throws for a cell that is not forest.
        /// </summary>
        public int CutsLeft(WorldPosition at)
        {
            var cell = CellOf(at, TerrainKind.Forest);

            if (_cells[cell] == 0)
            {
                return TreeCuts;
            }

            var cuts = Count(cell);

            if (cuts < TreeCuts)
            {
                return TreeCuts - cuts;
            }

            return _clock.Now.DayNumber - Period(cell) >= RegrowDays ? TreeCuts : 0;
        }

        /// <summary>
        /// Grubs out a bush or fells a tree for good, turning the cell to
        /// plains (#100): the ground a building or field goes on. Plains grow
        /// nothing, so nothing here regrows. Returns the cuts the tree had
        /// left (<see cref="CutsLeft"/>) - their wood is the clearer's - or
        /// zero for a bush. Throws for a cell that is neither scrub nor
        /// forest, which has nothing to clear.
        /// </summary>
        /// <remarks>
        /// A claim on the cell from a trip already out is simply forgotten:
        /// that trip still brings home what it set out for, and giving it
        /// back later is a no-op, since plains record no claims.
        /// </remarks>
        public int Clear(WorldPosition at)
        {
            var cell = _grid.IndexOf(at);
            var kind = _grid.KindAt(cell);

            if (kind != TerrainKind.Scrub && kind != TerrainKind.Forest)
            {
                throw new ArgumentException(at + " is " + kind + "; only scrub and forest are cleared.", nameof(at));
            }

            var cuts = kind == TerrainKind.Forest ? CutsLeft(at) : 0;
            _cells[cell] = 0;
            _grid.Set(at, TerrainKind.Plains);
            return cuts;
        }

        /// <summary>
        /// Whether the bush at this position has fruit to show: false in
        /// winter and once it has been stripped this season. For the view.
        /// Throws for a cell that is not scrub, which has no bush to ask about.
        /// </summary>
        public bool HasFruit(WorldPosition at) => IsWorkable(CellOf(at, TerrainKind.Scrub), _clock.Now);

        /// <summary>
        /// How far a tree at this position has grown back. For the view.
        /// Throws for a cell that is not forest.
        /// </summary>
        public TreeStage StageOf(WorldPosition at)
        {
            var cell = CellOf(at, TerrainKind.Forest);

            if (_cells[cell] == 0 || Count(cell) < TreeCuts)
            {
                return TreeStage.Standing;
            }

            var since = _clock.Now.DayNumber - Period(cell);
            return since >= RegrowDays ? TreeStage.Standing
                : since >= RegrowDays / 2 ? TreeStage.Sapling
                : TreeStage.Stump;
        }

        /// <summary>
        /// Claims given back so far. Time makes land workable again only at a
        /// season's start or a regrowth, which a dawn's search sees; a claim
        /// given back does it mid-day, so a search that found nothing compares
        /// this with what it was then and looks again (#141 review).
        /// </summary>
        public long Returns { get; private set; }

        /// <summary>Cells in the cover: the grid's.</summary>
        public int CellCount => _cells.Length;

        /// <summary>
        /// A cell's raw state, zero when untouched: for the world hash, which
        /// folds the touched cells in index order.
        /// </summary>
        public long StateAt(int cell) => _cells[cell];

        /// <summary>
        /// Harvests claimed from a cell in its current period - this season's
        /// picks of a bush, a tree's cuts. For the validator.
        /// </summary>
        public int ClaimsAt(int cell) => Count(cell);

        // Seasons since the world began: the period a bush's picks belong to.
        private static long SeasonIndex(SimulationTime now) => now.DayNumber / SimulationTime.DaysPerSeason;

        private int CellOf(WorldPosition at, TerrainKind kind)
        {
            var cell = _grid.IndexOf(at);

            if (_grid.KindAt(cell) != kind)
            {
                throw new ArgumentException(at + " is " + _grid.KindAt(cell) + ", not " + kind + ".", nameof(at));
            }

            return cell;
        }

        private bool IsWorkable(int cell, SimulationTime now)
        {
            switch (_grid.KindAt(cell))
            {
                case TerrainKind.Scrub:
                    return now.Season != Season.Winter
                        && (_cells[cell] == 0 || Period(cell) != SeasonIndex(now) || Count(cell) < BushPicks);
                case TerrainKind.Forest:
                    return _cells[cell] == 0 || Count(cell) < TreeCuts || now.DayNumber - Period(cell) >= RegrowDays;
                default:
                    return true;
            }
        }

        private bool IsPromising(int cell, SimulationTime now) =>
            (now.Season == Season.Winter && _grid.KindAt(cell) == TerrainKind.Scrub) || IsWorkable(cell, now);

        private static int RequireClaims(int value) =>
            value >= 1 && value <= MaxClaims
                ? value
                : throw new ArgumentOutOfRangeException(nameof(value), value, "Between 1 and " + MaxClaims + ".");

        private long Period(int cell) => (_cells[cell] >> CountBits) - 1L;

        private int Count(int cell) => (int)(_cells[cell] & CountMask);

        // A long, not an int: a day number at the end of time is past 2^46,
        // and shifted by CountBits still fits, where an int would not reach
        // the world's last day (the last-day tests found that).
        private static long Pack(long period, int count) => ((period + 1L) << CountBits) | (long)count;

        private sealed class RipeFilter : ISiteFilter
        {
            private readonly LandCover _land;

            public RipeFilter(LandCover land) => _land = land;

            public bool Accepts(int cell) => _land.IsWorkable(cell, _land._clock.Now);
        }

        private sealed class PromisingFilter : ISiteFilter
        {
            private readonly LandCover _land;

            public PromisingFilter(LandCover land) => _land = land;

            public bool Accepts(int cell) => _land.IsPromising(cell, _land._clock.Now);
        }
    }
}
