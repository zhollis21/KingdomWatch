using System;
using KingdomWatch.Core.Data;
using NUnit.Framework;

namespace KingdomWatch.Core.Tests.Data
{
    [TestFixture]
    public sealed class PersonHandleTests
    {
        [Test]
        public void Same_index_and_generation_are_equal()
        {
            var left = new PersonHandle(4, 1);
            var right = new PersonHandle(4, 1);

            Assert.Multiple(() =>
            {
                Assert.That(left, Is.EqualTo(right));
                Assert.That(left.GetHashCode(), Is.EqualTo(right.GetHashCode()));
            });
        }

        [Test]
        public void A_recycled_slot_does_not_match_the_old_handle()
        {
            // This is the whole reason the generation exists: slot 4 has been
            // reused, and the stale handle must not resolve to whoever moved in.
            var stale = new PersonHandle(4, 1);
            var current = new PersonHandle(4, 2);

            Assert.That(stale, Is.Not.EqualTo(current));
        }

        [Test]
        public void Generation_zero_means_no_person()
        {
            Assert.Multiple(() =>
            {
                Assert.That(default(PersonHandle), Is.EqualTo(PersonHandle.None));
                Assert.That(PersonHandle.None.IsNone, Is.True);
                Assert.That(new PersonHandle(0, 1).IsNone, Is.False);
            });
        }

        [Test]
        public void Negative_index_or_generation_is_rejected()
        {
            Assert.Multiple(() =>
            {
                Assert.That(
                    () => new PersonHandle(-1, 1),
                    Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(
                    () => new PersonHandle(1, -1),
                    Throws.TypeOf<ArgumentOutOfRangeException>());
            });
        }

        [Test]
        public void ToString_shows_index_and_generation()
        {
            Assert.Multiple(() =>
            {
                Assert.That(new PersonHandle(4, 2).ToString(), Is.EqualTo("Person[4:2]"));
                Assert.That(PersonHandle.None.ToString(), Is.EqualTo("None"));
            });
        }

        [Test]
        public void Equality_operators_match_typed_equality()
        {
            var handle = new PersonHandle(4, 1);
            var same = new PersonHandle(4, 1);
            var recycled = new PersonHandle(4, 2);
            object boxed = new PersonHandle(4, 1);

            Assert.Multiple(() =>
            {
                Assert.That(handle == same, Is.True);
                Assert.That(handle != recycled, Is.True);
                Assert.That(handle == recycled, Is.False);
                Assert.That(handle != same, Is.False);
                Assert.That(handle.Equals(boxed), Is.True);
                Assert.That(handle.Equals("not a handle"), Is.False);
                Assert.That(handle.Equals(null), Is.False);
            });
        }

        [Test]
        public void World_position_equality_operators_match_typed_equality()
        {
            var position = new WorldPosition(3, -4);
            var same = new WorldPosition(3, -4);
            var swapped = new WorldPosition(-4, 3);
            object boxed = new WorldPosition(3, -4);

            Assert.Multiple(() =>
            {
                Assert.That(position == same, Is.True);
                Assert.That(position != swapped, Is.True);
                Assert.That(position == swapped, Is.False);
                Assert.That(position.Equals(boxed), Is.True);
                Assert.That(position.Equals("not a position"), Is.False);
                Assert.That(position.GetHashCode(), Is.EqualTo(same.GetHashCode()));
            });
        }

        [Test]
        public void World_positions_compare_by_coordinates()
        {
            var position = new WorldPosition(3, -4);
            var same = new WorldPosition(3, -4);
            var swapped = new WorldPosition(-4, 3);

            Assert.Multiple(() =>
            {
                Assert.That(position, Is.EqualTo(same));
                Assert.That(position, Is.Not.EqualTo(swapped));
                Assert.That(position.ToString(), Is.EqualTo("(3, -4)"));
            });
        }
    }
}
