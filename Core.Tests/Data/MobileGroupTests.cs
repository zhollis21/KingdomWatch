using System;
using System.Collections.Generic;
using KingdomWatch.Core.Data;
using NUnit.Framework;

namespace KingdomWatch.Core.Tests.Data
{
    [TestFixture]
    public sealed class MobileGroupTests
    {
        private static MobileGroup NewBand() => new MobileGroup(
            new EntityId(EntityKind.MobileGroup, 1UL),
            MobileGroupPurpose.NomadicBand,
            new WorldPosition(10, 20));

        [Test]
        public void A_new_band_starts_empty_leaderless_and_going_nowhere()
        {
            var band = NewBand();

            Assert.Multiple(() =>
            {
                Assert.That(band.Members, Is.Empty);
                Assert.That(band.Leader.IsNone, Is.True);
                Assert.That(band.Destination, Is.Null);
                Assert.That(band.Position, Is.EqualTo(new WorldPosition(10, 20)));
                Assert.That(band.Purpose, Is.EqualTo(MobileGroupPurpose.NomadicBand));
                Assert.That(band.SharedSupplies.Stock(ResourceKind.Food), Is.Zero);
            });
        }

        [Test]
        public void Each_band_has_its_own_supplies()
        {
            // Two bands sharing a ledger would be the second-representation
            // bug in a different shape: one band eating the other's food.
            var first = NewBand();
            var second = NewBand();

            first.SharedSupplies.Open(ResourceKind.Food, 5);

            Assert.Multiple(() =>
            {
                Assert.That(first.SharedSupplies, Is.Not.SameAs(second.SharedSupplies));
                Assert.That(second.SharedSupplies.Stock(ResourceKind.Food), Is.Zero);
            });
        }

        [Test]
        public void Members_keep_insertion_order()
        {
            // Systems iterate members and act on them, so the order has to be a
            // property of the data rather than of a hash bucket.
            var band = NewBand();
            var first = new PersonHandle(7, 1);
            var second = new PersonHandle(2, 1);
            var third = new PersonHandle(5, 1);

            band.AddMember(first);
            band.AddMember(second);
            band.AddMember(third);

            Assert.That(band.Members, Is.EqualTo(new[] { first, second, third }));
        }

        [Test]
        public void Members_cannot_be_mutated_behind_the_guards()
        {
            // AddMember rejects duplicates and None because a person present
            // twice breaks the rule that every living person has exactly one
            // current spatial presence. Handing back the live list would make
            // those checks advisory - a downcast walks straight past them, and
            // can reorder members, which is part of the determinism contract.
            var band = NewBand();
            band.AddMember(new PersonHandle(7, 1));

            Assert.Multiple(() =>
            {
                Assert.That(band.Members, Is.Not.InstanceOf<List<PersonHandle>>());
                Assert.That(
                    () => ((IList<PersonHandle>)band.Members).Add(new PersonHandle(9, 1)),
                    Throws.TypeOf<NotSupportedException>());
                Assert.That(band.Members, Has.Count.EqualTo(1));
            });
        }

        [Test]
        public void Adding_the_same_person_twice_is_refused()
        {
            // A person in one group twice breaks the rule that every living
            // person has exactly one current spatial presence.
            var band = NewBand();
            var walker = new PersonHandle(7, 1);
            band.AddMember(walker);

            Assert.That(
                () => band.AddMember(walker),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public void A_recycled_slot_can_join_even_though_the_old_handle_did()
        {
            var band = NewBand();
            band.AddMember(new PersonHandle(7, 1));

            Assert.That(() => band.AddMember(new PersonHandle(7, 2)), Throws.Nothing);
        }

        [Test]
        public void The_none_handle_cannot_be_a_member()
        {
            var band = NewBand();

            Assert.That(
                () => band.AddMember(PersonHandle.None),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public void Removing_reports_whether_the_person_was_there()
        {
            var band = NewBand();
            var walker = new PersonHandle(7, 1);
            band.AddMember(walker);

            Assert.Multiple(() =>
            {
                Assert.That(band.RemoveMember(walker), Is.True);
                Assert.That(band.RemoveMember(walker), Is.False);
                Assert.That(band.Members, Is.Empty);
            });
        }

        [Test]
        public void A_group_needs_an_id_of_its_own_kind()
        {
            Assert.That(
                () => new MobileGroup(
                    new EntityId(EntityKind.Person, 1UL),
                    MobileGroupPurpose.NomadicBand,
                    default),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public void A_group_needs_a_real_purpose()
        {
            Assert.That(
                () => new MobileGroup(
                    new EntityId(EntityKind.MobileGroup, 1UL),
                    MobileGroupPurpose.None,
                    default),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void An_undefined_purpose_is_rejected()
        {
            // Purpose is persisted with the group, so an unrecognised value
            // would reach saves and leave a group nothing knows how to run.
            Assert.That(
                () => new MobileGroup(
                    new EntityId(EntityKind.MobileGroup, 1UL),
                    (MobileGroupPurpose)999,
                    default),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void All_three_launch_purposes_are_constructible()
        {
            var purposes = new[]
            {
                MobileGroupPurpose.NomadicBand,
                MobileGroupPurpose.FoundingParty,
                MobileGroupPurpose.Army,
            };

            foreach (var purpose in purposes)
            {
                var group = new MobileGroup(
                    new EntityId(EntityKind.MobileGroup, 1UL), purpose, default);

                Assert.That(group.Purpose, Is.EqualTo(purpose));
            }
        }
    }
}
