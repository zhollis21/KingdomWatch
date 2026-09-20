using System;
using System.Collections.Generic;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Events;
using KingdomWatch.Core.Relationships;
using KingdomWatch.Core.Rng;

namespace KingdomWatch.Core.Lifecycle
{
    /// <summary>
    /// Who pairs off, until the social decision system decides it. Owns
    /// <see cref="ScheduledEventKind.CourtshipDue"/>: once a year each
    /// tracked community's unpartnered adults are considered in member
    /// order, and each eligible pair gets one keyed chance to marry, better
    /// the closer they are in age.
    /// </summary>
    /// <remarks>
    /// **A stand-in, and it says so.** Section 7 puts partner choice in the
    /// social and personal decision system (#38, M7), weighing the social
    /// tie, status and the family's view. None of that exists, and M1's
    /// 200-year run (#17) dies out without a second generation of marriages
    /// - so this pairs people off with the least machinery that still reads
    /// like a village rather than a queue: not everyone eligible marries the
    /// year they become so, and a couple a generation apart is rare. When
    /// #38 lands, it replaces this class rather than extending it.
    ///
    /// **The rulebook is <see cref="FamilyFormation"/>.** Nothing here
    /// judges eligibility; <see cref="FamilyFormation.Evaluate"/> answers
    /// "may these two?" and <see cref="FamilyFormation.Partner"/> does the
    /// rest - the partnership, the household, the children moving in. What
    /// this class adds is the draw, keyed on the two people and the year so
    /// that the same world gives the same weddings on every platform, and
    /// the age-gap weighting, which is the one preference cheap enough to
    /// be worth having before #38.
    ///
    /// **Women propose, in member order, to men in member order.** Not a
    /// statement about the culture - just an order, because section 5 needs
    /// one and the alternative is scanning every pair twice. The first man
    /// the draw accepts is the one; the rest of the year's candidates are
    /// not consulted. A woman refused by everyone tries again next year.
    ///
    /// The numbers are placeholders: plausible, not tuned.
    ///
    /// Allocation-free after <see cref="Track"/>: the scan is by index over
    /// the community's members, and the only state is the pending event.
    /// </remarks>
    public sealed class Matchmaking : IScheduledEventHandler
    {
        /// <summary>Ticks between one community's courtships.</summary>
        public const long Interval = SimulationTime.TicksPerYear;

        /// <summary>Chance, per mille, that an eligible pair of the same age marries this year.</summary>
        public const int BaseChancePerMille = 250;

        /// <summary>What each year of age gap takes off the chance.</summary>
        public const int PerYearOfGapPerMille = 25;

        /// <summary>The chance never falls below this: a gap makes a match rare, not impossible.</summary>
        public const int FloorChancePerMille = 50;

        private const int PerMille = 1000;

        /// <summary>A wedding is a household and social matter.</summary>
        public const SimulationPhase Phase = SimulationPhase.HouseholdAndSocial;

        private readonly SimulationClock _clock;
        private readonly PersonStore _people;
        private readonly FamilyFormation _family;
        private readonly Partnerships _partnerships;
        private readonly DeterministicRng _rng;

        // A list, scanned by id, for the reason Hunger's is.
        private readonly List<Tracked> _tracked = new List<Tracked>();

        public Matchmaking(
            DomainEventBus bus,
            PersonStore people,
            FamilyFormation family,
            Partnerships partnerships,
            DeterministicRng rng)
        {
            if (bus is null)
            {
                throw new ArgumentNullException(nameof(bus));
            }

            _people = people ?? throw new ArgumentNullException(nameof(people));
            _family = family ?? throw new ArgumentNullException(nameof(family));
            _partnerships = partnerships ?? throw new ArgumentNullException(nameof(partnerships));
            _rng = rng ?? throw new ArgumentNullException(nameof(rng));
            _clock = bus.Clock;
        }

        /// <summary>How many communities pair off.</summary>
        public int TrackedCount => _tracked.Count;

        /// <summary>
        /// Fills <paramref name="into"/> with every community that pairs off, in the order they were
        /// tracked. Clears the list first.
        /// </summary>
        /// <remarks>
        /// For the validator (issue 13), which cannot otherwise tell that a
        /// tracked community still exists, or that the people it holds are
        /// alive. The list is the caller's so a check taken once per
        /// simulated day reuses one buffer.
        ///
        /// Read-only in the list sense only: the entries are the live
        /// communities, and <see cref="ICommunity"/> can add and remove
        /// members. Same as <see cref="Lifecycle.Households.All"/>. It is
        /// handed out for reading, and writing through it is a caller bug
        /// rather than something this can prevent.
        /// </remarks>
        public void CopyTrackedTo(List<ICommunity> into)
        {
            if (into is null)
            {
                throw new ArgumentNullException(nameof(into));
            }

            into.Clear();

            for (var i = 0; i < _tracked.Count; i++)
            {
                into.Add(_tracked[i].Community);
            }
        }

        /// <summary>
        /// Fills <paramref name="into"/> with every courtship round this system has
        /// booked and not yet seen come due (#80). Clears the list first.
        /// </summary>
        /// <remarks>
        /// The validator confirms each one is still in the queue, and the
        /// world hash folds them in: a world that agrees on its people and
        /// disagrees on what it has booked for them has already diverged, it
        /// has just not shown yet.
        /// </remarks>
        public void CopyBookingsTo(List<PendingBooking> into)
        {
            if (into is null)
            {
                throw new ArgumentNullException(nameof(into));
            }

            into.Clear();

            for (var i = 0; i < _tracked.Count; i++)
            {
                var booked = _tracked[i].PendingCourtship;

                if (!booked.IsNone)
                {
                    into.Add(new PendingBooking(
                        _tracked[i].Community.Id, ScheduledEventKind.CourtshipDue, booked));
                }
            }
        }

        /// <summary>Whether this community is tracked here.</summary>
        public bool IsTracked(ICommunity community) =>
            IndexOf((community ?? throw new ArgumentNullException(nameof(community))).Id) >= 0;

        /// <summary>
        /// The chance, per mille, that two people this many years apart
        /// marry in a year they are both eligible and considered.
        /// </summary>
        public static int ChancePerMille(long ageGapYears)
        {
            // Compared before it is multiplied, so a gap at the ends of the
            // type - where the product would wrap, and where the minimum
            // has no absolute value - reads as "large" rather than as
            // arithmetic.
            const long GapAtFloor = BaseChancePerMille / PerYearOfGapPerMille;

            if (ageGapYears >= GapAtFloor || ageGapYears <= -GapAtFloor)
            {
                return FloorChancePerMille;
            }

            var reduction = (int)(Math.Abs(ageGapYears) * PerYearOfGapPerMille);
            return Math.Max(FloorChancePerMille, BaseChancePerMille - reduction);
        }

        /// <summary>
        /// Starts a community pairing off: its first courtship is booked one
        /// <see cref="Interval"/> from now, and each books the next. Refuses
        /// one already tracked.
        /// </summary>
        public void Track(ICommunity community)
        {
            if (community is null)
            {
                throw new ArgumentNullException(nameof(community));
            }

            if (IndexOf(community.Id) >= 0)
            {
                throw new InvalidOperationException(
                    community.Id + " is already tracked; a second stream would court twice a year.");
            }

            // Booked before recorded, as Hunger does.
            var due = Schedule(community.Id);
            _tracked.Add(new Tracked(community) { PendingCourtship = due });
        }

        /// <summary>
        /// Stops a community pairing off: its pending courtship is cancelled
        /// and the stream ends. Throws when it was never tracked.
        /// </summary>
        public void Untrack(ICommunity community)
        {
            if (community is null)
            {
                throw new ArgumentNullException(nameof(community));
            }

            var index = IndexOf(community.Id);

            if (index < 0)
            {
                throw new InvalidOperationException(community.Id + " is not tracked by Matchmaking.");
            }

            _clock.Cancel(_tracked[index].PendingCourtship);
            _tracked.RemoveAt(index);
        }

        public void Handle(ScheduledEvent scheduled, SimulationClock clock)
        {
            if (scheduled.Kind != ScheduledEventKind.CourtshipDue)
            {
                throw new InvalidOperationException(
                    "Matchmaking owns " + ScheduledEventKind.CourtshipDue + ", but was handed " + scheduled + ".");
            }

            if (!ReferenceEquals(clock, _clock))
            {
                throw new InvalidOperationException(
                    "Matchmaking schedules on its bus's clock, but was dispatched by another.");
            }

            var index = IndexOf(scheduled.PrimaryEntity);

            if (index < 0)
            {
                throw new InvalidOperationException(
                    scheduled + " came due for a community Matchmaking is not tracking.");
            }

            var tracked = _tracked[index];

            // The community names the courtship it booked, and only that one
            // runs - the rule Jobs and Hunger apply to their streams.
            if (scheduled.Id != tracked.PendingCourtship)
            {
                throw new InvalidOperationException(
                    scheduled + " came due for " + scheduled.PrimaryEntity
                    + ", whose next courtship is " + tracked.PendingCourtship + ".");
            }

            tracked.PendingCourtship = EventId.None;
            Court(tracked.Community, clock.Now);

            // The stream ends with time itself, as Hunger's does.
            if (clock.Now.Ticks <= long.MaxValue - Interval)
            {
                tracked.PendingCourtship = Schedule(scheduled.PrimaryEntity);
            }
        }

        private void Court(ICommunity community, SimulationTime now)
        {
            var members = community.Members;
            var year = now.Ticks / SimulationTime.TicksPerYear;

            for (var i = 0; i < members.Count; i++)
            {
                var woman = members[i];

                if (!IsSingleAdult(woman, Sex.Female))
                {
                    continue;
                }

                for (var j = 0; j < members.Count; j++)
                {
                    var man = members[j];

                    if (!IsSingleAdult(man, Sex.Male) || _family.Evaluate(woman, man) != PartnerRefusal.None)
                    {
                        continue;
                    }

                    var gap = _people.GetAgeYears(woman, now) - _people.GetAgeYears(man, now);
                    var womanId = _people.GetId(woman);
                    var manId = _people.GetId(man);

                    if (_rng.Key(RandomDomain.Courtship, RandomSite.MarriageRoll).Mix(womanId).Mix(manId).Mix(year)
                        .Chance(ChancePerMille(gap), PerMille))
                    {
                        _family.Partner(woman, man, Reasons.None);
                        break;
                    }
                }
            }
        }

        // Alive, grown, of the sex asked for, and not in a partnership. The
        // dead may still be listed until the cascade strikes them.
        private bool IsSingleAdult(PersonHandle person, Sex sex) =>
            _people.IsAlive(person)
            && _people.GetSex(person) == sex
            && AgeStages.IsAdult(_people.GetAgeStage(person))
            && _partnerships.ActivePartnerOf(_people.GetId(person)).IsNone;

        private EventId Schedule(EntityId community) =>
            _clock.Schedule(
                _clock.Now.Plus(Interval), Phase, ScheduledEventKind.CourtshipDue, community, EntityId.None);

        private int IndexOf(EntityId community)
        {
            for (var i = 0; i < _tracked.Count; i++)
            {
                if (_tracked[i].Community.Id == community)
                {
                    return i;
                }
            }

            return -1;
        }

        private sealed class Tracked
        {
            public Tracked(ICommunity community)
            {
                Community = community;
            }

            public ICommunity Community { get; }

            // The courtship this community booked, so that no other runs
            // and Untrack can cancel it.
            public EventId PendingCourtship { get; set; }
        }
    }
}
