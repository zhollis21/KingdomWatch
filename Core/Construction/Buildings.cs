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
    /// **Placement is a stand-in for #23.** A house or barn goes on the
    /// nearest footprint to the settlement, a field on the nearest within
    /// <see cref="FieldRadius"/> of its barn, each found by the same bounded
    /// search a work site is, so it is reachable and known. A footprint of
    /// open plains is taken if there is one; otherwise one of plains, scrub
    /// or forest, not under another building, which Builders clear before
    /// building starts - <see cref="FellTreeTicks"/> for a standing tree,
    /// what woodcutting takes to bring one down, and the wood it gives goes
    /// into stock; <see cref="ClearTicksPerCell"/> for a bush or a stump.
    /// Nothing regrows on cleared ground. Buildings block nobody's path
    /// until #23 lays out roads.
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
    /// bargain: early spring, a season of bushes in reach, and ground for a
    /// barn - so a new village can feed itself to its first harvest.
    ///
    /// The numbers are placeholders, set by the harness.
    /// </remarks>
    public sealed class Buildings : IDomainEventSubscriber
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

        /// <summary>Worker-ticks to grub out a bush or clear a stump for good.</summary>
        public const long ClearTicksPerCell = Hour;

        /// <summary>
        /// Worker-ticks to fell a standing tree for good: what woodcutting
        /// takes to bring one down - every cut of <see cref="LandCover.TreeCuts"/>
        /// at <see cref="PrimitiveTier.GatherWood"/>'s four hours.
        /// </summary>
        public const long FellTreeTicks = LandCover.DefaultTreeCuts * 4L * Hour;

        /// <summary>Wood a standing tree gives when felled: what its cuts would have.</summary>
        public const int WoodPerFelledTree = LandCover.DefaultTreeCuts * 2;

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

        private readonly bool[] _scrub;
        private readonly bool[] _ground;
        private readonly FootprintFilter _footprint;
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
            _footprint = new FootprintFilter(this);
        }

        /// <summary>Every building, oldest first. The order to iterate in.</summary>
        public IReadOnlyList<Building> All => _allView;

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
        public static long YearlyNeed(int living) => (long)Hunger.DailyRation * living * SimulationTime.DaysPerYear;

        /// <summary>The building whose footprint covers this cell, or null.</summary>
        public Building? At(WorldPosition at) =>
            _onCell.TryGetValue(_grid.IndexOf(at), out var building) ? building : null;

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
        /// Whether a band of this many could settle here: the bushes in reach
        /// would feed it half as much again as one season's eating, and there
        /// is ground for a barn. A season, not a year, because that is what
        /// they have to carry: a barn and a field are days of work, and the
        /// first crop is in some fifteen days after - no camp on the
        /// placeholder map has bushes for a year (#100's harness runs).
        /// And only in the first half of spring, so the village has a whole
        /// growing season for its fields before its first winter: one that
        /// settled late in spring got a single crop in and starved.
        /// </summary>
        public bool CanSettle(WorldPosition at, EntityId mapHolder, int living)
        {
            var now = _clock.Now;

            return now.Season == Season.Spring
                && now.DayOfSeason < SimulationTime.DaysPerSeason / 2
                && ForageInReach(at, mapHolder) * SettleDenominator * SimulationTime.SeasonsPerYear >= YearlyNeed(living) * SettleNumerator
                && TryPlace(BuildingKind.Barn, at, at, mapHolder, out _);
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
        /// nothing.
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

            // A share of a harvest day brings home its share of the day's
            // Grain. Shares are half a day, so this divides exactly.
            if (field.Stage == FieldStage.Harvesting)
            {
                stores.Gather(ResourceKind.Grain, checked((int)(ticks * GrainPerHarvestDay / FieldDayTicks)));
            }

            if (field.WorkedToday < FieldDayTicks)
            {
                return;
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
            var felled = 0;

            for (var dy = 0; dy < building.Height; dy++)
            {
                for (var dx = 0; dx < building.Width; dx++)
                {
                    var at = new WorldPosition(building.Anchor.X + dx, building.Anchor.Y + dy);
                    var kind = _grid[at];

                    if ((kind == TerrainKind.Scrub || kind == TerrainKind.Forest) && _land.Clear(at))
                    {
                        felled++;
                    }
                }
            }

            building.Cleared = true;

            if (felled > 0)
            {
                stores.Gather(ResourceKind.Wood, felled * WoodPerFelledTree);
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
                || !TryPlace(kind, from, settlement.Position, settlement.Id, out var anchor, barn))
            {
                return;
            }

            stores.Embody(ResourceKind.Wood, spec.Wood);
            var clearTicks = ClearTicksOf(kind, anchor);
            var building = new Building(
                _clock.Ids.Next(EntityKind.Building), kind, settlement.Id, barn?.Id ?? EntityId.None,
                anchor, clearTicks, clearTicks + spec.BuildTicks);

            _all.Add(building);

            for (var dy = 0; dy < building.Height; dy++)
            {
                for (var dx = 0; dx < building.Width; dx++)
                {
                    _onCell.Add(_grid.IndexOf(new WorldPosition(anchor.X + dx, anchor.Y + dy)), building);
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

        // The clearing a footprint needs, priced as it stands when approved:
        // a standing tree as long as woodcutting takes to bring one down, a
        // bush or a stump an hour.
        private long ClearTicksOf(BuildingKind kind, WorldPosition anchor)
        {
            var spec = BuildingTable.Of(kind);
            var ticks = 0L;

            for (var dy = 0; dy < spec.Height; dy++)
            {
                for (var dx = 0; dx < spec.Width; dx++)
                {
                    var at = new WorldPosition(anchor.X + dx, anchor.Y + dy);
                    var terrain = _grid[at];

                    if (terrain == TerrainKind.Forest && _land.StageOf(at) == TreeStage.Standing)
                    {
                        ticks += FellTreeTicks;
                    }
                    else if (terrain == TerrainKind.Scrub || terrain == TerrainKind.Forest)
                    {
                        ticks += ClearTicksPerCell;
                    }
                }
            }

            return ticks;
        }

        // The nearest footprint by walking: the same bounded search a work
        // site is found by, landing on an anchor whose whole rectangle is
        // free ground. A house or barn looks around the settlement, a field
        // around its barn. Open plains first - the bushes and trees a village
        // lives on are cleared only when no plains will do.
        private bool TryPlace(
            BuildingKind kind, WorldPosition from, WorldPosition settlement, EntityId mapHolder, out WorldPosition anchor, Building? barn = null)
        {
            _footprint.Spec = BuildingTable.Of(kind);
            _footprint.Keep = settlement;
            var radius = barn is null ? Jobs.MaxSiteRadius : FieldRadius;
            var known = _knownMaps.For(mapHolder);

            _footprint.PlainsOnly = true;
            var found = _pathfinder.TryFindNearest(from, Jobs.Mover, _ground, known, _footprint, radius, _scratchRoute, out _);

            if (!found)
            {
                _footprint.PlainsOnly = false;
                found = _pathfinder.TryFindNearest(from, Jobs.Mover, _ground, known, _footprint, radius, _scratchRoute, out _);
            }

            anchor = found ? _scratchRoute[_scratchRoute.Count - 1] : default;
            return found;
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

        // A footprint anchored at a cell: every cell of it on the map, plains,
        // scrub or forest, under no other building, and clear of the cell the
        // settlement keeps for its camp.
        private sealed class FootprintFilter : ISiteFilter
        {
            private readonly Buildings _owner;

            public FootprintFilter(Buildings owner) => _owner = owner;

            public BuildingSpec Spec { get; set; }

            public WorldPosition Keep { get; set; }

            // Whether every cell must already be plains, rather than plains,
            // scrub or forest to be cleared.
            public bool PlainsOnly { get; set; }

            public bool Accepts(int cell)
            {
                var grid = _owner._grid;
                var anchor = grid.PositionAt(cell);

                for (var dy = 0; dy < Spec.Height; dy++)
                {
                    for (var dx = 0; dx < Spec.Width; dx++)
                    {
                        var at = new WorldPosition(anchor.X + dx, anchor.Y + dy);

                        if (!grid.Contains(at) || at == Keep)
                        {
                            return false;
                        }

                        var index = grid.IndexOf(at);

                        var kind = grid.KindAt(index);

                        if (!_owner._ground[(int)kind] || (PlainsOnly && kind != TerrainKind.Plains) || _owner._onCell.ContainsKey(index))
                        {
                            return false;
                        }
                    }
                }

                return true;
            }
        }
    }
}
