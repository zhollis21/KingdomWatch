using System;
using System.Collections.Generic;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;

namespace KingdomWatch.Core.Needs
{
    /// <summary>
    /// People keep warm, or do not. Owns
    /// <see cref="ScheduledEventKind.WarmthDue"/>: every evening each tracked
    /// community's hearths are lit, and in winter each one burns
    /// <see cref="FuelPerFire"/> wood from the community's ledger or stays
    /// dark; anyone whose hearth has been dark for long enough loses health.
    /// </summary>
    /// <remarks>
    /// **The shape of <see cref="Hunger"/>, one evening a day.** The economy
    /// ladder's section 2 asked for exactly that: a <c>WarmthDue</c> like
    /// <c>MealDue</c>, drawing fuel from the same ledger, costing health past
    /// a grace period. So everything <see cref="Hunger"/> says about why it
    /// is a daily scheduled event rather than a per-person crossing holds
    /// here too: a thirty-day jump dispatches thirty evenings, and the one
    /// that runs out of wood is the one the damage lands on. Exposure is
    /// integrated, not polled: a person stores only when they last slept
    /// warm (<see cref="PersonRecord.LastWarmedAt"/>).
    ///
    /// **Only winter burns.** The other three seasons' evenings mark
    /// everyone warm and draw nothing, so the first winter night measures
    /// its grace period from last night rather than from whenever the
    /// person was born. Wood only: Charcoal is the ladder's efficient fuel
    /// and is not a resource yet.
    ///
    /// **A fire per household, not per person** (#53, decided at kickoff).
    /// A household keeps one hearth, and everyone in it is warm when it is
    /// lit - the household is what the ladder's House will shelter once #69
    /// gives homes an identity, so this becomes a fire per house without
    /// changing what is counted. People in no household share one communal
    /// fire. The wood still comes from the community's ledger: households
    /// have access to supplies, not supplies of their own, for the reason
    /// <see cref="Hunger"/> gives.
    ///
    /// **When wood is short, homes with children are lit first.** Hearths
    /// are lit in a fixed order - households with a dependent (infant, child
    /// or adolescent) in them, then the rest, each in the order they formed,
    /// and the communal fire last - until the wood runs out. It is
    /// <see cref="Hunger"/>'s protect-the-next-generation rule at the unit a
    /// fire actually warms, and it reads the way a chronicle would tell it:
    /// the old couple's house went cold first. Formation order is id order,
    /// since household ids are allocated as households form and never reused.
    ///
    /// **Damage here, death elsewhere.** A person whose hearth stays dark
    /// past <see cref="ExposureGrace"/> loses <see cref="ExposureDamagePerNight"/>
    /// health that night, never below zero; the night that reaches zero
    /// raises <see cref="ScheduledEventKind.ExposureCritical"/> for the same
    /// instant in the lifecycle phase, and <see cref="Lifecycle.Mortality"/>
    /// answers it with the death - exactly <see cref="Hunger"/>'s starvation
    /// crossing, with its own kind so the death reads
    /// <see cref="Events.ReasonCode.Froze"/>. Health never recovers yet, for
    /// cold as for hunger.
    ///
    /// No domain events: the chronicle hears about the cold through the
    /// deaths it causes and the ledger's consumption flows. No randomness.
    ///
    /// The numbers are placeholders in the <see cref="PrimitiveTier"/> sense.
    ///
    /// Allocation-free once every hearth list has grown to the most
    /// households a community has held.
    /// </remarks>
    public sealed class Warmth : IScheduledEventHandler
    {
        /// <summary>Tick of day the fires are lit - after dusk, when everyone is home.</summary>
        public const long Nightfall = 20L * SimulationTime.TicksPerHour;

        /// <summary>Wood one hearth burns in one winter night.</summary>
        public const int FuelPerFire = 1;

        /// <summary>
        /// How long someone can go without a lit hearth before a cold night
        /// costs health. Two dark nights are hardship; the third hurts.
        /// </summary>
        public const long ExposureGrace = 2L * SimulationTime.TicksPerDay;

        /// <summary>Health lost per cold night once past the grace period.</summary>
        public const short ExposureDamagePerNight = 10;

        /// <summary>
        /// Burning is a resource change, so it runs in the physical phase, as
        /// meals do.
        /// </summary>
        public const SimulationPhase Phase = SimulationPhase.Physical;

        private readonly SimulationClock _clock;
        private readonly PersonStore _people;

        // A list, scanned by id, for the reason Hunger's is.
        private readonly List<Tracked> _tracked = new List<Tracked>();

        // One evening's hearths, reused: every living member's seat (their
        // household, and whether they are a dependent), then the households at
        // the fire, sorted by id, whether each has a dependent in it, and the
        // place each takes in the lighting order.
        private readonly List<Seat> _seats = new List<Seat>();
        private readonly List<EntityId> _hearths = new List<EntityId>();
        private readonly List<bool> _hearthHasDependents = new List<bool>();
        private readonly List<int> _hearthRank = new List<int>();

        public Warmth(SimulationClock clock, PersonStore people)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _people = people ?? throw new ArgumentNullException(nameof(people));
        }

        /// <summary>How many communities have fires scheduled.</summary>
        public int TrackedCount => _tracked.Count;

        /// <summary>
        /// How many hearths a winter night would light for these members if
        /// wood were no object: one per household among the living, plus one
        /// communal fire if any of the living is in none.
        /// </summary>
        /// <remarks>
        /// Static, and taking the caller's buffer, so that
        /// <see cref="Work.Jobs"/> can size a woodpile against it at dawn
        /// without depending on this instance. Clears
        /// <paramref name="scratch"/> first; what it leaves there is
        /// the distinct households, in id order.
        ///
        /// One sort rather than a membership check per member: a list scan
        /// per member is members times households, every dawn, and grows
        /// with the community the way #83's per-pick scan did.
        /// </remarks>
        public static int CountHearths(IReadOnlyList<PersonHandle> members, PersonStore people, List<EntityId> scratch)
        {
            if (members is null)
            {
                throw new ArgumentNullException(nameof(members));
            }

            if (people is null)
            {
                throw new ArgumentNullException(nameof(people));
            }

            if (scratch is null)
            {
                throw new ArgumentNullException(nameof(scratch));
            }

            scratch.Clear();
            var communal = false;

            for (var i = 0; i < members.Count; i++)
            {
                var member = members[i];

                if (!people.IsAlive(member))
                {
                    continue;
                }

                var household = people.GetHousehold(member);

                if (household.IsNone)
                {
                    communal = true;
                }
                else
                {
                    scratch.Add(household);
                }
            }

            // Sorted, a household's members are adjacent: keep the first of
            // each run and drop the rest.
            scratch.Sort();
            var distinct = 0;

            for (var i = 0; i < scratch.Count; i++)
            {
                if (distinct == 0 || scratch[i] != scratch[distinct - 1])
                {
                    scratch[distinct++] = scratch[i];
                }
            }

            scratch.RemoveRange(distinct, scratch.Count - distinct);
            return distinct + (communal ? 1 : 0);
        }

        /// <summary>
        /// Fills <paramref name="into"/> with every community with fires
        /// scheduled, in the order they were tracked. Clears the list first.
        /// </summary>
        /// <remarks>For the validator, as <see cref="Hunger.CopyTrackedTo"/> is.</remarks>
        public void CopyTrackedTo(List<ICommunity> into)
        {
            if (into is null)
            {
                throw new ArgumentNullException(nameof(into));
            }

            into.Clear();

            for (var i = 0; i < _tracked.Count; i++)
            {
                into.Add(_tracked[i].Group);
            }
        }

        /// <summary>
        /// Fills <paramref name="into"/> with every evening this system has
        /// booked and not yet seen come due. Clears the list first.
        /// </summary>
        /// <remarks>For the validator and the world hash, as <see cref="Hunger.CopyBookingsTo"/> is.</remarks>
        public void CopyBookingsTo(List<PendingBooking> into)
        {
            if (into is null)
            {
                throw new ArgumentNullException(nameof(into));
            }

            into.Clear();

            for (var i = 0; i < _tracked.Count; i++)
            {
                var booked = _tracked[i].PendingFire;

                if (!booked.IsNone)
                {
                    into.Add(new PendingBooking(_tracked[i].Group.Id, ScheduledEventKind.WarmthDue, booked));
                }
            }
        }

        /// <summary>Whether this community is tracked here.</summary>
        public bool IsTracked(ICommunity community) =>
            IndexOf((community ?? throw new ArgumentNullException(nameof(community))).Id) >= 0;

        /// <summary>
        /// Starts keeping a community's fires: its first evening is booked
        /// for the next <see cref="Nightfall"/>, and each evening books the
        /// next. Refuses a community already tracked - two streams on one
        /// woodpile would burn twice a night.
        /// </summary>
        public void Track(ICommunity group)
        {
            if (group is null)
            {
                throw new ArgumentNullException(nameof(group));
            }

            if (IndexOf(group.Id) >= 0)
            {
                throw new InvalidOperationException(
                    group.Id + " is already tracked; a second stream of fires would burn twice a night.");
            }

            // Booked before recorded, as Hunger does.
            var fire = _clock.Schedule(NextNightfall(_clock.Now), Phase, ScheduledEventKind.WarmthDue, group.Id, EntityId.None);
            _tracked.Add(new Tracked(group) { PendingFire = fire });
        }

        /// <summary>
        /// Stops keeping a community's fires: its pending evening is
        /// cancelled. A band that settles hands its people and wood to the
        /// settlement, which is tracked in its place. Throws when the
        /// community was never tracked.
        /// </summary>
        public void Untrack(ICommunity group)
        {
            var index = IndexOf(TrackedFor(group).Group.Id);
            _clock.Cancel(_tracked[index].PendingFire);
            _tracked.RemoveAt(index);
        }

        public void Handle(ScheduledEvent scheduled, SimulationClock clock)
        {
            if (scheduled.Kind != ScheduledEventKind.WarmthDue)
            {
                throw new InvalidOperationException(
                    "Warmth owns " + ScheduledEventKind.WarmthDue + ", but was handed " + scheduled + ".");
            }

            if (!ReferenceEquals(clock, _clock))
            {
                throw new InvalidOperationException(
                    "Warmth schedules on its own clock, but was dispatched by another.");
            }

            var index = IndexOf(scheduled.PrimaryEntity);

            if (index < 0)
            {
                throw new InvalidOperationException(
                    scheduled + " came due for a community Warmth is not tracking.");
            }

            var tracked = _tracked[index];

            // The community names the evening it booked, and only that one
            // burns - the rule Hunger applies to its meals (#80).
            if (scheduled.Id != tracked.PendingFire)
            {
                throw new InvalidOperationException(
                    scheduled + " came due for " + scheduled.PrimaryEntity
                    + ", whose next evening is " + tracked.PendingFire + ".");
            }

            tracked.PendingFire = EventId.None;
            var now = clock.Now;

            if (now.Season == Season.Winter)
            {
                Burn(tracked.Group, now);
            }
            else
            {
                WarmEveryone(tracked.Group, now);
            }

            // The stream ends with time itself, as Hunger's does.
            if (now.Ticks <= long.MaxValue - SimulationTime.TicksPerDay)
            {
                tracked.PendingFire = _clock.Schedule(
                    now.Plus(SimulationTime.TicksPerDay), Phase, ScheduledEventKind.WarmthDue, tracked.Group.Id, EntityId.None);
            }
        }

        private void WarmEveryone(ICommunity group, SimulationTime now)
        {
            var members = group.Members;

            for (var i = 0; i < members.Count; i++)
            {
                if (_people.IsAlive(members[i]))
                {
                    _people.SetLastWarmedAt(members[i], now);
                }
            }
        }

        // A winter night: gather the hearths, light as many as the wood
        // covers in lighting order, then walk the members - warm by a lit
        // hearth, or one night colder.
        private void Burn(ICommunity group, SimulationTime now)
        {
            var members = group.Members;
            var communal = GatherHearths(members);
            var hearths = _hearths.Count + (communal ? 1 : 0);

            // Every fire burns the same, so the lit ones are a prefix of the
            // lighting order: rank below this is lit, at or above it is dark.
            var ledger = group.SharedSupplies;
            var lit = Math.Min(hearths, ledger.Available(ResourceKind.Wood) / FuelPerFire);

            if (lit > 0)
            {
                ledger.Consume(ResourceKind.Wood, lit * FuelPerFire);
            }

            for (var i = 0; i < members.Count; i++)
            {
                var member = members[i];

                // Membership lags death until the cascade strikes the dead
                // from the group, as it does at a meal.
                if (!_people.IsAlive(member))
                {
                    continue;
                }

                if (RankOf(_people.GetHousehold(member)) < lit)
                {
                    _people.SetLastWarmedAt(member, now);
                }
                else if (_people.GetLastWarmedAt(member).TicksUntil(now) > ExposureGrace)
                {
                    Chill(member);
                }
            }
        }

        // Fills the hearth lists from the living members, sorted by id, and
        // ranks them: households with a dependent first, then the rest, each
        // in id order. Returns whether anyone living is in no household and
        // so sits at the communal fire, which ranks after every hearth.
        // Collect, sort once, then walk the runs - for the reason
        // CountHearths does rather than an insertion per new household.
        private bool GatherHearths(IReadOnlyList<PersonHandle> members)
        {
            _seats.Clear();
            _hearths.Clear();
            _hearthHasDependents.Clear();
            _hearthRank.Clear();
            var communal = false;

            for (var i = 0; i < members.Count; i++)
            {
                var member = members[i];

                if (!_people.IsAlive(member))
                {
                    continue;
                }

                var household = _people.GetHousehold(member);

                if (household.IsNone)
                {
                    communal = true;
                    continue;
                }

                _seats.Add(new Seat(household, AgeStages.IsDependent(_people.GetAgeStage(member))));
            }

            // Seats compare by household alone, so a household's seats are
            // adjacent whatever order the sort leaves them in, and the flag
            // is an OR over the run, which no order changes.
            _seats.Sort();

            for (var i = 0; i < _seats.Count; i++)
            {
                var seat = _seats[i];
                var last = _hearths.Count - 1;

                if (last >= 0 && _hearths[last] == seat.Household)
                {
                    _hearthHasDependents[last] |= seat.IsDependent;
                    continue;
                }

                _hearths.Add(seat.Household);
                _hearthHasDependents.Add(seat.IsDependent);
                _hearthRank.Add(0);
            }

            var rank = 0;
            RankHearths(withDependents: true, ref rank);
            RankHearths(withDependents: false, ref rank);
            return communal;
        }

        private void RankHearths(bool withDependents, ref int rank)
        {
            for (var i = 0; i < _hearths.Count; i++)
            {
                if (_hearthHasDependents[i] == withDependents)
                {
                    _hearthRank[i] = rank++;
                }
            }
        }

        private int RankOf(EntityId household)
        {
            if (household.IsNone)
            {
                return _hearths.Count;
            }

            return _hearthRank[_hearths.BinarySearch(household)];
        }

        // Hunger.Starve's shape: zero is as bad as it gets, the night that
        // reaches it raises the crossing once, and health already at or below
        // zero is left exactly as it is.
        private void Chill(PersonHandle member)
        {
            var health = _people.GetHealth(member);

            if (health <= 0)
            {
                return;
            }

            var remaining = (short)Math.Max(0, health - ExposureDamagePerNight);
            _people.SetHealth(member, remaining);

            if (remaining == 0)
            {
                _clock.Schedule(
                    _clock.Now,
                    Lifecycle.Mortality.Phase,
                    ScheduledEventKind.ExposureCritical,
                    _people.GetId(member),
                    EntityId.None);
            }
        }

        // The first Nightfall strictly after now: a community tracked at
        // nightfall itself burns tomorrow, not twice today.
        private static SimulationTime NextNightfall(SimulationTime now)
        {
            var tickOfDay = now.TickOfDay;
            var until = tickOfDay < Nightfall
                ? Nightfall - tickOfDay
                : SimulationTime.TicksPerDay - tickOfDay + Nightfall;
            return now.Plus(until);
        }

        private Tracked TrackedFor(ICommunity group)
        {
            if (group is null)
            {
                throw new ArgumentNullException(nameof(group));
            }

            var index = IndexOf(group.Id);

            if (index < 0)
            {
                throw new InvalidOperationException(group.Id + " is not tracked by Warmth.");
            }

            return _tracked[index];
        }

        private int IndexOf(EntityId holder)
        {
            for (var i = 0; i < _tracked.Count; i++)
            {
                if (_tracked[i].Group.Id == holder)
                {
                    return i;
                }
            }

            return -1;
        }

        // One living member at the fire: which hearth, and whether they are
        // one of the dependents who put it first in the lighting order.
        private readonly struct Seat : IComparable<Seat>
        {
            public Seat(EntityId household, bool isDependent)
            {
                Household = household;
                IsDependent = isDependent;
            }

            public EntityId Household { get; }

            public bool IsDependent { get; }

            public int CompareTo(Seat other) => Household.CompareTo(other.Household);
        }

        private sealed class Tracked
        {
            public Tracked(ICommunity group)
            {
                Group = group;
            }

            public ICommunity Group { get; }

            // The evening this community booked, so that no other WarmthDue
            // burns and Untrack can cancel it. None only while an evening is
            // burning, or when the stream has reached the end of time.
            public EventId PendingFire { get; set; }
        }
    }
}
