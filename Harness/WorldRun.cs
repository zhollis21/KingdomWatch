using System;
using System.Collections.Generic;
using KingdomWatch.Core;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Events;
using KingdomWatch.Core.Settlements;
using KingdomWatch.Core.Traversal;
using KingdomWatch.Core.Validation;

namespace KingdomWatch.Harness
{
    /// <summary>
    /// One seed of the M1 world (section 19), run a year at a time and
    /// validated after each year. Records what the chronicle prints and
    /// the tests assert: each homeland's population by year, births and
    /// deaths, when the reachable milestones first fired, and the first
    /// validation break if there is one.
    /// </summary>
    /// <remarks>
    /// Homelands, not bands: a band that settles becomes a settlement with a
    /// new id, so what is counted is everyone living in communities on each
    /// side of the river. The river is a wall until bridges (section 12), so
    /// nobody changes side.
    ///
    /// Only milestone 1 of the economy ladder's section 9 can fire before
    /// buildings exist (#100). First settlement stands in for the rest in M1.
    /// </remarks>
    public sealed class WorldRun
    {
        // A map big enough for two bands to wander without meeting the edge
        // every week, small enough that a 200-year run takes seconds.
        public const int Width = 48;
        public const int Height = 48;

        // Section 15's largest two starting bands.
        public const int WestSize = 60;
        public const int EastSize = 45;

        private readonly List<ICommunity> _communities = new List<ICommunity>();
        private readonly List<ICommunity> _tracked = new List<ICommunity>();
        private readonly List<PendingBooking> _bookings = new List<PendingBooking>();
        private readonly List<MobileGroup> _bands = new List<MobileGroup>();
        private readonly List<YearSummary> _years = new List<YearSummary>();
        private readonly WorldValidator _validator = new WorldValidator();
        private int _journalRead;

        public WorldRun(ulong seed)
            : this(World.TwoBands(seed, Width, Height, WestSize, EastSize))
        {
        }

        public WorldRun(World world)
        {
            World = world ?? throw new ArgumentNullException(nameof(world));
            FoundingWest = CountSide(true);
            FoundingEast = CountSide(false);
            ReadJournal(out _);
        }

        public World World { get; }

        public int FoundingWest { get; }

        public int FoundingEast { get; }

        public IReadOnlyList<YearSummary> Years => _years;

        public SimulationTime? FirstCamp { get; private set; }

        public SimulationTime? FirstSettlement { get; private set; }

        /// <summary>The validator's report for the first year that broke, or null.</summary>
        public string? Failure { get; private set; }

        public bool IsClean => Failure is null;

        /// <summary>
        /// Advances year by year, validating after each, and stops at the first
        /// year that breaks an invariant: a world past its first break is not
        /// worth reading.
        /// </summary>
        public WorldRun RunYears(long years)
        {
            if (years < 0L)
            {
                throw new ArgumentOutOfRangeException(nameof(years), years, "Cannot run a negative number of years.");
            }

            // Refused before the first year rather than discovered part-way,
            // as SchedulerSoak.RunDays does: a run that throws in its fortieth
            // year has already changed the world it was asked to run.
            if (years > (long.MaxValue - World.Now.Ticks) / SimulationTime.TicksPerYear)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(years), years, "That many years from " + World.Now + " would run past the end of simulation time.");
            }

            for (var i = 0L; i < years && IsClean; i++)
            {
                World.Advance(SimulationTime.TicksPerYear);
                ReadJournal(out var tally);
                _years.Add(new YearSummary(World.Now.YearNumber, CountSide(true), CountSide(false), tally));
                Validate();
            }

            return this;
        }

        /// <summary>
        /// The canonical hash of every section <see cref="WorldHash"/> has:
        /// people, households, settlements, wandering bands, known maps, every
        /// stream's bookings (the #97 review note on #17) and the pending queue.
        /// </summary>
        /// <remarks>
        /// Not yet everything the world holds. Partnerships, genealogy,
        /// memories, job task routes, band councils and famine flags have no
        /// section yet (#104).
        /// </remarks>
        public ulong Hash()
        {
            World.CopyBookingsTo(_bookings);
            World.Nomads.CopyTrackedTo(_tracked);
            _bands.Clear();

            for (var i = 0; i < _tracked.Count; i++)
            {
                _bands.Add((MobileGroup)_tracked[i]);
            }

            return new WorldHash()
                .AddTerrain(World.Grid)
                .AddIds(World.Ids)
                .AddPeople(World.People)
                .AddHouseholds(World.Households, World.People)
                .AddSettlements(World.Founding, World.People)
                .AddBands(_bands, World.People)
                .AddKnownMaps(World.KnownMaps)
                .AddBookings(_bookings)
                .AddPending(World.Clock)
                .Value;
        }

        private void Validate()
        {
            var world = World;
            _communities.Clear();
            world.CopyCommunitiesTo(_communities);

            _validator
                .Reset()
                .CheckPeople(world.People, world.Clock, world.Settings)
                .CheckHouseholds(world.Households, world.People, world.Clock)
                .CheckGenealogy(world.Genealogy, world.People, world.Clock)
                .CheckSchedule(world.Clock, world.People, _communities, world.Households)
                .CheckJobs(world.Jobs, world.People, world.Clock)
                .CheckCommunities(_communities, world.People, world.Clock);

            // Each system's own tracked set: a community one system still has
            // events booked for and another has let go has no other symptom.
            CheckTracked(world.Hunger.CopyTrackedTo);
            CheckTracked(world.Warmth.CopyTrackedTo);
            CheckTracked(world.Jobs.CopyTrackedTo);
            CheckTracked(world.Fertility.CopyTrackedTo);
            CheckTracked(world.Matchmaking.CopyTrackedTo);

            world.CopyBookingsTo(_bookings);
            _validator.CheckBookings(_bookings, world.Clock);

            for (var i = 0; i < _communities.Count; i++)
            {
                if (_communities[i] is MobileGroup band)
                {
                    _validator.CheckSupplies(band.SharedSupplies, band.Id, world.Clock);
                }
                else if (_communities[i] is Settlement settlement)
                {
                    _validator.CheckSupplies(settlement.SharedSupplies, settlement.Id, world.Clock);
                }
            }

            if (!_validator.IsClean)
            {
                Failure = "year " + world.Now.YearNumber + ", " + _validator.Report(world.Seed);
            }
        }

        private void CheckTracked(Action<List<ICommunity>> copy)
        {
            copy(_tracked);
            _validator.CheckTracked(_tracked, World.People, World.Clock);
        }

        // New journal entries since the last read: births and deaths counted,
        // milestone firsts noted.
        private void ReadJournal(out YearTally tally)
        {
            tally = default;
            var journal = World.Journal;

            for (; _journalRead < journal.Count; _journalRead++)
            {
                var entry = journal[_journalRead];

                switch (entry.Kind)
                {
                    case DomainEventKind.PersonBorn when entry.Time.Ticks > 0L:
                        tally.Births++;
                        break;
                    case DomainEventKind.PersonDied when entry.Reasons.Contains(ReasonCode.Starved):
                        tally.Starved++;
                        break;
                    case DomainEventKind.PersonDied when entry.Reasons.Contains(ReasonCode.Froze):
                        tally.Froze++;
                        break;
                    case DomainEventKind.PersonDied when entry.Reasons.Contains(ReasonCode.OldAge):
                        tally.OldAge++;
                        break;
                    case DomainEventKind.PersonDied when entry.Reasons.Contains(ReasonCode.Illness):
                        tally.Illness++;
                        break;
                    case DomainEventKind.PersonDied:
                        tally.OtherDeaths++;
                        break;
                    case DomainEventKind.CampPitched when FirstCamp is null:
                        FirstCamp = entry.Time;
                        break;
                    case DomainEventKind.SettlementFounded when FirstSettlement is null:
                        FirstSettlement = entry.Time;
                        break;
                }
            }
        }

        private int CountSide(bool west)
        {
            _communities.Clear();
            World.CopyCommunitiesTo(_communities);
            var count = 0;

            for (var i = 0; i < _communities.Count; i++)
            {
                if (IsWest(_communities[i].Position) == west)
                {
                    count += _communities[i].Members.Count;
                }
            }

            return count;
        }

        // West of the river in the community's own row: the river drifts
        // from row to row, so one column does not split the whole map.
        private bool IsWest(WorldPosition at)
        {
            for (var x = 0; x < World.Grid.Width; x++)
            {
                if (World.Grid[new WorldPosition(x, at.Y)] == TerrainKind.SmallRiver)
                {
                    return at.X < x;
                }
            }

            throw new InvalidOperationException("Row " + at.Y + " has no river.");
        }
    }

    /// <summary>What one year's journal entries add up to.</summary>
    public struct YearTally
    {
        public int Births;
        public int Starved;
        public int Froze;
        public int OldAge;
        public int Illness;
        public int OtherDeaths;

        public int Deaths => Starved + Froze + OldAge + Illness + OtherDeaths;
    }

    public readonly struct YearSummary
    {
        public YearSummary(long year, int west, int east, YearTally tally)
        {
            Year = year;
            West = west;
            East = east;
            Tally = tally;
        }

        public long Year { get; }

        public int West { get; }

        public int East { get; }

        public YearTally Tally { get; }
    }
}