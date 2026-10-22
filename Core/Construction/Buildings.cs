using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Events;
using KingdomWatch.Core.Knowledge;
using KingdomWatch.Core.Land;
using KingdomWatch.Core.Lifecycle;
using KingdomWatch.Core.Needs;
using KingdomWatch.Core.Relationships;
using KingdomWatch.Core.Traversal;
using KingdomWatch.Core.Work;

namespace KingdomWatch.Core.Construction
{
    /// <summary>
    /// Every building there is, what decides that a settlement puts up
    /// another, and the work that clears, raises and farms them (#100).
    /// </summary>
    /// <remarks>
    /// **Five conditions, checked at dawn.** <see cref="Jobs"/> calls
    /// <see cref="AtDawn"/> for each settlement after its workers have
    /// picked. A new building is approved only when all five of the plan's
    /// section 9 conditions hold: someone with the skill
    /// (<see cref="ISkillSource"/>), the wood in stock over and above the
    /// coming winter's fuel, the buildings it stands on (a barn needs a
    /// house, a field a barn with room for it in its count and on the
    /// ground around it), hands to spare - somebody found
    /// nothing needed at this dawn - and real demand. Demand is a household
    /// without a home, or food in reach falling short of the mouths to feed
    /// with less than a year in store (<see cref="IsFoodShort"/>), which a
    /// barn and then its fields answer. Food comes first: a hungry village
    /// builds a barn before it houses everyone, once it has the one house a
    /// barn needs. One building goes up at a time, so a settlement never
    /// spreads its hands across half-finished work.
    ///
    /// **Storage** (#98). A store that is full - within a tenth of its
    /// capacity (<see cref="CapacityOf"/>, <see cref="Storage.IsFull"/>) -
    /// while more is wanted going into a winter is demand for the building
    /// that holds more of it: a Granary, once there is a barn, for Food or
    /// for Grain, which Farmers stop at; a Woodshed, once there is a house,
    /// for Wood. A hungry village whose stores are full
    /// builds a Granary rather than more farmland; otherwise a Woodshed
    /// comes before a Granary, and both before houses. Storage is built from
    /// any wood in stock, the winter's fuel included.
    ///
    /// **The work competes with gathering.** An approved building's
    /// unclaimed hours, and each field's unclaimed hours today, are needs
    /// <see cref="Jobs"/> ranks against food, wood and stone by the same
    /// shortfall-as-a-share rule. The spare-hands condition is checked once,
    /// at approval; after that a village short of food still sends hands to
    /// food first, because food is then the store furthest below its
    /// target. Builder and Farmer trips claim <see cref="ShareTicks"/> at a
    /// time, so two Farmers split a field's day between them.
    ///
    /// **Placement is the town planner's** (#23, Buildings.Planning.cs): a
    /// house or barn goes where it scores best around the settlement with a
    /// lane from its door to the roads, a field where it scores best within
    /// <see cref="FieldRadius"/> of its barn with a road along one of its
    /// edges, each among the footprints the
    /// same bounded search a work site is found by reaches, so it is
    /// reachable and known. A footprint may stand on plains, scrub, forest
    /// or rocks alike (#155), which Builders clear before building starts,
    /// along with whatever is on its lane and its share of the square -
    /// <see cref="TicksPerCut"/> for each cut a standing tree has left, what
    /// woodcutting takes for one, and the wood those cuts would have given
    /// goes into stock; <see cref="ClearTicksPerCell"/> for a bush or a
    /// stump, a bush paying the fruit it has left this season and
    /// <see cref="WoodPerBush"/>; and <see cref="TicksPerRock"/> for each
    /// rock, which pays <see cref="StonePerRock"/>. The lane is laid as road when
    /// the clearing is done. Approved
    /// ground and lanes are reserved: nobody gathers there until they are
    /// cleared (<see cref="IsReserved(WorldPosition)"/>), so they are cleared
    /// as they were priced. Nothing regrows on cleared ground unless a
    /// Forester plants it (Buildings.Forestry.cs). Buildings
    /// still block nobody's path; that is #25's.
    ///
    /// **Fields.** A field needs <see cref="TendingDays"/> days of
    /// <see cref="FieldDayTicks"/> worker-ticks, then
    /// <see cref="HarvestDays"/> more of harvest, each harvest day bringing
    /// <see cref="GrainPerHarvestDay"/> Grain home; then it is sown again.
    /// A day counts once its hours are done, at most one a day, so a crop
    /// takes at least fifteen days however many hands it has. A day short
    /// of hands is a day later, never a crop lost. Nobody works a field in
    /// winter.
    ///
    /// **Homes, and the housing stock** (#69). A finished house goes to the
    /// first household in the settlement without one, in member order, at
    /// the next dawn: the band's tent families are housed first. A new
    /// household forms only into a finished house nobody lives in, once no
    /// tent family is waiting (<see cref="HasVacancy"/>, through
    /// <see cref="SettlementHousing"/>). Beyond the tent families, one spare
    /// house is built for the next couple when single adults of both sexes
    /// are waiting and the forest in reach can fuel the hearth it would
    /// light (<see cref="CanFuelAnotherHearth"/>) - housing, land and food
    /// together are what limit a village's growth.
    ///
    /// **Settling.** <see cref="CanSettle"/> is the band's half of the
    /// bargain: early spring, bushes in reach whose year's fruit would cover
    /// half as much again as a season's eating, ground for a barn - so a
    /// new village can feed itself to its first harvest - and a rock in
    /// reach, since every house, barn and store needs stone (#166).
    ///
    /// The numbers are placeholders, set by the harness.
    /// </remarks>
    public sealed partial class Buildings : IDomainEventSubscriber, IFoodOutlook
    {
        private const long Hour = SimulationTime.TicksPerHour;

        /// <summary>Worker-ticks of tending or harvest a field needs for one day to count.</summary>
        public const long FieldDayTicks = 4L * Hour;

        /// <summary>The most work one Builder or Farmer trip claims.</summary>
        public const long ShareTicks = 2L * Hour;

        /// <summary>Days of tending before a field is ripe.</summary>
        public const int TendingDays = 10;

        /// <summary>Days of harvest that bring a crop in.</summary>
        public const int HarvestDays = 5;

        /// <summary>
        /// Grain each day of harvest brings home: a crop of thirty, which at
        /// up to six crops a year mills into meals for about eight people.
        /// </summary>
        public const int GrainPerHarvestDay = 6;

        /// <summary>Fields one barn can have.</summary>
        public const int FieldsPerBarn = 4;

        /// <summary>How far from its barn, in cells, a field may be laid out.</summary>
        public const int FieldRadius = 8;

        /// <summary>
        /// Cells from a settlement's camp, each way, kept free of footprints
        /// and roofs: the yard its fire, well, woodpile, stone pile and
        /// first tents stand in (#150).
        /// </summary>
        public const int CampYardRadius = 3;

        /// <summary>Worker-ticks to grub out a bush or clear a stump for good.</summary>
        public const long ClearTicksPerCell = Hour;

        /// <summary>
        /// Worker-ticks each cut a standing tree has left adds to clearing
        /// it: what woodcutting takes for one (<see cref="PrimitiveTier.GatherWood"/>).
        /// </summary>
        public static readonly long TicksPerCut = PrimitiveTier.GatherWood.Duration;

        /// <summary>Quarrying trips a rock takes to clear (#23): in the square, on a lane or under a footprint.</summary>
        public const int TripsPerRock = 3;

        /// <summary>Worker-ticks to quarry away a rock: <see cref="TripsPerRock"/> stone-gathering trips.</summary>
        public static readonly long TicksPerRock = TripsPerRock * PrimitiveTier.GatherStone.Duration;

        /// <summary>Stone a cleared rock gives the clearer: what its <see cref="TripsPerRock"/> trips would have.</summary>
        public static readonly int StonePerRock = TripsPerRock * PrimitiveTier.GatherStone.Outputs[0].Quantity;

        /// <summary>
        /// Wood a bush gives the clearer, fruit or none (#155): brushwood, half
        /// what a woodcutting trip brings home.
        /// </summary>
        public const int WoodPerBush = 1;

        /// <summary>Wood each cut a tree had left gives the clearer: what woodcutting gets for one.</summary>
        public static readonly int WoodPerCut = PrimitiveTier.GatherWood.Outputs[0].Quantity;

        /// <summary>
        /// A building's or field's target is its hours times this, so its
        /// shortfall share starts at a half: below an empty store, level with
        /// one half full.
        /// </summary>
        public const long NeedScale = 2L;

        /// <summary>
        /// A field's meals in a year, counted toward food in reach: four
        /// crops, milled - a cautious count of the six a well-worked field gets.
        /// </summary>
        public const long FieldYearlyMeals = 4L * HarvestDays * GrainPerHarvestDay * PrimitiveTier.MealsPerGrain;

        // Food is short when what is in reach covers less than 5/4 of a
        // year's eating; a band settles only where it covers 3/2 of a season's.
        private const long ShortNumerator = 5L;
        private const long ShortDenominator = 4L;
        private const long SettleNumerator = 3L;
        private const long SettleDenominator = 2L;

        /// <summary>
        /// A new household's house waits until the forest in reach gives this
        /// many quarters of what its hearths, the new one included, burn in a
        /// winter (<see cref="CanFuelAnotherHearth"/>): the rest is the wood
        /// houses, barns and fields are built from.
        /// </summary>
        public const long WoodHeadroomNumerator = 5L;

        /// <summary>The denominator of <see cref="WoodHeadroomNumerator"/>.</summary>
        public const long WoodHeadroomDenominator = 4L;

        /// <summary>
        /// Years of eating a store must hold to count as cover even while it
        /// falls (<see cref="IsFoodShort(ICommunity, int)"/>): a village
        /// living off savings that deep keeps growing, and stops only as they
        /// run low.
        /// </summary>
        public const long DeepStoreYears = 5L;

        private static readonly int DefinedTerrainKinds = EnumGuard.BuildMask(typeof(TerrainKind)).Length;

        private readonly DomainEventBus _bus;
        private readonly SimulationClock _clock;
        private readonly PersonStore _people;
        private readonly Households _households;
        private readonly Partnerships _partnerships;
        private readonly Pathfinder _pathfinder;
        private readonly TerrainGrid _grid;
        private readonly KnownMaps _knownMaps;
        private readonly LandCover _land;
        private readonly ISkillSource _skills;

        // Creation order, which is id order: the order to iterate in.
        private readonly List<Building> _all = new List<Building>();
        private readonly ReadOnlyCollection<Building> _allView;

        // Footprint cells to the building on them, and households to their
        // house. Looked up, never iterated.
        private readonly Dictionary<int, Building> _onCell = new Dictionary<int, Building>();
        private readonly Dictionary<EntityId, Building> _homeOf = new Dictionary<EntityId, Building>();

        // Cells under a roof (BuildingSpec.Clearance), which no footprint may
        // take. Looked up, never iterated.
        private readonly HashSet<int> _underRoof = new HashSet<int>();

        private IReadOnlyList<ICommunity> _camps = Array.Empty<ICommunity>();

        // Founding order: the order a settlement is first seen at a dawn.
        private readonly List<StoreTrend> _trends = new List<StoreTrend>();
        private readonly ReadOnlyCollection<StoreTrend> _trendsView;

        private readonly bool[] _scrub;
        private readonly bool[] _forest;
        private readonly bool[] _ground;
        private readonly List<WorldPosition> _scratchRoute = new List<WorldPosition>();

        // What the store being placed stands by: its settlement's barns' middles
        // for a Granary, the forest cell its Woodcutters work for a Woodshed
        // (#167). Empty outside TryPlaceStorage.
        private readonly List<WorldPosition> _storeBy = new List<WorldPosition>();
        private readonly Woodland _woodland;
        private readonly Dictionary<EntityId, ReachCount> _forageCounts = new Dictionary<EntityId, ReachCount>();
        private readonly Dictionary<EntityId, ReachCount> _woodCounts = new Dictionary<EntityId, ReachCount>();
        private readonly Dictionary<EntityId, ReachCount> _plantedWoodCounts = new Dictionary<EntityId, ReachCount>();
        private readonly PlantableFilter _plantable;
        private readonly PlantableFilter _besideForest;
        private readonly List<EntityId> _scratchHearths = new List<EntityId>();

        public Buildings(
            DomainEventBus bus,
            PersonStore people,
            Households households,
            Partnerships partnerships,
            Pathfinder pathfinder,
            KnownMaps knownMaps,
            LandCover land,
            ISkillSource skills)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _people = people ?? throw new ArgumentNullException(nameof(people));
            _households = households ?? throw new ArgumentNullException(nameof(households));
            _partnerships = partnerships ?? throw new ArgumentNullException(nameof(partnerships));
            _pathfinder = pathfinder ?? throw new ArgumentNullException(nameof(pathfinder));
            _knownMaps = knownMaps ?? throw new ArgumentNullException(nameof(knownMaps));
            _land = land ?? throw new ArgumentNullException(nameof(land));
            _skills = skills ?? throw new ArgumentNullException(nameof(skills));
            _clock = bus.Clock;
            _grid = pathfinder.Grid;
            _allView = _all.AsReadOnly();
            _trendsView = _trends.AsReadOnly();

            _scrub = new bool[DefinedTerrainKinds];
            _scrub[(int)TerrainKind.Scrub] = true;
            _forest = new bool[DefinedTerrainKinds];
            _forest[(int)TerrainKind.Forest] = true;
            _ground = new bool[DefinedTerrainKinds];
            _ground[(int)TerrainKind.Plains] = true;
            _ground[(int)TerrainKind.Scrub] = true;
            _ground[(int)TerrainKind.Forest] = true;
            _ground[(int)TerrainKind.Rocks] = true;
            _anyKind = new bool[DefinedTerrainKinds];

            for (var kind = (int)TerrainKind.None + 1; kind < DefinedTerrainKinds; kind++)
            {
                _anyKind[kind] = true;
            }

            _scorer = new SiteScorer(this);
            _plantable = new PlantableFilter(this, besideForest: false);
            _besideForest = new PlantableFilter(this, besideForest: true);
            _woodland = new Woodland(land, this);
            _laneGate = new LaneGate(this);
            _networkTarget = new NetworkTarget(this);
            _streetGate = new StreetGate(this);
            _realNetwork = new RealNetwork(this);
            _linkTarget = new LinkTarget(this);
            _linkGate = new LinkGate(this);
        }

        /// <summary>Every building, oldest first. The order to iterate in.</summary>
        public IReadOnlyList<Building> All => _allView;

        /// <summary>
        /// Every settlement, whose camp yards placement keeps clear - not only
        /// the builder's own (the #153 review). <see cref="World"/> sets it
        /// to <see cref="Settlements.Founding.All"/>; empty until then.
        /// </summary>
        public IReadOnlyList<ICommunity> Camps
        {
            get => _camps;
            set => _camps = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>
        /// Food a year in a settlement's reach can give, as Food: every berry
        /// bush its foragers could be sent to, at a whole year's picking.
        /// </summary>
        public long BushYearlyFood
        {
            get
            {
                long perBush = 0L;

                for (var season = Season.Spring; season < Season.Winter; season++)
                {
                    perBush += PrimitiveTier.ForageIn(season).Outputs[0].Quantity;
                }

                return perBush * _land.BushPicks;
            }
        }

        /// <summary>
        /// How far, in route cost, the bushes <see cref="ForageInReach(WorldPosition, ReadOnlySpan{bool})"/>
        /// counts lie: a forager's near walk (<see cref="Jobs.NearWalk"/>),
        /// about an hour. Foragers go further, out to dusk, but a village
        /// does not count on bushes that far: counted, even at half, they
        /// held off its barn and fields until its near bushes were gone and
        /// trips a day out could not feed it - whole sides starved in the
        /// world runs.
        /// </summary>
        public static readonly long ForageReach = Jobs.NearWalk(PrimitiveTier.Forage.Duration);

        /// <summary>
        /// How far, in route cost, the trees <see cref="WoodInReach(WorldPosition, ReadOnlySpan{bool})"/>
        /// counts stand: as far as a woodcutter goes (<see cref="Jobs.ReachCost(long)"/>).
        /// </summary>
        public static readonly long WoodReach = Jobs.ReachCost(PrimitiveTier.GatherWood.Duration);

        /// <summary>
        /// The furthest walk, in route cost, at which a tree counts whole in
        /// <see cref="WoodInReach(WorldPosition, ReadOnlySpan{bool})"/> - a
        /// woodcutter's near walk (<see cref="Jobs.NearWalk"/>). Past it, out
        /// to <see cref="WoodReach"/>, a tree counts half: a woodcutter makes
        /// a trip a day fewer to it, and a village leans on it less.
        /// </summary>
        public static readonly long WoodFullReach = Jobs.NearWalk(PrimitiveTier.GatherWood.Duration);

        /// <summary>
        /// How far, in route cost, <see cref="HasRocksInReach"/> looks: as far
        /// as a stone gatherer goes (<see cref="Jobs.ReachCost(long)"/>).
        /// </summary>
        public static readonly long StoneReach = Jobs.ReachCost(PrimitiveTier.GatherStone.Duration);

        private static readonly TreeWeight Trees = new TreeWeight();

        /// <summary>A year of meals for this many people.</summary>
        public static long YearlyNeed(int living) =>
            living >= 0
                ? (long)Hunger.DailyRation * living * SimulationTime.DaysPerYear
                : throw new ArgumentOutOfRangeException(nameof(living), living, "Nobody is fewer than nobody.");

        /// <summary>The building whose footprint covers this cell, or null.</summary>
        public Building? At(WorldPosition at) =>
            _onCell.TryGetValue(_grid.IndexOf(at), out var building) ? building : null;

        /// <summary>
        /// Whether a cell is ground approved for a building, or for its lane,
        /// and not cleared yet: nobody gathers there (the #148 review), so the
        /// clearing it was priced at is the clearing it gets. Trips already
        /// out when it was approved still come home with what they claimed.
        /// </summary>
        public bool IsReserved(WorldPosition at) => IsReserved(_grid.IndexOf(at));

        /// <summary>
        /// <see cref="IsReserved(WorldPosition)"/>, by cell index. Throws for
        /// an index off the grid, as the position form does.
        /// </summary>
        public bool IsReserved(int cell)
        {
            if (cell < 0 || cell >= _grid.CellCount)
            {
                throw new ArgumentOutOfRangeException(nameof(cell), cell, "Not a cell of the grid.");
            }

            return (_onCell.TryGetValue(cell, out var building) && !building.Cleared) || _laneOf.ContainsKey(cell);
        }

        /// <summary>The house a household lives in, or null.</summary>
        public Building? HomeOf(EntityId household) =>
            _homeOf.TryGetValue(household, out var house) ? house : null;

        /// <summary>
        /// Food in reach of a place, in a year: the bushes a community there,
        /// knowing what <paramref name="mapHolder"/> knows, could forage.
        /// </summary>
        public long ForageInReach(WorldPosition at, EntityId mapHolder) =>
            Weighed(_forageCounts, at, mapHolder, _scrub, ForageReach, null, null) * BushYearlyFood;

        // A holder's bushes or trees in reach, weighed (null counts each
        // one) and filtered (null takes every one), counted again only when
        // it asks from somewhere else, the ground or any map has changed
        // since, or a tree it filtered out as growing has grown (#155): food in reach is asked at
        // every courtship and conception, and searching each time - for
        // trees, out to dusk - made a world run several times slower. Kept,
        // never saved: the count is the same whenever it is made, so a world
        // restored without it counts again.
        private long Weighed(
            Dictionary<EntityId, ReachCount> counts, WorldPosition at, EntityId holder, bool[] kinds, long reach, ICostWeight? weight, ISiteFilter? sites)
        {
            var known = _knownMaps.For(holder);

            if (!counts.TryGetValue(holder, out var count))
            {
                count = new ReachCount();
                counts.Add(holder, count);
            }
            else if (count.At == at && count.Rewrites == _grid.Rewrites && count.MapChanges == _knownMaps.Changes
                && _clock.Now.DayNumber < count.Until)
            {
                return count.Weighed;
            }

            count.At = at;
            count.Rewrites = _grid.Rewrites;
            count.MapChanges = _knownMaps.Changes;
            count.Until = sites is null ? long.MaxValue : _land.NextGrowth();
            count.Weighed = _pathfinder.WeighReachable(at, Jobs.Mover, kinds, known, int.MaxValue, reach, weight, sites);
            return count.Weighed;
        }

        private sealed class ReachCount
        {
            public WorldPosition At;
            public long Rewrites;
            public long MapChanges;

            // The day the count may next change with no rewrite: when a tree
            // the filter left out as growing stands.
            public long Until;
            public long Weighed;
        }

        /// <summary>
        /// <see cref="ForageInReach(WorldPosition, EntityId)"/> for a map
        /// nobody holds: one flag per grid cell, as <see cref="KnownMaps.For"/>
        /// gives, or empty for the whole map, as the <see cref="Pathfinder"/>
        /// reads it - what a world picks its starting villages from (#146).
        /// </summary>
        public long ForageInReach(WorldPosition at, ReadOnlySpan<bool> known) =>
            _pathfinder.CountReachable(at, Jobs.Mover, _scrub, known, int.MaxValue, ForageReach) * BushYearlyFood;

        /// <summary>
        /// Whether a band of this many could settle here: a whole year's
        /// picking of the bushes in reach (<see cref="ForageInReach"/>) would
        /// cover half as much again as one season's eating, the camp's yard
        /// keeps <see cref="VillageGap"/> from every settlement's buildings
        /// and yard - so nothing stands in or roofs over it either (the #153
        /// review, #154) - there is ground for a barn, and a rock in reach
        /// (<see cref="HasRocksInReach"/>, #166). The bushes are a third of
        /// what the words "a season of bushes" would suggest, since a bush
        /// gives a year's fruit over three seasons; it is the measure the
        /// #100 harness runs were tuned
        /// with, and one that camps on the placeholder map can pass - none
        /// has bushes for a year's eating. It only has to carry the village to
        /// its first harvest: a barn and a field are days of work, and the
        /// first crop is in some fifteen days after.
        /// And only in the first half of spring, so the village has a whole
        /// growing season for its fields before its first winter: one that
        /// settled late in spring got a single crop in and starved.
        /// </summary>
        /// <remarks>
        /// Ground for a barn is checked on its own (the #148 review). The
        /// house a barn needs first could take the only spot, or a barn could
        /// fit where no field does, leaving a village that cannot farm. Rare
        /// on a map that is mostly plains, and placement is #23's to replace,
        /// so this checks the barn alone.
        /// </remarks>
        public bool CanSettle(WorldPosition at, EntityId mapHolder, int living) =>
            CanSettle(at, _knownMaps.For(mapHolder), living);

        /// <summary>
        /// <see cref="CanSettle(WorldPosition, EntityId, int)"/> for a map
        /// nobody holds, read as <see cref="ForageInReach(WorldPosition, ReadOnlySpan{bool})"/>
        /// reads it: what a world checks its starting villages' sites against
        /// before anyone exists to know them (#146).
        /// </summary>
        public bool CanSettle(WorldPosition at, ReadOnlySpan<bool> known, int living)
        {
            // Inputs first, in every season: checked after the season, they
            // were refused in early spring and silently accepted the rest of
            // the year.
            var need = YearlyNeed(living);
            _grid.IndexOf(at);
            if (!known.IsEmpty && known.Length < _grid.CellCount)
            {
                throw new ArgumentException(
                    "The known mask covers " + known.Length + " cells; the grid has " + _grid.CellCount + ".", nameof(known));
            }

            var now = _clock.Now;

            return now.Season == Season.Spring
                && now.DayOfSeason < SimulationTime.DaysPerSeason / 2
                && ForageInReach(at, known) * SettleDenominator * SimulationTime.SeasonsPerYear >= need * SettleNumerator
                && YardIsApart(at)
                && TryPlace(BuildingKind.Barn, at, at, known, EntityId.None, out _)
                && HasRocksInReach(at, known);
        }

        /// <summary>
        /// Whether a settlement is outgrowing its food: bushes in reach and
        /// fields standing give less than five quarters of a year's eating,
        /// and the stores do not already hold a year of it - or they do, but
        /// held more a year ago (<see cref="StoreTrends"/>) and hold less than
        /// <see cref="DeepStoreYears"/> years of it. What a barn and
        /// its fields are built for; a village sitting on a year's Grain that
        /// is not shrinking builds no more fields however few bushes it has.
        /// </summary>
        /// <remarks>
        /// A store falling year on year is a village eating its savings: in
        /// the #100 harness runs one held off new fields for seventeen years
        /// on a year's Grain that was draining the whole time, and by the year
        /// food counted as short it had no wood left to build with (#149).
        /// </remarks>
        public bool IsFoodShort(ICommunity settlement, int living)
        {
            if (settlement is null)
            {
                throw new ArgumentNullException(nameof(settlement));
            }

            var need = YearlyNeed(living);

            var meals = Hunger.MealsInStore(settlement.SharedSupplies);

            if (meals >= need * DeepStoreYears || (meals >= need && !IsStoreFalling(settlement.Id)))
            {
                return false;
            }

            var supply = ForageInReach(settlement.Position, settlement.Id);

            for (var i = 0; i < _all.Count; i++)
            {
                var field = _all[i];

                if (field.Settlement == settlement.Id && field.Kind == BuildingKind.Field && field.IsComplete)
                {
                    supply += FieldYearlyMeals;
                }
            }

            return supply * ShortDenominator < need * ShortNumerator;
        }

        /// <summary>
        /// <see cref="IsFoodShort(ICommunity, int)"/> for a settlement, counting its
        /// living members; false for any other community, which builds no
        /// fields to be short of. What holds back a hungry village's courtship
        /// and births (<see cref="IFoodOutlook"/>, #69).
        /// </summary>
        public bool IsFoodShort(EntityId community)
        {
            if (community.Kind != EntityKind.Settlement)
            {
                return false;
            }

            var settlement = CampOf(community);
            var members = settlement.Members;
            var living = 0;

            for (var i = 0; i < members.Count; i++)
            {
                if (_people.IsAlive(members[i]))
                {
                    living++;
                }
            }

            return IsFoodShort(settlement, living);
        }

        /// <summary>
        /// Wood a year the forest in a settlement's reach gives back: every
        /// grown tree its woodcutters could be sent to, felled and replanted
        /// every <see cref="LandCover.RegrowDays"/> (#155: nothing grows back
        /// unless a Forester plants it). A sapling is left out until it has
        /// grown, and felled ground is plains and is not counted.
        /// </summary>
        public long WoodInReach(ICommunity settlement)
        {
            if (settlement is null)
            {
                throw new ArgumentNullException(nameof(settlement));
            }

            return WoodFromTrees(Weighed(_woodCounts, settlement.Position, settlement.Id, _forest, WoodReach, Trees, _land.Grown)) / TreeWeight.Whole;
        }

        /// <summary>
        /// <see cref="WoodInReach(ICommunity)"/> from a place, knowing a map
        /// nobody holds, read as <see cref="ForageInReach(WorldPosition, ReadOnlySpan{bool})"/>
        /// reads it: what a world ranks its starting villages' sites by, since
        /// it is the wood that bounds how many hearths a village can keep
        /// (#146).
        /// </summary>
        public long WoodInReach(WorldPosition at, ReadOnlySpan<bool> known)
        {
            return WoodFromTrees(_pathfinder.WeighReachable(at, Jobs.Mover, _forest, known, int.MaxValue, WoodReach, Trees, _land.Grown)) / TreeWeight.Whole;
        }

        /// <summary>
        /// Whether a stone gatherer from a place, knowing a map as
        /// <see cref="ForageInReach(WorldPosition, ReadOnlySpan{bool})"/>
        /// reads it, could walk to a rock within <see cref="StoneReach"/>.
        /// One rock is enough: rocks are never used up (<see cref="LandCover"/>),
        /// so a village with one in reach has stone for good, and one with
        /// none could never build (#166).
        /// </summary>
        public bool HasRocksInReach(WorldPosition at, ReadOnlySpan<bool> known) =>
            _pathfinder.TryFindNearest(
                at, Jobs.Mover, JobTable.Terrain(JobKind.StoneGatherer), known, null, null, false, int.MaxValue, StoneReach, _scratchRoute, out _);

        /// <summary>
        /// Wood a year this many forest cells give back, each felled and
        /// replanted every <see cref="LandCover.RegrowDays"/>: what
        /// <see cref="WoodInReach(ICommunity)"/> makes of the trees it counts.
        /// Never falls as the count rises, so a bound on the trees is a bound
        /// on the wood (#146).
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The count is negative.</exception>
        /// <exception cref="OverflowException">The wood would not fit a long: refused rather than wrapped.</exception>
        public long WoodFromTrees(long trees) =>
            trees >= 0
                ? checked(trees * _land.TreeCuts * WoodPerCut * SimulationTime.DaysPerYear) / LandCover.RegrowDays
                : throw new ArgumentOutOfRangeException(nameof(trees), trees, "No fewer trees than none.");

        // A tree in reach weighs Whole out to WoodFullReach and one out to
        // WoodReach: half, counted in halves.
        private sealed class TreeWeight : ICostWeight
        {
            public const long Whole = 2L;

            public long WeightOf(long cost) => cost <= WoodFullReach ? Whole : cost <= WoodReach ? 1L : 0L;
        }

        /// <summary>
        /// <see cref="WoodInReach(WorldPosition, ReadOnlySpan{bool})"/> as it
        /// would come to with this many trees near enough to count whole and
        /// this many in all, each other one at the weight of a trip a day
        /// fewer: an estimate, read off counts of trees rather than walks to
        /// them. What a world ranks its starting sites' candidates by (#146).
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">A count is negative, or the near ones outnumber them all.</exception>
        public long WoodEstimate(int nearTrees, int trees)
        {
            if (nearTrees < 0 || nearTrees > trees)
            {
                throw new ArgumentOutOfRangeException(nameof(nearTrees), nearTrees, "Between none and the " + trees + " in all.");
            }

            return WoodFromTrees((nearTrees * TreeWeight.Whole) + (trees - nearTrees)) / TreeWeight.Whole;
        }

        /// <summary>
        /// Whether the forest in reach (<see cref="WoodInReach"/>) would keep
        /// one more hearth than the settlement has lit through every winter,
        /// with <see cref="WoodHeadroomNumerator"/>/<see cref="WoodHeadroomDenominator"/>
        /// of it to spare for building. What a house for a new household
        /// waits on (#69): a home is a hearth, and the land has to carry it.
        /// </summary>
        public bool CanFuelAnotherHearth(ICommunity settlement)
        {
            var hearths = Warmth.CountHearths(
                (settlement ?? throw new ArgumentNullException(nameof(settlement))).Members, _people, _scratchHearths);
            var burn = (hearths + 1L) * Warmth.FuelPerFire * SimulationTime.DaysPerSeason;
            return WoodInReach(settlement) * WoodHeadroomDenominator >= burn * WoodHeadroomNumerator;
        }

        /// <summary>
        /// Each settlement's meals in store at its last two first-days-of-spring,
        /// in founding order: what <see cref="IsFoodShort"/> reads to tell a
        /// store being saved from one being eaten.
        /// </summary>
        public IReadOnlyList<StoreTrend> StoreTrends => _trendsView;

        /// <summary>
        /// How much of a kind a community can hold (#98): the
        /// <see cref="Storage"/> base, plus what each of its finished storage
        /// buildings adds. A band has no buildings, so it has the base.
        /// </summary>
        public long CapacityOf(EntityId community, ResourceKind kind)
        {
            long capacity = Storage.BaseOf(kind);

            if (community.Kind != EntityKind.Settlement)
            {
                return capacity;
            }

            for (var i = 0; i < _all.Count; i++)
            {
                var building = _all[i];

                if (building.Settlement == community && building.IsComplete)
                {
                    capacity += Storage.AddedBy(building.Kind, kind);
                }
            }

            return capacity;
        }

        /// <summary>
        /// A community's finished houses: none for a band, which builds
        /// nothing. What its stone reserve grows with (<see cref="Jobs.StoneWant"/>, #166).
        /// </summary>
        public int FinishedHouses(EntityId community)
        {
            var houses = 0;

            if (community.Kind != EntityKind.Settlement)
            {
                return houses;
            }

            for (var i = 0; i < _all.Count; i++)
            {
                var building = _all[i];

                if (building.Settlement == community && building.Kind == BuildingKind.House && building.IsComplete)
                {
                    houses++;
                }
            }

            return houses;
        }

        /// <summary>
        /// Whether a settlement has a home for a new household: a finished
        /// house nobody lives in, and no household of its own still in a
        /// tent - those are housed first, oldest first (#69).
        /// For <see cref="SettlementHousing"/>.
        /// </summary>
        public bool HasVacancy(EntityId settlement) =>
            !(FreeHouse(settlement) is null) && !TryFirstUnhoused(CampOf(settlement), out _);

        /// <summary>
        /// The house a household about to form in this settlement will take:
        /// its first free one. Changes nothing - the household moves in when
        /// its forming is announced (<see cref="DomainEventKind.HouseholdFormed"/>,
        /// whose secondary entity is this id), which <see cref="Households.Form"/>
        /// does next - so a claim never leaves state behind for the world
        /// hash to miss (the #156 review). Throws when there is no vacancy.
        /// For <see cref="SettlementHousing"/>.
        /// </summary>
        public EntityId Claim(EntityId settlement)
        {
            if (!HasVacancy(settlement) || !(FreeHouse(settlement) is Building house))
            {
                throw new InvalidOperationException(settlement + " has no house free for a new household.");
            }

            return house.Id;
        }

        /// <summary>
        /// A house given back by a household that has dissolved. Its
        /// <see cref="DomainEventKind.HouseholdDissolved"/> has already
        /// emptied it, since <see cref="Households.Dissolve"/> announces
        /// before it releases; this only checks that it did.
        /// For <see cref="SettlementHousing"/>.
        /// </summary>
        public void Release(EntityId home)
        {
            var house = Find(home);

            if (house is null || house.Kind != BuildingKind.House || !house.Occupant.IsNone)
            {
                throw new InvalidOperationException(home + " is not an empty house, so it cannot be given back.");
            }
        }

        /// <summary>
        /// A settlement's dawn, after its workers have picked: empty houses go
        /// to households without one, and when <paramref name="idle"/> says
        /// there were hands to spare, the next building is approved if the
        /// other four conditions hold. Bands are not settlements and build
        /// nothing. Refuses counts that cannot be: a negative one, or more
        /// hands idle than people alive (the #148 review).
        /// </summary>
        public void AtDawn(ICommunity settlement, int idle, int living)
        {
            if (settlement is null)
            {
                throw new ArgumentNullException(nameof(settlement));
            }

            if (settlement.Id.Kind != EntityKind.Settlement)
            {
                throw new ArgumentException(settlement.Id + " is not a settlement; only settlements build.", nameof(settlement));
            }

            if (living < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(living), living, "Nobody is fewer than nobody.");
            }

            if (idle < 0 || idle > living)
            {
                throw new ArgumentOutOfRangeException(nameof(idle), idle, "Between none and the " + living + " alive.");
            }

            if (_clock.Now.Season == Season.Winter)
            {
                Frost(settlement);
            }

            ReadStore(settlement);
            PaveSquare(settlement.Position);
            AssignHomes(settlement);

            if (idle > 0)
            {
                Plan(settlement, living);
            }
        }

        /// <summary>
        /// The settlement's building under way with hours nobody has claimed,
        /// and how short it is against its target: what <see cref="Jobs"/>
        /// ranks a Builder by.
        /// </summary>
        public bool TryBuildWork(EntityId settlement, [NotNullWhen(true)] out Building? project, out long shortBy, out long target)
        {
            for (var i = 0; i < _all.Count; i++)
            {
                var building = _all[i];

                if (building.Settlement != settlement || building.IsComplete)
                {
                    continue;
                }

                project = building;
                shortBy = building.LabourTicks - building.Worked - building.Claimed;
                target = building.LabourTicks * NeedScale;
                return shortBy > 0L;
            }

            project = null;
            shortBy = 0L;
            target = 0L;
            return false;
        }

        /// <summary>
        /// The settlement's first field with today's hours unclaimed, and how
        /// short all its fields are today: what <see cref="Jobs"/> ranks a
        /// Farmer by. Nothing in winter, nor while the Grain is at capacity:
        /// the fields stand idle rather than grow a crop with nowhere to keep
        /// it (#98).
        /// </summary>
        public bool TryFieldWork(EntityId settlement, [NotNullWhen(true)] out Building? field, out long shortBy, out long target)
        {
            field = null;
            shortBy = 0L;
            target = 0L;
            var now = _clock.Now;

            if (now.Season == Season.Winter)
            {
                return false;
            }

            for (var i = 0; i < _all.Count; i++)
            {
                var candidate = _all[i];

                if (candidate.Settlement != settlement || candidate.Kind != BuildingKind.Field || !candidate.IsComplete)
                {
                    continue;
                }

                ToToday(candidate, now);
                var left = FieldDayTicks - candidate.ClaimedToday - candidate.WorkedToday;
                target += FieldDayTicks * NeedScale;

                if (left > 0L)
                {
                    shortBy += left;
                    field ??= candidate;
                }
            }

            if (field is object && CampOf(settlement).SharedSupplies.Available(ResourceKind.Grain) >= CapacityOf(settlement, ResourceKind.Grain))
            {
                field = null;
                shortBy = 0L;
                target = 0L;
            }

            return field is object;
        }

        /// <summary>The work one trip to this building claims: a share, or what is left.</summary>
        /// <exception cref="ArgumentException">The job does not work this building; see <see cref="Claim"/>.</exception>
        public long ShareOf(Building building, JobKind job) => Math.Min(ShareTicks, LeftFor(building, job));

        /// <summary>
        /// A trip sets out to work this building for this long. A Builder
        /// works a building still going up, a Farmer a finished field; and a
        /// trip claims no more than is left.
        /// </summary>
        public void Claim(Building building, JobKind job, long ticks)
        {
            var left = LeftFor(building, job);

            if (ticks <= 0L || ticks > left)
            {
                throw new ArgumentOutOfRangeException(nameof(ticks), ticks, "Between one tick and the " + left + " left on " + building + ".");
            }

            if (job == JobKind.Farmer)
            {
                building.ClaimedToday += ticks;
            }
            else
            {
                building.Claimed += ticks;
            }
        }

        /// <summary>
        /// A trip that never came home gives its claim back. A field's day
        /// has always turned since a trip that died yesterday, and its claim
        /// went with it. Throws for a cell no building covers, a job that
        /// does not work it, or more than is claimed.
        /// </summary>
        public void Unclaim(WorldPosition at, JobKind job, long ticks)
        {
            var building = RequireAt(at);
            RequireWorks(building, job);

            if (job == JobKind.Farmer)
            {
                if (building.CountsDay == _clock.Now.DayNumber)
                {
                    building.ClaimedToday -= RequireClaimed(building, ticks, building.ClaimedToday);
                }
            }
            else
            {
                building.Claimed -= RequireClaimed(building, ticks, building.Claimed);
            }
        }

        /// <summary>
        /// A trip comes home having worked this long: the ground clears, the
        /// building stands, a field's day counts or its harvest comes in, as
        /// the hours reach each. Felled wood and harvested Grain go to
        /// <paramref name="stores"/>. Throws for a cell no building covers,
        /// a job that does not work it, or more than was claimed.
        /// </summary>
        public void Credit(WorldPosition at, JobKind job, long ticks, ResourceLedger stores)
        {
            if (stores is null)
            {
                throw new ArgumentNullException(nameof(stores));
            }

            var building = RequireAt(at);
            RequireWorks(building, job);

            if (job == JobKind.Farmer)
            {
                ToToday(building, _clock.Now);
                RequireClaimed(building, ticks, building.ClaimedToday);
                CreditField(building, ticks, stores);
                return;
            }

            RequireClaimed(building, ticks, building.Claimed);
            building.Claimed -= ticks;
            building.Worked += ticks;

            if (!building.Cleared && building.Worked >= building.ClearTicks)
            {
                ClearGround(building, stores);
            }

            if (building.IsComplete)
            {
                _bus.Publish(DomainEventKind.BuildingCompleted, building.Id, building.Settlement);
            }
        }

        public void On(in DomainEvent published)
        {
            // A household formed into a house moves in as its forming is
            // announced (Claim): the announcement names the house. One named
            // into a house that is not an empty house of ours is a wiring
            // bug, refused before anything changes.
            if (published.Kind == DomainEventKind.HouseholdFormed)
            {
                if (published.SecondaryEntity.Kind == EntityKind.Building)
                {
                    var home = Find(published.SecondaryEntity);

                    if (home is null || home.Kind != BuildingKind.House || !home.IsComplete || !home.Occupant.IsNone)
                    {
                        throw new InvalidOperationException(
                            published.PrimaryEntity + " was formed into " + published.SecondaryEntity + ", which is not an empty house.");
                    }

                    home.Occupant = published.PrimaryEntity;
                    _homeOf.Add(published.PrimaryEntity, home);
                }

                return;
            }

            if (published.Kind != DomainEventKind.HouseholdDissolved)
            {
                return;
            }

            if (_homeOf.TryGetValue(published.PrimaryEntity, out var house))
            {
                house.Occupant = EntityId.None;
                _homeOf.Remove(published.PrimaryEntity);
            }
        }

        // Unclaimed hours a job could still take on a building today.
        private long LeftFor(Building building, JobKind job)
        {
            RequireWorks(building, job);

            if (job == JobKind.Farmer)
            {
                ToToday(building, _clock.Now);
                return FieldDayTicks - building.ClaimedToday - building.WorkedToday;
            }

            return building.LabourTicks - building.Worked - building.Claimed;
        }

        // A Builder works what is still going up, a Farmer a finished field;
        // any other job, a gathering one or an undefined value, works neither.
        private static void RequireWorks(Building building, JobKind job)
        {
            if (building is null)
            {
                throw new ArgumentNullException(nameof(building));
            }

            var works = job == JobKind.Builder ? !building.IsComplete
                : job == JobKind.Farmer && building.Kind == BuildingKind.Field && building.IsComplete;

            if (!works)
            {
                throw new ArgumentException(job + " does not work " + building + (building.IsComplete ? ", which stands." : ", which is going up."), nameof(job));
            }
        }

        private static long RequireClaimed(Building building, long ticks, long claimed) =>
            ticks > 0L && ticks <= claimed
                ? ticks
                : throw new ArgumentOutOfRangeException(nameof(ticks), ticks, "Between one tick and the " + claimed + " claimed on " + building + ".");

        private void CreditField(Building field, long ticks, ResourceLedger stores)
        {
            field.ClaimedToday -= ticks;
            field.WorkedToday += ticks;

            if (field.WorkedToday < FieldDayTicks)
            {
                return;
            }

            // A harvest day's Grain comes in when its last hours do: a day
            // left unfinished is forgotten at midnight, and Grain paid per
            // share would let a share a day harvest forever (the #148 review).
            if (field.Stage == FieldStage.Harvesting)
            {
                stores.Gather(ResourceKind.Grain, GrainPerHarvestDay);
                Storage.Spill(stores, ResourceKind.Grain, CapacityOf(field.Settlement, ResourceKind.Grain));
            }

            field.DaysDone++;

            if (field.Stage == FieldStage.Tending && field.DaysDone == TendingDays)
            {
                field.Stage = FieldStage.Harvesting;
                field.DaysDone = 0;
            }
            else if (field.Stage == FieldStage.Harvesting && field.DaysDone == HarvestDays)
            {
                field.Stage = FieldStage.Tending;
                field.DaysDone = 0;
                _bus.Publish(DomainEventKind.FieldHarvested, field.Id, field.Settlement);
            }
        }

        // Today's field counts start from nothing: yesterday's unfinished
        // hours are a day lost, not a day half done.
        private static void ToToday(Building field, SimulationTime now)
        {
            if (field.CountsDay != now.DayNumber)
            {
                field.CountsDay = now.DayNumber;
                field.ClaimedToday = 0L;
                field.WorkedToday = 0L;
            }
        }

        private void ClearGround(Building building, ResourceLedger stores)
        {
            for (var dy = 0; dy < building.Height; dy++)
            {
                for (var dx = 0; dx < building.Width; dx++)
                {
                    var at = new WorldPosition(building.Anchor.X + dx, building.Anchor.Y + dy);
                    var kind = _grid[at];

                    // Bushes are grubbed out below, with their fruit counted.
                    if (kind == TerrainKind.Forest)
                    {
                        _land.Clear(at);
                    }
                    else if (kind == TerrainKind.Rocks)
                    {
                        // Quarried away; rocks hold no claims to forget.
                        _grid.Set(at, TerrainKind.Plains);
                    }
                }
            }

            // Bushes are grubbed out with the rest, footprint, lane and
            // square: their fruit, as they stand now, and a little wood each.
            var bushes = 0;
            var picks = 0;
            GrubBushes(building.Anchor, building.Width, building.Height, ref bushes, ref picks);
            GrubBushes(building.Lane, ref bushes, ref picks);
            GrubBushes(building.Square, ref bushes, ref picks);

            building.Cleared = true;
            LayLane(building);

            if (picks > 0)
            {
                stores.Gather(ResourceKind.Food, picks * FoodPerPick(_clock.Now.Season));
                Storage.Spill(stores, ResourceKind.Food, CapacityOf(building.Settlement, ResourceKind.Food));
            }

            if (bushes > 0)
            {
                stores.Gather(ResourceKind.Wood, bushes * WoodPerBush);
                Storage.Spill(stores, ResourceKind.Wood, CapacityOf(building.Settlement, ResourceKind.Wood));
            }

            // The Wood of the cuts the clearing was priced at: a claim out at
            // approval and given back since leaves a tree with a cut more than
            // anyone was charged for, and that cut is not paid.
            if (building.ClearCuts > 0)
            {
                stores.Gather(ResourceKind.Wood, building.ClearCuts * WoodPerCut);
                Storage.Spill(stores, ResourceKind.Wood, CapacityOf(building.Settlement, ResourceKind.Wood));
            }

            if (building.ClearRocks > 0)
            {
                stores.Gather(ResourceKind.Stone, building.ClearRocks * StonePerRock);
                Storage.Spill(stores, ResourceKind.Stone, CapacityOf(building.Settlement, ResourceKind.Stone));
            }
        }

        // Counts and grubs out the bushes of a footprint, adding their fruit
        // left (LandCover.PicksLeft) to picks.
        private void GrubBushes(WorldPosition anchor, int width, int height, ref int bushes, ref int picks)
        {
            for (var dy = 0; dy < height; dy++)
            {
                for (var dx = 0; dx < width; dx++)
                {
                    GrubBush(new WorldPosition(anchor.X + dx, anchor.Y + dy), ref bushes, ref picks);
                }
            }
        }

        private void GrubBushes(IReadOnlyList<WorldPosition> cells, ref int bushes, ref int picks)
        {
            for (var i = 0; i < cells.Count; i++)
            {
                GrubBush(cells[i], ref bushes, ref picks);
            }
        }

        private void GrubBush(WorldPosition at, ref int bushes, ref int picks)
        {
            if (_grid[at] != TerrainKind.Scrub)
            {
                return;
            }

            bushes++;
            picks += _land.PicksLeft(at);
            _land.Clear(at);
        }

        /// <summary>
        /// Food a cleared bush gives for each trip it had left this season
        /// (#155): what a forager's trip would have brought home.
        /// </summary>
        public static int FoodPerPick(Season season) => PrimitiveTier.ForageIn(season).Outputs[0].Quantity;

        // Winter kills what stands in the settlement's fields: a growing crop,
        // or the part of a harvest not yet brought in - its finished days'
        // Grain is already in store. Each field is sown afresh, to be tended
        // from the spring (#150). Every winter dawn, which finds the fields
        // already sown from the first.
        private void Frost(ICommunity settlement)
        {
            for (var i = 0; i < _all.Count; i++)
            {
                var field = _all[i];

                if (field.Settlement == settlement.Id && field.Kind == BuildingKind.Field)
                {
                    field.Stage = FieldStage.Tending;
                    field.DaysDone = 0;
                }
            }
        }

        // Empty finished houses go to the settlement's households without
        // one, oldest first. The
        // household is told, so its Home names the house as one claimed at
        // forming does, and dissolving it gives the house back.
        private void AssignHomes(ICommunity settlement)
        {
            for (var i = 0; i < _all.Count; i++)
            {
                var house = _all[i];

                if (house.Settlement != settlement.Id || house.Kind != BuildingKind.House
                    || !house.IsComplete || !house.Occupant.IsNone)
                {
                    continue;
                }

                if (!TryFirstUnhoused(settlement, out var household))
                {
                    return;
                }

                house.Occupant = household.Id;
                _homeOf.Add(household.Id, house);
                _households.MoveIn(household, house.Id);
            }
        }

        // One house kept ready for the next couple (#69): none stands empty,
        // a single woman and a single man are waiting, and the land can fuel
        // the hearth the house would light. A household in a tent is housed
        // first, so it is not asked about here.
        private bool WantsSpareHouse(ICommunity settlement)
        {
            if (!(FreeHouse(settlement.Id) is null))
            {
                return false;
            }

            var women = false;
            var men = false;
            var members = settlement.Members;

            for (var i = 0; i < members.Count && !(women && men); i++)
            {
                var member = members[i];

                if (!_people.IsAlive(member) || !AgeStages.IsAdult(_people.GetAgeStage(member))
                    || !_partnerships.ActivePartnerOf(_people.GetId(member)).IsNone)
                {
                    continue;
                }

                women |= _people.GetSex(member) == Sex.Female;
                men |= _people.GetSex(member) == Sex.Male;
            }

            return women && men && CanFuelAnotherHearth(settlement);
        }

        // The settlement's first finished house nobody lives in, in creation
        // order.
        private Building? FreeHouse(EntityId settlement)
        {
            for (var i = 0; i < _all.Count; i++)
            {
                var house = _all[i];

                if (house.Settlement == settlement && house.Kind == BuildingKind.House && house.IsComplete
                    && house.Occupant.IsNone)
                {
                    return house;
                }
            }

            return null;
        }

        private Building? Find(EntityId building)
        {
            for (var i = 0; i < _all.Count; i++)
            {
                if (_all[i].Id == building)
                {
                    return _all[i];
                }
            }

            return null;
        }

        private ICommunity CampOf(EntityId settlement)
        {
            for (var i = 0; i < _camps.Count; i++)
            {
                if (_camps[i].Id == settlement)
                {
                    return _camps[i];
                }
            }

            throw new InvalidOperationException(settlement + " is not a settlement Buildings knows (Camps).");
        }

        // On the first day of each year, what the stores hold moves this
        // year's reading to last year's (IsFoodShort's trend).
        private void ReadStore(ICommunity settlement)
        {
            StoreTrend? trend = null;

            for (var i = 0; i < _trends.Count && trend is null; i++)
            {
                if (_trends[i].Settlement == settlement.Id)
                {
                    trend = _trends[i];
                }
            }

            if (trend is null)
            {
                trend = new StoreTrend(settlement.Id);
                _trends.Add(trend);
            }

            var day = _clock.Now.DayNumber;

            if (_clock.Now.DayOfYear != 0 || trend.ReadOn == day)
            {
                return;
            }

            trend.LastYear = trend.ThisYear;
            trend.ThisYear = Hunger.MealsInStore(settlement.SharedSupplies);
            trend.ReadOn = day;
        }

        private bool IsStoreFalling(EntityId settlement)
        {
            for (var i = 0; i < _trends.Count; i++)
            {
                var trend = _trends[i];

                if (trend.Settlement == settlement)
                {
                    return trend.LastYear >= 0L && trend.ThisYear < trend.LastYear;
                }
            }

            return false;
        }

        private void Plan(ICommunity settlement, int living)
        {
            var barnWithRoom = false;
            var hasHouse = false;
            var hasBarn = false;

            for (var i = 0; i < _all.Count; i++)
            {
                var standing = _all[i];

                if (standing.Settlement != settlement.Id)
                {
                    continue;
                }

                // One at a time.
                if (!standing.IsComplete)
                {
                    return;
                }

                hasHouse |= standing.Kind == BuildingKind.House;
                hasBarn |= standing.Kind == BuildingKind.Barn;

                barnWithRoom |= standing.Kind == BuildingKind.Barn && FieldsOf(standing) < FieldsPerBarn;
            }

            BuildingKind kind;
            WorldPosition anchor;
            Building? barn = null;
            var known = _knownMaps.For(settlement.Id);

            var foodShort = hasHouse && IsFoodShort(settlement, living);
            var wantsGranary = hasBarn && WantsGranary(settlement, living, foodShort);

            // Food short with the stores full: more farmland cannot help, a
            // Granary can (#98).
            if (foodShort && wantsGranary)
            {
                kind = BuildingKind.Granary;

                if (!TryPlaceStorage(settlement, kind, known, out anchor))
                {
                    return;
                }
            }
            else if (foodShort)
            {
                if (barnWithRoom && !CanAfford(settlement, BuildingKind.Field))
                {
                    return;
                }

                // A field goes to the first barn with ground left for one. A
                // barn with room on paper but none on the ground counts as
                // full: kept as the barn with room, it held off every barn
                // after it, and a hungry village stayed hungry with its hands
                // idle for decades (#163).
                if (barnWithRoom && TryPlaceField(settlement, known, out anchor, out barn))
                {
                    kind = BuildingKind.Field;
                }
                else
                {
                    kind = BuildingKind.Barn;

                    if (!CanAfford(settlement, kind)
                        || !TryPlace(kind, settlement.Position, settlement.Position, known, settlement.Id, out anchor))
                    {
                        return;
                    }
                }
            }
            else if (hasHouse && WantsWoodshed(settlement))
            {
                kind = BuildingKind.Woodshed;

                if (!TryPlaceStorage(settlement, kind, known, out anchor))
                {
                    return;
                }
            }
            else if (wantsGranary)
            {
                kind = BuildingKind.Granary;

                if (!TryPlaceStorage(settlement, kind, known, out anchor))
                {
                    return;
                }
            }
            else if (TryFirstUnhoused(settlement, out _) || WantsSpareHouse(settlement))
            {
                kind = BuildingKind.House;

                if (!CanAfford(settlement, kind)
                    || !TryPlace(kind, settlement.Position, settlement.Position, known, settlement.Id, out anchor))
                {
                    return;
                }
            }
            else
            {
                return;
            }

            var spec = BuildingTable.Of(kind);
            var stores = settlement.SharedSupplies;

            stores.Embody(ResourceKind.Wood, spec.Wood);

            if (spec.Stone > 0)
            {
                stores.Embody(ResourceKind.Stone, spec.Stone);
            }

            CollectSquare(settlement.Position);
            AddLinks(kind, anchor, settlement.Position);
            var clearTicks = ClearTicksOf(kind, anchor, _lane, out var clearCuts, out var clearRocks);
            var building = new Building(
                _clock.Ids.Next(EntityKind.Building), kind, settlement.Id, barn?.Id ?? EntityId.None,
                anchor, _lane.ToArray(), _square.ToArray(), clearTicks, clearCuts, clearRocks, clearTicks + spec.BuildTicks);

            _all.Add(building);

            for (var i = 0; i < building.Lane.Count; i++)
            {
                _laneOf.Add(_grid.IndexOf(building.Lane[i]), building);
            }

            for (var i = 0; i < building.Square.Count; i++)
            {
                _laneOf.Add(_grid.IndexOf(building.Square[i]), building);
            }

            // Nothing to clear: the lane is laid at once.
            if (building.Cleared)
            {
                LayLane(building);
            }

            for (var dy = 0; dy < building.Height; dy++)
            {
                for (var dx = 0; dx < building.Width; dx++)
                {
                    _onCell.Add(_grid.IndexOf(new WorldPosition(anchor.X + dx, anchor.Y + dy)), building);
                }
            }

            for (var dy = 1; dy <= spec.Clearance; dy++)
            {
                for (var dx = 0; dx < building.Width; dx++)
                {
                    var under = new WorldPosition(anchor.X + dx, anchor.Y - dy);

                    if (_grid.Contains(under))
                    {
                        _underRoof.Add(_grid.IndexOf(under));
                    }
                }
            }
        }

        // Someone has the skill, the stone is in stock (#166), and the wood
        // is spare: what is left must still light every hearth through the
        // coming winter, the reserve Jobs stocks for.
        private bool CanAfford(ICommunity settlement, BuildingKind kind)
        {
            var spec = BuildingTable.Of(kind);
            var winterFuel = (long)Warmth.CountHearths(settlement.Members, _people, _scratchHearths)
                * Warmth.FuelPerFire * Jobs.WinterDaysAhead(_clock.Now);

            return _skills.BestIn(settlement, spec.Skill) >= spec.MinimumTier
                && HasStoneFor(settlement, spec)
                && settlement.SharedSupplies.Available(ResourceKind.Wood) - winterFuel >= spec.Wood;
        }

        private static bool HasStoneFor(ICommunity settlement, BuildingSpec spec) =>
            settlement.SharedSupplies.Available(ResourceKind.Stone) >= spec.Stone;

        // Storage is built with any wood in stock, the winter's fuel included
        // (its stone it needs like any building, #166): a village that cannot
        // hold its winter's fuel is what a Woodshed is for, and it could
        // otherwise never afford one (#98). It stands in
        // one yard with the others of its kind, the first by what fills it
        // (#167): a Granary by the settlement's barns, a Woodshed by the
        // forest its Woodcutters work, the nearest a woodcutter's search from
        // the centre reaches. A first Woodshed with no forest in reach goes
        // on the barns' ring instead (StorageSite).
        private bool TryPlaceStorage(ICommunity settlement, BuildingKind kind, ReadOnlySpan<bool> known, out WorldPosition anchor)
        {
            var spec = BuildingTable.Of(kind);
            anchor = default;

            if (_skills.BestIn(settlement, spec.Skill) < spec.MinimumTier
                || !HasStoneFor(settlement, spec)
                || settlement.SharedSupplies.Available(ResourceKind.Wood) < spec.Wood)
            {
                return false;
            }

            var from = StorageSite(settlement, kind, known);
            var placed = TryPlace(kind, from, settlement.Position, known, settlement.Id, out anchor);
            _storeBy.Clear();
            return placed;
        }

        // What a store of this kind stands by, into _storeBy, and where its
        // search starts. Stores of a kind keep to one yard: once one stands,
        // the next stands by those already there, searched for from the
        // first one's door. The first stands by the forest cell, or by the
        // barns, searched for from the door of the one nearest the centre;
        // by nothing, from the centre, when there is neither.
        private WorldPosition StorageSite(ICommunity settlement, BuildingKind kind, ReadOnlySpan<bool> known)
        {
            var centre = settlement.Position;
            var spec = BuildingTable.Of(kind);
            var yard = centre;
            _storeBy.Clear();

            for (var i = 0; i < _all.Count; i++)
            {
                var standing = _all[i];

                if (standing.Settlement == settlement.Id && standing.Kind == kind)
                {
                    if (_storeBy.Count == 0)
                    {
                        yard = DoorOf(kind, standing.Anchor);
                    }

                    _storeBy.Add(MiddleOf(spec, standing.Anchor));
                }
            }

            if (_storeBy.Count > 0)
            {
                return yard;
            }

            if (kind == BuildingKind.Woodshed)
            {
                if (_pathfinder.TryFindNearest(
                    centre, Jobs.Mover, _forest, known, _woodland, null, false, int.MaxValue,
                    Jobs.ReachCost(JobKind.Woodcutter, _clock.Now.Season), _scratchRoute, out _))
                {
                    _storeBy.Add(_scratchRoute[_scratchRoute.Count - 1]);
                }

                return _storeBy.Count > 0 ? _storeBy[0] : centre;
            }

            var barnSpec = BuildingTable.Of(BuildingKind.Barn);
            var from = centre;
            var nearest = int.MaxValue;

            for (var i = 0; i < _all.Count; i++)
            {
                var standing = _all[i];

                if (standing.Settlement == settlement.Id && standing.Kind == BuildingKind.Barn)
                {
                    var middle = MiddleOf(barnSpec, standing.Anchor);
                    _storeBy.Add(middle);

                    // From its door: open ground, where its middle is under
                    // its roof.
                    if (Chebyshev(middle, centre) < nearest)
                    {
                        nearest = Chebyshev(middle, centre);
                        from = DoorOf(BuildingKind.Barn, standing.Anchor);
                    }
                }
            }

            return from;
        }

        // Whether Foragers or Farmers are stopping short for want of room
        // (#98): the Food is full (Storage.IsFull) and the food wanted going
        // into a winter, less the Grain in store, is more than it holds; or
        // the Grain is full while food is short. Grain has no want of its
        // own - Farmers fill whatever room there is - so a full Grain store in
        // a fed village wants nothing; counted, it built Granary after
        // Granary for Farmers to fill. A store with room is not short of it,
        // however much is wanted.
        private bool WantsGranary(ICommunity settlement, int living, bool foodShort)
        {
            var stores = settlement.SharedSupplies;
            var grain = stores.Available(ResourceKind.Grain);
            var food = CapacityOf(settlement.Id, ResourceKind.Food);
            var wanted = Jobs.FoodWant(living, SimulationTime.DaysPerSeason) - ((long)grain * PrimitiveTier.MealsPerGrain);

            return (Storage.IsFull(stores.Available(ResourceKind.Food), food) && wanted > food)
                || (foodShort && Storage.IsFull(grain, CapacityOf(settlement.Id, ResourceKind.Grain)));
        }

        // Whether Woodcutters are stopping short for want of room (#98): the
        // Wood is full, and the wood wanted going into a winter is more.
        private bool WantsWoodshed(ICommunity settlement)
        {
            var capacity = CapacityOf(settlement.Id, ResourceKind.Wood);

            if (!Storage.IsFull(settlement.SharedSupplies.Available(ResourceKind.Wood), capacity))
            {
                return false;
            }

            var hearths = Warmth.CountHearths(settlement.Members, _people, _scratchHearths);
            return Jobs.WoodWant(hearths, SimulationTime.DaysPerSeason) > capacity;
        }

        // The next field and the barn it belongs to: the first of the
        // settlement's barns, in building order, with room for another field
        // and ground within FieldRadius to put it on.
        private bool TryPlaceField(ICommunity settlement, ReadOnlySpan<bool> known, out WorldPosition anchor, [NotNullWhen(true)] out Building? barn)
        {
            for (var i = 0; i < _all.Count; i++)
            {
                var standing = _all[i];

                if (standing.Settlement == settlement.Id && standing.Kind == BuildingKind.Barn && FieldsOf(standing) < FieldsPerBarn
                    && TryPlace(BuildingKind.Field, standing.Anchor, settlement.Position, known, settlement.Id, out anchor, standing))
                {
                    barn = standing;
                    return true;
                }
            }

            anchor = default;
            barn = null;
            return false;
        }

        private int FieldsOf(Building barn)
        {
            var fields = 0;

            for (var i = 0; i < _all.Count; i++)
            {
                if (_all[i].Barn == barn.Id)
                {
                    fields++;
                }
            }

            return fields;
        }

        // The clearing a footprint, its lane and its share of the square need,
        // priced as they stand
        // when approved: a standing tree as long as woodcutting takes for the
        // cuts it has left, a bush or a stump an hour, a rock as its quarrying
        // trips. Nobody gathers on approved ground (IsReserved), and a claim
        // already out that comes back changes nothing: clearing pays the
        // timber and stone priced here (ClearCuts, ClearRocks). A bush's
        // fruit is counted when it is grubbed out, since it ripens and is
        // stripped with the seasons.
        private long ClearTicksOf(BuildingKind kind, WorldPosition anchor, List<WorldPosition> lane, out int timber, out int rocks)
        {
            var spec = BuildingTable.Of(kind);
            var ticks = 0L;
            timber = 0;
            rocks = 0;

            for (var dy = 0; dy < spec.Height; dy++)
            {
                for (var dx = 0; dx < spec.Width; dx++)
                {
                    ticks += ClearTicksAt(new WorldPosition(anchor.X + dx, anchor.Y + dy), ref timber, ref rocks);
                }
            }

            for (var i = 0; i < lane.Count; i++)
            {
                ticks += ClearTicksAt(lane[i], ref timber, ref rocks);
            }

            for (var i = 0; i < _square.Count; i++)
            {
                ticks += ClearTicksAt(_square[i], ref timber, ref rocks);
            }

            return ticks;
        }

        // Ground a lane may cross: what a footprint may stand on, rocks
        // included, which clearing quarries away (#172, #155).
        private bool IsLaneGround(TerrainKind kind) => _ground[(int)kind];

        // A rock is quarried away, footprint, lane or square alike (#155).
        private long ClearTicksAt(WorldPosition at, ref int timber, ref int rocks)
        {
            var terrain = _grid[at];

            if (terrain == TerrainKind.Rocks)
            {
                rocks++;
                return TicksPerRock;
            }

            var cuts = terrain == TerrainKind.Forest ? _land.CutsLeft(at) : 0;

            if (cuts > 0)
            {
                timber += cuts;
                return cuts * TicksPerCut;
            }

            return terrain == TerrainKind.Scrub || terrain == TerrainKind.Forest ? ClearTicksPerCell : 0L;
        }

        // The oldest household of the settlement's with no house: the one
        // with the smallest id, since ids are handed out in order (as Warmth
        // ranks hearths). Not the first one met among the members, whose
        // order drifts as people die and arrive (the #156 review).
        private bool TryFirstUnhoused(ICommunity settlement, [NotNullWhen(true)] out Household? household)
        {
            var members = settlement.Members;
            household = null;

            for (var i = 0; i < members.Count; i++)
            {
                var member = members[i];

                if (!_people.IsAlive(member) || !(_households.Of(member) is Household home))
                {
                    continue;
                }

                if (!_homeOf.ContainsKey(home.Id) && (household is null || home.Id < household.Id))
                {
                    household = home;
                }
            }

            return !(household is null);
        }

        private Building RequireAt(WorldPosition at) =>
            At(at) ?? throw new InvalidOperationException("No building stands at " + at + ".");

        // Within CampYardRadius of a camp, each way.
        private static bool WithinYard(WorldPosition at, WorldPosition camp) =>
            Math.Abs(at.X - camp.X) <= CampYardRadius && Math.Abs(at.Y - camp.Y) <= CampYardRadius;
    }
}
