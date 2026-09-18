using System;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Settlements;
using NUnit.Framework;

namespace KingdomWatch.Core.Tests.Settlements
{
    [TestFixture]
    public sealed class SettlementTests
    {
        private static readonly EntityId Id = new EntityId(EntityKind.Settlement, 3UL);
        private static readonly WorldPosition Here = new WorldPosition(4, 5);

        [Test]
        public void A_settlement_stands_where_it_was_founded_and_never_travels()
        {
            var settlement = new Settlement(Id, Here);

            Assert.Multiple(() =>
            {
                Assert.That(settlement.Id, Is.EqualTo(Id));
                Assert.That(settlement.Position, Is.EqualTo(Here));
                Assert.That(settlement.Destination, Is.Null);
                Assert.That(settlement.Members, Is.Empty);
                Assert.That(settlement.SharedSupplies.AuditBalances(), Is.True);
                Assert.That(settlement, Is.InstanceOf<ICommunity>());
            });
        }

        [Test]
        public void Construction_refuses_an_id_of_another_kind()
        {
            Assert.Multiple(() =>
            {
                Assert.That(() => new Settlement(new EntityId(EntityKind.MobileGroup, 3UL), Here), Throws.ArgumentException);
                Assert.That(() => new Settlement(EntityId.None, Here), Throws.ArgumentException);
            });
        }

        [Test]
        public void Members_are_kept_in_order_and_never_twice()
        {
            var settlement = new Settlement(Id, Here);
            var first = new PersonHandle(3, 1);
            var second = new PersonHandle(1, 1);

            settlement.AddMember(first);
            settlement.AddMember(second);

            Assert.Multiple(() =>
            {
                Assert.That(settlement.Members, Is.EqualTo(new[] { first, second }), "insertion order, not handle order");
                Assert.That(() => settlement.AddMember(first), Throws.ArgumentException);
                Assert.That(() => settlement.AddMember(PersonHandle.None), Throws.ArgumentException);
                Assert.That(settlement.RemoveMember(first), Is.True);
                Assert.That(settlement.RemoveMember(first), Is.False);
                Assert.That(settlement.Members, Is.EqualTo(new[] { second }));
            });
        }

        [Test]
        public void The_member_list_cannot_be_mutated_from_outside()
        {
            var settlement = new Settlement(Id, Here);
            settlement.AddMember(new PersonHandle(1, 1));

            Assert.That(settlement.Members, Is.Not.InstanceOf<System.Collections.Generic.List<PersonHandle>>());
            Assert.That(
                () => ((System.Collections.Generic.IList<PersonHandle>)settlement.Members).Add(new PersonHandle(2, 1)),
                Throws.TypeOf<NotSupportedException>());
        }
    }
}
