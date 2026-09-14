using System;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Events;
using KingdomWatch.Core.Lifecycle;
using NUnit.Framework;

namespace KingdomWatch.Core.Tests.Lifecycle
{
    [TestFixture]
    public sealed class AgingTests
    {
        // A table nobody dies to, so a life can be walked end to end.
        private static readonly DemographicSettings Immortal = new DemographicSettings
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
                Assert.That(() => new Aging(null!, w.People, w.Settings), Throws.ArgumentNullException);
                Assert.That(() => new Aging(w.Bus, null!, w.Settings), Throws.ArgumentNullException);
                Assert.That(() => new Aging(w.Bus, w.People, null!), Throws.ArgumentNullException);
                Assert.That(
                    () => new Aging(w.Bus, w.People, new DemographicSettings { AdultAtYears = 2L }),
                    Throws.TypeOf<ArgumentOutOfRangeException>(),
                    "a boundary out of order");
            });
        }

        [Test]
        public void A_newborn_crosses_each_boundary_on_the_exact_birthday()
        {
            var w = new DemographicWorld(Immortal, 1UL);
            var s = w.Settings;
            var person = w.NewPerson(0L, Sex.Female);

            var before = new AgeStage[4];
            var after = new AgeStage[4];
            var boundaries = new[] { s.ChildAtYears, s.AdolescentAtYears, s.AdultAtYears, s.ElderAtYears };

            for (var i = 0; i < boundaries.Length; i++)
            {
                w.AdvanceTo(w.BirthdayOf(person, boundaries[i]).Plus(-1L));
                before[i] = w.People.GetAgeStage(person);
                w.Advance(1L);
                after[i] = w.People.GetAgeStage(person);
            }

            Assert.Multiple(() =>
            {
                Assert.That(before, Is.EqualTo(new[] { AgeStage.Infant, AgeStage.Child, AgeStage.Adolescent, AgeStage.Adult }));
                Assert.That(after, Is.EqualTo(new[] { AgeStage.Child, AgeStage.Adolescent, AgeStage.Adult, AgeStage.Elder }));
                Assert.That(w.People.GetAgeYears(person, w.Clock.Now), Is.EqualTo(s.ElderAtYears));
            });
        }

        [Test]
        public void An_elder_has_no_boundary_left_to_book()
        {
            var w = new DemographicWorld(Immortal, 1UL);
            var elder = w.NewPerson(w.Settings.ElderAtYears + 10L, Sex.Male);

            // The one wake-up booked for an elder is mortality's yearly check.
            Assert.Multiple(() =>
            {
                Assert.That(w.Clock.ScheduledCount, Is.EqualTo(1));
                Assert.That(w.Clock.TryPeekNext(out var next), Is.True);
                Assert.That(next.Kind, Is.EqualTo(ScheduledEventKind.MortalityCheck));
                Assert.That(next.PrimaryEntity, Is.EqualTo(w.IdOf(elder)));
            });
        }

        [Test]
        public void A_founder_announced_mid_stage_crosses_the_next_boundary_on_their_birthday_not_a_stage_from_now()
        {
            // Born eighteen months before the world began: fourteen and a
            // half at tick zero, an adult at exactly sixteen - a year and a
            // half in, not sixteen years in.
            var w = new DemographicWorld(Immortal, 1UL);
            var half = SimulationTime.TicksPerYear / 2L;
            var bornTick = -(14L * SimulationTime.TicksPerYear + half);
            var person = w.NewPersonBornAt(bornTick, Sex.Female);
            var sixteenth = new SimulationTime(bornTick + w.Settings.AdultAtYears * SimulationTime.TicksPerYear);

            w.AdvanceTo(sixteenth.Plus(-1L));
            var justBefore = w.People.GetAgeStage(person);
            w.Advance(1L);

            Assert.Multiple(() =>
            {
                Assert.That(justBefore, Is.EqualTo(AgeStage.Adolescent));
                Assert.That(w.People.GetAgeStage(person), Is.EqualTo(AgeStage.Adult));
                Assert.That(w.Clock.Now, Is.EqualTo(sixteenth));
                Assert.That(sixteenth.Ticks, Is.EqualTo(SimulationTime.TicksPerYear + half), "a year and a half in");
            });
        }

        [Test]
        public void A_stage_seeded_wrong_for_the_age_is_corrected_at_the_next_boundary()
        {
            // Thirty and seeded as a child: wrong until the elder boundary,
            // when the age is read again and wins.
            var w = new DemographicWorld(Immortal, 1UL);
            var person = w.NewPerson(30L, Sex.Male, AgeStage.Child);

            w.AdvanceTo(w.BirthdayOf(person, w.Settings.ElderAtYears).Plus(-1L));
            var stillWrong = w.People.GetAgeStage(person);
            w.Advance(1L);

            Assert.Multiple(() =>
            {
                Assert.That(stillWrong, Is.EqualTo(AgeStage.Child), "nothing polls the age between boundaries");
                Assert.That(w.People.GetAgeStage(person), Is.EqualTo(AgeStage.Elder), "not Adolescent: the age wins, not the next step");
            });
        }

        [Test]
        public void A_boundary_that_comes_due_for_the_dead_changes_nothing()
        {
            var w = new DemographicWorld(Immortal, 1UL);
            var band = w.NewBand();
            var child = w.NewPerson(2L, Sex.Female);
            band.AddMember(child);
            w.Deaths.Die(child, Reasons.None);

            Assert.Multiple(() =>
            {
                Assert.That(() => w.AdvanceYears(2L), Throws.Nothing);
                Assert.That(w.People.IsAlive(child), Is.False);
                Assert.That(w.People.Count, Is.Zero);
            });
        }

        [Test]
        public void A_person_announced_within_a_year_of_the_end_of_time_books_nothing()
        {
            // Their next birthday is past the last representable instant, so
            // neither the boundary nor the yearly check can be booked; the
            // world reaches the end of time rather than throwing short of it.
            var w = new DemographicWorld(Immortal, 1UL);
            w.Clock.AdvanceTo(new SimulationTime(long.MaxValue - 1L), w.Router);

            Assert.Multiple(() =>
            {
                Assert.That(() => w.NewPerson(30L, Sex.Female), Throws.Nothing);
                Assert.That(w.Clock.ScheduledCount, Is.Zero, "no boundary, and no mortality check either");
                Assert.That(() => w.Advance(1L), Throws.Nothing);
            });
        }

        [Test]
        public void A_birth_announced_for_nobody_is_refused()
        {
            var w = new DemographicWorld(Immortal, 1UL);
            var ghost = w.Base.Ids.Next(EntityKind.Person);

            Assert.That(
                () => w.Bus.Publish(DomainEventKind.PersonBorn, ghost, EntityId.None),
                Throws.InvalidOperationException);
        }

        [Test]
        public void Handle_refuses_a_kind_it_does_not_own_and_a_clock_that_is_not_its_own()
        {
            var w = new DemographicWorld(Immortal, 1UL);
            var person = w.NewPerson(1L, Sex.Male);
            var foreign = new ScheduledEvent(
                new EventId(1UL), SimulationTime.Zero, Aging.Phase, ScheduledEventKind.MealDue, w.IdOf(person), EntityId.None);
            var owned = new ScheduledEvent(
                new EventId(2UL), SimulationTime.Zero, Aging.Phase, ScheduledEventKind.AgeStageDue, w.IdOf(person), EntityId.None);

            Assert.Multiple(() =>
            {
                Assert.That(() => w.Aging.Handle(foreign, w.Clock), Throws.InvalidOperationException);
                Assert.That(
                    () => w.Aging.Handle(owned, new SimulationClock(new IdAllocator())),
                    Throws.InvalidOperationException);
            });
        }

        [Test]
        public void Events_of_other_kinds_are_ignored()
        {
            var w = new DemographicWorld(Immortal, 1UL);
            var before = w.Clock.ScheduledCount;

            w.Bus.Publish(DomainEventKind.DivineActWitnessed, EntityId.None, EntityId.None);

            Assert.That(w.Clock.ScheduledCount, Is.EqualTo(before));
        }
    }
}
