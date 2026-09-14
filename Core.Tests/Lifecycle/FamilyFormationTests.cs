using System;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Events;
using KingdomWatch.Core.Lifecycle;
using NUnit.Framework;

namespace KingdomWatch.Core.Tests.Lifecycle
{
    [TestFixture]
    public sealed class FamilyFormationTests
    {
        private static readonly FamilyFormationSettings CousinsPermitted =
            new FamilyFormationSettings(FamilyFormationSettings.DefaultMourningTicks, firstCousinsPermitted: true);

        [Test]
        public void Construction_refuses_a_missing_collaborator()
        {
            var w = new HouseholdWorld();

            Assert.Multiple(() =>
            {
                Assert.That(() => new FamilyFormation(null!, w.People, w.Genealogy, w.Partnerships, w.Households, default), Throws.ArgumentNullException);
                Assert.That(() => new FamilyFormation(w.Bus, null!, w.Genealogy, w.Partnerships, w.Households, default), Throws.ArgumentNullException);
                Assert.That(() => new FamilyFormation(w.Bus, w.People, null!, w.Partnerships, w.Households, default), Throws.ArgumentNullException);
                Assert.That(() => new FamilyFormation(w.Bus, w.People, w.Genealogy, null!, w.Households, default), Throws.ArgumentNullException);
                Assert.That(() => new FamilyFormation(w.Bus, w.People, w.Genealogy, w.Partnerships, null!, default), Throws.ArgumentNullException);
            });
        }

        [Test]
        public void Settings_refuse_a_negative_mourning_period_and_default_to_the_conservative_reading()
        {
            Assert.Multiple(() =>
            {
                Assert.That(() => new FamilyFormationSettings(-1L, true), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(FamilyFormationSettings.Default.MourningTicks, Is.EqualTo(365L * SimulationTime.TicksPerDay));
                Assert.That(FamilyFormationSettings.Default.FirstCousinsPermitted, Is.False);
            });
        }

        [Test]
        public void Two_unrelated_adults_of_opposite_sex_are_eligible()
        {
            var w = new HouseholdWorld();
            var her = w.NewPerson(AgeStage.Adult, Sex.Female);
            var him = w.NewPerson(AgeStage.Elder, Sex.Male);

            Assert.Multiple(() =>
            {
                Assert.That(w.Family.Evaluate(her, him), Is.EqualTo(PartnerRefusal.None));
                Assert.That(w.Family.Evaluate(him, her), Is.EqualTo(PartnerRefusal.None), "symmetric");
                Assert.That(w.Journal.Count, Is.Zero, "evaluating changes nothing");
                Assert.That(w.Households.Count, Is.Zero);
            });
        }

        [Test]
        public void The_same_person_twice_is_refused_but_a_stale_handle_twice_is_an_error()
        {
            var w = new HouseholdWorld();
            var her = w.NewPerson(AgeStage.Adult, Sex.Female);
            var dead = w.NewPerson(AgeStage.Adult, Sex.Female);
            w.People.Remove(dead);

            Assert.Multiple(() =>
            {
                Assert.That(w.Family.Evaluate(her, her), Is.EqualTo(PartnerRefusal.SamePerson));
                Assert.That(() => w.Family.Evaluate(dead, dead), Throws.ArgumentException, "a stale handle is a bug, not a refusal");
                Assert.That(() => w.Family.Evaluate(PersonHandle.None, PersonHandle.None), Throws.ArgumentException);
            });
        }

        [TestCase(AgeStage.Infant)]
        [TestCase(AgeStage.Child)]
        [TestCase(AgeStage.Adolescent)]
        public void Anyone_below_adult_is_refused(AgeStage stage)
        {
            var w = new HouseholdWorld();
            var young = w.NewPerson(stage, Sex.Female);
            var grown = w.NewPerson(AgeStage.Adult, Sex.Male);

            Assert.Multiple(() =>
            {
                Assert.That(w.Family.Evaluate(young, grown), Is.EqualTo(PartnerRefusal.NotAdult));
                Assert.That(w.Family.Evaluate(grown, young), Is.EqualTo(PartnerRefusal.NotAdult));
            });
        }

        [Test]
        public void The_same_sex_is_refused()
        {
            var w = new HouseholdWorld();
            var a = w.NewPerson(AgeStage.Adult, Sex.Male);
            var b = w.NewPerson(AgeStage.Adult, Sex.Male);

            Assert.That(w.Family.Evaluate(a, b), Is.EqualTo(PartnerRefusal.SameSex));
        }

        [Test]
        public void An_active_partnership_on_either_side_is_refused()
        {
            var w = new HouseholdWorld();
            w.NewCouple(out var wife, out var husband);
            var single = w.NewPerson(AgeStage.Adult, Sex.Male);
            var otherSingle = w.NewPerson(AgeStage.Adult, Sex.Female);

            Assert.Multiple(() =>
            {
                Assert.That(w.Family.Evaluate(wife, single), Is.EqualTo(PartnerRefusal.AlreadyPartnered));
                Assert.That(w.Family.Evaluate(otherSingle, husband), Is.EqualTo(PartnerRefusal.AlreadyPartnered));
            });
        }

        [Test]
        public void A_widow_is_refused_inside_the_mourning_period_and_eligible_after_it()
        {
            var w = new HouseholdWorld();
            w.NewCouple(out var widow, out var husband);
            var suitor = w.NewPerson(AgeStage.Adult, Sex.Male);
            w.Deaths.Die(husband, Reasons.None);

            Assert.That(w.Family.Evaluate(widow, suitor), Is.EqualTo(PartnerRefusal.Mourning), "the day of");

            w.Advance(FamilyFormationSettings.DefaultMourningTicks - 1L);
            Assert.That(w.Family.Evaluate(suitor, widow), Is.EqualTo(PartnerRefusal.Mourning), "one tick short");

            w.Advance(1L);
            Assert.That(w.Family.Evaluate(widow, suitor), Is.EqualTo(PartnerRefusal.None), "the period has passed");
        }

        [Test]
        public void A_zero_mourning_period_permits_remarriage_at_once()
        {
            var w = new HouseholdWorld(new FamilyFormationSettings(0L, false), new CampSpace());
            w.NewCouple(out var widow, out var husband);
            var suitor = w.NewPerson(AgeStage.Adult, Sex.Male);
            w.Deaths.Die(husband, Reasons.None);

            Assert.That(w.Family.Evaluate(widow, suitor), Is.EqualTo(PartnerRefusal.None));
        }

        [Test]
        public void Parent_sibling_and_grandparent_are_banned()
        {
            // One founding couple, two children, and a grandchild.
            var w = new HouseholdWorld();
            var grandmother = w.NewPerson(AgeStage.Elder, Sex.Female);
            var grandfather = w.NewPerson(AgeStage.Elder, Sex.Male);
            var son = w.NewPerson(AgeStage.Adult, Sex.Male, grandmother, grandfather);
            var daughter = w.NewPerson(AgeStage.Adult, Sex.Female, grandmother, grandfather);
            var sonsWife = w.NewPerson(AgeStage.Adult, Sex.Female);
            var sonsDaughter = w.NewPerson(AgeStage.Adult, Sex.Female, sonsWife, son);

            Assert.Multiple(() =>
            {
                Assert.That(w.Family.Evaluate(grandmother, son), Is.EqualTo(PartnerRefusal.KinshipBanned), "parent and child");
                Assert.That(w.Family.Evaluate(son, daughter), Is.EqualTo(PartnerRefusal.KinshipBanned), "siblings");
                Assert.That(w.Family.Evaluate(grandfather, sonsDaughter), Is.EqualTo(PartnerRefusal.KinshipBanned), "grandparent and grandchild");
            });
        }

        [Test]
        public void Kinship_is_checked_after_sex_and_first_cousins_are_a_taboo_not_a_ban()
        {
            var w = new HouseholdWorld();
            var grandmother = w.NewPerson(AgeStage.Elder, Sex.Female);
            var grandfather = w.NewPerson(AgeStage.Elder, Sex.Male);
            var son = w.NewPerson(AgeStage.Adult, Sex.Male, grandmother, grandfather);
            var daughter = w.NewPerson(AgeStage.Adult, Sex.Female, grandmother, grandfather);
            var sonsWife = w.NewPerson(AgeStage.Adult, Sex.Female);
            var daughtersHusband = w.NewPerson(AgeStage.Adult, Sex.Male);
            var sonsDaughter = w.NewPerson(AgeStage.Adult, Sex.Female, sonsWife, son);
            var daughtersSon = w.NewPerson(AgeStage.Adult, Sex.Male, daughter, daughtersHusband);

            Assert.Multiple(() =>
            {
                Assert.That(w.Family.Evaluate(daughter, sonsDaughter), Is.EqualTo(PartnerRefusal.SameSex), "aunt and niece: sex is checked before kinship");
                Assert.That(w.Family.Evaluate(son, sonsDaughter), Is.EqualTo(PartnerRefusal.KinshipBanned), "father and daughter");
                Assert.That(w.Family.Evaluate(daughtersSon, sonsWife), Is.EqualTo(PartnerRefusal.None), "an aunt by marriage is not kin");
                Assert.That(w.Family.Evaluate(daughtersSon, daughter), Is.EqualTo(PartnerRefusal.KinshipBanned), "mother and son");
                Assert.That(w.Family.Evaluate(son, daughtersSon), Is.EqualTo(PartnerRefusal.SameSex));
                Assert.That(w.Family.Evaluate(sonsDaughter, daughtersSon), Is.EqualTo(PartnerRefusal.CousinTaboo), "first cousins, refused by default");
            });

            var permissive = new FamilyFormation(w.Bus, w.People, w.Genealogy, w.Partnerships, w.Households, CousinsPermitted);

            Assert.That(permissive.Evaluate(sonsDaughter, daughtersSon), Is.EqualTo(PartnerRefusal.None), "first cousins, permitted by setting");
        }

        [Test]
        public void An_uncle_and_niece_are_banned()
        {
            var w = new HouseholdWorld();
            var grandmother = w.NewPerson(AgeStage.Elder, Sex.Female);
            var grandfather = w.NewPerson(AgeStage.Elder, Sex.Male);
            var son = w.NewPerson(AgeStage.Adult, Sex.Male, grandmother, grandfather);
            var daughter = w.NewPerson(AgeStage.Adult, Sex.Female, grandmother, grandfather);
            var daughtersHusband = w.NewPerson(AgeStage.Adult, Sex.Male);
            var daughtersDaughter = w.NewPerson(AgeStage.Adult, Sex.Female, daughter, daughtersHusband);

            Assert.That(w.Family.Evaluate(son, daughtersDaughter), Is.EqualTo(PartnerRefusal.KinshipBanned));
        }

        [Test]
        public void Half_siblings_are_banned()
        {
            var w = new HouseholdWorld();
            var mother = w.NewPerson(AgeStage.Elder, Sex.Female);
            var firstFather = w.NewPerson(AgeStage.Elder, Sex.Male);
            var secondFather = w.NewPerson(AgeStage.Elder, Sex.Male);
            var a = w.NewPerson(AgeStage.Adult, Sex.Female, mother, firstFather);
            var b = w.NewPerson(AgeStage.Adult, Sex.Male, mother, secondFather);

            Assert.That(w.Family.Evaluate(a, b), Is.EqualTo(PartnerRefusal.KinshipBanned));
        }

        [Test]
        public void No_home_is_the_last_refusal()
        {
            var w = new HouseholdWorld(FamilyFormationSettings.Default, new NoRoom());
            var her = w.NewPerson(AgeStage.Adult, Sex.Female);
            var him = w.NewPerson(AgeStage.Adult, Sex.Male);
            var child = w.NewPerson(AgeStage.Child, Sex.Male);

            Assert.Multiple(() =>
            {
                Assert.That(w.Family.Evaluate(her, him), Is.EqualTo(PartnerRefusal.NoHomeAvailable));
                Assert.That(w.Family.Evaluate(her, child), Is.EqualTo(PartnerRefusal.NotAdult), "every other refusal comes first");
                Assert.That(() => w.Family.Partner(her, him, Reasons.None), Throws.InvalidOperationException);
                Assert.That(w.Journal.Count, Is.Zero, "a refused Partner announces nothing");
            });
        }

        [Test]
        public void Someone_outside_the_genealogy_cannot_be_evaluated_whatever_else_would_refuse_them()
        {
            var w = new HouseholdWorld();
            var recorded = w.NewPerson(AgeStage.Adult, Sex.Female);
            var unrecorded = w.People.Add(w.Ids.Next(EntityKind.Person), default, 100, AgeStage.Adult, Sex.Male, 0, 0, default);
            var unrecordedChild = w.People.Add(w.Ids.Next(EntityKind.Person), default, 100, AgeStage.Child, Sex.Male, 0, 0, default);
            var unrecordedWoman = w.People.Add(w.Ids.Next(EntityKind.Person), default, 100, AgeStage.Adult, Sex.Female, 0, 0, default);

            Assert.Multiple(() =>
            {
                Assert.That(() => w.Family.Evaluate(recorded, unrecorded), Throws.ArgumentException);
                Assert.That(() => w.Family.Evaluate(unrecordedChild, recorded), Throws.ArgumentException, "not NotAdult");
                Assert.That(() => w.Family.Evaluate(recorded, unrecordedWoman), Throws.ArgumentException, "not SameSex");
                Assert.That(() => w.Family.Evaluate(unrecorded, unrecorded), Throws.ArgumentException, "not SamePerson");
            });
        }

        [Test]
        public void Partnering_announces_records_houses_and_orders_it_that_way()
        {
            var w = new HouseholdWorld();
            w.Advance(SimulationTime.TicksPerDay);
            var her = w.NewPerson(AgeStage.Adult, Sex.Female);
            var him = w.NewPerson(AgeStage.Adult, Sex.Male);
            var reasons = new Reasons(ReasonCode.KinLiveThere);

            var household = w.Family.Partner(her, him, reasons);

            var marriage = w.Journal[0];
            var partnership = w.Partnerships.History(w.IdOf(her))[0];

            Assert.Multiple(() =>
            {
                Assert.That(w.Published(), Is.EqualTo(new[] { DomainEventKind.MarriageFormed, DomainEventKind.HouseholdFormed }));
                Assert.That(marriage.PrimaryEntity, Is.EqualTo(w.IdOf(her)));
                Assert.That(marriage.SecondaryEntity, Is.EqualTo(w.IdOf(him)));
                Assert.That(marriage.Reasons, Is.EqualTo(reasons), "the caller's reasons travel with the event");
                Assert.That(partnership.FormedBy, Is.EqualTo(marriage.Id), "the partnership names the event that formed it");
                Assert.That(partnership.FormedAt, Is.EqualTo(SimulationTime.FromDays(1L)));
                Assert.That(w.Partnerships.ActivePartnerOf(w.IdOf(him)), Is.EqualTo(w.IdOf(her)));
                Assert.That(household.Members, Is.EqualTo(new[] { her, him }));
                Assert.That(w.Households.Of(her), Is.SameAs(household));
                Assert.That(w.Households.Of(him), Is.SameAs(household));
                Assert.That(w.Households.Count, Is.EqualTo(1));
            });
        }

        [Test]
        public void Partnering_refuses_what_evaluate_refuses()
        {
            var w = new HouseholdWorld();
            var a = w.NewPerson(AgeStage.Adult, Sex.Male);
            var b = w.NewPerson(AgeStage.Adult, Sex.Male);
            var dead = w.NewPerson(AgeStage.Adult, Sex.Female);
            w.People.Remove(dead);

            Assert.Multiple(() =>
            {
                Assert.That(() => w.Family.Partner(a, b, Reasons.None), Throws.InvalidOperationException.With.Message.Contains("SameSex"));
                Assert.That(() => w.Family.Partner(a, dead, Reasons.None), Throws.ArgumentException, "a stale handle fails at the store");
                Assert.That(() => w.Family.Evaluate(dead, a), Throws.ArgumentException);
                Assert.That(w.Journal.Count, Is.Zero);
                Assert.That(w.Households.Count, Is.Zero);
            });
        }

        [Test]
        public void A_grown_child_leaving_home_leaves_the_parents_household_standing()
        {
            var w = new HouseholdWorld();
            var parents = w.NewCouple(out var mother, out var father);
            var grown = w.NewChildOf(parents, mother, father, AgeStage.Adult);
            var spouse = w.NewPerson(AgeStage.Adult, Sex.Male);

            var formed = w.Family.Partner(grown, spouse, Reasons.None);

            Assert.Multiple(() =>
            {
                Assert.That(parents.Members, Is.EqualTo(new[] { mother, father }));
                Assert.That(formed.Members, Is.EqualTo(new[] { grown, spouse }));
                Assert.That(w.Households.Count, Is.EqualTo(2));
            });
        }

        [Test]
        public void A_widow_remarrying_brings_her_dependent_children_and_her_old_household_dissolves()
        {
            var w = new HouseholdWorld(new FamilyFormationSettings(0L, false), new CampSpace());
            var old = w.NewCouple(out var widow, out var husband);
            var child = w.NewChildOf(old, widow, husband, AgeStage.Child);
            var adolescent = w.NewChildOf(old, widow, husband, AgeStage.Adolescent);
            var grownDaughter = w.NewChildOf(old, widow, husband, AgeStage.Adult);
            var suitor = w.NewPerson(AgeStage.Adult, Sex.Male);
            w.Deaths.Die(husband, Reasons.None);

            var formed = w.Family.Partner(widow, suitor, Reasons.None);

            Assert.Multiple(() =>
            {
                Assert.That(formed.Members, Is.EqualTo(new[] { widow, child, adolescent, suitor }), "her dependents follow her; her grown daughter does not");
                Assert.That(old.Members, Is.EqualTo(new[] { grownDaughter }), "the old household stands while anyone remains");
                Assert.That(w.Households.Count, Is.EqualTo(2));
            });
        }

        [Test]
        public void A_household_emptied_by_a_marriage_is_dissolved()
        {
            var w = new HouseholdWorld(new FamilyFormationSettings(0L, false), new CampSpace());
            var old = w.NewCouple(out var widow, out var husband);
            var child = w.NewChildOf(old, widow, husband, AgeStage.Child);
            var suitor = w.NewPerson(AgeStage.Adult, Sex.Male);
            w.Deaths.Die(husband, Reasons.None);

            var formed = w.Family.Partner(widow, suitor, Reasons.None);

            Assert.Multiple(() =>
            {
                Assert.That(formed.Members, Is.EqualTo(new[] { widow, child, suitor }));
                Assert.That(w.Households.TryGet(old.Id, out _), Is.False);
                Assert.That(w.Households.Count, Is.EqualTo(1));
                Assert.That(w.Published(), Does.Contain(DomainEventKind.HouseholdDissolved));
            });
        }

        [Test]
        public void A_parent_in_no_household_brings_their_unhoused_dependents()
        {
            // Worldgen may seed a widow and her child without a household.
            // Partnering her houses them both; a child of hers housed
            // elsewhere stays where they are.
            var w = new HouseholdWorld();
            var widow = w.NewPerson(AgeStage.Adult, Sex.Female);
            var late = w.NewPerson(AgeStage.Adult, Sex.Male);
            var child = w.NewPerson(AgeStage.Child, Sex.Female, widow, late);
            var fostered = w.NewPerson(AgeStage.Child, Sex.Male, widow, late);
            var fosterHome = w.Households.Form();
            w.Households.Join(fosterHome, fostered);
            var suitor = w.NewPerson(AgeStage.Adult, Sex.Male);

            var formed = w.Family.Partner(widow, suitor, Reasons.None);

            Assert.Multiple(() =>
            {
                Assert.That(formed.Members, Is.EqualTo(new[] { widow, child, suitor }));
                Assert.That(fosterHome.Members, Is.EqualTo(new[] { fostered }));
                Assert.That(w.Households.Count, Is.EqualTo(2));
            });
        }

        [Test]
        public void Only_the_partners_own_dependents_follow_them()
        {
            // A widow living with her dependent nephew: he is in her household
            // but not her child, so he stays.
            var w = new HouseholdWorld(new FamilyFormationSettings(0L, false), new CampSpace());
            var old = w.NewCouple(out var widow, out var husband);
            var sister = w.NewPerson(AgeStage.Adult, Sex.Female);
            var nephew = w.NewPerson(AgeStage.Child, Sex.Male, w.IdOf(sister), EntityId.None);
            w.Households.Join(old, nephew);
            var suitor = w.NewPerson(AgeStage.Adult, Sex.Male);
            w.Deaths.Die(husband, Reasons.None);

            var formed = w.Family.Partner(widow, suitor, Reasons.None);

            Assert.Multiple(() =>
            {
                Assert.That(formed.Members, Is.EqualTo(new[] { widow, suitor }));
                Assert.That(old.Members, Is.EqualTo(new[] { nephew }));
            });
        }

        private sealed class NoRoom : IHousing
        {
            public bool HasVacancy => false;

            public EntityId Claim() => throw new InvalidOperationException("full");

            public void Release(EntityId home)
            {
            }
        }
    }
}
