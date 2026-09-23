using System;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Events;
using KingdomWatch.Core.Needs;
using KingdomWatch.Core.Rng;

namespace KingdomWatch.Core.Lifecycle
{
    /// <summary>
    /// Decides natural deaths. Owns
    /// <see cref="ScheduledEventKind.MortalityCheck"/> - each person's yearly
    /// roll against the life table, on their birthday - and answers
    /// <see cref="ScheduledEventKind.StarvationCritical"/>, the crossing
    /// <see cref="Hunger"/> raises when a missed meal takes someone to zero,
    /// and <see cref="ScheduledEventKind.ExposureCritical"/>, the one
    /// <see cref="Warmth"/> raises for a cold night (#53). All three end in
    /// <see cref="Deaths.Die"/>; the cascade is not repeated here.
    /// </summary>
    /// <remarks>
    /// **Once a year, not every tick.** Section 6: "every second, roll chance
    /// of dying" is exactly the pattern the scheduler exists to replace.
    /// A yearly roll on the birthday is the coarsest interval at which the
    /// table's numbers still mean what they say - a chance per year of
    /// life, rolled at the birthday that ends it, so the first birthday
    /// rolls infancy's first year and the maximum birthday is certain - and
    /// it spreads deaths across the calendar, because birthdays are, so the
    /// chronicle does not report every death on the same day of the year.
    /// The roll is keyed on the person and the instant, so the same seed
    /// produces the same deaths and adding a roll elsewhere shifts none of
    /// them.
    ///
    /// **Health and hunger raise the odds; zero health is certain.** The
    /// table gives a base chance per year; being below
    /// <see cref="DemographicSettings.HealthFloor"/> or unfed past
    /// <see cref="Hunger.StarvationGrace"/> multiplies it. Those are section
    /// 6's health and nutrition modifiers, and they are multipliers rather
    /// than additions so that they scale with age - a starving elder is in
    /// more danger than a starving child. Actually starving to death is not
    /// a roll: the meal that takes health to zero raises
    /// <see cref="ScheduledEventKind.StarvationCritical"/> for the same
    /// instant in the lifecycle phase, and this class kills whoever it names
    /// if their health is still there. Section 4's phases exist for exactly
    /// that: the meal is a resource change in the physical phase, the death
    /// is its consequence in the next, and the meal's loop over the table is
    /// not mutating the table it is walking.
    ///
    /// **People arrive through <see cref="DomainEventKind.PersonBorn"/>**,
    /// as with <see cref="Aging"/>, and for the same reason: one way in. A
    /// check that comes due for the dead is ignored.
    ///
    /// **Why a death says what it does.** The table does not know what
    /// killed anyone, so the reason is read off the age: past the soft
    /// lifespan it is <see cref="ReasonCode.OldAge"/>, before it
    /// <see cref="ReasonCode.Illness"/>, a starvation crossing is
    /// <see cref="ReasonCode.Starved"/> and an exposure crossing
    /// <see cref="ReasonCode.Froze"/>. Emitted at the decision site, as
    /// section 5 asks, so the chronicle can say why.
    ///
    /// Allocation-free after construction: a check reads a record, draws a
    /// key and books one event; a death is the cascade's allocation-free
    /// path.
    /// </remarks>
    public sealed class Mortality : IScheduledEventHandler, IDomainEventSubscriber
    {
        /// <summary>Section 4: birth, ageing, injury, death.</summary>
        public const SimulationPhase Phase = SimulationPhase.Lifecycle;

        private const int PerMille = 1000;

        private readonly SimulationClock _clock;
        private readonly PersonStore _people;
        private readonly Deaths _deaths;
        private readonly DeterministicRng _rng;
        private readonly DemographicSettings _settings;

        /// <param name="bus">
        /// Where births are heard, and where the clock comes from - the one
        /// the deaths this class decides will be stamped with.
        /// </param>
        public Mortality(
            DomainEventBus bus,
            PersonStore people,
            Deaths deaths,
            DeterministicRng rng,
            DemographicSettings settings)
        {
            if (bus is null)
            {
                throw new ArgumentNullException(nameof(bus));
            }

            _people = people ?? throw new ArgumentNullException(nameof(people));
            _deaths = deaths ?? throw new ArgumentNullException(nameof(deaths));
            _rng = rng ?? throw new ArgumentNullException(nameof(rng));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _settings.Validate();
            _clock = bus.Clock;
        }

        /// <summary>
        /// The chance per mille that this person dies at their next birthday,
        /// as the check there will roll it: the table's rate for the year of
        /// life they are in now, multiplied for frailty and hunger, capped at
        /// certainty - and certain outright if that birthday is the maximum
        /// or their health is already gone. A query, for the validator and
        /// the tooltip; the roll itself is <see cref="Handle"/>'s.
        /// </summary>
        public int YearlyChancePerMille(PersonHandle person) =>
            ChanceAtNextBirthday(person, _people.GetAgeYears(person, _clock.Now));

        // The roll for the year of life numbered yearLived - from that
        // birthday to the next - made at the birthday that ends it. Certain
        // when that birthday is the maximum, and when health is already at
        // zero: the check kills there without rolling, so that is what the
        // year holds.
        private int ChanceAtNextBirthday(PersonHandle person, long yearLived)
        {
            if (_people.GetHealth(person) <= 0 || yearLived + 1L >= _settings.MaxLifespanYears)
            {
                return PerMille;
            }

            var now = _clock.Now;
            long chance = _settings.BaseMortalityPerMille(yearLived);

            if (_people.GetHealth(person) < _settings.HealthFloor)
            {
                chance *= _settings.FrailtyMultiplier;
            }

            if (_people.GetLastFedAt(person).TicksUntil(now) > Hunger.StarvationGrace)
            {
                chance *= _settings.HungerMultiplier;
            }

            return (int)Math.Min(chance, PerMille);
        }

        public void On(in DomainEvent published)
        {
            if (published.Kind != DomainEventKind.PersonBorn)
            {
                return;
            }

            if (!_people.TryGetHandle(published.PrimaryEntity, out var person))
            {
                throw new InvalidOperationException(
                    published + " names nobody in the store; a birth is announced after the person is added.");
            }

            ScheduleNextCheck(person, published.PrimaryEntity);
        }

        public void Handle(ScheduledEvent scheduled, SimulationClock clock)
        {
            if (!ReferenceEquals(clock, _clock))
            {
                throw new InvalidOperationException(
                    "Mortality schedules on its bus's clock, but was dispatched by another.");
            }

            switch (scheduled.Kind)
            {
                case ScheduledEventKind.MortalityCheck:
                    Check(scheduled);
                    break;
                case ScheduledEventKind.StarvationCritical:
                    DieAtZero(scheduled.PrimaryEntity, ReasonCode.Starved);
                    break;
                case ScheduledEventKind.ExposureCritical:
                    DieAtZero(scheduled.PrimaryEntity, ReasonCode.Froze);
                    break;
                default:
                    throw new InvalidOperationException(
                        "Mortality owns " + ScheduledEventKind.MortalityCheck + ", "
                        + ScheduledEventKind.StarvationCritical + " and " + ScheduledEventKind.ExposureCritical
                        + ", but was handed " + scheduled + ".");
            }
        }

        private void Check(ScheduledEvent scheduled)
        {
            var id = scheduled.PrimaryEntity;

            // A check for someone already dead is ignored rather than refused:
            // durable ids are never reused, so there is nobody it could wrongly
            // touch, and it books no successor on its way out.
            if (!_people.TryGetHandle(id, out var person))
            {
                return;
            }

            // The person names the check they booked, and only that one is
            // rolled - the rule Hunger and Jobs apply to their streams. Any
            // other MortalityCheck would roll the life table again and book a
            // second stream, doubling this person's yearly hazard from then on
            // and reading as bad tuning rather than as a bug (#80).
            if (scheduled.Id != _people.GetPendingMortalityCheck(person))
            {
                throw new InvalidOperationException(
                    scheduled + " came due for " + id + ", whose next check is "
                    + _people.GetPendingMortalityCheck(person) + ".");
            }

            // Cleared before the roll, not after. This event is in flight, so
            // cancelling it is already a no-op - the queue drops a dispatched
            // id from its live set before handing it over (EventQueue) - but a
            // death below publishes PersonDied, and every subscriber to that
            // runs while this record is readable. Clearing first means they
            // read None rather than the id of a check that will never come.
            _people.SetPendingMortalityCheck(person, EventId.None);

            // Zero health is certain death whichever wake-up finds it. The
            // meal or cold night that took them there can share this instant
            // with a birthday, and this check sorts before the crossing it
            // raised, so the roll would otherwise name an illness for what
            // was starvation or exposure.
            if (_people.GetHealth(person) <= 0)
            {
                _deaths.Die(person, new Reasons(ZeroHealthReason(person, _clock.Now)));
                return;
            }

            // A birthday ends a year of life, and that year is what is rolled:
            // the first birthday rolls infancy's first year, and the maximum
            // birthday is the one nobody survives. Rolling the year ahead
            // instead would leave the year from birth to the first birthday
            // with no exposure at all.
            var now = _clock.Now;
            var yearLived = _people.GetAgeYears(person, now) - 1L;
            var chance = ChanceAtNextBirthday(person, yearLived);

            if (_rng.Key(RandomDomain.Mortality, RandomSite.LifeTableRoll).Mix(id).Mix(now.Ticks).Chance(chance, PerMille))
            {
                var reason = yearLived >= _settings.SoftLifespanYears ? ReasonCode.OldAge : ReasonCode.Illness;
                _deaths.Die(person, new Reasons(reason));
                return;
            }

            ScheduleNextCheck(person, id);
        }

        // Health is read again rather than trusted from the meal or the
        // night: the crossing was booked in an earlier phase of the same
        // instant, and although nothing today heals between the two, the
        // check is what keeps that an accident of ordering rather than a
        // dependency. A person both hungry and cold whose two crossings land
        // together dies to whichever is dispatched first, and the second
        // finds nobody.
        private void DieAtZero(EntityId id, ReasonCode reason)
        {
            if (!_people.TryGetHandle(id, out var person) || _people.GetHealth(person) > 0)
            {
                return;
            }

            _deaths.Die(person, new Reasons(reason));
        }

        // What took someone to zero, when a birthday rather than the crossing
        // finds them there: cold when they have slept warm less recently than
        // the grace allows and eaten within it, hunger otherwise - hunger was
        // the only way to zero before #53, and stays the answer for anyone
        // both starving and cold.
        private ReasonCode ZeroHealthReason(PersonHandle person, SimulationTime now)
        {
            var cold = _people.GetLastWarmedAt(person).TicksUntil(now) > Warmth.ExposureGrace;
            var hungry = _people.GetLastFedAt(person).TicksUntil(now) > Hunger.StarvationGrace;
            return cold && !hungry ? ReasonCode.Froze : ReasonCode.Starved;
        }

        // The next birthday after now. Booked on the birthday rather than a
        // year from now so that the checks stay on the calendar a person
        // was born to, whatever instant they were announced at. The part of
        // the year already lived is taken from the two remainders, never from
        // the difference: the difference can exceed a long for someone the
        // clock has outrun (PersonStore.GetTicksLived saturates it, which is
        // right for an age and wrong for a calendar), and each remainder is
        // already small. C# keeps the dividend's sign, so a negative birth
        // is brought back into the year before the two are combined.
        private void ScheduleNextCheck(PersonHandle person, EntityId id)
        {
            var now = _clock.Now;
            var year = SimulationTime.TicksPerYear;
            var bornIntoYear = (_people.GetBornTick(person) % year + year) % year;
            var livedIntoYear = (now.Ticks % year - bornIntoYear + year) % year;
            var untilBirthday = year - livedIntoYear;

            // A birthday past the end of time never comes; see Aging.
            if (now.Ticks > long.MaxValue - untilBirthday)
            {
                return;
            }

            _people.SetPendingMortalityCheck(
                person,
                _clock.Schedule(
                    now.Plus(untilBirthday), Phase, ScheduledEventKind.MortalityCheck, id, EntityId.None));
        }
    }
}
