using System;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Relationships;
using NUnit.Framework;

namespace KingdomWatch.Core.Tests.Relationships
{
    [TestFixture]
    public sealed class PartnershipsTests
    {
        [Test]
        public void Forming_a_partnership_is_visible_from_both_sides_in_canonical_order()
        {
            var ids = new IdAllocator();
            var store = new Partnerships();
            var aldric = ids.Next(EntityKind.Person);
            var mira = ids.Next(EntityKind.Person);
            var wedding = ids.NextEvent();

            // Named the "wrong" way round on purpose.
            store.Form(mira, aldric, wedding, SimulationTime.FromDays(3));

            var expected = store.History(aldric)[0];

            Assert.Multiple(() =>
            {
                Assert.That(store.ActivePartnerOf(aldric), Is.EqualTo(mira));
                Assert.That(store.ActivePartnerOf(mira), Is.EqualTo(aldric));
                Assert.That(expected.First, Is.EqualTo(aldric), "lower id first");
                Assert.That(expected.Second, Is.EqualTo(mira));
                Assert.That(expected.FormedBy, Is.EqualTo(wedding));
                Assert.That(expected.FormedAt, Is.EqualTo(SimulationTime.FromDays(3)));
                Assert.That(expected.IsActive, Is.True);
                Assert.That(expected.EndedBy, Is.EqualTo(EventId.None));
                Assert.That(store.History(mira).ToArray(), Is.EqualTo(new[] { expected }), "same record both sides");
            });
        }

        [Test]
        public void Ending_marks_the_record_on_both_sides_and_keeps_it()
        {
            var ids = new IdAllocator();
            var store = new Partnerships();
            var aldric = ids.Next(EntityKind.Person);
            var mira = ids.Next(EntityKind.Person);
            store.Form(aldric, mira, ids.NextEvent(), SimulationTime.FromDays(3));
            var death = ids.NextEvent();

            store.End(mira, aldric, death, SimulationTime.FromDays(400));

            Assert.Multiple(() =>
            {
                Assert.That(store.ActivePartnerOf(aldric), Is.EqualTo(EntityId.None));
                Assert.That(store.ActivePartnerOf(mira), Is.EqualTo(EntityId.None));
                Assert.That(store.History(aldric).Length, Is.EqualTo(1), "ended is not deleted");
                Assert.That(store.History(aldric)[0].IsActive, Is.False);
                Assert.That(store.History(aldric)[0].EndedBy, Is.EqualTo(death));
                Assert.That(store.History(aldric)[0].EndedAt, Is.EqualTo(SimulationTime.FromDays(400)));
                Assert.That(store.History(mira)[0], Is.EqualTo(store.History(aldric)[0]));
            });
        }

        [Test]
        public void A_widow_may_partner_again_and_keeps_both_records_oldest_first()
        {
            var ids = new IdAllocator();
            var store = new Partnerships();
            var mira = ids.Next(EntityKind.Person);
            var aldric = ids.Next(EntityKind.Person);
            var bram = ids.Next(EntityKind.Person);
            store.Form(mira, aldric, ids.NextEvent(), SimulationTime.FromDays(1));
            store.End(mira, aldric, ids.NextEvent(), SimulationTime.FromDays(2));

            store.Form(mira, bram, ids.NextEvent(), SimulationTime.FromDays(3));

            Assert.Multiple(() =>
            {
                Assert.That(store.ActivePartnerOf(mira), Is.EqualTo(bram));
                Assert.That(store.History(mira).Length, Is.EqualTo(2));
                Assert.That(store.History(mira)[0].PartnerOf(mira), Is.EqualTo(aldric));
                Assert.That(store.History(mira)[1].PartnerOf(mira), Is.EqualTo(bram));
                Assert.That(store.History(aldric)[0].IsActive, Is.False);
            });
        }

        [Test]
        public void One_active_partnership_per_person()
        {
            var ids = new IdAllocator();
            var store = new Partnerships();
            var mira = ids.Next(EntityKind.Person);
            var aldric = ids.Next(EntityKind.Person);
            var bram = ids.Next(EntityKind.Person);
            store.Form(mira, aldric, ids.NextEvent(), SimulationTime.Zero);

            Assert.Multiple(() =>
            {
                Assert.That(
                    () => store.Form(mira, bram, ids.NextEvent(), SimulationTime.Zero),
                    Throws.InvalidOperationException);
                Assert.That(
                    () => store.Form(bram, aldric, ids.NextEvent(), SimulationTime.Zero),
                    Throws.InvalidOperationException);
                Assert.That(
                    () => store.Form(mira, aldric, ids.NextEvent(), SimulationTime.Zero),
                    Throws.InvalidOperationException,
                    "already partnered with each other");
                Assert.That(store.History(bram).Length, Is.Zero, "a refused Form records nothing");
                Assert.That(store.History(mira).Length, Is.EqualTo(1));
            });
        }

        [Test]
        public void Ending_requires_an_active_partnership_between_exactly_those_two()
        {
            var ids = new IdAllocator();
            var store = new Partnerships();
            var mira = ids.Next(EntityKind.Person);
            var aldric = ids.Next(EntityKind.Person);
            var bram = ids.Next(EntityKind.Person);
            store.Form(mira, aldric, ids.NextEvent(), SimulationTime.Zero);

            Assert.Multiple(() =>
            {
                Assert.That(
                    () => store.End(mira, bram, ids.NextEvent(), SimulationTime.Zero),
                    Throws.InvalidOperationException,
                    "partnered, but not with bram");
                Assert.That(
                    () => store.End(bram, aldric, ids.NextEvent(), SimulationTime.Zero),
                    Throws.InvalidOperationException,
                    "bram has no partnership at all");
                Assert.That(store.ActivePartnerOf(mira), Is.EqualTo(aldric), "a refused End changes nothing");
            });

            store.End(mira, aldric, ids.NextEvent(), SimulationTime.Zero);

            Assert.That(
                () => store.End(mira, aldric, ids.NextEvent(), SimulationTime.Zero),
                Throws.InvalidOperationException,
                "already ended");
        }

        [Test]
        public void A_partnership_cannot_end_before_it_formed()
        {
            var ids = new IdAllocator();
            var store = new Partnerships();
            var mira = ids.Next(EntityKind.Person);
            var aldric = ids.Next(EntityKind.Person);
            store.Form(mira, aldric, ids.NextEvent(), SimulationTime.FromDays(10));

            Assert.Multiple(() =>
            {
                Assert.That(
                    () => store.End(mira, aldric, ids.NextEvent(), SimulationTime.FromDays(9)),
                    Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(store.ActivePartnerOf(mira), Is.EqualTo(aldric));
            });
        }

        [Test]
        public void Refused_inputs()
        {
            var ids = new IdAllocator();
            var store = new Partnerships();
            var mira = ids.Next(EntityKind.Person);
            var aldric = ids.Next(EntityKind.Person);
            var settlement = ids.Next(EntityKind.Settlement);
            var wedding = ids.NextEvent();

            Assert.Multiple(() =>
            {
                Assert.That(() => store.Form(EntityId.None, aldric, wedding, SimulationTime.Zero), Throws.ArgumentException);
                Assert.That(() => store.Form(mira, EntityId.None, wedding, SimulationTime.Zero), Throws.ArgumentException);
                Assert.That(() => store.Form(settlement, aldric, wedding, SimulationTime.Zero), Throws.ArgumentException);
                Assert.That(() => store.Form(mira, mira, wedding, SimulationTime.Zero), Throws.ArgumentException, "self");
                Assert.That(() => store.Form(mira, aldric, EventId.None, SimulationTime.Zero), Throws.ArgumentException, "no provenance");
                Assert.That(() => store.End(mira, aldric, EventId.None, SimulationTime.Zero), Throws.ArgumentException);
                Assert.That(() => store.End(mira, mira, wedding, SimulationTime.Zero), Throws.ArgumentException);
                Assert.That(() => store.End(EntityId.None, aldric, wedding, SimulationTime.Zero), Throws.ArgumentException);
                Assert.That(() => store.End(mira, settlement, wedding, SimulationTime.Zero), Throws.ArgumentException);
                Assert.That(() => store.History(EntityId.None), Throws.ArgumentException);
                Assert.That(() => store.ActivePartnerOf(EntityId.None), Throws.ArgumentException);
                Assert.That(() => store.ActivePartnerOf(settlement), Throws.ArgumentException);
                Assert.That(() => store.History(settlement), Throws.ArgumentException);
                Assert.That(store.History(mira).Length, Is.Zero, "nothing was recorded");
            });
        }

        [Test]
        public void Someone_never_partnered_has_an_empty_history_and_no_partner()
        {
            var ids = new IdAllocator();
            var store = new Partnerships();
            var loner = ids.Next(EntityKind.Person);

            Assert.Multiple(() =>
            {
                Assert.That(store.History(loner).Length, Is.Zero);
                Assert.That(store.ActivePartnerOf(loner), Is.EqualTo(EntityId.None));
            });
        }

        [Test]
        public void PartnerOf_refuses_an_outsider()
        {
            var ids = new IdAllocator();
            var store = new Partnerships();
            var mira = ids.Next(EntityKind.Person);
            var aldric = ids.Next(EntityKind.Person);
            var bram = ids.Next(EntityKind.Person);
            store.Form(mira, aldric, ids.NextEvent(), SimulationTime.Zero);
            var record = store.History(mira)[0];

            Assert.Multiple(() =>
            {
                Assert.That(record.Involves(bram), Is.False);
                Assert.That(() => record.PartnerOf(bram), Throws.ArgumentException);
            });
        }
    }
}
