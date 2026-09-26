using System;
using System.Collections.Generic;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Events;
using KingdomWatch.Core.History;
using KingdomWatch.Core.Knowledge;
using KingdomWatch.Core.Lifecycle;
using KingdomWatch.Core.Needs;
using KingdomWatch.Core.Nomadic;
using KingdomWatch.Core.Relationships;
using KingdomWatch.Core.Rng;
using KingdomWatch.Core.Settlements;
using KingdomWatch.Core.Traversal;
using KingdomWatch.Core.Validation;
using KingdomWatch.Core.Work;
using KingdomWatch.Core.WorldGen;

namespace KingdomWatch.Core
{
    /// <summary>
    /// Every system, wired once: the composition root the harness runs and
    /// the Unity build (#72) will run, so both reach the same state from the
    /// same seed by building it the same way.
    /// </summary>
    /// <remarks>
    /// The same graph the test fixtures assemble (<c>HouseholdWorld</c>,
    /// <c>DemographicWorld</c>, <c>WorkWorld</c>), made from parts rather
    /// than stacked, because nothing here needs a fixture's shortcuts.
    ///
    /// One race: a second needs a race model first (#102), so both bands are
    /// built from the one <see cref="DemographicSettings"/>.
    /// </remarks>
    public sealed class World
    {
        // Placeholders, like every other tuning number: memories are not yet
        // written by anything in M1, so these only have to be valid.
        private static readonly MemorySettings MemoryDefaults = new MemorySettings(
            4, 5L * SimulationTime.TicksPerYear, 50L * SimulationTime.TicksPerYear, 4);

        // Roughly an event a day for a couple of hundred people over two
        // centuries; the journal grows past it by doubling if a run is busier.
        private const int JournalCapacity = 1 << 16;

        private readonly List<PendingBooking> _scratch = new List<PendingBooking>();
        private readonly List<PendingBooking> _hashBookings = new List<PendingBooking>();
        private readonly List<ICommunity> _hashTracked = new List<ICommunity>();
        private readonly List<MobileGroup> _hashBands = new List<MobileGroup>();
        private readonly WorldHash _hash = new WorldHash();

        public World(ulong seed, TerrainGrid grid, DemographicSettings settings)
        {
            Grid = grid ?? throw new ArgumentNullException(nameof(grid));
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));

            Ids = new IdAllocator();
            Clock = new SimulationClock(Ids);
            Bus = new DomainEventBus(Clock);
            Journal = new EventJournal(JournalCapacity);
            Bus.Subscribe(Journal);
            Rng = new DeterministicRng(seed);

            People = new PersonStore();
            Genealogy = new Genealogy();
            Partnerships = new Partnerships();
            Memories = new Memories(MemoryDefaults);
            Households = new Households(Bus, People, new CampSpace());
            Family = new FamilyFormation(Bus, People, Genealogy, Partnerships, Households, FamilyFormationSettings.Default);

            Pathfinder = new Pathfinder(Grid, TerrainRules.Default);
            KnownMaps = new KnownMaps(Grid);
            Jobs = new Jobs(Clock, People, Pathfinder, KnownMaps);
            Deaths = new Deaths(Bus, People, Genealogy, Partnerships, Memories, Households, Jobs);

            Aging = new Aging(Bus, People, settings);
            Mortality = new Mortality(Bus, People, Deaths, Rng, settings);
            Fertility = new Fertility(Bus, People, Genealogy, Partnerships, Households, Rng, settings);
            Hunger = new Hunger(Bus, People);
            Warmth = new Warmth(Clock, People);
            Matchmaking = new Matchmaking(Bus, People, Family, Partnerships, Rng);
            Generator = new BandGenerator(Bus, People, Genealogy, Family, Households, settings, Rng);
            Founding = new Founding(Bus, Deaths, Fertility, Hunger, Warmth, Jobs, Matchmaking, KnownMaps);
            Nomads = new NomadicBands(Bus, People, Pathfinder, Founding, Rng, KnownMaps);

            Bus.Subscribe(Aging);
            Bus.Subscribe(Mortality);
            Bus.Subscribe(Fertility);

            Router = new ScheduledEventRouter();
            Router.Register(ScheduledEventKind.AgeStageDue, Aging);
            Router.Register(ScheduledEventKind.MortalityCheck, Mortality);
            Router.Register(ScheduledEventKind.StarvationCritical, Mortality);
            Router.Register(ScheduledEventKind.ExposureCritical, Mortality);
            Router.Register(ScheduledEventKind.BirthCheck, Fertility);
            Router.Register(ScheduledEventKind.BirthDue, Fertility);
            Router.Register(ScheduledEventKind.MealDue, Hunger);
            Router.Register(ScheduledEventKind.CourtshipDue, Matchmaking);
            Router.Register(ScheduledEventKind.WarmthDue, Warmth);
            Router.Register(ScheduledEventKind.WorkDayDue, Jobs);
            Router.Register(ScheduledEventKind.TaskCompleted, Jobs);
            Router.Register(ScheduledEventKind.CouncilDue, Nomads);
            Router.Register(ScheduledEventKind.BandArrival, Nomads);
        }

        /// <summary>
        /// The M1 world (section 19): a <see cref="PlaceholderMap"/> with one
        /// band on each side of its river. Both are the one race there is.
        /// </summary>
        public static World TwoBands(ulong seed, int width, int height, int westSize, int eastSize)
        {
            // The map draws from its own keys, so building it from the world's
            // rng would give the same terrain; a separate rng only keeps the
            // map a function of the seed that does not need a world to exist.
            var world = new World(seed, PlaceholderMap.Generate(width, height, new DeterministicRng(seed)), DemographicSettings.Default);
            var row = height / 2;
            var river = world.RiverColumn(row);

            world.AddBand(westSize, world.NearestStandable(new WorldPosition(river / 2, row), 0, river - 1));
            world.AddBand(eastSize, world.NearestStandable(new WorldPosition((river + width) / 2, row), river + 1, width - 1));
            return world;
        }

        public ulong Seed => Rng.WorldSeed;

        public DemographicSettings Settings { get; }

        public TerrainGrid Grid { get; }

        public IdAllocator Ids { get; }

        public SimulationClock Clock { get; }

        public DomainEventBus Bus { get; }

        public EventJournal Journal { get; }

        public DeterministicRng Rng { get; }

        public PersonStore People { get; }

        public Genealogy Genealogy { get; }

        public Partnerships Partnerships { get; }

        public Memories Memories { get; }

        public Households Households { get; }

        public FamilyFormation Family { get; }

        public Pathfinder Pathfinder { get; }

        public KnownMaps KnownMaps { get; }

        public Jobs Jobs { get; }

        public Deaths Deaths { get; }

        public Aging Aging { get; }

        public Mortality Mortality { get; }

        public Fertility Fertility { get; }

        public Hunger Hunger { get; }

        public Warmth Warmth { get; }

        public Matchmaking Matchmaking { get; }

        public BandGenerator Generator { get; }

        public Founding Founding { get; }

        public NomadicBands Nomads { get; }

        public ScheduledEventRouter Router { get; }

        public SimulationTime Now => Clock.Now;

        /// <summary>
        /// A generated band standing at a position, put on every system that
        /// tracks bands, wandering from its first camp. Refuses a cell off
        /// the map or one nobody can stand on before anything is made:
        /// <see cref="NomadicBands.Track"/> refuses it too, but only after
        /// the band's people exist and every other system tracks it.
        /// </summary>
        public MobileGroup AddBand(int size, WorldPosition at)
        {
            if (!Grid.Contains(at) || !Pathfinder.IsPassable(at, Jobs.Mover))
            {
                throw new ArgumentOutOfRangeException(nameof(at), at, "A band has to be placed where its feet can stand.");
            }

            var band = Generator.Generate(size, at);

            for (var i = 0; i < band.Members.Count; i++)
            {
                People.SetPosition(band.Members[i], at);
            }

            Deaths.Track(band);
            Fertility.Track(band);
            Hunger.Track(band);
            Warmth.Track(band);
            Jobs.Track(band);
            Matchmaking.Track(band);
            Nomads.Track(band);
            return band;
        }

        public void AdvanceTo(SimulationTime time) => Clock.AdvanceTo(time, Router);

        public void Advance(long ticks) => AdvanceTo(Now.Plus(ticks));

        /// <summary>
        /// The canonical hash of every section <see cref="WorldHash"/> has:
        /// terrain, next ids, people, households, settlements, wandering
        /// bands, known maps, partnerships, genealogy, memories, work in hand,
        /// band councils, famine, the recorded history, each system's tracked
        /// communities (the #108 review), every stream's
        /// bookings (the #97 review note on #17) and the pending queue.
        /// </summary>
        /// <remarks>
        /// Here rather than in the harness so the harness and the Unity build
        /// (#72, #90) compare the same number. Every system in this class with
        /// durable state has a section (#104); one gained later needs one here
        /// too (AGENTS.md).
        /// </remarks>
        public ulong Hash()
        {
            CopyBookingsTo(_hashBookings);
            Nomads.CopyTrackedTo(_hashTracked);
            _hashBands.Clear();

            for (var i = 0; i < _hashTracked.Count; i++)
            {
                _hashBands.Add((MobileGroup)_hashTracked[i]);
            }

            return _hash
                .Reset()
                .AddTerrain(Grid)
                .AddIds(Ids)
                .AddPeople(People)
                .AddHouseholds(Households, People)
                .AddSettlements(Founding, People)
                .AddBands(_hashBands, People)
                .AddKnownMaps(KnownMaps)
                .AddPartnerships(Partnerships)
                .AddGenealogy(Genealogy)
                .AddMemories(Memories)
                .AddWork(Jobs, People)
                .AddCouncils(Nomads)
                .AddFamine(Hunger)
                .AddJournal(Journal)
                .AddTracking(Deaths, Fertility, Warmth, Matchmaking)
                .AddBookings(_hashBookings)
                .AddPending(Clock)
                .Value;
        }

        /// <summary>
        /// Every pending event every stream is holding, in a fixed stream
        /// order - the set the validator checks and the hash folds in.
        /// </summary>
        public void CopyBookingsTo(List<PendingBooking> into)
        {
            if (into is null)
            {
                throw new ArgumentNullException(nameof(into));
            }

            into.Clear();
            Gather(into, Hunger.CopyBookingsTo);
            Gather(into, Warmth.CopyBookingsTo);
            Gather(into, Jobs.CopyBookingsTo);
            Gather(into, Nomads.CopyBookingsTo);
            Gather(into, Fertility.CopyBookingsTo);
            Gather(into, Matchmaking.CopyBookingsTo);
        }

        /// <summary>
        /// Where people physically are: the bands still wandering, then every
        /// settlement founded so far, in founding order.
        /// </summary>
        public void CopyCommunitiesTo(List<ICommunity> into)
        {
            if (into is null)
            {
                throw new ArgumentNullException(nameof(into));
            }

            Nomads.CopyTrackedTo(into);

            for (var i = 0; i < Founding.All.Count; i++)
            {
                into.Add(Founding.All[i]);
            }
        }

        // CopyBookingsTo fills a list rather than appending, since each
        // system owns its own answer; gathering several goes through one
        // scratch buffer.
        private void Gather(List<PendingBooking> into, Action<List<PendingBooking>> copy)
        {
            copy(_scratch);
            into.AddRange(_scratch);
        }

        private int RiverColumn(int row)
        {
            for (var x = 0; x < Grid.Width; x++)
            {
                if (Grid[new WorldPosition(x, row)] == TerrainKind.SmallRiver)
                {
                    return x;
                }
            }

            throw new InvalidOperationException("Row " + row + " has no river; this is not a placeholder map.");
        }

        // The standable cell nearest a target along its own row, between two
        // columns. Only along the row: the river drifts from row to row, so
        // the columns that are one side of it here may be the other side a
        // row away. A band has to be tracked where its feet can stand.
        private WorldPosition NearestStandable(WorldPosition target, int minX, int maxX)
        {
            for (var offset = 0; offset <= maxX - minX; offset++)
            {
                for (var sign = -1; sign <= 1; sign += 2)
                {
                    var at = new WorldPosition(target.X + sign * offset, target.Y);

                    if (at.X >= minX && at.X <= maxX && Pathfinder.IsPassable(at, Jobs.Mover))
                    {
                        return at;
                    }
                }
            }

            throw new InvalidOperationException(
                "No standable cell in row " + target.Y + " between columns " + minX + " and " + maxX + ".");
        }
    }
}
