using System;
using KingdomWatch.Core.Data;
using NUnit.Framework;

namespace KingdomWatch.Core.Tests.Data
{
    [TestFixture]
    public sealed class ResourceQuantityTests
    {
        [Test]
        public void Holds_a_kind_and_a_positive_quantity()
        {
            var line = new ResourceQuantity(ResourceKind.Wood, 4);

            Assert.Multiple(() =>
            {
                Assert.That(line.Kind, Is.EqualTo(ResourceKind.Wood));
                Assert.That(line.Quantity, Is.EqualTo(4));
                Assert.That(line.ToString(), Is.EqualTo("Wood x4"));
            });
        }

        [Test]
        public void None_and_undefined_kinds_are_rejected()
        {
            // An enum is an int with names; any int casts in. The kind indexes
            // the ledger's arrays and is persisted, so an undefined one has to
            // stop here.
            Assert.Multiple(() =>
            {
                Assert.That(
                    () => new ResourceQuantity(ResourceKind.None, 1),
                    Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(
                    () => new ResourceQuantity((ResourceKind)999, 1),
                    Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(
                    () => new ResourceQuantity((ResourceKind)(-1), 1),
                    Throws.TypeOf<ArgumentOutOfRangeException>());
            });
        }

        [Test]
        public void Zero_and_negative_quantities_are_rejected()
        {
            Assert.Multiple(() =>
            {
                Assert.That(
                    () => new ResourceQuantity(ResourceKind.Food, 0),
                    Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(
                    () => new ResourceQuantity(ResourceKind.Food, -1),
                    Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(
                    () => new ResourceQuantity(ResourceKind.Food, int.MinValue),
                    Throws.TypeOf<ArgumentOutOfRangeException>());
            });
        }

        [Test]
        public void Equality_is_by_kind_and_quantity()
        {
            var a = new ResourceQuantity(ResourceKind.Stone, 2);
            var same = new ResourceQuantity(ResourceKind.Stone, 2);
            var otherKind = new ResourceQuantity(ResourceKind.Food, 2);
            var otherQuantity = new ResourceQuantity(ResourceKind.Stone, 3);

            Assert.Multiple(() =>
            {
                Assert.That(a, Is.EqualTo(same));
                Assert.That(a == same, Is.True);
                Assert.That(a.GetHashCode(), Is.EqualTo(same.GetHashCode()));
                Assert.That(a != otherKind, Is.True);
                Assert.That(a != otherQuantity, Is.True);
                Assert.That(a.Equals((object)same), Is.True);
                Assert.That(a.Equals(null), Is.False);
            });
        }

        [Test]
        public void The_largest_quantity_the_type_holds_is_valid()
        {
            var line = new ResourceQuantity(ResourceKind.Food, int.MaxValue);

            Assert.That(line.Quantity, Is.EqualTo(int.MaxValue));
        }

        [Test]
        public void Default_is_not_a_resource()
        {
            // A defaulted struct never ran the constructor. Anything that takes
            // one from outside has to check for this - Recipe does.
            var line = default(ResourceQuantity);

            Assert.Multiple(() =>
            {
                Assert.That(line.Kind, Is.EqualTo(ResourceKind.None));
                Assert.That(line.Quantity, Is.Zero);
            });
        }
    }
}
