using System;
using System.Collections.Generic;
using KingdomWatch.Core.Data;
using NUnit.Framework;

namespace KingdomWatch.Core.Tests.Data
{
    /// <summary>
    /// The identity value types. The property that matters most here is that
    /// kind is part of identity - without it, a durable reference to
    /// Settlement 1 would compare equal to one to Person 1, which is exactly
    /// the history-corruption bug the split exists to prevent.
    /// </summary>
    [TestFixture]
    public sealed class EntityIdTests
    {
        [Test]
        public void Same_kind_and_value_are_equal()
        {
            var left = new EntityId(EntityKind.Person, 1UL);
            var right = new EntityId(EntityKind.Person, 1UL);

            Assert.Multiple(() =>
            {
                Assert.That(left, Is.EqualTo(right));
                Assert.That(left == right, Is.True);
                Assert.That(left.GetHashCode(), Is.EqualTo(right.GetHashCode()));
            });
        }

        [Test]
        public void Same_value_of_a_different_kind_is_not_equal()
        {
            var person = new EntityId(EntityKind.Person, 1UL);
            var settlement = new EntityId(EntityKind.Settlement, 1UL);

            Assert.Multiple(() =>
            {
                Assert.That(person, Is.Not.EqualTo(settlement));
                Assert.That(person != settlement, Is.True);
            });
        }

        [Test]
        public void Default_is_none()
        {
            Assert.Multiple(() =>
            {
                Assert.That(default(EntityId), Is.EqualTo(EntityId.None));
                Assert.That(EntityId.None.IsNone, Is.True);
                Assert.That(new EntityId(EntityKind.Person, 1UL).IsNone, Is.False);
            });
        }

        [Test]
        public void Ordering_is_by_kind_then_value()
        {
            var ids = new List<EntityId>
            {
                new EntityId(EntityKind.Settlement, 1UL),
                new EntityId(EntityKind.Person, 2UL),
                new EntityId(EntityKind.Person, 1UL),
            };

            ids.Sort();

            Assert.That(
                ids,
                Is.EqualTo(new[]
                {
                    new EntityId(EntityKind.Person, 1UL),
                    new EntityId(EntityKind.Person, 2UL),
                    new EntityId(EntityKind.Settlement, 1UL),
                }));
        }

        [Test]
        public void Ordering_is_a_total_order_over_a_mixed_set()
        {
            // The scheduler breaks ties on entity id, so a partial order would
            // leave event order depending on collection iteration - which
            // section 4 calls out as the way determinism silently dies.
            var ids = new List<EntityId>();

            foreach (EntityKind kind in Enum.GetValues(typeof(EntityKind)))
            {
                for (var value = 1UL; value <= 3UL; value++)
                {
                    ids.Add(new EntityId(kind, value));
                }
            }

            ids.Sort();

            for (var i = 1; i < ids.Count; i++)
            {
                Assert.That(
                    ids[i - 1].CompareTo(ids[i]),
                    Is.LessThan(0),
                    "No two distinct ids may compare equal: " + ids[i - 1] + " vs " + ids[i]);
            }
        }

        [Test]
        public void ToString_names_the_kind_and_the_value()
        {
            Assert.Multiple(() =>
            {
                Assert.That(new EntityId(EntityKind.Person, 1234UL).ToString(), Is.EqualTo("Person#1234"));
                Assert.That(EntityId.None.ToString(), Is.EqualTo("None"));
            });
        }

        [Test]
        public void Comparison_operators_agree_with_CompareTo()
        {
            var low = new EntityId(EntityKind.Person, 1UL);
            var high = new EntityId(EntityKind.Person, 2UL);
            var alsoLow = new EntityId(EntityKind.Person, 1UL);

            Assert.Multiple(() =>
            {
                Assert.That(low < high, Is.True);
                Assert.That(high > low, Is.True);
                Assert.That(low <= alsoLow, Is.True);
                Assert.That(low >= alsoLow, Is.True);
                Assert.That(high < low, Is.False);
                Assert.That(low > high, Is.False);
                Assert.That(high <= low, Is.False);
                Assert.That(low >= high, Is.False);
            });
        }

        [Test]
        public void Untyped_equality_matches_typed_equality()
        {
            object same = new EntityId(EntityKind.Person, 1UL);
            object otherKind = new EntityId(EntityKind.Settlement, 1UL);
            var id = new EntityId(EntityKind.Person, 1UL);

            Assert.Multiple(() =>
            {
                Assert.That(id.Equals(same), Is.True);
                Assert.That(id.Equals(otherKind), Is.False);
                Assert.That(id.Equals("not an id"), Is.False);
                Assert.That(id.Equals(null), Is.False);
            });
        }

        [Test]
        public void Event_id_operators_agree_with_CompareTo()
        {
            var low = new EventId(1UL);
            var high = new EventId(2UL);
            var alsoLow = new EventId(1UL);
            object boxed = new EventId(1UL);

            Assert.Multiple(() =>
            {
                Assert.That(low == alsoLow, Is.True);
                Assert.That(low != high, Is.True);
                Assert.That(low < high, Is.True);
                Assert.That(high > low, Is.True);
                Assert.That(low <= alsoLow, Is.True);
                Assert.That(low >= alsoLow, Is.True);
                Assert.That(high < low, Is.False);
                Assert.That(low > high, Is.False);
                Assert.That(low.Equals(boxed), Is.True);
                Assert.That(low.Equals("not an event"), Is.False);
                Assert.That(low.GetHashCode(), Is.EqualTo(alsoLow.GetHashCode()));
                Assert.That(EventId.None.ToString(), Is.EqualTo("None"));
            });
        }

        [Test]
        public void Event_ids_compare_and_print_by_value()
        {
            var first = new EventId(1UL);
            var second = new EventId(2UL);

            Assert.Multiple(() =>
            {
                Assert.That(first, Is.EqualTo(new EventId(1UL)));
                Assert.That(first, Is.Not.EqualTo(second));
                Assert.That(first.CompareTo(second), Is.LessThan(0));
                Assert.That(first.ToString(), Is.EqualTo("Event#1"));
                Assert.That(EventId.None.IsNone, Is.True);
            });
        }
    }
}
