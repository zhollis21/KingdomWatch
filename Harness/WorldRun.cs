using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using KingdomWatch.Core;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Events;
using KingdomWatch.Core.Settlements;

namespace KingdomWatch.Harness
{
    /// <summary>
    /// One seed of the M1 world (section 19), run a year at a time and
    /// validated after each year. Records what the chronicle prints and
    /// the tests assert: each village's population by year, its births and
    /// deaths, when the reachable milestones first fired, and the first
    /// validation break if there is one.
    /// </summary>
    /// <remarks>
    /// Villages, not homelands (#163): counted by side of the river, a
    /// village dying out hid behind its growing neighbour. Every community
    /// is counted, a band as much as a settlement, in the order the run
    /// first sees it - the founding order for those standing at the start.
    /// A band that settles hands its people to the settlement it founds,
    /// which is a move, not a death.
    ///
    /// Milestones 1 to 3 of the economy ladder's section 9 can fire: the
    /// first settlement, founded at world start since #146, and since #100 a
    /// village's first house and first harvest, which the chronicle reports.
    /// The rest wait on skills (#22) and the buildings that need them.
    /// </remarks>
    public sealed class WorldRun
    {
        // The M1 world's standard size lives in Core (World.M1), where the
        // Unity driver reads it too; these names are kept for the harness.
        public const int Width = World.M1Width;
        public const int Height = World.M1Height;

        private readonly List<ICommunity> _communities = new List<ICommunity>();
        private readonly List<ICommunity> _tracked = new List<ICommunity>();
        private readonly List<PendingBooking> _bookings = new List<PendingBooking>();
        private readonly List<YearSummary> _years = new List<YearSummary>();
        private readonly ReadOnlyCollection<YearSummary> _yearsView;
        private readonly List<VillageYear> _founding = new List<VillageYear>();
        private readonly ReadOnlyCollection<VillageYear> _foundingView;
        private readonly List<DieOut> _diedOut = new List<DieOut>();
        private readonly ReadOnlyCollection<DieOut> _diedOutView;
        private readonly WorldValidator _validator = new WorldValidator();
        private int _journalRead;

        // Every community the run has seen, in the order it first saw them,
        // and each one's place in that order: looked up, never iterated.
        private readonly List<EntityId> _villages = new List<EntityId>();
        private readonly Dictionary<EntityId, int> _indexOf = new Dictionary<EntityId, int>();

        // This year's births and deaths, one per village in _villages.
        private readonly List<YearTally> _tallies = new List<YearTally>();

        // The village each living person is counted in, and the bands that
        // settled, which are gone without having died out.
        private readonly Dictionary<EntityId, EntityId> _homeOf = new Dictionary<EntityId, EntityId>();
        private readonly HashSet<EntityId> _settled = new HashSet<EntityId>();

        public WorldRun(ulong seed)
            : this(World.M1(seed))
        {
        }

        public WorldRun(World world)
        {
            World = world ?? throw new ArgumentNullException(nameof(world));
            _yearsView = _years.AsReadOnly();
            _foundingView = _founding.AsReadOnly();
            _diedOutView = _diedOut.AsReadOnly();
            RememberFounders();
            ReadJournal(tally: false);
        }

        public World World { get; }

        /// <summary>Each village standing at the start, and how many it held.</summary>
        public IReadOnlyList<VillageYear> Founding => _foundingView;

        public IReadOnlyList<YearSummary> Years => _yearsView;

        public SimulationTime? FirstCamp { get; private set; }

        public SimulationTime? FirstSettlement { get; private set; }

        /// <summary>The validator's report for the first year that broke, or null.</summary>
        public string? Failure { get; private set; }

        public bool IsClean => Failure is null;

        /// <summary>
        /// Each village that has died out, and the first year that ended with
        /// nobody in it, in the order they died.
        /// </summary>
        public IReadOnlyList<DieOut> DiedOut => _diedOutView;

        /// <summary>
        /// Whether the run passed: it broke no invariant. A die-out used to
        /// fail it too (the #103 review), but since #26 berries run
        /// out and bands can starve on the move, and since #100 a village
        /// that has cut every tree in its reach freezes (#147), so a
        /// die-out is reported - <see cref="DiedOut"/>, the chronicle and
        /// the sweep line - and not failed. Settling is not part of it either.
        /// </summary>
        public bool Held => IsClean;

        /// <summary>
        /// A village's numbers over the run, from the first time it was seen:
        /// its founding size for one standing at the start, its first year's
        /// end for one founded later - so the years before it existed are not
        /// counted as empty ones. Throws for a village the run has never seen.
        /// </summary>
        public VillageRange RangeOf(EntityId village)
        {
            if (!_indexOf.TryGetValue(village, out var index))
            {
                throw new ArgumentException(village + " is not a village this run has seen.", nameof(village));
            }

            int? first = null;
            var low = 0;
            var high = 0;
            var last = 0;

            for (var f = 0; f < _founding.Count; f++)
            {
                if (_founding[f].Village == village)
                {
                    first = _founding[f].Living;
                }
            }

            if (first is int founded)
            {
                low = high = last = founded;
            }

            // A village's place in the order is fixed and the order only
            // grows, so a year has seen it exactly when it lists that many.
            for (var y = 0; y < _years.Count; y++)
            {
                var villages = _years[y].Villages;

                if (villages.Count <= index)
                {
                    continue;
                }

                last = villages[index].Living;

                if (first is null)
                {
                    first = low = high = last;
                }

                low = Math.Min(low, last);
                high = Math.Max(high, last);
            }

            return new VillageRange(village, first ?? 0, last, low, high);
        }

        /// <summary>
        /// Advances year by year, validating after each, and stops at the first
        /// year that breaks an invariant: a world past its first break is not
        /// worth reading.
        /// </summary>
        public WorldRun RunYears(long years) => RunYears(years, null);

        /// <summary>
        /// <see cref="RunYears(long)"/>, advancing a day at a time and
        /// calling <paramref name="eachDay"/> after each when it is given:
        /// what the town planner's fed-growth picture tops up stores by
        /// (#23). Null advances a year at a time, as the plain overload does.
        /// </summary>
        public WorldRun RunYears(long years, Action<World>? eachDay)
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
                if (eachDay is null)
                {
                    World.Advance(SimulationTime.TicksPerYear);
                }
                else
                {
                    for (var day = 0; day < SimulationTime.DaysPerYear; day++)
                    {
                        World.Advance(SimulationTime.TicksPerDay);
                        eachDay(World);
                    }
                }

                ReadJournal(tally: true);
                CloseYear();
                Validate();
            }

            return this;
        }

        /// <summary>The world's canonical hash (<see cref="World.Hash"/>).</summary>
        public ulong Hash() => World.Hash();

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
                .CheckCommunities(_communities, world.People, world.Clock)
                .CheckLand(world.Land, world.Grid, world.Clock)
                .CheckBuildings(world.Buildings, world.Founding, world.Households, world.Grid, world.Clock);

            // Each system's own tracked set: a community one system still has
            // events booked for and another has let go has no other symptom.
            CheckTracked(world.Hunger.CopyTrackedTo);
            CheckTracked(world.Warmth.CopyTrackedTo);
            CheckTracked(world.Jobs.CopyTrackedTo);
            CheckTracked(world.Fertility.CopyTrackedTo);
            CheckTracked(world.Matchmaking.CopyTrackedTo);
            CheckTracked(world.Deaths.CopyTrackedTo);
            CheckTracked(world.Nomads.CopyTrackedTo);

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

        // Everyone alive at the start, in the village they live in. Each
        // newborn joins their mother's village as the journal reports the
        // birth, so every death is counted where it happened (the #156
        // review).
        private void RememberFounders()
        {
            _communities.Clear();
            World.CopyCommunitiesTo(_communities);

            for (var i = 0; i < _communities.Count; i++)
            {
                var village = _communities[i];
                Know(village.Id);
                _founding.Add(new VillageYear(village.Id, village.Members.Count, default));

                for (var m = 0; m < village.Members.Count; m++)
                {
                    _homeOf[World.People.GetId(village.Members[m])] = village.Id;
                }
            }
        }

        // New journal entries since the last read: births and deaths counted
        // for the village they happened in, a band's people moved to the
        // village it founds, milestone firsts noted. What the journal held
        // before the run started is history: read for its milestones, not
        // tallied - and its dead are not in the villages the run started
        // from, so tallying them would throw (the #156 review).
        private void ReadJournal(bool tally)
        {
            var journal = World.Journal;

            for (; _journalRead < journal.Count; _journalRead++)
            {
                var entry = journal[_journalRead];

                switch (entry.Kind)
                {
                    case DomainEventKind.PersonBorn when tally && entry.Time.Ticks > 0L:
                        var home = HomeOf(entry.SecondaryEntity, entry);
                        _homeOf[entry.PrimaryEntity] = home;
                        var born = _tallies[_indexOf[home]];
                        born.Births++;
                        _tallies[_indexOf[home]] = born;
                        break;
                    case DomainEventKind.PersonDied when tally:
                        var index = _indexOf[HomeOf(entry.PrimaryEntity, entry)];
                        var died = _tallies[index];
                        CountDeath(ref died, entry);
                        _tallies[index] = died;
                        _homeOf.Remove(entry.PrimaryEntity);
                        break;
                    case DomainEventKind.SettlementFounded:
                        Settle(entry.SecondaryEntity, entry.PrimaryEntity);

                        if (FirstSettlement is null)
                        {
                            FirstSettlement = entry.Time;
                        }

                        break;
                    case DomainEventKind.CampPitched when FirstCamp is null:
                        FirstCamp = entry.Time;
                        break;
                }
            }
        }

        // A band that founds a settlement moves everyone in it there. Who
        // moves is a set, so the order the map is walked in changes nothing.
        private void Settle(EntityId band, EntityId settlement)
        {
            Know(settlement);

            if (!_indexOf.ContainsKey(band))
            {
                return;
            }

            _settled.Add(band);
            var movers = new List<EntityId>();

            foreach (var pair in _homeOf)
            {
                if (pair.Value == band)
                {
                    movers.Add(pair.Key);
                }
            }

            for (var i = 0; i < movers.Count; i++)
            {
                _homeOf[movers[i]] = settlement;
            }
        }

        // The year's line: each village's people and its tally, every
        // village the run has seen, and the ones newly empty noted as dead.
        private void CloseYear()
        {
            _communities.Clear();
            World.CopyCommunitiesTo(_communities);

            for (var i = 0; i < _communities.Count; i++)
            {
                Know(_communities[i].Id);
            }

            var living = new int[_villages.Count];

            for (var i = 0; i < _communities.Count; i++)
            {
                living[_indexOf[_communities[i].Id]] = _communities[i].Members.Count;
            }

            var year = World.Now.YearNumber;
            var villages = new VillageYear[_villages.Count];

            for (var i = 0; i < villages.Length; i++)
            {
                var count = living[i];
                villages[i] = new VillageYear(_villages[i], count, _tallies[i]);

                if (count == 0 && !_settled.Contains(_villages[i]) && !HasDied(_villages[i]))
                {
                    _diedOut.Add(new DieOut(_villages[i], year));
                }

                _tallies[i] = default;
            }

            _years.Add(new YearSummary(year, villages));
        }

        private bool HasDied(EntityId village)
        {
            for (var i = 0; i < _diedOut.Count; i++)
            {
                if (_diedOut[i].Village == village)
                {
                    return true;
                }
            }

            return false;
        }

        // A village's place in the run's order, given one the first time it
        // is seen.
        private int Know(EntityId village)
        {
            if (!_indexOf.TryGetValue(village, out var index))
            {
                index = _villages.Count;
                _villages.Add(village);
                _indexOf.Add(village, index);
                _tallies.Add(default);
            }

            return index;
        }

        private static void CountDeath(ref YearTally tally, in DomainEvent died)
        {
            if (died.Reasons.Contains(ReasonCode.Starved))
            {
                tally.Starved++;
            }
            else if (died.Reasons.Contains(ReasonCode.Froze))
            {
                tally.Froze++;
            }
            else if (died.Reasons.Contains(ReasonCode.OldAge))
            {
                tally.OldAge++;
            }
            else if (died.Reasons.Contains(ReasonCode.Illness))
            {
                tally.Illness++;
            }
            else
            {
                tally.OtherDeaths++;
            }
        }

        // A person's village, which every person the journal names has:
        // founders from the start, everyone after from their mother. One
        // without is a tally that would quietly go missing, so it throws.
        private EntityId HomeOf(EntityId person, in DomainEvent entry) =>
            _homeOf.TryGetValue(person, out var home)
                ? home
                : throw new InvalidOperationException(entry + " names " + person + ", whose village is unknown.");
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

        /// <summary>Two tallies added together.</summary>
        public static YearTally operator +(YearTally a, YearTally b) => new YearTally
        {
            Births = a.Births + b.Births,
            Starved = a.Starved + b.Starved,
            Froze = a.Froze + b.Froze,
            OldAge = a.OldAge + b.OldAge,
            Illness = a.Illness + b.Illness,
            OtherDeaths = a.OtherDeaths + b.OtherDeaths,
        };
    }

    /// <summary>One village at the end of a year: who lives there, and what the year brought it.</summary>
    public readonly struct VillageYear
    {
        public VillageYear(EntityId village, int living, YearTally tally)
        {
            Village = village;
            Living = living;
            Tally = tally;
        }

        public EntityId Village { get; }

        public int Living { get; }

        public YearTally Tally { get; }
    }

    /// <summary>A village's numbers over a run: where it started, where it is, and the fewest and most in between.</summary>
    public readonly struct VillageRange
    {
        public VillageRange(EntityId village, int first, int last, int low, int high)
        {
            Village = village;
            First = first;
            Last = last;
            Low = low;
            High = high;
        }

        public EntityId Village { get; }

        public int First { get; }

        public int Last { get; }

        public int Low { get; }

        public int High { get; }
    }

    /// <summary>A village that died out, and the first year that ended with nobody in it.</summary>
    public readonly struct DieOut
    {
        public DieOut(EntityId village, long year)
        {
            Village = village;
            Year = year;
        }

        public EntityId Village { get; }

        public long Year { get; }
    }

    public readonly struct YearSummary
    {
        private static readonly IReadOnlyList<VillageYear> NoVillages = Array.AsReadOnly(Array.Empty<VillageYear>());

        // Null in default(YearSummary), which reads as a year of nobody.
        private readonly VillageYear[]? _villages;
        private readonly IReadOnlyList<VillageYear>? _view;

        /// <summary>
        /// A year of these villages. The array is copied: the year owns its
        /// villages, so editing the caller's array afterwards changes nothing.
        /// </summary>
        public YearSummary(long year, VillageYear[] villages)
        {
            Year = year;
            _villages = (VillageYear[])(villages ?? throw new ArgumentNullException(nameof(villages))).Clone();
            _view = Array.AsReadOnly(_villages);
        }

        public long Year { get; }

        /// <summary>Every village the run has seen by this year's end, in the run's order.</summary>
        public IReadOnlyList<VillageYear> Villages => _view ?? NoVillages;

        /// <summary>Every village's births and deaths this year.</summary>
        public YearTally Tally
        {
            get
            {
                var tally = default(YearTally);
                var villages = _villages ?? Array.Empty<VillageYear>();

                for (var i = 0; i < villages.Length; i++)
                {
                    tally += villages[i].Tally;
                }

                return tally;
            }
        }

        /// <summary>This village's year: what it held, or nobody and nothing if the run had not seen it yet.</summary>
        public VillageYear Of(EntityId village)
        {
            var villages = _villages ?? Array.Empty<VillageYear>();

            for (var i = 0; i < villages.Length; i++)
            {
                if (villages[i].Village == village)
                {
                    return villages[i];
                }
            }

            return new VillageYear(village, 0, default);
        }
    }
}
