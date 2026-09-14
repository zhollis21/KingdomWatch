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
    public sealed class HouseholdsTests
    {
        [Test]
        public void Construction_refuses_a_missing_collaborator()
        {
            var world = new HouseholdWorld();

            Assert.Multiple(() =>
            {
                Assert.That(() => new Households(null!, world.People, world.Housing), Throws.ArgumentNullException);
                Assert.That(() => new Households(world.Bus, null!, world.Housing), Throws.ArgumentNullException);
                Assert.That(() => new Households(world.Bus, world.People, null!), Throws.ArgumentNullException);
            });
        }

        [Test]
        public void Forming_claims_a_home_stamps_the_time_and_announces_it()
        {
            var world = new HouseholdWorld();
            world.Advance(SimulationTime.TicksPerDay);

            var household = world.Households.Form();

            Assert.Multiple(() =>
            {
                Assert.That(household.Id.Kind, Is.EqualTo(EntityKind.Household));
                Assert.That(household.Home, Is.EqualTo(EntityId.None), "camp space has no home to name");
                Assert.That(household.FormedAt, Is.EqualTo(SimulationTime.FromDays(1L)));
                Assert.That(household.Members, Is.Empty);
                Assert.That(world.Households.Count, Is.EqualTo(1));
                Assert.That(world.Households.All, Is.EqualTo(new[] { household }));
                Assert.That(world.Households.TryGet(household.Id, out var found), Is.True);
                Assert.That(found, Is.SameAs(household));
                Assert.That(world.LastPublished().Kind, Is.EqualTo(DomainEventKind.HouseholdFormed));
                Assert.That(world.LastPublished().PrimaryEntity, Is.EqualTo(household.Id));
            });
        }

        [Test]
        public void Forming_refuses_when_the_housing_is_full()
        {
            var world = new HouseholdWorld(FamilyFormationSettings.Default, new FullHousing());

            Assert.Multiple(() =>
            {
                Assert.That(world.Households.HasVacancy, Is.False);
                Assert.That(() => world.Households.Form(), Throws.InvalidOperationException);
                Assert.That(world.Households.Count, Is.Zero);
                Assert.That(world.Journal.Count, Is.Zero, "nothing was announced");
            });
        }

        [Test]
        public void Households_iterate_oldest_first()
        {
            var world = new HouseholdWorld();
            var first = world.Households.Form();
            var second = world.Households.Form();
            var third = world.Households.Form();

            world.Households.Dissolve(second);

            Assert.That(world.Households.All, Is.EqualTo(new[] { first, third }));
        }

        [Test]
        public void Joining_records_the_household_on_the_person_and_the_person_on_the_household()
        {
            var world = new HouseholdWorld();
            var household = world.Households.Form();
            var person = world.NewPerson(AgeStage.Adult, Sex.Female);

            world.Households.Join(household, person);

            Assert.Multiple(() =>
            {
                Assert.That(household.Members, Is.EqualTo(new[] { person }));
                Assert.That(world.People.GetHousehold(person), Is.EqualTo(household.Id));
                Assert.That(world.Households.Of(person), Is.SameAs(household));
            });
        }

        [Test]
        public void A_person_is_in_at_most_one_household()
        {
            var world = new HouseholdWorld();
            var first = world.Households.Form();
            var second = world.Households.Form();
            var person = world.NewPerson(AgeStage.Adult, Sex.Female);
            world.Households.Join(first, person);

            Assert.Multiple(() =>
            {
                Assert.That(() => world.Households.Join(second, person), Throws.InvalidOperationException);
                Assert.That(() => world.Households.Join(first, person), Throws.InvalidOperationException);
                Assert.That(first.Members, Is.EqualTo(new[] { person }));
                Assert.That(second.Members, Is.Empty);
            });
        }

        [Test]
        public void Leaving_clears_the_person_and_leaves_the_household_standing()
        {
            var world = new HouseholdWorld();
            var household = world.Households.Form();
            var person = world.NewPerson(AgeStage.Adult, Sex.Female);
            world.Households.Join(household, person);

            world.Households.Leave(person);

            Assert.Multiple(() =>
            {
                Assert.That(household.Members, Is.Empty);
                Assert.That(world.People.GetHousehold(person), Is.EqualTo(EntityId.None));
                Assert.That(world.Households.Of(person), Is.Null);
                Assert.That(world.Households.Count, Is.EqualTo(1), "an empty household is not dissolved by leaving it");
                Assert.That(() => world.Households.Leave(person), Throws.InvalidOperationException, "not in one");
            });
        }

        [Test]
        public void Dissolving_releases_the_home_forgets_the_household_and_announces_it()
        {
            var housing = new CountedHousing();
            var world = new HouseholdWorld(FamilyFormationSettings.Default, housing);
            var household = world.Households.Form();

            world.Households.Dissolve(household);

            Assert.Multiple(() =>
            {
                Assert.That(world.Households.Count, Is.Zero);
                Assert.That(world.Households.TryGet(household.Id, out _), Is.False);
                Assert.That(housing.Released, Is.EqualTo(new[] { household.Home }));
                Assert.That(world.LastPublished().Kind, Is.EqualTo(DomainEventKind.HouseholdDissolved));
                Assert.That(world.LastPublished().PrimaryEntity, Is.EqualTo(household.Id));
                Assert.That(world.LastPublished().SecondaryEntity, Is.EqualTo(household.Home));
            });
        }

        [Test]
        public void Dissolving_refuses_a_household_with_members_and_one_it_does_not_know()
        {
            var world = new HouseholdWorld();
            var household = world.Households.Form();
            var person = world.NewPerson(AgeStage.Adult, Sex.Female);
            world.Households.Join(household, person);
            var other = new HouseholdWorld().Households.Form();

            Assert.Multiple(() =>
            {
                Assert.That(() => world.Households.Dissolve(household), Throws.InvalidOperationException);
                Assert.That(() => world.Households.Dissolve(other), Throws.InvalidOperationException);
                Assert.That(() => world.Households.Dissolve(null!), Throws.ArgumentNullException);
                Assert.That(() => world.Households.Join(other, world.NewPerson(AgeStage.Adult, Sex.Male)), Throws.InvalidOperationException);
                Assert.That(world.Households.Count, Is.EqualTo(1));
            });

            world.Households.Dissolve(world.Households.Form());
            world.Households.Leave(person);
            world.Households.Dissolve(household);

            Assert.That(() => world.Households.Dissolve(household), Throws.InvalidOperationException, "twice");
        }

        [Test]
        public void The_dead_cannot_join_or_leave_and_nothing_moves_when_refused()
        {
            var w = new HouseholdWorld();
            var household = w.Households.Form();
            var dead = w.NewPerson(AgeStage.Adult, Sex.Female);
            w.People.Remove(dead);

            Assert.Multiple(() =>
            {
                Assert.That(() => w.Households.Join(household, dead), Throws.ArgumentException);
                Assert.That(() => w.Households.Leave(dead), Throws.ArgumentException);
                Assert.That(() => w.Households.Of(dead), Throws.ArgumentException);
                Assert.That(household.Members, Is.Empty);
            });
        }

        [Test]
        public void Has_adult_refuses_a_household_it_does_not_know()
        {
            var w = new HouseholdWorld();
            var other = new HouseholdWorld().Households.Form();

            Assert.That(() => w.Households.HasAdult(other), Throws.InvalidOperationException);
        }

        [Test]
        public void Records_and_member_lists_agree_across_a_mixed_sequence()
        {
            var w = new HouseholdWorld(new FamilyFormationSettings(0L, false), new CampSpace());
            var first = w.NewCouple(out var a, out var b);
            var child = w.NewChildOf(first, a, b, AgeStage.Child);
            var second = w.NewCouple(out var c, out var d);
            var spare = w.Households.Form();
            var lodger = w.NewPerson(AgeStage.Elder, Sex.Male);
            w.Households.Join(spare, lodger);
            w.AssertHouseholdsConsistent();

            w.Households.Leave(lodger);
            w.Households.Dissolve(spare);
            w.Households.Join(second, lodger);
            w.Deaths.Die(b, Reasons.None);
            w.AssertHouseholdsConsistent();

            w.Family.Partner(a, w.NewPerson(AgeStage.Adult, Sex.Male), Reasons.None);
            w.Deaths.Die(c, Reasons.None);
            w.Deaths.Die(d, Reasons.None);
            w.AssertHouseholdsConsistent();

            Assert.Multiple(() =>
            {
                Assert.That(w.Households.Count, Is.EqualTo(2), "the remarried couple and the lodger");
                Assert.That(w.People.GetHousehold(child), Is.EqualTo(w.People.GetHousehold(a)));
            });
        }

        [Test]
        public void A_housed_person_cannot_be_removed_until_they_leave()
        {
            var w = new HouseholdWorld();
            var household = w.Households.Form();
            var person = w.NewPerson(AgeStage.Adult, Sex.Female);
            w.Households.Join(household, person);

            Assert.Multiple(() =>
            {
                Assert.That(() => w.People.Remove(person), Throws.InvalidOperationException);
                Assert.That(w.People.IsAlive(person), Is.True, "a refused Remove changed nothing");
                Assert.That(household.Members, Is.EqualTo(new[] { person }));
            });

            w.Households.Leave(person);

            Assert.That(() => w.People.Remove(person), Throws.Nothing);
        }

        [Test]
        public void A_refused_dissolution_announcement_leaves_the_household_standing()
        {
            var housing = new CountedHousing();
            var w = new HouseholdWorld(FamilyFormationSettings.Default, housing);
            w.Bus.Subscribe(new Refuser(DomainEventKind.HouseholdDissolved));
            var household = w.Households.Form();

            Assert.Multiple(() =>
            {
                Assert.That(() => w.Households.Dissolve(household), Throws.InvalidOperationException);
                Assert.That(w.Households.TryGet(household.Id, out _), Is.True);
                Assert.That(w.Households.All, Is.EqualTo(new[] { household }));
                Assert.That(housing.Released, Is.Empty, "the home was not given back");
            });
        }

        [Test]
        public void A_refused_formation_announcement_registers_nothing()
        {
            var w = new HouseholdWorld();
            w.Bus.Subscribe(new Refuser(DomainEventKind.HouseholdFormed));

            Assert.Multiple(() =>
            {
                Assert.That(() => w.Households.Form(), Throws.InvalidOperationException);
                Assert.That(w.Households.Count, Is.Zero);
            });
        }

        [Test]
        public void Has_adult_is_whether_anyone_grown_is_in_it()
        {
            var world = new HouseholdWorld();
            var household = world.Households.Form();
            var child = world.NewPerson(AgeStage.Child, Sex.Female);
            var adolescent = world.NewPerson(AgeStage.Adolescent, Sex.Male);
            world.Households.Join(household, child);
            world.Households.Join(household, adolescent);

            Assert.That(world.Households.HasAdult(household), Is.False, "an adolescent is not an adult");

            var elder = world.NewPerson(AgeStage.Elder, Sex.Female);
            world.Households.Join(household, elder);

            Assert.That(world.Households.HasAdult(household), Is.True);
        }

        [Test]
        public void Camp_space_hands_out_no_id_and_takes_back_nothing_else()
        {
            var camp = new CampSpace();
            var ids = new IdAllocator();

            Assert.Multiple(() =>
            {
                Assert.That(camp.HasVacancy, Is.True);
                Assert.That(camp.Claim(), Is.EqualTo(EntityId.None));
                Assert.That(() => camp.Release(EntityId.None), Throws.Nothing);
                Assert.That(() => camp.Release(ids.Next(EntityKind.Settlement)), Throws.ArgumentException);
            });
        }

        // A subscriber that refuses one kind of event - the wiring bug the bus
        // propagates rather than swallows.
        private sealed class Refuser : IDomainEventSubscriber
        {
            private readonly DomainEventKind _refused;

            public Refuser(DomainEventKind refused)
            {
                _refused = refused;
            }

            public void On(in DomainEvent published)
            {
                if (published.Kind == _refused)
                {
                    throw new InvalidOperationException("refused " + published.Kind);
                }
            }
        }

        // Housing with no room: what a settled band's stock will look like at
        // its worst.
        private sealed class FullHousing : IHousing
        {
            public bool HasVacancy => false;

            public EntityId Claim() => throw new InvalidOperationException("full");

            public void Release(EntityId home)
            {
            }
        }

        // Housing whose homes have ids, so a test can see them come back.
        private sealed class CountedHousing : IHousing
        {
            private readonly IdAllocator _ids = new IdAllocator();

            public List<EntityId> Released { get; } = new List<EntityId>();

            public bool HasVacancy => true;

            public EntityId Claim() => _ids.Next(EntityKind.Settlement);

            public void Release(EntityId home) => Released.Add(home);
        }
    }
}
