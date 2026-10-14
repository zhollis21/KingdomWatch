using System;
using System.Collections.Generic;
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
    /// now there is nothing to pick at all. A felled tree does not grow back
    /// (#155): its last cut leaves a stump, cleared to plains once the trip
    /// that took it is home (<see cref="Fell"/>), and only a forester's
    /// <see cref="Plant"/> puts a tree there again.
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
    /// for a bush, the day of the last cut for a tree - and a bush's new
    /// season or a planted tree's growth is read off the clock when the cell
    /// is next asked about. Nothing is polled and
    /// nothing is booked, so land offscreen costs nothing.
    ///
    /// One long per cell, dense, the stance <see cref="Knowledge.KnownMaps"/>
    /// takes: about nine megabytes on the M1 map, and a dense array has one
    /// canonical order for the world hash. Zero is untouched.
    ///
    /// **Foresters plant** (#155). A tree planted on plains turns the cell to
    /// forest and is a sapling from the day it went in - never a stump, which
    /// is what felling leaves - and stands <see cref="RegrowDays"/> later. Until it
    /// first stands it is <see cref="IsGrowing(int)"/>: in the ground, but
    /// not yet a tree, and the wood a village counts on (<see cref="Grown"/>)
    /// leaves it out. Once its first cut is taken it is a tree like any
    /// other, felled for good by its last.
    ///
    /// The numbers are placeholders, set by the harness (#26).
    /// </remarks>
    public sealed class LandCover
    {
        /// <summary>Trips a berry bush gives in a fruiting season before it is bare, unless set otherwise.</summary>
        public const int DefaultBushPicks = 7;

        /// <summary>Woodcutting tasks that fell a tree, unless set otherwise.</summary>
        public const int DefaultTreeCuts = 5;

        /// <summary>Days from a tree's planting until it stands: five years.</summary>
        public const long RegrowDays = 5L * SimulationTime.DaysPerYear;

        /// <summary>Days a felled tree's stump shows on the plains it left: five years (#155).</summary>
        public const long StumpDays = 5L * SimulationTime.DaysPerYear;

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

        // Trees planted and not yet stood (#155): cell to the day it went in.
        // Grown ones are dropped when next noticed (NextGrowth, Take), so an
        // entry is read against the clock rather than trusted.
        private readonly Dictionary<int, long> _planted = new Dictionary<int, long>();

        public LandCover(TerrainGrid grid, SimulationClock clock)
        {
            _grid = grid ?? throw new ArgumentNullException(nameof(grid));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _cells = new long[grid.CellCount];
            Ripe = new RipeFilter(this);
            Promising = new PromisingFilter(this);
            Grown = new GrownFilter(this);
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

        /// <summary>
        /// Every cell but a planted tree still growing (<see cref="IsGrowing(int)"/>):
        /// what a village counts its trees by, since a sapling it planted is
        /// wood it does not have yet (#155).
        /// </summary>
        public ISiteFilter Grown { get; }

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
                    // A planted tree that has grown takes its first cut and
                    // is an ordinary tree from then on.
                    var cuts = Count(cell) >= TreeCuts ? 0 : Count(cell);
                    _cells[cell] = Pack(now.DayNumber, cuts + 1);
                    _planted.Remove(cell);
                    break;
            }
        }

        /// <summary>
        /// Clears the stump a tree's last cut left to plains, as the trip
        /// that took it comes home (#155): nothing grows back on its own, and
        /// the ground is open for a forester to plant. Kept a stump until
        /// then so a trip that never comes home can give the cut back
        /// (<see cref="Return"/>). The plains keep the day, so the view can
        /// draw the stump on them for <see cref="StumpDays"/>
        /// (<see cref="ShowsStump"/>). Nothing to do for any other cell - one
        /// with cuts left, or ground cleared since.
        /// </summary>
        public void Fell(WorldPosition at)
        {
            var cell = _grid.IndexOf(at);

            if (IsStump(cell))
            {
                _cells[cell] = Pack(_clock.Now.DayNumber, 0);
                _grid.Set(at, TerrainKind.Plains);
            }
        }

        /// <summary>
        /// Whether plains at this position were felled from a tree less than
        /// <see cref="StumpDays"/> ago (<see cref="Fell"/>): drawn with its
        /// stump, though open ground to everything else. For the view. False
        /// for any cell that is not plains, or plains planted or built on
        /// since; the view still checks for roads and buildings.
        /// </summary>
        public bool ShowsStump(WorldPosition at)
        {
            var cell = _grid.IndexOf(at);

            return _grid.KindAt(cell) == TerrainKind.Plains
                && _cells[cell] != 0
                && _clock.Now.DayNumber - Period(cell) < StumpDays;
        }

        /// <summary>
        /// Plants a tree on a cell of plains (#155): the cell turns to forest,
        /// and the tree stands <see cref="RegrowDays"/> from now, a sapling
        /// and <see cref="IsGrowing(int)"/> until then. Throws for a cell
        /// that is not plains.
        /// </summary>
        public void Plant(WorldPosition at)
        {
            var cell = CellOf(at, TerrainKind.Plains);
            var today = _clock.Now.DayNumber;

            _grid.Set(at, TerrainKind.Forest);
            _cells[cell] = Pack(today, TreeCuts);
            _planted[cell] = today;
        }

        /// <summary>
        /// Takes back a tree planted by a trip that never finished - its
        /// forester died on the way - leaving plains again, as
        /// <see cref="Return"/> gives back a harvest. Nothing to do when the
        /// ground has been cleared since, which leaves plains already. Throws
        /// for any other cell that is not a tree still growing.
        /// </summary>
        public void Unplant(WorldPosition at)
        {
            var cell = _grid.IndexOf(at);

            if (_grid.KindAt(cell) == TerrainKind.Plains)
            {
                return;
            }

            if (!IsGrowing(cell))
            {
                throw new InvalidOperationException(at + " has no planted tree growing to take back.");
            }

            _cells[cell] = 0;
            _planted.Remove(cell);
            _grid.Set(at, TerrainKind.Plains);
        }

        /// <summary>
        /// Whether the cell holds a tree planted on plains that has not stood
        /// yet (#155). Throws for an index off the grid.
        /// </summary>
        public bool IsGrowing(int cell)
        {
            if (cell < 0 || cell >= _cells.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(cell), cell, "Not a cell of the grid.");
            }

            return _planted.TryGetValue(cell, out var day) && _clock.Now.DayNumber - day < RegrowDays;
        }

        /// <summary>
        /// The day a planted tree went in, for one not yet cut - growing or
        /// grown; false for any other cell. What tells a grown tree from a
        /// stump, so the world hash folds it in. Throws for an index off the
        /// grid.
        /// </summary>
        public bool TryGetPlanted(int cell, out long day)
        {
            if (cell < 0 || cell >= _cells.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(cell), cell, "Not a cell of the grid.");
            }

            return _planted.TryGetValue(cell, out day);
        }

        /// <summary>
        /// The day the next planted tree stands, or <see cref="long.MaxValue"/>
        /// when none is growing: until then, what <see cref="Grown"/> accepts
        /// changes only with the grid.
        /// </summary>
        public long NextGrowth()
        {
            var today = _clock.Now.DayNumber;
            var next = long.MaxValue;

            // The smallest, so the dictionary's order does not matter.
            foreach (var planted in _planted)
            {
                var stands = planted.Value + RegrowDays;

                if (stands > today)
                {
                    next = Math.Min(next, stands);
                }
            }

            return next;
        }

        /// <summary>
        /// Trips a bush has left this season: <see cref="BushPicks"/> less
        /// those claimed, or none when it has no fruit (<see cref="HasFruit"/>).
        /// What clearing it gives (#155). Throws for a cell that is not scrub.
        /// </summary>
        public int PicksLeft(WorldPosition at)
        {
            var cell = CellOf(at, TerrainKind.Scrub);
            var now = _clock.Now;

            if (!IsWorkable(cell, now))
            {
                return 0;
            }

            return _cells[cell] != 0 && Period(cell) == SeasonIndex(now) ? BushPicks - Count(cell) : BushPicks;
        }

        /// <summary>
        /// Gives back a claim from a trip that never finished - its worker
        /// died on the way. A stump its claim had made stands again. Nothing
        /// to give back on ground cleared since: plains, or a tree planted
        /// there after (#155).
        /// </summary>
        public void Return(WorldPosition at)
        {
            var cell = _grid.IndexOf(at);

            switch (_grid.KindAt(cell))
            {
                case TerrainKind.Scrub:
                case TerrainKind.Forest:
                    // Ground cleared under the trip and planted since: the
                    // claim went with the clearing, and the sapling owes it
                    // nothing (#155).
                    if (_grid.KindAt(cell) == TerrainKind.Forest && _planted.ContainsKey(cell))
                    {
                        break;
                    }

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
        /// one untouched or planted and grown, fewer for one already being cut, none
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

            return IsGrown(cell, _clock.Now) ? TreeCuts : 0;
        }

        /// <summary>
        /// Grubs out a bush or fells a tree for good, turning the cell to
        /// plains (#100): the ground a building or field goes on. Plains grow
        /// nothing by themselves; only a forester's <see cref="Plant"/> puts a
        /// tree back (#155). Returns the cuts the tree had
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
            _planted.Remove(cell);
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
        /// How far a tree at this position has grown: a planted one a sapling
        /// until it stands, and a felled one a stump until its trip is home
        /// (<see cref="Fell"/>). For the view.
        /// Throws for a cell that is not forest.
        /// </summary>
        public TreeStage StageOf(WorldPosition at)
        {
            var cell = CellOf(at, TerrainKind.Forest);

            if (_cells[cell] == 0 || Count(cell) < TreeCuts)
            {
                return TreeStage.Standing;
            }

            if (!_planted.TryGetValue(cell, out var day))
            {
                return TreeStage.Stump;
            }

            // A sapling from the day it goes in: a stump is what felling
            // leaves, and planted ground should not look cut.
            return _clock.Now.DayNumber - day >= RegrowDays ? TreeStage.Standing : TreeStage.Sapling;
        }

        /// <summary>
        /// Claims given back so far. Time makes land workable again only at a
        /// season's start or a planted tree's growth, which a dawn's search sees; a claim
        /// given back does it mid-day, so a search that found nothing compares
        /// this with what it was then and looks again (#141 review).
        /// </summary>
        public long Returns { get; private set; }

        /// <summary>
        /// Whether any bush can have fruit in a season: none does in winter
        /// (#26), however long it has rested.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Not a defined season.</exception>
        public static bool BushesFruitIn(Season season) =>
            EnumGuard.IsDefined(DefinedSeasons, (int)season)
                ? season != Season.Winter
                : throw new ArgumentOutOfRangeException(nameof(season), season, "Not a defined Season.");

        private static readonly bool[] DefinedSeasons = EnumGuard.BuildMask(typeof(Season));

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
                    return BushesFruitIn(now.Season)
                        && (_cells[cell] == 0 || Period(cell) != SeasonIndex(now) || Count(cell) < BushPicks);
                case TerrainKind.Forest:
                    return _cells[cell] == 0 || Count(cell) < TreeCuts || IsGrown(cell, now);
                default:
                    return true;
            }
        }

        // A planted tree that has stood and not yet been cut.
        private bool IsGrown(int cell, SimulationTime now) =>
            _planted.TryGetValue(cell, out var day) && now.DayNumber - day >= RegrowDays;

        // Forest with every cut taken that is no planted tree: what a tree's
        // last cut leaves until its trip is home.
        private bool IsStump(int cell) =>
            _grid.KindAt(cell) == TerrainKind.Forest && _cells[cell] != 0 && Count(cell) >= TreeCuts && !_planted.ContainsKey(cell);

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

        private sealed class GrownFilter : ISiteFilter
        {
            private readonly LandCover _land;

            public GrownFilter(LandCover land) => _land = land;

            public bool Accepts(int cell) => !_land.IsGrowing(cell);
        }

        private sealed class PromisingFilter : ISiteFilter
        {
            private readonly LandCover _land;

            public PromisingFilter(LandCover land) => _land = land;

            public bool Accepts(int cell) => _land.IsPromising(cell, _land._clock.Now);
        }
    }
}
