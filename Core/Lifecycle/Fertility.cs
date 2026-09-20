using System;
using System.Collections.Generic;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Events;
using KingdomWatch.Core.Needs;
using KingdomWatch.Core.Relationships;
using KingdomWatch.Core.Rng;

namespace KingdomWatch.Core.Lifecycle
{
    /// <summary>
    /// Children arrive. Owns <see cref="ScheduledEventKind.BirthCheck"/> -
    /// each household's periodic chance of conceiving - and
    /// <see cref="ScheduledEventKind.BirthDue"/>, the end of a pregnancy, at
    /// which a person is added to the world with a mother, a father, a
    /// household and a band, and <see cref="DomainEventKind.PersonBorn"/>
    /// is announced.
    /// </summary>
    /// <remarks>
    /// **Per household, on an interval.** Section 4's own sketch of the
    /// scheduler has <c>nextBirthCheck</c> beside task completion and the
    /// social decision pass, and the kind was reserved that way: a
    /// household is the unit that has a couple in it, and a check every few
    /// days is the coarsest interval at which a chance per check still
    /// means something. The check itself is not a domain event; the birth
    /// is. A failed check is nothing in the chronicle.
    ///
    /// **Who conceives.** The first woman in the household, in the stable
    /// order the household lists its members, who is of fertile age and
    /// whose active partner is a living man in the same household. She
    /// conceives only if she is not already pregnant, has not given birth
    /// within <see cref="DemographicSettings.PostpartumTicks"/>, has eaten
    /// within <see cref="Hunger.StarvationGrace"/> and is not below
    /// <see cref="DemographicSettings.HealthFloor"/> - section 6's
    /// nutrition and health modifiers, as gates rather than curves, because
    /// a curve would be tuning nothing measures yet. The postpartum gate
    /// matters more than it looks: a delivery and a check can share an
    /// instant - they always do when gestation is a multiple of the check
    /// interval - and the delivery runs first, so without it she would
    /// conceive the day she gave birth. Race fertility
    /// compatibility (section 10: couples yes, children no) waits for a
    /// race to exist (#34). Choosing the couple is not this class's: that
    /// is family formation (#9), and behind it the social decision system
    /// (#38); this class asks only whether a household has one.
    ///
    /// **The pregnancy is the pending event.** Conception books
    /// <see cref="ScheduledEventKind.BirthDue"/> at term and writes its id
    /// to <see cref="PersonRecord.PregnancyDue"/>; nothing else records the
    /// pregnancy. Section 17 names birth due dates among the future
    /// commitments a save keeps, and a due date kept separately could
    /// disagree with the queue. The death cascade cancels the event and
    /// clears the field together. A delivery is that one event and no other:
    /// a due for a dead mother, or one the record does not name - stale,
    /// duplicated, rebuilt from a save that disagrees with the store - is
    /// ignored.
    ///
    /// **Households arrive through
    /// <see cref="DomainEventKind.HouseholdFormed"/>**, which
    /// <see cref="Households.Form"/> publishes for every household there
    /// is, seeded or married into. This class subscribes and books the
    /// first check; a check that finds its household dissolved simply does
    /// not book another, so the stream ends with the household. Listen and
    /// book.
    ///
    /// **Where the child lands.** In the mother's household, at the mother's
    /// position, in the mother's community - band or settlement, handed in
    /// through <see cref="Track"/> and scanned for her, as
    /// <see cref="Deaths"/> does, because nothing on a record says which
    /// one holds someone. Culture is the mother's for now; what a
    /// newborn inherits is #40's. The child is announced last, once every
    /// store that will be asked about them can answer, so a subscriber to
    /// PersonBorn - <see cref="Aging"/>, <see cref="Mortality"/> - books
    /// against a person who fully exists.
    ///
    /// A check that does not conceive allocates nothing. A birth does -
    /// a person, their genealogy entry, a member in two lists - and is not
    /// held to zero: births are rare, and section 18's zero-allocation loop
    /// is about the steady state, not the moment the population grows.
    /// </remarks>
    public sealed class Fertility : IScheduledEventHandler, IDomainEventSubscriber
    {
        /// <summary>Section 4: birth, ageing, injury, death.</summary>
        public const SimulationPhase Phase = SimulationPhase.Lifecycle;

        private const int PerMille = 1000;

        private readonly DomainEventBus _bus;
        private readonly SimulationClock _clock;
        private readonly PersonStore _people;
        private readonly Genealogy _genealogy;
        private readonly Partnerships _partnerships;
        private readonly Households _households;
        private readonly DeterministicRng _rng;
        private readonly DemographicSettings _settings;

        // The communities a newborn may need adding to. See the type's remarks.
        private readonly List<ICommunity> _groups = new List<ICommunity>();

        // The BirthCheck each household's stream is currently booked as, so a
        // stray one can be told from the real one and refused (#80). A map of
        // its own rather than a field on Household: a household is a plain
        // object in a registry with a get-only shape (Household.cs), and this
        // is Fertility's bookkeeping, not something the household knows about
        // itself. Mortality keeps the same thing on PersonRecord, because
        // people already have a record and PregnancyDue already lives there.
        //
        // Only ever looked up by key, never iterated, so the dictionary's
        // order cannot reach the simulation.
        private readonly Dictionary<EntityId, EventId> _pendingChecks = new Dictionary<EntityId, EventId>();

        public Fertility(
            DomainEventBus bus,
            PersonStore people,
            Genealogy genealogy,
            Partnerships partnerships,
            Households households,
            DeterministicRng rng,
            DemographicSettings settings)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _people = people ?? throw new ArgumentNullException(nameof(people));
            _genealogy = genealogy ?? throw new ArgumentNullException(nameof(genealogy));
            _partnerships = partnerships ?? throw new ArgumentNullException(nameof(partnerships));
            _households = households ?? throw new ArgumentNullException(nameof(households));
            _rng = rng ?? throw new ArgumentNullException(nameof(rng));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _settings.Validate();
            _clock = bus.Clock;
        }

        /// <summary>How many communities a newborn may be placed in.</summary>
        public int TrackedCount => _groups.Count;

        /// <summary>
        /// Fills <paramref name="into"/> with every community a newborn may be placed in, in the order they were
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

            for (var i = 0; i < _groups.Count; i++)
            {
                into.Add(_groups[i]);
            }
        }

        /// <summary>
        /// Fills <paramref name="into"/> with every birth check this system
        /// has booked and not yet seen come due (#80), ordered. Clears the
        /// list first.
        /// </summary>
        /// <remarks>
        /// The bookings are kept in a dictionary keyed by household, and
        /// section 5 forbids acting on the order a dictionary hands its
        /// entries back in - it is a function of hash codes and insertion
        /// history, not of the world. The world hash folds these in, so the
        /// order has to be a property of the bookings themselves: they are
        /// sorted before they leave, by <see cref="PendingBooking"/>'s own
        /// total ordering.
        /// </remarks>
        public void CopyBookingsTo(List<PendingBooking> into)
        {
            if (into is null)
            {
                throw new ArgumentNullException(nameof(into));
            }

            into.Clear();

            foreach (var entry in _pendingChecks)
            {
                if (!entry.Value.IsNone)
                {
                    into.Add(new PendingBooking(
                        entry.Key, ScheduledEventKind.BirthCheck, entry.Value));
                }
            }

            into.Sort();
        }

        /// <summary>
        /// How many households have a <c>BirthCheck</c> booked. Exposed for
        /// the same reason <see cref="TrackedCount"/> is: bookkeeping kept by
        /// hand is worth being able to assert on directly, and an entry that
        /// outlived the household that owned it has no other symptom - ids are
        /// never reused, so nothing would ever collide with it.
        /// </summary>
        public int PendingCheckCount => _pendingChecks.Count;

        /// <summary>Whether this community is tracked here.</summary>
        public bool IsTracked(ICommunity community) =>
            IndexOf((community ?? throw new ArgumentNullException(nameof(community))).Id) >= 0;

        /// <summary>
        /// Registers a community whose members may give birth. Refuses one
        /// already tracked, as <see cref="Deaths.Track"/> does.
        /// </summary>
        public void Track(ICommunity group)
        {
            if (group is null)
            {
                throw new ArgumentNullException(nameof(group));
            }

            if (IndexOf(group.Id) >= 0)
            {
                throw new InvalidOperationException(group.Id + " is already tracked.");
            }

            _groups.Add(group);
        }

        /// <summary>
        /// Stops placing newborns in a community, as <see cref="Deaths.Untrack"/>
        /// does. Pregnancies carry on: a birth check finds the mother by her
        /// household, and the child lands in whichever tracked community
        /// holds her by then.
        /// </summary>
        public void Untrack(ICommunity group)
        {
            if (group is null)
            {
                throw new ArgumentNullException(nameof(group));
            }

            var index = IndexOf(group.Id);

            if (index < 0)
            {
                throw new InvalidOperationException(group.Id + " is not tracked by Fertility.");
            }

            _groups.RemoveAt(index);
        }

        private int IndexOf(EntityId community)
        {
            for (var i = 0; i < _groups.Count; i++)
            {
                if (_groups[i].Id == community)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>Whether this person is carrying a pregnancy.</summary>
        public bool IsPregnant(PersonHandle person) => !_people.GetPregnancyDue(person).IsNone;

        public void On(in DomainEvent published)
        {
            if (published.Kind == DomainEventKind.HouseholdFormed)
            {
                ScheduleCheck(published.PrimaryEntity);
            }
        }

        public void Handle(ScheduledEvent scheduled, SimulationClock clock)
        {
            if (!ReferenceEquals(clock, _clock))
            {
                throw new InvalidOperationException(
                    "Fertility schedules on its bus's clock, but was dispatched by another.");
            }

            switch (scheduled.Kind)
            {
                case ScheduledEventKind.BirthCheck:
                    Check(scheduled);
                    break;
                case ScheduledEventKind.BirthDue:
                    GiveBirth(scheduled.Id, scheduled.PrimaryEntity, scheduled.SecondaryEntity);
                    break;
                default:
                    throw new InvalidOperationException(
                        "Fertility owns " + ScheduledEventKind.BirthCheck + " and "
                        + ScheduledEventKind.BirthDue + ", but was handed " + scheduled + ".");
            }
        }

        private void Check(ScheduledEvent scheduled)
        {
            var householdId = scheduled.PrimaryEntity;

            if (!_households.TryGet(householdId, out var household))
            {
                // A dissolved household books no successor, so the stream ends
                // itself; the entry goes with it rather than outliving the
                // household that owned it.
                _pendingChecks.Remove(householdId);
                return;
            }

            // The household names the check it booked, and only that one runs -
            // the rule Hunger and Jobs apply to their streams. Any other
            // BirthCheck would book a second stream, and two streams check
            // twice as often, which doubles the conception chance per interval
            // and reads as bad tuning rather than as a bug (#80).
            var booked = _pendingChecks.TryGetValue(householdId, out var pending) ? pending : EventId.None;

            if (scheduled.Id != booked)
            {
                throw new InvalidOperationException(
                    scheduled + " came due for " + householdId + ", whose next check is " + booked + ".");
            }

            _pendingChecks.Remove(householdId);

            if (TryFindCouple(household, out var mother, out var father))
            {
                Conceive(household.Id, mother, father);
            }

            ScheduleCheck(householdId);
        }

        // The first woman of fertile age who may conceive today and whose
        // active partner is a living man in this household. Members are
        // listed in a stable order, so two runs find the same couple - and
        // an ineligible woman ahead of an eligible one does not stand in her
        // way, which matters only when two couples share a household.
        private bool TryFindCouple(Household household, out PersonHandle mother, out PersonHandle father)
        {
            var members = household.Members;
            var now = _clock.Now;

            for (var i = 0; i < members.Count; i++)
            {
                var candidate = members[i];

                if (_people.GetSex(candidate) != Sex.Female
                    || !_settings.IsFertileAge(_people.GetAgeYears(candidate, now))
                    || !IsEligible(candidate))
                {
                    continue;
                }

                var partner = _partnerships.ActivePartnerOf(_people.GetId(candidate));

                if (partner.IsNone
                    || !_people.TryGetHandle(partner, out var partnerHandle)
                    || _people.GetSex(partnerHandle) != Sex.Male
                    || _people.GetHousehold(partnerHandle) != household.Id)
                {
                    continue;
                }

                mother = candidate;
                father = partnerHandle;
                return true;
            }

            mother = PersonHandle.None;
            father = PersonHandle.None;
            return false;
        }

        private bool IsEligible(PersonHandle mother) =>
            !IsPregnant(mother)
            && !IsPostpartum(mother)
            && _people.GetHealth(mother) >= _settings.HealthFloor
            && _people.GetLastFedAt(mother).TicksUntil(_clock.Now) <= Hunger.StarvationGrace;

        // When she last gave birth is read off her youngest living child,
        // rather than kept as a field that would need a "never" sentinel.
        // Youngest by birth tick, not last recorded: the genealogy keeps
        // children in the order they were written, and worldgen may seed a
        // family in any order. A child who has died takes their birth tick
        // with them, and the next youngest stands in - older, so the gate
        // can only be looser.
        private bool IsPostpartum(PersonHandle mother)
        {
            var children = _genealogy.Children(_people.GetId(mother));
            var now = _clock.Now;

            for (var i = 0; i < children.Length; i++)
            {
                if (_people.TryGetHandle(children[i], out var child)
                    && _people.GetTicksLived(child, now) < _settings.PostpartumTicks)
                {
                    return true;
                }
            }

            return false;
        }

        private void Conceive(EntityId householdId, PersonHandle mother, PersonHandle father)
        {
            var now = _clock.Now;

            // No term inside time means no pregnancy, not a throw; see
            // ScheduleCheck.
            if (now.Ticks > long.MaxValue - _settings.GestationTicks)
            {
                return;
            }

            if (!_rng.Key(RandomDomain.Conception, RandomSite.ConceptionRoll).Mix(householdId).Mix(now.Ticks)
                .Chance(_settings.ConceptionPerMille, PerMille))
            {
                return;
            }

            var due = _clock.Schedule(
                now.Plus(_settings.GestationTicks),
                Phase,
                ScheduledEventKind.BirthDue,
                _people.GetId(mother),
                _people.GetId(father));

            _people.SetPregnancyDue(mother, due);
        }

        // A delivery is the one event the record names, and nothing else:
        // a due that is not hers - stale, duplicated, rebuilt from a save
        // that disagrees with the store - bears no child.
        private void GiveBirth(EventId due, EntityId motherId, EntityId fatherId)
        {
            if (!_people.TryGetHandle(motherId, out var mother) || _people.GetPregnancyDue(mother) != due)
            {
                return;
            }

            _people.SetPregnancyDue(mother, EventId.None);

            var now = _clock.Now;
            var childId = _clock.Ids.Next(EntityKind.Person);
            var sex = _rng.Key(RandomDomain.ChildSex, RandomSite.NewbornSex).Mix(motherId).Mix(now.Ticks).Chance(1, 2)
                ? Sex.Female
                : Sex.Male;

            var child = _people.Add(
                childId,
                _people.GetPosition(mother),
                _settings.NewbornHealth,
                AgeStage.Infant,
                sex,
                _people.GetBirthCulture(mother),
                _people.GetAssimilation(mother),
                now,
                now.Ticks);

            _genealogy.Record(childId, motherId, fatherId);

            var household = _households.Of(mother);

            if (household != null)
            {
                _households.Join(household, child);
            }

            var group = GroupOf(mother);

            if (group != null)
            {
                group.AddMember(child);
            }

            _bus.Publish(DomainEventKind.PersonBorn, childId, motherId);
        }

        private ICommunity? GroupOf(PersonHandle person)
        {
            for (var i = 0; i < _groups.Count; i++)
            {
                var members = _groups[i].Members;

                for (var j = 0; j < members.Count; j++)
                {
                    if (members[j] == person)
                    {
                        return _groups[i];
                    }
                }
            }

            return null;
        }

        // The stream ends with time itself, as Hunger's does: a check within
        // one interval of the last representable instant has no next to book.
        private void ScheduleCheck(EntityId householdId)
        {
            var now = _clock.Now;

            if (now.Ticks > long.MaxValue - _settings.BirthCheckTicks)
            {
                return;
            }

            _pendingChecks[householdId] = _clock.Schedule(
                now.Plus(_settings.BirthCheckTicks), Phase, ScheduledEventKind.BirthCheck, householdId, EntityId.None);
        }
    }
}
