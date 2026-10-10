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
    /// house, a field a barn with room), hands to spare - somebody found
    /// nothing needed at this dawn - and real demand. Demand is a household
    /// without a home, or food in reach falling short of the mouths to feed
    /// with less than a year in store (<see cref="IsFoodShort"/>), which a
    /// barn and then its fields answer. Food comes first: a hungry village
    /// builds a barn before it houses everyone, once it has the one house a
    /// barn needs. One building goes up at a time, so a settlement never
    /// spreads its hands across half-finished work.
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
    /// <see cref="FieldRadius"/> of its barn, each among the footprints the
    /// same bounded search a work site is found by reaches, so it is
    /// reachable and known. A footprint of open plains is taken if there is
    /// one; otherwise one of plains, scrub or forest, which Builders clear
    /// before building starts, along with any scrub or trees on its lane -
    /// <see cref="TicksPerCut"/> for each cut a standing tree has left, what
    /// woodcutting takes for one, and the wood those cuts would have given
    /// goes into stock; <see cref="ClearTicksPerCell"/> for a bush or a
    /// stump. The lane is laid as road when the clearing is done. Approved
    /// ground and lanes are reserved: nobody gathers there until they are
    /// cleared (<see cref="IsReserved(WorldPosition)"/>), so they are cleared
    /// as they were priced. Nothing regrows on cleared ground. Buildings
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
    /// **Homes.** A finished house goes to the first household in the
    /// settlement without one, in member order, at the next dawn. Camp space
    /// still lets households form without one; #69 makes houses the limit.
    ///
    /// **Settling.** <see cref="CanSettle"/> is the band's half of the
    /// bargain: early spring, bushes in reach whose year's fruit would cover
    /// half as much again as a season's eating, and ground for a barn - so a
    /// new village can feed itself to its first harvest.
    ///
    /// The numbers are placeholders, set by the harness.
    /// </remarks>
    public sealed partial class Buildings : IDomainEventSubscriber
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

        private static readonly int DefinedTerrainKinds = EnumGuard.BuildMask(typeof(TerrainKind)).Length;

        private readonly DomainEventBus _bus;
        private readonly SimulationClock _clock;
        private readonly PersonStore _people;
        private readonly Households _households;
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

        private readonly bool[] _scrub;
        private readonly bool[] _ground;
        private readonly List<WorldPosition> _scratchRoute = new List<WorldPosition>();
        private readonly List<EntityId> _scratchHearths = new List<EntityId>();

        public Buildings(
            DomainEventBus bus,
            PersonStore people,
            Households households,
            Pathfinder pathfinder,
            KnownMaps knownMaps,
            LandCover land,
            ISkillSource skills)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _people = people ?? throw new ArgumentNullException(nameof(people));
            _households = households ?? throw new ArgumentNullException(nameof(households));
            _pathfinder = pathfinder ?? throw new ArgumentNullException(nameof(pathfinder));
            _knownMaps = knownMaps ?? throw new ArgumentNullException(nameof(knownMaps));
            _land = land ?? throw new ArgumentNullException(nameof(land));
            _skills = skills ?? throw new ArgumentNullException(nameof(skills));
            _clock = bus.Clock;
            _grid = pathfinder.Grid;
            _allView = _all.AsReadOnly();

            _scrub = new bool[DefinedTerrainKinds];
            _scrub[(int)TerrainKind.Scrub] = true;
            _ground = new bool[DefinedTerrainKinds];
            _ground[(int)TerrainKind.Plains] = true;
            _ground[(int)TerrainKind.Scrub] = true;
            _ground[(int)TerrainKind.Forest] = true;
            _anyKind = new bool[DefinedTerrainKinds];

            for (var kind = (int)TerrainKind.None + 1; kind < DefinedTerrainKinds; kind++)
            {
                _anyKind[kind] = true;
            }

            _scorer = new SiteScorer(this);
            _laneGate = new LaneGate(this);
            _networkTarget = new NetworkTarget(_grid);
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
            _pathfinder.CountReachable(at, Jobs.Mover, _scrub, _knownMaps.For(mapHolder), Jobs.MaxSiteRadius) * BushYearlyFood;

        /// <summary>
        /// Whether a band of this many could settle here: a whole year's
        /// picking of the bushes in reach (<see cref="ForageInReach"/>) would
        /// cover half as much again as one season's eating, nothing stands in
        /// or roofs over the camp's yard (the #153 review), and there is
        /// ground for a barn. That is a third of what the words "a season of
        /// bushes" would suggest, since a bush gives a year's fruit over
        /// three seasons; it is the measure the #100 harness runs were tuned
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
        public bool CanSettle(WorldPosition at, EntityId mapHolder, int living)
        {
            // Inputs first, in every season: checked after the season, they
            // were refused in early spring and silently accepted the rest of
            // the year.
            var need = YearlyNeed(living);
            _grid.IndexOf(at);
            _knownMaps.For(mapHolder);
            var now = _clock.Now;

            return now.Season == Season.Spring
                && now.DayOfSeason < SimulationTime.DaysPerSeason / 2
                && ForageInReach(at, mapHolder) * SettleDenominator * SimulationTime.SeasonsPerYear >= need * SettleNumerator
                && YardIsClear(at)
                && TryPlace(BuildingKind.Barn, at, at, mapHolder, EntityId.None, out _);
        }

        /// <summary>
        /// Whether a settlement is outgrowing its food: bushes in reach and
        /// fields standing give less than five quarters of a year's eating,
        /// and the stores do not already hold a year of it. What a barn and
        /// its fields are built for; a village sitting on a year's Grain
        /// builds no more fields however few bushes it has.
        /// </summary>
        public bool IsFoodShort(ICommunity settlement, int living)
        {
            if (settlement is null)
            {
                throw new ArgumentNullException(nameof(settlement));
            }

            var need = YearlyNeed(living);

            if (Hunger.MealsInStore(settlement.SharedSupplies) >= need)
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
        /// Farmer by. Nothing in winter.
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

                    if (kind == TerrainKind.Scrub || kind == TerrainKind.Forest)
                    {
                        _land.Clear(at);
                    }
                }
            }

            building.Cleared = true;
            LayLane(building);

            // The Wood of the cuts the clearing was priced at: a claim out at
            // approval and given back since leaves a tree with a cut more than
            // anyone was charged for, and that cut is not paid.
            if (building.ClearCuts > 0)
            {
                stores.Gather(ResourceKind.Wood, building.ClearCuts * WoodPerCut);
            }
        }

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
        // one, in the order their members stand in the settlement.
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

                house.Occupant = household;
                _homeOf.Add(household, house);
            }
        }

        private void Plan(ICommunity settlement, int living)
        {
            Building? barnWithRoom = null;
            var hasHouse = false;

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

                if (standing.Kind == BuildingKind.Barn && barnWithRoom is null && FieldsOf(standing) < FieldsPerBarn)
                {
                    barnWithRoom = standing;
                }
            }

            BuildingKind kind;
            WorldPosition from;
            Building? barn = null;

            if (hasHouse && IsFoodShort(settlement, living))
            {
                if (barnWithRoom is object)
                {
                    kind = BuildingKind.Field;
                    barn = barnWithRoom;
                    from = barnWithRoom.Anchor;
                }
                else
                {
                    kind = BuildingKind.Barn;
                    from = settlement.Position;
                }
            }
            else if (TryFirstUnhoused(settlement, out _))
            {
                kind = BuildingKind.House;
                from = settlement.Position;
            }
            else
            {
                return;
            }

            var spec = BuildingTable.Of(kind);
            var stores = settlement.SharedSupplies;

            // The wood has to be spare: what is left must still light every
            // hearth through the coming winter, the reserve Jobs stocks for.
            var winterFuel = (long)Warmth.CountHearths(settlement.Members, _people, _scratchHearths)
                * Warmth.FuelPerFire * Jobs.WinterDaysAhead(_clock.Now);

            if (_skills.BestIn(settlement, spec.Skill) < spec.MinimumTier
                || stores.Available(ResourceKind.Wood) - winterFuel < spec.Wood
                || !TryPlace(kind, from, settlement.Position, settlement.Id, settlement.Id, out var anchor, barn))
            {
                return;
            }

            stores.Embody(ResourceKind.Wood, spec.Wood);
            var clearTicks = ClearTicksOf(kind, anchor, _lane, out var clearCuts);
            var building = new Building(
                _clock.Ids.Next(EntityKind.Building), kind, settlement.Id, barn?.Id ?? EntityId.None,
                anchor, _lane.ToArray(), clearTicks, clearCuts, clearTicks + spec.BuildTicks);

            _all.Add(building);

            for (var i = 0; i < building.Lane.Count; i++)
            {
                _laneOf.Add(_grid.IndexOf(building.Lane[i]), building);
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

        // The clearing a footprint and its lane need, priced as they stand
        // when approved: a standing tree as long as woodcutting takes for the
        // cuts it has left, a bush or a stump an hour. Nobody gathers on
        // approved ground (IsReserved), and a claim already out that comes
        // back changes nothing: clearing pays the timber priced here
        // (ClearCuts).
        private long ClearTicksOf(BuildingKind kind, WorldPosition anchor, List<WorldPosition> lane, out int timber)
        {
            var spec = BuildingTable.Of(kind);
            var ticks = 0L;
            timber = 0;

            for (var dy = 0; dy < spec.Height; dy++)
            {
                for (var dx = 0; dx < spec.Width; dx++)
                {
                    ticks += ClearTicksAt(new WorldPosition(anchor.X + dx, anchor.Y + dy), ref timber);
                }
            }

            for (var i = 0; i < lane.Count; i++)
            {
                ticks += ClearTicksAt(lane[i], ref timber);
            }

            return ticks;
        }

        private long ClearTicksAt(WorldPosition at, ref int timber)
        {
            var terrain = _grid[at];
            var cuts = terrain == TerrainKind.Forest ? _land.CutsLeft(at) : 0;

            if (cuts > 0)
            {
                timber += cuts;
                return cuts * TicksPerCut;
            }

            return terrain == TerrainKind.Scrub || terrain == TerrainKind.Forest ? ClearTicksPerCell : 0L;
        }

        private bool TryFirstUnhoused(ICommunity settlement, out EntityId household)
        {
            var members = settlement.Members;

            for (var i = 0; i < members.Count; i++)
            {
                var member = members[i];

                if (!_people.IsAlive(member) || !(_households.Of(member) is Household home))
                {
                    continue;
                }

                if (!_homeOf.ContainsKey(home.Id))
                {
                    household = home.Id;
                    return true;
                }
            }

            household = EntityId.None;
            return false;
        }

        private Building RequireAt(WorldPosition at) =>
            At(at) ?? throw new InvalidOperationException("No building stands at " + at + ".");

        // Within CampYardRadius of a camp, each way.
        private static bool WithinYard(WorldPosition at, WorldPosition camp) =>
            Math.Abs(at.X - camp.X) <= CampYardRadius && Math.Abs(at.Y - camp.Y) <= CampYardRadius;

        // Whether a camp here would have a yard nothing stands in or roofs over.
        private bool YardIsClear(WorldPosition camp)
        {
            for (var y = camp.Y - CampYardRadius; y <= camp.Y + CampYardRadius; y++)
            {
                for (var x = camp.X - CampYardRadius; x <= camp.X + CampYardRadius; x++)
                {
                    var at = new WorldPosition(x, y);

                    if (_grid.Contains(at) && (_onCell.ContainsKey(_grid.IndexOf(at)) || _underRoof.Contains(_grid.IndexOf(at))))
                    {
                        return false;
                    }
                }
            }

            return true;
        }
    }
}
