using System;
using System.Collections.Generic;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Events;
using KingdomWatch.Core.Lifecycle;
using KingdomWatch.Core.Needs;
using NUnit.Framework;

namespace KingdomWatch.Core.Tests.Lifecycle
{
    [TestFixture]
    public sealed class MortalityTests
    {
        // A table nobody dies to unless a test says so.
        private static DemographicSettings Immortal() => new DemographicSettings
        {
            InfantMortalityPerMille = 0,
            ChildMortalityPerMille = 0,
            AdolescentMortalityPerMille = 0,
            AdultMortalityPerMille = 0,
            ElderMortalityPerMille = 0,
            SoftLifespanYears = 1_000L,
            MaxLifespanYears = 2_000L,
            ConceptionPerMille = 0,
        };

        [Test]
        public void Construction_refuses_a_missing_collaborator_and_a_bad_table()
        {
            var w = new DemographicWorld();

            Assert.Multiple(() =>
            {
                Assert.That(() => new Mortality(null!, w.People, w.Deaths, w.Rng, w.Settings), Throws.ArgumentNullException);
                Assert.That(() => new Mortality(w.Bus, null!, w.Deaths, w.Rng, w.Settings), Throws.ArgumentNullException);
                Assert.That(() => new Mortality(w.Bus, w.People, null!, w.Rng, w.Settings), Throws.ArgumentNullException);
                Assert.That(() => new Mortality(w.Bus, w.People, w.Deaths, null!, w.Settings), Throws.ArgumentNullException);
                Assert.That(() => new Mortality(w.Bus, w.People, w.Deaths, w.Rng, null!), Throws.ArgumentNullException);
                Assert.That(
                    () => new Mortality(w.Bus, w.People, w.Deaths, w.Rng, new DemographicSettings { MaxLifespanYears = 10L }),
                    Throws.TypeOf<ArgumentOutOfRangeException>());
            });
        }

        [Test]
        public void The_first_check_is_booked_for_the_next_birthday_after_the_announcement()
        {
            var w = new DemographicWorld(Immortal(), 1UL);
            w.Advance(SimulationTime.TicksPerDay * 7L);
            var person = w.NewPerson(30L, Sex.Male);

            var found = false;
            var count = w.Clock.ScheduledCount;

            // The elder boundary is also booked; the check is whichever of
            // the two comes first, and a thirty-year-old's next birthday is.
            if (w.Clock.TryPeekNext(out var next))
            {
                found = next.Kind == ScheduledEventKind.MortalityCheck
                    && next.Time == w.BirthdayOf(person, 31L)
                    && next.Phase == Mortality.Phase
                    && next.PrimaryEntity == w.IdOf(person);
            }

            Assert.Multiple(() =>
            {
                Assert.That(found, Is.True, "the next birthday, a year less a week out");
                Assert.That(count, Is.EqualTo(2), "one check and one boundary");
            });
        }

        [Test]
        public void Nobody_dies_to_a_table_of_zeros()
        {
            var w = new DemographicWorld(Immortal(), 1UL);
            var band = w.NewBand();
            var people = new List<PersonHandle>();

            for (var i = 0; i < 20; i++)
            {
                var person = w.NewPerson(i, i % 2 == 0 ? Sex.Female : Sex.Male);
                band.AddMember(person);
                people.Add(person);
            }

            w.AdvanceYears(100L);

            Assert.Multiple(() =>
            {
                Assert.That(w.People.Count, Is.EqualTo(20));
                Assert.That(w.Published(DomainEventKind.PersonDied), Is.Empty);
                Assert.That(w.People.GetAgeStage(people[0]), Is.EqualTo(AgeStage.Elder), "they aged while surviving");
            });
        }

        [Test]
        public void A_certain_table_kills_on_the_next_birthday_and_the_reason_reads_off_the_age()
        {
            var settings = new DemographicSettings
            {
                AdultMortalityPerMille = 1000,
                ElderMortalityPerMille = 1000,
                InfantMortalityPerMille = 0,
                ChildMortalityPerMille = 0,
                AdolescentMortalityPerMille = 0,
                SoftLifespanYears = 70L,
                MaxLifespanYears = 100L,
                ConceptionPerMille = 0,
            };
            var w = new DemographicWorld(settings, 1UL);
            var band = w.NewBand();
            var young = w.NewPerson(30L, Sex.Male);
            var old = w.NewPerson(75L, Sex.Female);
            var child = w.NewPerson(5L, Sex.Female);
            band.AddMember(young);
            band.AddMember(old);
            band.AddMember(child);
            var youngId = w.IdOf(young);
            var oldId = w.IdOf(old);

            w.AdvanceTo(w.BirthdayOf(young, 31L).Plus(-1L));
            var youngBefore = w.People.IsAlive(young);
            w.AdvanceYears(1L);

            var deaths = w.Published(DomainEventKind.PersonDied);

            Assert.Multiple(() =>
            {
                Assert.That(youngBefore, Is.True, "nothing between birthdays");
                Assert.That(w.People.IsAlive(young), Is.False);
                Assert.That(w.People.IsAlive(old), Is.False);
                Assert.That(w.People.IsAlive(child), Is.True, "the child's rate is zero");
                Assert.That(deaths, Has.Count.EqualTo(2));
                Assert.That(ReasonFor(deaths, youngId), Is.EqualTo(ReasonCode.Illness), "before the soft lifespan");
                Assert.That(ReasonFor(deaths, oldId), Is.EqualTo(ReasonCode.OldAge), "past it");
                Assert.That(band.Members, Is.EqualTo(new[] { child }), "the cascade ran");
            });
        }

        [Test]
        public void The_first_birthday_rolls_the_first_year_of_life()
        {
            // A table on which infancy ends at one and only infants die:
            // the roll at the first birthday covers the year just lived,
            // which was infancy, and it is certain. Rolling the rate for
            // the year ahead instead would skip year zero entirely.
            var settings = new DemographicSettings
            {
                ChildAtYears = 1L,
                AdolescentAtYears = 2L,
                AdultAtYears = 3L,
                ElderAtYears = 4L,
                FertileFromYears = 3L,
                FertileUntilYears = 4L,
                InfantMortalityPerMille = 1000,
                ChildMortalityPerMille = 0,
                AdolescentMortalityPerMille = 0,
                AdultMortalityPerMille = 0,
                ElderMortalityPerMille = 0,
                ConceptionPerMille = 0,
            };
            var w = new DemographicWorld(settings, 1UL);
            var band = w.NewBand();
            var newborn = w.NewPerson(0L, Sex.Female);
            var toddler = w.NewPerson(1L, Sex.Female);
            band.AddMember(newborn);
            band.AddMember(toddler);

            var queried = w.Mortality.YearlyChancePerMille(newborn);
            w.AdvanceYears(1L);

            Assert.Multiple(() =>
            {
                Assert.That(queried, Is.EqualTo(1000), "the year ahead of a newborn is infancy");
                Assert.That(w.People.IsAlive(newborn), Is.False, "died at the first birthday, of the first year");
                Assert.That(w.People.IsAlive(toddler), Is.True, "the year a one-year-old lived was childhood");
            });
        }

        [Test]
        public void The_query_reports_certainty_when_the_next_birthday_is_the_maximum()
        {
            var w = new DemographicWorld(Immortal(), 1UL);
            var s = w.Settings;
            var lastYear = w.NewPerson(s.MaxLifespanYears - 1L, Sex.Male);
            var yearBefore = w.NewPerson(s.MaxLifespanYears - 2L, Sex.Male);

            Assert.Multiple(() =>
            {
                Assert.That(w.Mortality.YearlyChancePerMille(lastYear), Is.EqualTo(1000));
                Assert.That(w.Mortality.YearlyChancePerMille(yearBefore), Is.LessThan(1000));
            });
        }

        [Test]
        public void Nobody_outlives_the_maximum_lifespan()
        {
            var settings = new DemographicSettings
            {
                InfantMortalityPerMille = 0,
                ChildMortalityPerMille = 0,
                AdolescentMortalityPerMille = 0,
                AdultMortalityPerMille = 0,
                ElderMortalityPerMille = 0,
                SoftLifespanYears = 90L,
                MaxLifespanYears = 91L,
                ConceptionPerMille = 0,
            };
            var w = new DemographicWorld(settings, 1UL);
            var band = w.NewBand();
            var person = w.NewPerson(89L, Sex.Male);
            band.AddMember(person);

            // The ninetieth birthday rolls the soft-lifespan rate, which is
            // the elder rate - zero. The ninety-first is certain.
            w.AdvanceTo(w.BirthdayOf(person, 90L));
            var atNinety = w.People.IsAlive(person);
            w.AdvanceTo(w.BirthdayOf(person, 91L));

            Assert.Multiple(() =>
            {
                Assert.That(atNinety, Is.True);
                Assert.That(w.People.IsAlive(person), Is.False);
                Assert.That(w.Published(DomainEventKind.PersonDied)[0].Reasons.Contains(ReasonCode.OldAge), Is.True);
            });
        }

        [Test]
        public void Frailty_and_hunger_multiply_the_yearly_chance_and_certainty_caps_it()
        {
            var settings = new DemographicSettings
            {
                AdultMortalityPerMille = 100,
                HealthFloor = 50,
                FrailtyMultiplier = 3,
                HungerMultiplier = 4,
                ConceptionPerMille = 0,
            };
            var w = new DemographicWorld(settings, 1UL);
            var well = w.NewPerson(30L, Sex.Male);
            var frail = w.NewPerson(30L, Sex.Male);
            var hungry = w.NewPerson(30L, Sex.Male);
            var both = w.NewPerson(30L, Sex.Male);
            var atFloor = w.NewPerson(30L, Sex.Male);
            var gone = w.NewPerson(30L, Sex.Male);
            w.People.SetHealth(frail, 49);
            w.People.SetHealth(both, 0);
            w.People.SetHealth(atFloor, 50);
            w.People.SetHealth(gone, 0);

            // Past the grace period for those who have not eaten since tick
            // zero; the others are fed now.
            w.Advance(Hunger.StarvationGrace + 1L);
            w.People.SetLastFedAt(well, w.Clock.Now);
            w.People.SetLastFedAt(frail, w.Clock.Now);
            w.People.SetLastFedAt(atFloor, w.Clock.Now);
            w.People.SetLastFedAt(gone, w.Clock.Now);

            Assert.Multiple(() =>
            {
                Assert.That(w.Mortality.YearlyChancePerMille(well), Is.EqualTo(100));
                Assert.That(w.Mortality.YearlyChancePerMille(atFloor), Is.EqualTo(100), "at the floor is not below it");
                Assert.That(w.Mortality.YearlyChancePerMille(frail), Is.EqualTo(300));
                Assert.That(w.Mortality.YearlyChancePerMille(hungry), Is.EqualTo(400));
                Assert.That(w.Mortality.YearlyChancePerMille(both), Is.EqualTo(1000), "1200 capped");
                Assert.That(w.Mortality.YearlyChancePerMille(gone), Is.EqualTo(1000), "zero health is certain, not merely frail");
            });
        }

        [Test]
        public void A_fatal_meal_on_a_birthday_is_still_a_starvation_death()
        {
            // Meals, checks and birthdays all fall on day boundaries, so the
            // meal that takes someone to zero can share an instant with their
            // yearly check - and the check sorts first. Zero health is
            // certain death whichever wake-up finds it, and it reads as
            // starvation, not as the illness the roll would otherwise name.
            var settings = new DemographicSettings
            {
                AdultMortalityPerMille = 1000,
                ConceptionPerMille = 0,
            };
            var w = new DemographicWorld(settings, 1UL);
            var band = w.NewStarvingBand();
            var person = w.NewPerson(30L, Sex.Female);
            band.AddMember(person);
            var id = w.IdOf(person);

            // Damage starts at the day-3 meal; the birthday is day 120, so
            // 118 meals of damage land exactly on it.
            var birthday = w.BirthdayOf(person, 31L);
            Assert.That(birthday, Is.EqualTo(SimulationTime.FromDays(120L)));
            w.People.SetHealth(person, (short)(118 * Hunger.StarvationDamagePerMeal));

            w.AdvanceTo(birthday.Plus(-1L));
            var justBefore = w.People.GetHealth(person);
            w.Advance(1L);

            var deaths = w.Published(DomainEventKind.PersonDied);

            Assert.Multiple(() =>
            {
                Assert.That(justBefore, Is.EqualTo(Hunger.StarvationDamagePerMeal));
                Assert.That(deaths, Has.Count.EqualTo(1));
                Assert.That(deaths[0].Time, Is.EqualTo(birthday));
                Assert.That(ReasonFor(deaths, id), Is.EqualTo(ReasonCode.Starved), "not Illness");
            });
        }

        [Test]
        public void Starving_to_zero_health_is_death_at_that_meal_with_the_reason_starved()
        {
            var w = new DemographicWorld(Immortal(), 1UL);
            var band = w.NewStarvingBand();
            var person = w.NewPerson(30L, Sex.Female);
            band.AddMember(person);

            // Meals on days 1 and 2 are within grace; day 3 onward costs
            // health. A hundred health at ten a meal is ten meals: day 12.
            var fatal = SimulationTime.FromDays(12L);
            w.AdvanceTo(fatal.Plus(-1L));
            var justBefore = w.People.GetHealth(person);
            w.AdvanceTo(fatal);

            var deaths = w.Published(DomainEventKind.PersonDied);

            Assert.Multiple(() =>
            {
                Assert.That(justBefore, Is.EqualTo(Hunger.StarvationDamagePerMeal));
                Assert.That(w.People.IsAlive(person), Is.False);
                Assert.That(deaths, Has.Count.EqualTo(1));
                Assert.That(deaths[0].Time, Is.EqualTo(fatal), "the day of the meal, not a later check");
                Assert.That(deaths[0].Reasons.Contains(ReasonCode.Starved), Is.True);
                Assert.That(band.Members, Is.Empty);
            });
        }

        [Test]
        public void A_starvation_crossing_for_someone_with_health_left_kills_nobody()
        {
            var w = new DemographicWorld(Immortal(), 1UL);
            var band = w.NewBand();
            var person = w.NewPerson(30L, Sex.Female);
            band.AddMember(person);

            w.Clock.Schedule(
                w.Clock.Now.Plus(1L), Mortality.Phase, ScheduledEventKind.StarvationCritical, w.IdOf(person), EntityId.None);
            w.Advance(1L);

            Assert.That(w.People.IsAlive(person), Is.True);
        }

        [Test]
        public void A_starvation_crossing_for_someone_below_zero_kills_them()
        {
            // Hunger never takes health below zero, but the bulk span can;
            // what a value there means is decided here, and it means dead.
            var w = new DemographicWorld(Immortal(), 1UL);
            var band = w.NewBand();
            var person = w.NewPerson(30L, Sex.Female);
            band.AddMember(person);
            w.People.SetHealth(person, -7);

            w.Clock.Schedule(
                w.Clock.Now.Plus(1L), Mortality.Phase, ScheduledEventKind.StarvationCritical, w.IdOf(person), EntityId.None);
            w.Advance(1L);

            Assert.That(w.People.IsAlive(person), Is.False);
        }

        [Test]
        public void A_starvation_crossing_for_the_dead_is_ignored()
        {
            var w = new DemographicWorld(Immortal(), 1UL);
            var band = w.NewBand();
            var person = w.NewPerson(30L, Sex.Female);
            band.AddMember(person);
            var id = w.IdOf(person);

            w.Clock.Schedule(w.Clock.Now.Plus(1L), Mortality.Phase, ScheduledEventKind.StarvationCritical, id, EntityId.None);
            w.Deaths.Die(person, Reasons.None);

            Assert.Multiple(() =>
            {
                Assert.That(() => w.Advance(1L), Throws.Nothing);
                Assert.That(w.Published(DomainEventKind.PersonDied), Has.Count.EqualTo(1));
            });
        }

        [Test]
        public void A_founder_the_clock_has_outrun_is_checked_on_the_next_birthday_and_dies_of_old_age()
        {
            // Three quarters of the way to the end of time, a founder born at
            // the earliest accepted tick: the distance no longer fits, the
            // age saturates, and the next check must still be derived from
            // the calendar rather than from a birth plus an age that would
            // wrap when multiplied back up. They are past every lifespan,
            // so that check is certain.
            var w = new DemographicWorld(Immortal(), 1UL);
            var start = new SimulationTime(long.MaxValue / 4L * 3L);
            w.Clock.AdvanceTo(start, w.Router);
            var band = w.NewBand();
            var founder = w.NewPersonBornAt(PersonStore.EarliestBornTick, Sex.Female);
            band.AddMember(founder);
            var id = w.IdOf(founder);

            w.AdvanceYears(1L);

            var deaths = w.Published(DomainEventKind.PersonDied);

            Assert.Multiple(() =>
            {
                Assert.That(deaths, Has.Count.EqualTo(1));
                Assert.That(deaths[0].PrimaryEntity, Is.EqualTo(id));
                Assert.That(deaths[0].Reasons.Contains(ReasonCode.OldAge), Is.True);
                Assert.That(deaths[0].Time, Is.GreaterThan(start).And.LessThanOrEqualTo(start.Plus(SimulationTime.TicksPerYear)));
                Assert.That(
                    deaths[0].Time.Ticks % SimulationTime.TicksPerYear,
                    Is.EqualTo(FloorMod(PersonStore.EarliestBornTick, SimulationTime.TicksPerYear)),
                    "on the birthday, not merely within the year");
            });
        }

        [Test]
        public void A_check_that_comes_due_for_the_dead_is_ignored()
        {
            var w = new DemographicWorld(Immortal(), 1UL);
            var band = w.NewBand();
            var person = w.NewPerson(30L, Sex.Female);
            band.AddMember(person);
            w.Deaths.Die(person, Reasons.None);

            Assert.Multiple(() =>
            {
                Assert.That(() => w.AdvanceYears(2L), Throws.Nothing);
                Assert.That(w.Published(DomainEventKind.PersonDied), Has.Count.EqualTo(1));
            });
        }

        [Test]
        public void The_same_seed_produces_the_same_deaths_and_another_seed_does_not()
        {
            var first = Timeline(seed: 7UL);
            var again = Timeline(seed: 7UL);
            var other = Timeline(seed: 8UL);

            Assert.Multiple(() =>
            {
                Assert.That(first, Is.Not.Empty, "somebody died in fifty years");
                Assert.That(first.Count, Is.LessThan(40), "and not everybody at once");
                Assert.That(again, Is.EqualTo(first));
                Assert.That(other, Is.Not.EqualTo(first));
            });
        }

        [Test]
        public void Handle_refuses_a_kind_it_does_not_own_and_a_clock_that_is_not_its_own()
        {
            var w = new DemographicWorld(Immortal(), 1UL);
            var person = w.NewPerson(1L, Sex.Male);
            var foreign = new ScheduledEvent(
                new EventId(1UL), SimulationTime.Zero, Mortality.Phase, ScheduledEventKind.MealDue, w.IdOf(person), EntityId.None);
            var owned = new ScheduledEvent(
                new EventId(2UL), SimulationTime.Zero, Mortality.Phase, ScheduledEventKind.MortalityCheck, w.IdOf(person), EntityId.None);

            Assert.Multiple(() =>
            {
                Assert.That(() => w.Mortality.Handle(foreign, w.Clock), Throws.InvalidOperationException);
                Assert.That(
                    () => w.Mortality.Handle(owned, new SimulationClock(new IdAllocator())),
                    Throws.InvalidOperationException);
            });
        }

        // The tick of every death over fifty years among forty adults on the
        // default table, in order.
        private static List<long> Timeline(ulong seed)
        {
            var w = new DemographicWorld(new DemographicSettings { ConceptionPerMille = 0 }, seed);
            var band = w.NewBand();

            for (var i = 0; i < 40; i++)
            {
                band.AddMember(w.NewPerson(20L + i, i % 2 == 0 ? Sex.Female : Sex.Male));
            }

            w.AdvanceYears(50L);

            var ticks = new List<long>();

            foreach (var death in w.Published(DomainEventKind.PersonDied))
            {
                ticks.Add(death.Time.Ticks);
            }

            return ticks;
        }

        // The remainder in [0, modulus) whatever the sign of the value, as
        // a birthday is: C# keeps the dividend's sign.
        private static long FloorMod(long value, long modulus) => ((value % modulus) + modulus) % modulus;

        private static ReasonCode ReasonFor(List<DomainEvent> deaths, EntityId person)
        {
            foreach (var death in deaths)
            {
                if (death.PrimaryEntity == person)
                {
                    return death.Reasons[0];
                }
            }

            throw new InvalidOperationException(person + " did not die.");
        }
    }
}
