using System;
using System.Collections.Generic;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Events;
using KingdomWatch.Core.Lifecycle;
using NUnit.Framework;

namespace KingdomWatch.Core.Tests.Lifecycle
{
    [TestFixture]
    public sealed class FertilityTests
    {
        // Nobody dies, and every check conceives - so what happens is
        // decided by eligibility alone. Gestation is not a multiple of the
        // check interval, so a birth and a check never share an instant and
        // the tests can look at the moment of delivery on its own.
        private static DemographicSettings Certain() => new DemographicSettings
        {
            GestationTicks = 95L * SimulationTime.TicksPerDay,
            InfantMortalityPerMille = 0,
            ChildMortalityPerMille = 0,
            AdolescentMortalityPerMille = 0,
            AdultMortalityPerMille = 0,
            ElderMortalityPerMille = 0,
            SoftLifespanYears = 1_000L,
            MaxLifespanYears = 2_000L,
            ConceptionPerMille = 1000,
        };

        private static long Check => Certain().BirthCheckTicks;

        private static long Gestation => Certain().GestationTicks;

        [Test]
        public void Construction_refuses_a_missing_collaborator_and_a_bad_table()
        {
            var w = new DemographicWorld();

            Assert.Multiple(() =>
            {
                Assert.That(() => new Fertility(null!, w.People, w.Genealogy, w.Partnerships, w.Households, w.Rng, w.Settings), Throws.ArgumentNullException);
                Assert.That(() => new Fertility(w.Bus, null!, w.Genealogy, w.Partnerships, w.Households, w.Rng, w.Settings), Throws.ArgumentNullException);
                Assert.That(() => new Fertility(w.Bus, w.People, null!, w.Partnerships, w.Households, w.Rng, w.Settings), Throws.ArgumentNullException);
                Assert.That(() => new Fertility(w.Bus, w.People, w.Genealogy, null!, w.Households, w.Rng, w.Settings), Throws.ArgumentNullException);
                Assert.That(() => new Fertility(w.Bus, w.People, w.Genealogy, w.Partnerships, null!, w.Rng, w.Settings), Throws.ArgumentNullException);
                Assert.That(() => new Fertility(w.Bus, w.People, w.Genealogy, w.Partnerships, w.Households, null!, w.Settings), Throws.ArgumentNullException);
                Assert.That(() => new Fertility(w.Bus, w.People, w.Genealogy, w.Partnerships, w.Households, w.Rng, null!), Throws.ArgumentNullException);
                Assert.That(
                    () => new Fertility(w.Bus, w.People, w.Genealogy, w.Partnerships, w.Households, w.Rng, new DemographicSettings { GestationTicks = 0L }),
                    Throws.TypeOf<ArgumentOutOfRangeException>());
            });
        }

        [Test]
        public void Tracking_refuses_null_and_a_group_already_tracked()
        {
            var w = new DemographicWorld();
            var band = w.NewBand();

            Assert.Multiple(() =>
            {
                Assert.That(() => w.Fertility.Track(null!), Throws.ArgumentNullException);
                Assert.That(() => w.Fertility.Track(band), Throws.InvalidOperationException);
                Assert.That(w.Fertility.TrackedCount, Is.EqualTo(1));
            });
        }

        [Test]
        public void A_household_forming_books_its_first_check_one_interval_out()
        {
            var w = new DemographicWorld(Certain(), 1UL);
            var before = w.Clock.ScheduledCount;

            var household = w.NewCouple(out _, out _);

            // Two people announced book four wake-ups between them; the
            // household books one more, the last in and the earliest due.
            Assert.Multiple(() =>
            {
                Assert.That(w.Clock.ScheduledCount, Is.EqualTo(before + 5));
                Assert.That(w.Clock.TryPeekNext(out var next), Is.True);
                Assert.That(next.Kind, Is.EqualTo(ScheduledEventKind.BirthCheck));
                Assert.That(next.Time, Is.EqualTo(new SimulationTime(Check)));
                Assert.That(next.Phase, Is.EqualTo(Fertility.Phase));
                Assert.That(next.PrimaryEntity, Is.EqualTo(household.Id));
            });
        }

        [Test]
        public void A_couple_conceives_at_the_first_check_and_the_child_arrives_at_term()
        {
            var w = new DemographicWorld(Certain(), 1UL);
            var band = w.NewBand();
            var household = w.NewCouple(out var wife, out var husband);
            band.AddMember(wife);
            band.AddMember(husband);
            w.People.SetPosition(wife, new WorldPosition(4, 9));

            w.Advance(Check);
            var pregnantAfterCheck = w.Fertility.IsPregnant(wife);
            var due = w.People.GetPregnancyDue(wife);
            w.Advance(Gestation - 1L);
            var countBeforeTerm = w.People.Count;
            w.Advance(1L);

            var births = w.Published(DomainEventKind.PersonBorn);
            var child = household.Members[household.Members.Count - 1];
            var childId = w.IdOf(child);
            var parents = w.Genealogy.Parents(childId);

            Assert.Multiple(() =>
            {
                Assert.That(pregnantAfterCheck, Is.True);
                Assert.That(due, Is.Not.EqualTo(EventId.None));
                Assert.That(countBeforeTerm, Is.EqualTo(2), "nothing until term");
                Assert.That(w.People.Count, Is.EqualTo(3));
                Assert.That(w.Fertility.IsPregnant(wife), Is.False, "delivered");
                Assert.That(w.People.GetPregnancyDue(wife), Is.EqualTo(EventId.None));
                Assert.That(household.Members, Has.Count.EqualTo(3));
                Assert.That(band.Members, Does.Contain(child));
                Assert.That(parents.Mother, Is.EqualTo(w.IdOf(wife)));
                Assert.That(parents.Father, Is.EqualTo(w.IdOf(husband)));
                Assert.That(w.People.GetAgeStage(child), Is.EqualTo(AgeStage.Infant));
                Assert.That(w.People.GetBornTick(child), Is.EqualTo(w.Clock.Now.Ticks));
                Assert.That(w.People.GetLastFedAt(child), Is.EqualTo(w.Clock.Now));
                Assert.That(w.People.GetHealth(child), Is.EqualTo(w.Settings.NewbornHealth));
                Assert.That(w.People.GetPosition(child), Is.EqualTo(new WorldPosition(4, 9)), "born where the mother is");
                Assert.That(w.People.GetSex(child), Is.EqualTo(Sex.Female).Or.EqualTo(Sex.Male));
                Assert.That(births, Has.Count.EqualTo(3), "two founders and the child");
                Assert.That(births[2].PrimaryEntity, Is.EqualTo(childId));
                Assert.That(births[2].SecondaryEntity, Is.EqualTo(w.IdOf(wife)));
                Assert.That(births[2].Time, Is.EqualTo(new SimulationTime(Check + Gestation)));
            });
        }

        [Test]
        public void The_child_is_announced_last_so_ageing_and_mortality_book_against_a_person_who_exists()
        {
            var w = new DemographicWorld(Certain(), 1UL);
            var band = w.NewBand();
            var household = w.NewCouple(out var wife, out var husband);
            band.AddMember(wife);
            band.AddMember(husband);

            w.Advance(Check + Gestation);
            var child = household.Members[2];

            w.AdvanceTo(w.BirthdayOf(child, w.Settings.ChildAtYears));

            Assert.That(w.People.GetAgeStage(child), Is.EqualTo(AgeStage.Child), "the boundary was booked at birth");
        }

        [Test]
        public void A_pregnant_woman_does_not_conceive_again_until_delivered_and_recovered()
        {
            var w = new DemographicWorld(Certain(), 1UL);
            var band = w.NewBand();
            var household = w.NewCouple(out var wife, out var husband);
            band.AddMember(wife);
            band.AddMember(husband);
            var postpartum = w.Settings.PostpartumTicks;

            w.Advance(Check);
            var due = w.People.GetPregnancyDue(wife);
            w.Advance(Gestation - 1L);
            var stillTheSamePregnancy = w.People.GetPregnancyDue(wife) == due;
            w.Advance(1L);
            var delivered = w.Clock.Now;

            // Every check between the delivery and the end of the postpartum
            // period finds her ineligible; the first one after it does not.
            w.AdvanceTo(delivered.Plus(postpartum - 1L));
            var duringPostpartum = w.Fertility.IsPregnant(wife);
            w.Advance(Check);

            Assert.Multiple(() =>
            {
                Assert.That(stillTheSamePregnancy, Is.True, "checks during the pregnancy changed nothing");
                Assert.That(w.People.Count, Is.EqualTo(3), "one child");
                Assert.That(duringPostpartum, Is.False);
                Assert.That(w.Fertility.IsPregnant(wife), Is.True, "and, a check after recovering, the next");
                Assert.That(household.Members, Has.Count.EqualTo(3));
            });
        }

        [Test]
        public void A_delivery_and_a_check_on_the_same_instant_do_not_conceive_the_day_the_child_is_born()
        {
            // The default gestation is a multiple of the check interval, so
            // this is every delivery on the default table. The delivery runs
            // first - the mother's id sorts before the household's - and the
            // check then finds her not pregnant. The postpartum gate is what
            // stops it; with the gate at zero, the coincidence shows.
            var gestation = DemographicSettings.Default.GestationTicks;
            var check = DemographicSettings.Default.BirthCheckTicks;
            Assert.That(gestation % check, Is.Zero, "the coincidence this test is about");

            var gated = new DemographicWorld(CoincidingTable(DemographicSettings.Default.PostpartumTicks), 1UL);
            var ungated = new DemographicWorld(CoincidingTable(postpartum: 0L), 1UL);
            var worlds = new[] { gated, ungated };
            var wives = new PersonHandle[2];

            for (var i = 0; i < worlds.Length; i++)
            {
                var band = worlds[i].NewBand();
                worlds[i].NewCouple(out wives[i], out var husband);
                band.AddMember(wives[i]);
                band.AddMember(husband);
                worlds[i].Advance(check + gestation);
            }

            Assert.Multiple(() =>
            {
                Assert.That(gated.People.Count, Is.EqualTo(3));
                Assert.That(gated.Fertility.IsPregnant(wives[0]), Is.False, "delivered today, not pregnant again");
                Assert.That(ungated.People.Count, Is.EqualTo(3));
                Assert.That(ungated.Fertility.IsPregnant(wives[1]), Is.True, "without the gate, the same instant conceives");
            });
        }

        [Test]
        public void The_postpartum_period_is_read_off_the_youngest_living_child()
        {
            var w = new DemographicWorld(Certain(), 1UL);
            var band = w.NewBand();
            var household = w.NewCouple(out var wife, out var husband);
            band.AddMember(wife);
            band.AddMember(husband);

            w.Advance(Check + Gestation);
            var child = household.Members[2];
            w.Deaths.Die(child, Reasons.None);
            w.Advance(Check);

            Assert.That(w.Fertility.IsPregnant(wife), Is.True, "no living child dates a birth, so nothing gates her");
        }

        [Test]
        public void A_partner_housed_elsewhere_or_of_the_same_sex_does_not_count()
        {
            var w = new DemographicWorld(Certain(), 1UL);
            var band = w.NewBand();

            // Married, then he moves out to another household.
            w.NewCouple(out var apart, out var husband);
            var elsewhere = w.Households.Form();
            w.Households.Leave(husband);
            w.Households.Join(elsewhere, husband);

            // Two women partnered directly in the store - family formation
            // refuses the pairing, but the eligibility check must not rely
            // on that.
            var one = w.NewPerson(25L, Sex.Female);
            var other = w.NewPerson(25L, Sex.Female);
            var together = w.Households.Form();
            w.Households.Join(together, one);
            w.Households.Join(together, other);
            var formed = w.Bus.Publish(DomainEventKind.MarriageFormed, w.IdOf(one), w.IdOf(other));
            w.Partnerships.Form(w.IdOf(one), w.IdOf(other), formed, w.Clock.Now);

            foreach (var person in new[] { apart, husband, one, other })
            {
                band.AddMember(person);
            }

            w.Advance(Check);

            Assert.Multiple(() =>
            {
                Assert.That(w.Fertility.IsPregnant(apart), Is.False);
                Assert.That(w.Fertility.IsPregnant(one), Is.False);
                Assert.That(w.Fertility.IsPregnant(other), Is.False);
            });
        }

        [Test]
        public void A_mother_in_no_household_at_term_bears_a_child_in_none()
        {
            var w = new DemographicWorld(Certain(), 1UL);
            var band = w.NewBand();
            var household = w.NewCouple(out var wife, out var husband);
            band.AddMember(wife);
            band.AddMember(husband);

            w.Advance(Check);
            w.Households.Leave(wife);
            w.Advance(Gestation);

            var child = band.Members[band.Members.Count - 1];

            Assert.Multiple(() =>
            {
                Assert.That(w.People.Count, Is.EqualTo(3));
                Assert.That(w.People.GetHousehold(child), Is.EqualTo(EntityId.None));
                Assert.That(household.Members, Is.EqualTo(new[] { husband }));
                Assert.That(w.Genealogy.Parents(w.IdOf(child)).Mother, Is.EqualTo(w.IdOf(wife)));
            });
        }

        [Test]
        public void A_conception_whose_term_lies_past_the_end_of_time_does_not_happen()
        {
            // The check stream's own guard stops one interval short of the
            // end; gestation is longer than an interval, so a check can still
            // land within a gestation of the end and must not book a term the
            // clock cannot represent. The world is empty when the clock
            // jumps, so nothing has to be dispatched across the whole of time.
            var w = new DemographicWorld(Certain(), 1UL);
            w.Clock.AdvanceTo(new SimulationTime(long.MaxValue - Gestation + 1L - Check), w.Router);
            var band = w.NewBand();
            w.NewCouple(out var wife, out var husband);
            band.AddMember(wife);
            band.AddMember(husband);

            Assert.Multiple(() =>
            {
                Assert.That(() => w.Advance(Check), Throws.Nothing);
                Assert.That(w.Fertility.IsPregnant(wife), Is.False);
            });
        }

        [Test]
        public void Nobody_conceives_without_a_living_partner_in_the_same_household()
        {
            var w = new DemographicWorld(Certain(), 1UL);
            var band = w.NewBand();

            // A woman alone in a household; a couple whose husband dies
            // before the first check; a widow's household after that.
            var alone = w.NewPerson(25L, Sex.Female);
            var lonely = w.Households.Form();
            w.Households.Join(lonely, alone);
            band.AddMember(alone);

            w.NewCouple(out var widow, out var husband);
            band.AddMember(widow);
            band.AddMember(husband);
            w.Deaths.Die(husband, Reasons.None);

            w.Advance(Check);

            Assert.Multiple(() =>
            {
                Assert.That(w.Fertility.IsPregnant(alone), Is.False);
                Assert.That(w.Fertility.IsPregnant(widow), Is.False);
            });
        }

        [Test]
        public void Nobody_conceives_outside_the_fertile_window_or_while_frail_or_unfed()
        {
            var w = new DemographicWorld(Certain(), 1UL);
            var band = w.NewBand();
            var s = w.Settings;

            var tooYoung = w.NewPerson(s.FertileFromYears - 1L, Sex.Female, AgeStage.Adult);
            var tooOld = w.NewPerson(s.FertileUntilYears, Sex.Female);
            var frail = w.NewPerson(30L, Sex.Female);
            var fine = w.NewPerson(30L, Sex.Female);

            foreach (var woman in new[] { tooYoung, tooOld, frail, fine })
            {
                var man = w.NewPerson(30L, Sex.Male);
                w.Family.Partner(woman, man, Reasons.None);
                band.AddMember(woman);
                band.AddMember(man);
            }

            w.People.SetHealth(frail, (short)(s.HealthFloor - 1));

            // The band eats daily, and the meal comes before the check on the
            // same day, so an unfed woman has to be in a band with nothing to
            // eat - and with health enough to be well above the floor after
            // eight missed meals, so that hunger is the only gate she fails.
            var starving = w.NewStarvingBand();
            var unfed = w.NewPerson(30L, Sex.Female);
            var unfedMan = w.NewPerson(30L, Sex.Male);
            w.Family.Partner(unfed, unfedMan, Reasons.None);
            starving.AddMember(unfed);
            starving.AddMember(unfedMan);
            w.People.SetHealth(unfed, 500);

            w.Advance(Check);
            var unfedHealth = w.People.GetHealth(unfed);

            Assert.Multiple(() =>
            {
                Assert.That(w.Fertility.IsPregnant(tooYoung), Is.False, "below the window");
                Assert.That(w.Fertility.IsPregnant(tooOld), Is.False, "at the top of it");
                Assert.That(w.Fertility.IsPregnant(frail), Is.False, "below the health floor");
                Assert.That(w.Fertility.IsPregnant(unfed), Is.False, "past the grace period");
                Assert.That(unfedHealth, Is.GreaterThanOrEqualTo(s.HealthFloor), "and not for frailty");
                Assert.That(w.Fertility.IsPregnant(fine), Is.True, "the control");
            });
        }

        [Test]
        public void A_chance_of_zero_never_conceives()
        {
            var settings = new DemographicSettings
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
            var w = new DemographicWorld(settings, 1UL);
            var band = w.NewBand();
            w.NewCouple(out var wife, out var husband);
            band.AddMember(wife);
            band.AddMember(husband);

            w.AdvanceYears(10L);

            Assert.Multiple(() =>
            {
                Assert.That(w.People.Count, Is.EqualTo(2));
                Assert.That(w.Fertility.IsPregnant(wife), Is.False);
            });
        }

        [Test]
        public void The_mothers_death_cancels_the_pregnancy()
        {
            var w = new DemographicWorld(Certain(), 1UL);
            var band = w.NewBand();
            w.NewCouple(out var wife, out var husband);
            band.AddMember(wife);
            band.AddMember(husband);

            w.Advance(Check);
            var due = w.People.GetPregnancyDue(wife);
            var pendingBefore = w.Clock.ScheduledCount;
            w.Deaths.Die(wife, Reasons.None);
            var pendingAfter = w.Clock.ScheduledCount;

            w.Advance(Gestation + Check);

            Assert.Multiple(() =>
            {
                Assert.That(due, Is.Not.EqualTo(EventId.None));
                Assert.That(pendingAfter, Is.EqualTo(pendingBefore - 1), "the birth was cancelled, not left to be ignored");
                Assert.That(w.Clock.Cancel(due), Is.False, "already gone");
                Assert.That(w.People.Count, Is.EqualTo(1));
                Assert.That(w.Published(DomainEventKind.PersonBorn), Has.Count.EqualTo(2), "the founders only");
            });
        }

        [Test]
        public void A_birth_that_comes_due_for_a_dead_mother_is_ignored()
        {
            // The cascade cancels the event, so this is booked by hand: the
            // belt-and-braces path, for a queue rebuilt from a save that
            // disagrees with the store.
            var w = new DemographicWorld(Certain(), 1UL);
            var band = w.NewBand();
            w.NewCouple(out var wife, out var husband);
            band.AddMember(wife);
            band.AddMember(husband);
            var wifeId = w.IdOf(wife);
            var husbandId = w.IdOf(husband);
            w.Deaths.Die(wife, Reasons.None);

            w.Clock.Schedule(w.Clock.Now.Plus(1L), Fertility.Phase, ScheduledEventKind.BirthDue, wifeId, husbandId);

            Assert.Multiple(() =>
            {
                Assert.That(() => w.Advance(1L), Throws.Nothing);
                Assert.That(w.People.Count, Is.EqualTo(1));
            });
        }

        [Test]
        public void A_widow_still_gives_birth_and_the_child_has_a_father()
        {
            var w = new DemographicWorld(Certain(), 1UL);
            var band = w.NewBand();
            var household = w.NewCouple(out var wife, out var husband);
            band.AddMember(wife);
            band.AddMember(husband);
            var husbandId = w.IdOf(husband);

            w.Advance(Check);
            w.Deaths.Die(husband, Reasons.None);
            w.Advance(Gestation);

            var child = household.Members[household.Members.Count - 1];

            Assert.Multiple(() =>
            {
                Assert.That(household.Members, Has.Count.EqualTo(2), "mother and child");
                Assert.That(w.Genealogy.Parents(w.IdOf(child)).Father, Is.EqualTo(husbandId));
                Assert.That(band.Members, Does.Contain(child));
            });
        }

        [Test]
        public void A_household_formed_within_a_check_of_the_end_of_time_books_nothing()
        {
            var w = new DemographicWorld(Certain(), 1UL);
            w.Clock.AdvanceTo(new SimulationTime(long.MaxValue - 1L), w.Router);

            Assert.Multiple(() =>
            {
                Assert.That(() => w.Households.Form(), Throws.Nothing);
                Assert.That(w.Clock.ScheduledCount, Is.Zero);
            });
        }

        [Test]
        public void A_dissolved_household_ends_its_stream_of_checks()
        {
            var w = new DemographicWorld(Certain(), 1UL);
            var band = w.NewBand();
            var household = w.NewCouple(out var wife, out var husband);
            band.AddMember(wife);
            band.AddMember(husband);
            w.Deaths.Die(wife, Reasons.None);
            w.Deaths.Die(husband, Reasons.None);

            var pendingBefore = w.Clock.ScheduledCount;
            w.Advance(Check);

            Assert.Multiple(() =>
            {
                Assert.That(w.Households.TryGet(household.Id, out _), Is.False);
                Assert.That(w.Clock.ScheduledCount, Is.EqualTo(pendingBefore - 1), "the check ran and booked no successor");
            });
        }

        [Test]
        public void The_first_partnered_woman_in_household_order_is_the_one_who_conceives()
        {
            // Two couples sharing a household is not something family
            // formation builds, but the household model allows it, and the
            // choice has to be stable.
            var w = new DemographicWorld(Certain(), 1UL);
            var band = w.NewBand();
            var household = w.NewCouple(out var firstWife, out var firstHusband);
            w.NewCouple(out var secondWife, out var secondHusband);
            w.Households.Leave(secondWife);
            w.Households.Leave(secondHusband);
            w.Households.Join(household, secondWife);
            w.Households.Join(household, secondHusband);
            band.AddMember(firstWife);
            band.AddMember(firstHusband);
            band.AddMember(secondWife);
            band.AddMember(secondHusband);

            w.Advance(Check);

            Assert.Multiple(() =>
            {
                Assert.That(w.Fertility.IsPregnant(firstWife), Is.True);
                Assert.That(w.Fertility.IsPregnant(secondWife), Is.False);
            });
        }

        [Test]
        public void A_mother_in_no_tracked_band_bears_a_child_in_no_band()
        {
            // A band that feeds the couple but that Fertility was never told
            // about: the child has a household and no band.
            var w = new DemographicWorld(Certain(), 1UL);
            var band = w.Base.NewBand();
            band.SharedSupplies.Gather(ResourceKind.Food, DemographicWorld.PlentifulFood);
            w.Hunger.Track(band);
            var household = w.NewCouple(out var wife, out var husband);
            band.AddMember(wife);
            band.AddMember(husband);

            w.Advance(Check + Gestation);

            Assert.Multiple(() =>
            {
                Assert.That(household.Members, Has.Count.EqualTo(3));
                Assert.That(band.Members, Has.Count.EqualTo(2));
                Assert.That(w.Fertility.TrackedCount, Is.Zero);
            });
        }

        [Test]
        public void The_same_seed_produces_the_same_children_and_another_seed_does_not()
        {
            var first = Children(seed: 3UL);
            var again = Children(seed: 3UL);
            var other = Children(seed: 4UL);

            Assert.Multiple(() =>
            {
                Assert.That(first, Has.Count.GreaterThan(3), "a couple has children in thirty years");
                Assert.That(again, Is.EqualTo(first));
                Assert.That(other, Is.Not.EqualTo(first));
            });
        }

        [Test]
        public void Handle_refuses_a_kind_it_does_not_own_and_a_clock_that_is_not_its_own()
        {
            var w = new DemographicWorld(Certain(), 1UL);
            var household = w.NewCouple(out _, out _);
            var foreign = new ScheduledEvent(
                new EventId(1UL), SimulationTime.Zero, Fertility.Phase, ScheduledEventKind.MealDue, household.Id, EntityId.None);
            var owned = new ScheduledEvent(
                new EventId(2UL), SimulationTime.Zero, Fertility.Phase, ScheduledEventKind.BirthCheck, household.Id, EntityId.None);

            Assert.Multiple(() =>
            {
                Assert.That(() => w.Fertility.Handle(foreign, w.Clock), Throws.InvalidOperationException);
                Assert.That(
                    () => w.Fertility.Handle(owned, new SimulationClock(new IdAllocator())),
                    Throws.InvalidOperationException);
            });
        }

        // Certain(), but with the default gestation, which coincides with a
        // check on delivery day.
        private static DemographicSettings CoincidingTable(long postpartum) => new DemographicSettings
        {
            InfantMortalityPerMille = 0,
            ChildMortalityPerMille = 0,
            AdolescentMortalityPerMille = 0,
            AdultMortalityPerMille = 0,
            ElderMortalityPerMille = 0,
            SoftLifespanYears = 1_000L,
            MaxLifespanYears = 2_000L,
            ConceptionPerMille = 1000,
            PostpartumTicks = postpartum,
        };

        // The tick and sex of every birth to one couple over thirty years on
        // the default chance, in order.
        private static List<(long, Sex)> Children(ulong seed)
        {
            var settings = new DemographicSettings
            {
                InfantMortalityPerMille = 0,
                ChildMortalityPerMille = 0,
                AdolescentMortalityPerMille = 0,
                AdultMortalityPerMille = 0,
                ElderMortalityPerMille = 0,
                SoftLifespanYears = 1_000L,
                MaxLifespanYears = 2_000L,
            };
            var w = new DemographicWorld(settings, seed);
            var band = w.NewBand();
            w.NewCouple(out var wife, out var husband);
            band.AddMember(wife);
            band.AddMember(husband);

            w.AdvanceYears(30L);

            var children = new List<(long, Sex)>();

            foreach (var birth in w.Published(DomainEventKind.PersonBorn))
            {
                if (birth.SecondaryEntity.IsNone)
                {
                    continue;
                }

                w.People.TryGetHandle(birth.PrimaryEntity, out var child);
                children.Add((birth.Time.Ticks, w.People.GetSex(child)));
            }

            return children;
        }
    }
}
