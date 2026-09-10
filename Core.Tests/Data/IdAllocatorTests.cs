using System;
using System.Collections.Generic;
using KingdomWatch.Core.Data;
using NUnit.Framework;

namespace KingdomWatch.Core.Tests.Data
{
    /// <summary>
    /// "Never reused" is the guarantee history, grievances and saves rest on,
    /// so it is tested rather than asserted in a comment.
    /// </summary>
    [TestFixture]
    public sealed class IdAllocatorTests
    {
        [Test]
        public void Ids_start_at_one_so_zero_stays_none()
        {
            var allocator = new IdAllocator();

            Assert.Multiple(() =>
            {
                Assert.That(allocator.Next(EntityKind.Person).Value, Is.EqualTo(1UL));
                Assert.That(allocator.NextEvent().Value, Is.EqualTo(1UL));
            });
        }

        [Test]
        public void Each_kind_counts_independently()
        {
            var allocator = new IdAllocator();

            var firstPerson = allocator.Next(EntityKind.Person);
            var firstSettlement = allocator.Next(EntityKind.Settlement);
            var secondPerson = allocator.Next(EntityKind.Person);

            Assert.Multiple(() =>
            {
                Assert.That(firstPerson, Is.EqualTo(new EntityId(EntityKind.Person, 1UL)));
                Assert.That(firstSettlement, Is.EqualTo(new EntityId(EntityKind.Settlement, 1UL)));
                Assert.That(secondPerson, Is.EqualTo(new EntityId(EntityKind.Person, 2UL)));
            });
        }

        [Test]
        public void Ids_are_never_handed_out_twice()
        {
            var allocator = new IdAllocator();
            var seen = new HashSet<EntityId>();

            for (var i = 0; i < 500; i++)
            {
                foreach (EntityKind kind in Enum.GetValues(typeof(EntityKind)))
                {
                    if (kind == EntityKind.None)
                    {
                        continue;
                    }

                    Assert.That(
                        seen.Add(allocator.Next(kind)),
                        Is.True,
                        "Allocator handed out a duplicate id.");
                }
            }
        }

        [Test]
        public void The_none_kind_is_not_allocatable()
        {
            var allocator = new IdAllocator();

            Assert.That(
                () => allocator.Next(EntityKind.None),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void An_undefined_kind_is_rejected()
        {
            var allocator = new IdAllocator();

            Assert.That(
                () => allocator.Next((EntityKind)999),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void A_negative_kind_cast_is_rejected()
        {
            // Enums in C# are just their underlying integer, so a bad cast can
            // produce a negative kind and index outside the counter array.
            var allocator = new IdAllocator();

            Assert.That(
                () => allocator.Next((EntityKind)(-1)),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void Peek_and_resume_reject_bad_kinds_too()
        {
            var allocator = new IdAllocator();

            Assert.Multiple(() =>
            {
                Assert.That(
                    () => allocator.PeekNext(EntityKind.None),
                    Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(
                    () => allocator.ResumeFrom((EntityKind)999, 5UL),
                    Throws.TypeOf<ArgumentOutOfRangeException>());
            });
        }

        [Test]
        public void Peek_reports_what_the_next_call_will_hand_out()
        {
            var allocator = new IdAllocator();
            allocator.Next(EntityKind.Person);
            allocator.NextEvent();

            Assert.Multiple(() =>
            {
                Assert.That(allocator.PeekNext(EntityKind.Person), Is.EqualTo(2UL));
                Assert.That(allocator.PeekNextEvent(), Is.EqualTo(2UL));
            });
        }

        [Test]
        public void Resuming_a_save_continues_without_reissuing()
        {
            var original = new IdAllocator();
            for (var i = 0; i < 10; i++)
            {
                original.Next(EntityKind.Person);
                original.NextEvent();
            }

            var reloaded = new IdAllocator();
            reloaded.ResumeFrom(EntityKind.Person, original.PeekNext(EntityKind.Person));
            reloaded.ResumeEventsFrom(original.PeekNextEvent());

            Assert.Multiple(() =>
            {
                Assert.That(reloaded.Next(EntityKind.Person).Value, Is.EqualTo(11UL));
                Assert.That(reloaded.NextEvent().Value, Is.EqualTo(11UL));
            });
        }

        [Test]
        public void Resuming_backwards_is_refused()
        {
            // A load that rewinds a counter starts reissuing ids that history
            // already points at. Failing loudly beats corrupting quietly.
            var allocator = new IdAllocator();
            allocator.Next(EntityKind.Person);
            allocator.Next(EntityKind.Person);
            allocator.NextEvent();
            allocator.NextEvent();

            Assert.Multiple(() =>
            {
                Assert.That(
                    () => allocator.ResumeFrom(EntityKind.Person, 2UL),
                    Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(
                    () => allocator.ResumeEventsFrom(2UL),
                    Throws.TypeOf<ArgumentOutOfRangeException>());
            });
        }

        [Test]
        public void Resuming_to_the_same_position_is_allowed()
        {
            var allocator = new IdAllocator();
            allocator.Next(EntityKind.Person);

            Assert.Multiple(() =>
            {
                Assert.That(() => allocator.ResumeFrom(EntityKind.Person, 2UL), Throws.Nothing);
                Assert.That(allocator.Next(EntityKind.Person).Value, Is.EqualTo(2UL));
            });
        }

        [Test]
        public void Exhausting_a_counter_fails_loudly_rather_than_wrapping()
        {
            // Unreachable in a real world, but wrapping would silently reuse
            // id 1 and repoint every reference to the first person ever born.
            var allocator = new IdAllocator();
            allocator.ResumeFrom(EntityKind.Person, ulong.MaxValue);
            allocator.ResumeEventsFrom(ulong.MaxValue);

            Assert.Multiple(() =>
            {
                Assert.That(
                    () => allocator.Next(EntityKind.Person),
                    Throws.TypeOf<InvalidOperationException>());
                Assert.That(
                    () => allocator.NextEvent(),
                    Throws.TypeOf<InvalidOperationException>());
            });
        }
    }
}
