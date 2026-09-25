using System.Collections.Generic;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Events;
using KingdomWatch.Core.Lifecycle;
using NUnit.Framework;

namespace KingdomWatch.Core.Tests.Lifecycle
{
    [TestFixture]
    public sealed class DeathsTests
    {
        [Test]
        public void Construction_refuses_a_missing_collaborator()
        {
            var w = new HouseholdWorld();

            Assert.Multiple(() =>
            {
                Assert.That(() => new Deaths(null!, w.People, w.Genealogy, w.Partnerships, w.Memories, w.Households, w.Jobs), Throws.ArgumentNullException);
                Assert.That(() => new Deaths(w.Bus, null!, w.Genealogy, w.Partnerships, w.Memories, w.Households, w.Jobs), Throws.ArgumentNullException);
                Assert.That(() => new Deaths(w.Bus, w.People, null!, w.Partnerships, w.Memories, w.Households, w.Jobs), Throws.ArgumentNullException);
                Assert.That(() => new Deaths(w.Bus, w.People, w.Genealogy, null!, w.Memories, w.Households, w.Jobs), Throws.ArgumentNullException);
                Assert.That(() => new Deaths(w.Bus, w.People, w.Genealogy, w.Partnerships, null!, w.Households, w.Jobs), Throws.ArgumentNullException);
                Assert.That(() => new Deaths(w.Bus, w.People, w.Genealogy, w.Partnerships, w.Memories, null!, w.Jobs), Throws.ArgumentNullException);
                Assert.That(() => new Deaths(w.Bus, w.People, w.Genealogy, w.Partnerships, w.Memories, w.Households, null!), Throws.ArgumentNullException);
            });
        }

        [Test]
        public void Tracking_refuses_null_and_the_same_group_twice()
        {
            var w = new HouseholdWorld();
            var band = w.NewBand();

            Assert.Multiple(() =>
            {
                Assert.That(() => w.Deaths.Track(null!), Throws.ArgumentNullException);
                Assert.That(() => w.Deaths.Track(band), Throws.InvalidOperationException);
                Assert.That(w.Deaths.TrackedCount, Is.EqualTo(1));
            });
        }

        [Test]
        public void The_tracked_groups_are_listed_in_the_order_they_were_tracked()
        {
            // For the validator and the world hash (#104), neither of which
            // could otherwise see which communities the cascade strikes from.
            var w = new HouseholdWorld();
            var first = w.NewBand();
            var second = w.NewBand();
            var untracked = w.NewBand();
            w.Deaths.Untrack(untracked);
            var into = new List<ICommunity> { untracked };

            w.Deaths.CopyTrackedTo(into);

            Assert.Multiple(() =>
            {
                Assert.That(into, Is.EqualTo(new ICommunity[] { first, second }));
                Assert.That(() => w.Deaths.CopyTrackedTo(null!), Throws.ArgumentNullException);
            });
        }

        [Test]
        public void An_untracked_group_keeps_its_dead_and_refuses_a_second_untrack()
        {
            // Untracked, the group is no longer the cascade's to strike from:
            // a band handed to a settlement (#54) keeps nothing, but a group
            // untracked by mistake would silently keep the dead listed, so
            // the test pins that the removal is the tracker's alone.
            var w = new HouseholdWorld();
            var band = w.NewBand();
            var person = w.NewPerson(AgeStage.Adult, Sex.Male);
            band.AddMember(person);

            w.Deaths.Untrack(band);
            w.Deaths.Die(person, Reasons.None);

            Assert.Multiple(() =>
            {
                Assert.That(w.Deaths.TrackedCount, Is.Zero);
                Assert.That(band.Members, Is.EqualTo(new[] { person }), "nobody strikes the dead from an untracked group");
                Assert.That(() => w.Deaths.Untrack(band), Throws.InvalidOperationException);
                Assert.That(() => w.Deaths.Untrack(null!), Throws.ArgumentNullException);
                Assert.That(() => w.Deaths.Track(band), Throws.Nothing, "and can be tracked afresh");
            });
        }

        [Test]
        public void Dying_announces_with_the_callers_reasons_and_frees_the_slot()
        {
            var w = new HouseholdWorld();
            w.Advance(SimulationTime.TicksPerDay);
            var person = w.NewPerson(AgeStage.Adult, Sex.Female);
            var id = w.IdOf(person);
            var reasons = new Reasons(ReasonCode.FoodShortage);

            w.Deaths.Die(person, reasons);

            var died = w.LastPublished();

            Assert.Multiple(() =>
            {
                Assert.That(died.Kind, Is.EqualTo(DomainEventKind.PersonDied));
                Assert.That(died.PrimaryEntity, Is.EqualTo(id));
                Assert.That(died.SecondaryEntity, Is.EqualTo(EntityId.None));
                Assert.That(died.Reasons, Is.EqualTo(reasons));
                Assert.That(died.Time, Is.EqualTo(SimulationTime.FromDays(1L)));
                Assert.That(w.People.IsAlive(person), Is.False);
                Assert.That(w.People.Count, Is.Zero);
                Assert.That(w.People.TryGetHandle(id, out _), Is.False);
                Assert.That(w.Genealogy.IsRecorded(id), Is.True, "genealogy is never touched by death");
            });
        }

        [Test]
        public void Nobody_dies_twice()
        {
            var w = new HouseholdWorld();
            var person = w.NewPerson(AgeStage.Adult, Sex.Female);
            w.Deaths.Die(person, Reasons.None);

            Assert.Multiple(() =>
            {
                Assert.That(() => w.Deaths.Die(person, Reasons.None), Throws.InvalidOperationException);
                Assert.That(() => w.Deaths.Die(PersonHandle.None, Reasons.None), Throws.InvalidOperationException);
                Assert.That(w.Journal.Count, Is.EqualTo(1), "the refused death announced nothing");
            });
        }

        [Test]
        public void A_partnership_is_ended_by_the_death_event_itself()
        {
            var w = new HouseholdWorld();
            w.NewCouple(out var wife, out var husband);
            w.Advance(SimulationTime.TicksPerDay);
            var wifeId = w.IdOf(wife);
            var husbandId = w.IdOf(husband);

            w.Deaths.Die(husband, Reasons.None);

            var died = w.LastPublished();
            var record = w.Partnerships.History(wifeId)[0];

            Assert.Multiple(() =>
            {
                Assert.That(record.IsActive, Is.False);
                Assert.That(record.EndedBy, Is.EqualTo(died.Id), "marked ended, not deleted, and by this event");
                Assert.That(record.EndedAt, Is.EqualTo(SimulationTime.FromDays(1L)));
                Assert.That(w.Partnerships.ActivePartnerOf(wifeId), Is.EqualTo(EntityId.None));
                Assert.That(w.Partnerships.History(husbandId).Length, Is.EqualTo(1), "the dead keep their history");
            });
        }

        [Test]
        public void The_dead_are_struck_from_every_witness_list()
        {
            var w = new HouseholdWorld();
            var holder = w.NewPerson(AgeStage.Adult, Sex.Female);
            var witness = w.NewPerson(AgeStage.Adult, Sex.Male);
            var other = w.NewPerson(AgeStage.Adult, Sex.Male);
            var origin = w.Bus.Publish(DomainEventKind.DivineActWitnessed, w.IdOf(holder), EntityId.None);
            w.Memories.Record(w.IdOf(holder), origin, EntityId.None, 1, new[] { w.IdOf(witness), w.IdOf(other) }, w.Clock.Now);

            w.Deaths.Die(witness, Reasons.None);

            Assert.That(w.Memories.Witnesses(w.IdOf(holder), origin).ToArray(), Is.EqualTo(new[] { w.IdOf(other) }));
        }

        [Test]
        public void Someone_in_no_household_and_no_group_dies_cleanly()
        {
            var w = new HouseholdWorld();
            var loner = w.NewPerson(AgeStage.Adult, Sex.Female);

            Assert.That(() => w.Deaths.Die(loner, Reasons.None), Throws.Nothing);
        }

        [Test]
        public void A_death_leaves_a_household_with_an_adult_standing()
        {
            var w = new HouseholdWorld();
            var home = w.NewCouple(out var wife, out var husband);
            var child = w.NewChildOf(home, wife, husband, AgeStage.Child);

            w.Deaths.Die(husband, Reasons.None);

            Assert.Multiple(() =>
            {
                Assert.That(home.Members, Is.EqualTo(new[] { wife, child }));
                Assert.That(w.Households.Count, Is.EqualTo(1));
                Assert.That(w.Published(), Does.Not.Contain(DomainEventKind.HouseholdDissolved));
            });
        }

        [Test]
        public void The_last_member_dying_dissolves_the_household_and_releases_its_home()
        {
            var w = new HouseholdWorld();
            var home = w.NewCouple(out var wife, out var husband);
            w.Deaths.Die(husband, Reasons.None);

            w.Deaths.Die(wife, Reasons.None);

            Assert.Multiple(() =>
            {
                Assert.That(w.Households.Count, Is.Zero);
                Assert.That(w.Households.TryGet(home.Id, out _), Is.False);
                Assert.That(
                    w.Published(),
                    Is.EqualTo(new[]
                    {
                        DomainEventKind.MarriageFormed,
                        DomainEventKind.HouseholdFormed,
                        DomainEventKind.PersonDied,
                        DomainEventKind.PersonDied,
                        DomainEventKind.HouseholdDissolved,
                    }));
            });
        }

        [Test]
        public void Orphans_go_to_the_surviving_parent_before_anyone_else()
        {
            // A separated couple: the child lives with the mother, the father
            // has remarried into a household of his own. The mother dies, and
            // the father is nearer kin than the grandparents.
            var w = new HouseholdWorld(new FamilyFormationSettings(0L, false), new CampSpace());
            var grandparents = w.NewCouple(out var grandmother, out var grandfather);
            var mother = w.NewPerson(AgeStage.Adult, Sex.Female, grandmother, grandfather);
            var father = w.NewPerson(AgeStage.Adult, Sex.Male);
            var mothers = w.Family.Partner(mother, father, Reasons.None);
            var child = w.NewChildOf(mothers, mother, father, AgeStage.Child);
            w.Partnerships.End(w.IdOf(mother), w.IdOf(father), w.Ids.NextEvent(), w.Clock.Now);
            w.Households.Leave(father);
            var stepmother = w.NewPerson(AgeStage.Adult, Sex.Female);
            var fathers = w.Family.Partner(father, stepmother, Reasons.None);

            w.Deaths.Die(mother, Reasons.None);

            Assert.Multiple(() =>
            {
                Assert.That(fathers.Members, Is.EqualTo(new[] { father, stepmother, child }));
                Assert.That(w.Households.TryGet(mothers.Id, out _), Is.False, "emptied, so dissolved");
                Assert.That(grandparents.Members, Is.EqualTo(new[] { grandmother, grandfather }));
            });
        }

        [Test]
        public void Orphans_go_to_the_nearest_degree_of_kin_with_a_household()
        {
            // Both parents die. The candidates are an adult brother housed
            // elsewhere, the grandparents, and an uncle. The brother wins.
            var w = new HouseholdWorld();
            var grandparents = w.NewCouple(out var grandmother, out var grandfather);
            var uncle = w.NewPerson(AgeStage.Adult, Sex.Male, grandmother, grandfather);
            var aunt = w.NewPerson(AgeStage.Adult, Sex.Female);
            var uncleHome = w.Family.Partner(uncle, aunt, Reasons.None);
            var mum = w.NewPerson(AgeStage.Adult, Sex.Female, grandmother, grandfather);
            var dad = w.NewPerson(AgeStage.Adult, Sex.Male);
            var parentsHome = w.Family.Partner(mum, dad, Reasons.None);
            var brother = w.NewPerson(AgeStage.Adult, Sex.Male, mum, dad);
            var brotherHome = w.Households.Form();
            w.Households.Join(brotherHome, brother);
            var orphan = w.NewChildOf(parentsHome, mum, dad, AgeStage.Infant);
            var baby = w.NewChildOf(parentsHome, mum, dad, AgeStage.Infant);

            w.Deaths.Die(dad, Reasons.None);
            Assert.That(parentsHome.Members, Is.EqualTo(new[] { mum, orphan, baby }), "one parent left, nobody moves");

            w.Deaths.Die(mum, Reasons.None);

            Assert.Multiple(() =>
            {
                Assert.That(brotherHome.Members, Is.EqualTo(new[] { brother, orphan, baby }), "the adult brother's household adopts both, in order");
                Assert.That(grandparents.Members, Is.EqualTo(new[] { grandmother, grandfather }), "not the grandparents");
                Assert.That(uncleHome.Members, Is.EqualTo(new[] { uncle, aunt }), "not the uncle");
                Assert.That(w.Households.TryGet(parentsHome.Id, out _), Is.False, "the orphaned household dissolved");
            });
        }

        [Test]
        public void Grandparents_then_aunts_and_uncles_then_first_cousins_in_that_order()
        {
            var w = new HouseholdWorld();
            var grandparents = w.NewCouple(out var grandmother, out var grandfather);
            var mum = w.NewPerson(AgeStage.Adult, Sex.Female, grandmother, grandfather);
            var uncle = w.NewPerson(AgeStage.Adult, Sex.Male, grandmother, grandfather);
            var aunt = w.NewPerson(AgeStage.Adult, Sex.Female);
            var uncleHome = w.Family.Partner(uncle, aunt, Reasons.None);
            var cousin = w.NewPerson(AgeStage.Adult, Sex.Female, aunt, uncle);
            var cousinHome = w.Households.Form();
            w.Households.Join(cousinHome, cousin);
            var dad = w.NewPerson(AgeStage.Adult, Sex.Male);
            var parentsHome = w.Family.Partner(mum, dad, Reasons.None);
            var orphan = w.NewChildOf(parentsHome, mum, dad, AgeStage.Child);

            w.Deaths.Die(dad, Reasons.None);
            w.Deaths.Die(mum, Reasons.None);
            Assert.That(grandparents.Members, Does.Contain(orphan), "grandparents before the uncle");

            // Then the grandparents die, and the uncle takes the child.
            w.Deaths.Die(grandmother, Reasons.None);
            w.Deaths.Die(grandfather, Reasons.None);
            Assert.That(uncleHome.Members, Does.Contain(orphan), "the uncle before the cousin");

            w.Deaths.Die(uncle, Reasons.None);
            w.Deaths.Die(aunt, Reasons.None);
            Assert.That(cousinHome.Members, Does.Contain(orphan), "the first cousin, last");
        }

        [Test]
        public void Within_a_degree_the_lowest_id_takes_the_child_whichever_the_walk_meets_first()
        {
            // The walk considers the mother's parents before the father's.
            // The father's parents were recorded first, so they have the
            // lower ids and must win despite being met second.
            var w = new HouseholdWorld();
            var paternal = w.NewCouple(out var fathersMother, out var fathersFather);
            var maternal = w.NewCouple(out var mothersMother, out var mothersFather);
            var mum = w.NewPerson(AgeStage.Adult, Sex.Female, mothersMother, mothersFather);
            var dad = w.NewPerson(AgeStage.Adult, Sex.Male, fathersMother, fathersFather);
            var home = w.Family.Partner(mum, dad, Reasons.None);
            var orphan = w.NewChildOf(home, mum, dad, AgeStage.Child);

            w.Deaths.Die(dad, Reasons.None);
            w.Deaths.Die(mum, Reasons.None);

            Assert.Multiple(() =>
            {
                Assert.That(w.IdOf(fathersMother), Is.LessThan(w.IdOf(mothersMother)), "the premise");
                Assert.That(paternal.Members, Is.EqualTo(new[] { fathersMother, fathersFather, orphan }));
                Assert.That(maternal.Members, Is.EqualTo(new[] { mothersMother, mothersFather }));
            });
        }

        [Test]
        public void Kin_without_a_household_or_not_yet_grown_cannot_adopt()
        {
            var w = new HouseholdWorld();
            var grandmother = w.NewPerson(AgeStage.Elder, Sex.Female);
            var grandfather = w.NewPerson(AgeStage.Elder, Sex.Male);
            var mum = w.NewPerson(AgeStage.Adult, Sex.Female, grandmother, grandfather);
            var dad = w.NewPerson(AgeStage.Adult, Sex.Male);
            var parentsHome = w.Family.Partner(mum, dad, Reasons.None);
            var orphan = w.NewChildOf(parentsHome, mum, dad, AgeStage.Child);
            var adolescentUncle = w.NewPerson(AgeStage.Adolescent, Sex.Male, grandmother, grandfather);
            var uncleHome = w.Households.Form();
            w.Households.Join(uncleHome, adolescentUncle);
            // The grandparents are alive, grown, and in no household.

            w.Deaths.Die(dad, Reasons.None);
            w.Deaths.Die(mum, Reasons.None);

            Assert.Multiple(() =>
            {
                Assert.That(parentsHome.Members, Is.EqualTo(new[] { orphan }), "nobody could take the child, so the household stands");
                Assert.That(uncleHome.Members, Is.EqualTo(new[] { adolescentUncle }));
                Assert.That(w.Households.HasAdult(parentsHome), Is.False);
                Assert.That(w.Households.TryGet(parentsHome.Id, out _), Is.True);
            });
        }

        [Test]
        public void An_orphan_with_no_kin_keeps_the_household()
        {
            var w = new HouseholdWorld();
            var home = w.NewCouple(out var mum, out var dad);
            var orphan = w.NewChildOf(home, mum, dad, AgeStage.Adolescent);
            var unrelated = w.NewCouple(out _, out _);

            w.Deaths.Die(dad, Reasons.None);
            w.Deaths.Die(mum, Reasons.None);

            Assert.Multiple(() =>
            {
                Assert.That(home.Members, Is.EqualTo(new[] { orphan }));
                Assert.That(unrelated.Members.Count, Is.EqualTo(2), "strangers do not adopt");
                Assert.That(w.Published(), Does.Not.Contain(DomainEventKind.HouseholdDissolved));
            });
        }

        [Test]
        public void A_dependent_outside_the_genealogy_has_no_kin_to_find()
        {
            var w = new HouseholdWorld();
            var home = w.NewCouple(out var mum, out var dad);
            var stray = w.People.Add(w.Ids.Next(EntityKind.Person), default, 100, AgeStage.Child, Sex.Male, 0, 0, default, 0L);
            w.Households.Join(home, stray);

            w.Deaths.Die(dad, Reasons.None);

            Assert.Multiple(() =>
            {
                Assert.That(() => w.Deaths.Die(mum, Reasons.None), Throws.Nothing);
                Assert.That(home.Members, Is.EqualTo(new[] { stray }));
            });
        }

        [Test]
        public void An_adopted_child_who_is_adult_by_then_is_not_moved()
        {
            // No adult remains only if every member is a dependent, so an
            // adult child keeps the household and their siblings with them.
            var w = new HouseholdWorld();
            var grandparents = w.NewCouple(out var grandmother, out var grandfather);
            var mum = w.NewPerson(AgeStage.Adult, Sex.Female, grandmother, grandfather);
            var dad = w.NewPerson(AgeStage.Adult, Sex.Male);
            var home = w.Family.Partner(mum, dad, Reasons.None);
            var grown = w.NewChildOf(home, mum, dad, AgeStage.Adult);
            var little = w.NewChildOf(home, mum, dad, AgeStage.Infant);

            w.Deaths.Die(dad, Reasons.None);
            w.Deaths.Die(mum, Reasons.None);

            Assert.Multiple(() =>
            {
                Assert.That(home.Members, Is.EqualTo(new[] { grown, little }));
                Assert.That(grandparents.Members.Count, Is.EqualTo(2));
            });
        }

        [Test]
        public void The_dead_are_struck_from_their_band_and_a_dead_leader_leaves_no_leader()
        {
            var w = new HouseholdWorld();
            var band = w.NewBand();
            var otherBand = w.NewBand();
            var leader = w.NewPerson(AgeStage.Adult, Sex.Male);
            var follower = w.NewPerson(AgeStage.Adult, Sex.Female);
            var elsewhere = w.NewPerson(AgeStage.Adult, Sex.Female);
            band.AddMember(leader);
            band.AddMember(follower);
            band.Leader = leader;
            otherBand.AddMember(elsewhere);

            w.Deaths.Die(leader, Reasons.None);

            Assert.Multiple(() =>
            {
                Assert.That(band.Members, Is.EqualTo(new[] { follower }));
                Assert.That(band.Leader, Is.EqualTo(PersonHandle.None));
                Assert.That(otherBand.Members, Is.EqualTo(new[] { elsewhere }));
            });

            w.Deaths.Die(follower, Reasons.None);

            Assert.Multiple(() =>
            {
                Assert.That(band.Members, Is.Empty);
                Assert.That(band.Leader, Is.EqualTo(PersonHandle.None), "still none; a follower's death changes nothing there");
            });
        }

        [Test]
        public void Dying_inside_a_scheduled_handler_is_fine()
        {
            var w = new HouseholdWorld();
            var person = w.NewPerson(AgeStage.Adult, Sex.Female);
            var router = new ScheduledEventRouter();
            router.Register(ScheduledEventKind.BirthCheck, new Killer(w, person));
            w.Clock.Schedule(SimulationTime.FromDays(1L), SimulationPhase.Lifecycle, ScheduledEventKind.BirthCheck, w.IdOf(person), EntityId.None);

            w.Clock.AdvanceTo(SimulationTime.FromDays(1L), router);

            Assert.That(w.People.IsAlive(person), Is.False);
        }

        [Test]
        public void Dying_inside_a_subscriber_is_the_recursion_the_bus_refuses()
        {
            var w = new HouseholdWorld();
            var victim = w.NewPerson(AgeStage.Adult, Sex.Male);
            var first = w.NewPerson(AgeStage.Adult, Sex.Female);
            w.Bus.Subscribe(new Avenger(w, victim));

            Assert.Multiple(() =>
            {
                Assert.That(() => w.Deaths.Die(first, Reasons.None), Throws.InvalidOperationException);
                Assert.That(w.People.IsAlive(victim), Is.True);
            });
        }

        private sealed class Killer : IScheduledEventHandler
        {
            private readonly HouseholdWorld _world;
            private readonly PersonHandle _person;

            public Killer(HouseholdWorld world, PersonHandle person)
            {
                _world = world;
                _person = person;
            }

            public void Handle(ScheduledEvent scheduled, SimulationClock clock) =>
                _world.Deaths.Die(_person, Reasons.None);
        }

        private sealed class Avenger : IDomainEventSubscriber
        {
            private readonly HouseholdWorld _world;
            private readonly PersonHandle _victim;

            public Avenger(HouseholdWorld world, PersonHandle victim)
            {
                _world = world;
                _victim = victim;
            }

            public void On(in DomainEvent published)
            {
                if (published.Kind == DomainEventKind.PersonDied && _world.People.IsAlive(_victim))
                {
                    _world.Deaths.Die(_victim, Reasons.None);
                }
            }
        }
    }
}
