using System;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Relationships;
using NUnit.Framework;

namespace KingdomWatch.Core.Tests.Relationships
{
    [TestFixture]
    public sealed class MemoriesTests
    {
        // Old after ten days, forgotten after a hundred, promoted at four
        // witnesses, and no more than four tracked.
        private static readonly MemorySettings Settings = new MemorySettings(
            4, SimulationTime.TicksPerDay * 10, SimulationTime.TicksPerDay * 100, 4);

        [Test]
        public void Recording_stores_the_memory_and_its_witnesses_in_order()
        {
            var ids = new IdAllocator();
            var memories = new Memories(Settings);
            var oakshire = ids.Next(EntityKind.Settlement);
            var raider = ids.Next(EntityKind.Person);
            var raid = ids.NextEvent();
            var witnesses = new[] { ids.Next(EntityKind.Person), ids.Next(EntityKind.Person) };

            var taken = memories.Record(oakshire, raid, raider, -40, witnesses, SimulationTime.FromDays(1));

            Assert.Multiple(() =>
            {
                Assert.That(taken, Is.EqualTo(2));
                Assert.That(memories.TryGet(oakshire, raid, out var memory), Is.True);
                Assert.That(memory.Holder, Is.EqualTo(oakshire));
                Assert.That(memory.OriginEvent, Is.EqualTo(raid));
                Assert.That(memory.Subject, Is.EqualTo(raider));
                Assert.That(memory.Valence, Is.EqualTo(-40));
                Assert.That(memory.IsGrievance, Is.True);
                Assert.That(memory.Tier, Is.EqualTo(MemoryTier.Recent));
                Assert.That(memory.FormedAt, Is.EqualTo(SimulationTime.FromDays(1)));
                Assert.That(memory.WitnessCount, Is.EqualTo(2));
                Assert.That(memories.Witnesses(oakshire, raid).ToArray(), Is.EqualTo(witnesses));
                Assert.That(memories.Held(oakshire).Length, Is.EqualTo(1));
            });
        }

        [Test]
        public void A_witness_list_is_a_capped_set()
        {
            var ids = new IdAllocator();
            var memories = new Memories(new MemorySettings(3, 0L, 0L, 3));
            var holder = ids.Next(EntityKind.Person);
            var raid = ids.NextEvent();
            var a = ids.Next(EntityKind.Person);
            var b = ids.Next(EntityKind.Person);
            var c = ids.Next(EntityKind.Person);
            var d = ids.Next(EntityKind.Person);

            var taken = memories.Record(holder, raid, EntityId.None, 1, new[] { a, a, b, c, d }, SimulationTime.Zero);

            Assert.Multiple(() =>
            {
                Assert.That(taken, Is.EqualTo(3), "the duplicate and the one past the cap are not taken");
                Assert.That(memories.Witnesses(holder, raid).ToArray(), Is.EqualTo(new[] { a, b, c }));
                Assert.That(memories.Teach(holder, raid, d), Is.False, "still full");
                Assert.That(memories.Teach(holder, raid, a), Is.False, "already there");
            });
        }

        [Test]
        public void Teaching_adds_a_witness_so_a_grievance_can_be_inherited()
        {
            var ids = new IdAllocator();
            var memories = new Memories(Settings);
            var holder = ids.Next(EntityKind.Person);
            var raid = ids.NextEvent();
            var witness = ids.Next(EntityKind.Person);
            var descendant = ids.Next(EntityKind.Person);
            memories.Record(holder, raid, EntityId.None, -10, new[] { witness }, SimulationTime.Zero);

            var taught = memories.Teach(holder, raid, descendant);

            Assert.Multiple(() =>
            {
                Assert.That(taught, Is.True);
                Assert.That(memories.Witnesses(holder, raid).ToArray(), Is.EqualTo(new[] { witness, descendant }));
                Assert.That(memories.TryGet(holder, raid, out var memory) && memory.WitnessCount == 2, Is.True);
            });
        }

        [Test]
        public void A_witness_death_decrements_every_memory_they_were_on()
        {
            var ids = new IdAllocator();
            var memories = new Memories(Settings);
            var oakshire = ids.Next(EntityKind.Settlement);
            var dunvale = ids.Next(EntityKind.Settlement);
            var raid = ids.NextEvent();
            var miracle = ids.NextEvent();
            var elder = ids.Next(EntityKind.Person);
            var other = ids.Next(EntityKind.Person);
            memories.Record(oakshire, raid, EntityId.None, -10, new[] { elder, other }, SimulationTime.Zero);
            memories.Record(oakshire, miracle, EntityId.None, 10, new[] { elder }, SimulationTime.Zero);
            memories.Record(dunvale, raid, EntityId.None, -5, new[] { other, elder }, SimulationTime.Zero);

            memories.WitnessDied(elder);

            Assert.Multiple(() =>
            {
                Assert.That(memories.Witnesses(oakshire, raid).ToArray(), Is.EqualTo(new[] { other }));
                Assert.That(memories.Witnesses(oakshire, miracle).Length, Is.Zero);
                Assert.That(memories.Witnesses(dunvale, raid).ToArray(), Is.EqualTo(new[] { other }));
                Assert.That(memories.Held(oakshire).Length, Is.EqualTo(2), "a death forgets nothing by itself");
            });
        }

        [Test]
        public void Compact_ages_then_forgets_only_what_nobody_living_witnessed()
        {
            var ids = new IdAllocator();
            var memories = new Memories(Settings);
            var holder = ids.Next(EntityKind.Person);
            var witnessed = ids.NextEvent();
            var unwitnessed = ids.NextEvent();
            var witness = ids.Next(EntityKind.Person);
            memories.Record(holder, witnessed, EntityId.None, -10, new[] { witness }, SimulationTime.Zero);
            memories.Record(holder, unwitnessed, EntityId.None, -10, ReadOnlySpan<EntityId>.Empty, SimulationTime.Zero);

            memories.Compact(holder, SimulationTime.FromDays(9));
            Assert.That(memories.Held(holder)[0].Tier, Is.EqualTo(MemoryTier.Recent), "not old yet");

            memories.Compact(holder, SimulationTime.FromDays(10));
            Assert.Multiple(() =>
            {
                Assert.That(memories.Held(holder)[0].Tier, Is.EqualTo(MemoryTier.Old));
                Assert.That(memories.Held(holder)[1].Tier, Is.EqualTo(MemoryTier.Old));
                Assert.That(memories.Held(holder).Length, Is.EqualTo(2), "old is not forgotten");
            });

            memories.Compact(holder, SimulationTime.FromDays(100));
            Assert.Multiple(() =>
            {
                Assert.That(memories.Held(holder).Length, Is.EqualTo(1));
                Assert.That(memories.Held(holder)[0].OriginEvent, Is.EqualTo(witnessed), "someone still remembers");
            });

            memories.WitnessDied(witness);
            memories.Compact(holder, SimulationTime.FromDays(100));
            Assert.That(memories.Held(holder).Length, Is.Zero, "and now nobody does");
        }

        [Test]
        public void One_compact_call_can_age_and_forget_in_a_single_step()
        {
            // A memory nobody compacted for a century goes straight from
            // Recent to gone, not to Old with the forgetting owed next time.
            var ids = new IdAllocator();
            var memories = new Memories(Settings);
            var holder = ids.Next(EntityKind.Person);
            memories.Record(holder, ids.NextEvent(), EntityId.None, 1, ReadOnlySpan<EntityId>.Empty, SimulationTime.Zero);

            memories.Compact(holder, SimulationTime.FromDays(100));

            Assert.That(memories.Held(holder).Length, Is.Zero);
        }

        [Test]
        public void Enough_witnesses_promotes_at_record_and_a_promoted_memory_is_never_forgotten()
        {
            var ids = new IdAllocator();
            var memories = new Memories(Settings);
            var oakshire = ids.Next(EntityKind.Settlement);
            var miracle = ids.NextEvent();
            var crowd = new[]
            {
                ids.Next(EntityKind.Person), ids.Next(EntityKind.Person),
                ids.Next(EntityKind.Person), ids.Next(EntityKind.Person),
            };

            memories.Record(oakshire, miracle, EntityId.None, 50, crowd, SimulationTime.Zero);

            foreach (var person in crowd)
            {
                memories.WitnessDied(person);
            }

            memories.Compact(oakshire, SimulationTime.FromDays(10_000));

            Assert.Multiple(() =>
            {
                Assert.That(memories.TryGet(oakshire, miracle, out var memory), Is.True, "outlives everyone who saw it");
                Assert.That(memory.Tier, Is.EqualTo(MemoryTier.Promoted));
                Assert.That(memory.WitnessCount, Is.Zero);
            });
        }

        [Test]
        public void Teaching_can_promote_an_old_memory()
        {
            var ids = new IdAllocator();
            var memories = new Memories(Settings);
            var holder = ids.Next(EntityKind.Person);
            var feud = ids.NextEvent();
            memories.Record(
                holder, feud, EntityId.None, -30,
                new[] { ids.Next(EntityKind.Person), ids.Next(EntityKind.Person), ids.Next(EntityKind.Person) },
                SimulationTime.Zero);
            memories.Compact(holder, SimulationTime.FromDays(10));
            Assert.That(memories.Held(holder)[0].Tier, Is.EqualTo(MemoryTier.Old));

            memories.Teach(holder, feud, ids.Next(EntityKind.Person));

            Assert.That(memories.Held(holder)[0].Tier, Is.EqualTo(MemoryTier.Promoted));
        }

        [Test]
        public void One_memory_per_holder_per_event_and_holders_are_independent()
        {
            var ids = new IdAllocator();
            var memories = new Memories(Settings);
            var oakshire = ids.Next(EntityKind.Settlement);
            var dunvale = ids.Next(EntityKind.Settlement);
            var raid = ids.NextEvent();
            memories.Record(oakshire, raid, EntityId.None, -10, ReadOnlySpan<EntityId>.Empty, SimulationTime.Zero);

            Assert.Multiple(() =>
            {
                Assert.That(
                    () => memories.Record(oakshire, raid, EntityId.None, -10, ReadOnlySpan<EntityId>.Empty, SimulationTime.Zero),
                    Throws.InvalidOperationException);
                Assert.That(
                    () => memories.Record(dunvale, raid, EntityId.None, 10, ReadOnlySpan<EntityId>.Empty, SimulationTime.Zero),
                    Throws.Nothing,
                    "the same event means something different to another settlement");
                Assert.That(memories.Held(oakshire).Length, Is.EqualTo(1));
                Assert.That(memories.Held(dunvale).Length, Is.EqualTo(1));
            });
        }

        [Test]
        public void Time_does_not_run_backwards_and_a_refused_compact_changes_nothing()
        {
            // The later memory is recorded first, so a walk that ages as it
            // goes would forget the earlier one before finding the error.
            var ids = new IdAllocator();
            var memories = new Memories(Settings);
            var holder = ids.Next(EntityKind.Person);
            var later = ids.NextEvent();
            var earlier = ids.NextEvent();
            memories.Record(holder, later, EntityId.None, 1, ReadOnlySpan<EntityId>.Empty, SimulationTime.FromDays(500));
            memories.Record(holder, earlier, EntityId.None, 1, ReadOnlySpan<EntityId>.Empty, SimulationTime.Zero);

            Assert.Multiple(() =>
            {
                Assert.That(
                    () => memories.Compact(holder, SimulationTime.FromDays(400)),
                    Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(memories.Held(holder).Length, Is.EqualTo(2));
                Assert.That(memories.Held(holder)[1].Tier, Is.EqualTo(MemoryTier.Recent));
            });
        }

        [Test]
        public void Forgetting_the_first_memory_keeps_the_rest_in_order()
        {
            var ids = new IdAllocator();
            var memories = new Memories(Settings);
            var holder = ids.Next(EntityKind.Person);
            var forgotten = ids.NextEvent();
            var second = ids.NextEvent();
            var third = ids.NextEvent();
            var witness = ids.Next(EntityKind.Person);
            memories.Record(holder, forgotten, EntityId.None, 1, ReadOnlySpan<EntityId>.Empty, SimulationTime.Zero);
            memories.Record(holder, second, EntityId.None, 2, new[] { witness }, SimulationTime.Zero);
            memories.Record(holder, third, EntityId.None, 3, new[] { witness }, SimulationTime.Zero);

            memories.Compact(holder, SimulationTime.FromDays(100));

            Assert.Multiple(() =>
            {
                Assert.That(memories.Held(holder).Length, Is.EqualTo(2));
                Assert.That(memories.Held(holder)[0].OriginEvent, Is.EqualTo(second));
                Assert.That(memories.Held(holder)[1].OriginEvent, Is.EqualTo(third));
            });
        }

        [Test]
        public void Compact_and_a_death_for_nobody_relevant_are_no_ops()
        {
            var ids = new IdAllocator();
            var memories = new Memories(Settings);
            var holder = ids.Next(EntityKind.Person);
            memories.Record(holder, ids.NextEvent(), EntityId.None, 1, new[] { ids.Next(EntityKind.Person) }, SimulationTime.Zero);

            Assert.Multiple(() =>
            {
                Assert.That(() => memories.Compact(ids.Next(EntityKind.Settlement), SimulationTime.FromDays(9)), Throws.Nothing);
                Assert.That(() => memories.WitnessDied(ids.Next(EntityKind.Person)), Throws.Nothing);
                Assert.That(memories.Held(holder)[0].WitnessCount, Is.EqualTo(1));
            });
        }

        [Test]
        public void Refused_inputs()
        {
            var ids = new IdAllocator();
            var memories = new Memories(Settings);
            var holder = ids.Next(EntityKind.Person);
            var settlement = ids.Next(EntityKind.Settlement);
            var raid = ids.NextEvent();

            Assert.Multiple(() =>
            {
                Assert.That(() => memories.Record(EntityId.None, raid, EntityId.None, 1, ReadOnlySpan<EntityId>.Empty, SimulationTime.Zero), Throws.ArgumentException);
                Assert.That(() => memories.Record(holder, EventId.None, EntityId.None, 1, ReadOnlySpan<EntityId>.Empty, SimulationTime.Zero), Throws.ArgumentException);
                Assert.That(() => memories.Record(holder, raid, EntityId.None, 1, new[] { settlement }, SimulationTime.Zero), Throws.ArgumentException, "a settlement cannot witness");
                Assert.That(() => memories.Record(holder, raid, EntityId.None, 1, new[] { EntityId.None }, SimulationTime.Zero), Throws.ArgumentException);
                Assert.That(() => memories.Teach(holder, raid, holder), Throws.InvalidOperationException, "no such memory");
                Assert.That(() => memories.Teach(EntityId.None, raid, holder), Throws.ArgumentException);
                Assert.That(() => memories.Teach(holder, raid, settlement), Throws.ArgumentException);
                Assert.That(() => memories.Teach(holder, EventId.None, holder), Throws.ArgumentException);
                Assert.That(() => memories.WitnessDied(settlement), Throws.ArgumentException);
                Assert.That(() => memories.Compact(EntityId.None, SimulationTime.Zero), Throws.ArgumentException);
                Assert.That(() => memories.Held(EntityId.None), Throws.ArgumentException);
                Assert.That(() => memories.Witnesses(holder, raid), Throws.InvalidOperationException, "no such memory");
                Assert.That(() => memories.TryGet(holder, EventId.None, out _), Throws.ArgumentException);
                Assert.That(memories.Held(holder).Length, Is.Zero, "nothing was recorded");
                Assert.That(memories.TryGet(holder, raid, out _), Is.False);
            });
        }

        [Test]
        public void Settings_are_validated()
        {
            Assert.Multiple(() =>
            {
                Assert.That(() => new MemorySettings(0, 0L, 0L, 1), Throws.TypeOf<ArgumentOutOfRangeException>(), "no witnesses");
                Assert.That(() => new MemorySettings(1, -1L, 0L, 1), Throws.TypeOf<ArgumentOutOfRangeException>(), "negative age");
                Assert.That(() => new MemorySettings(1, 10L, 9L, 1), Throws.TypeOf<ArgumentOutOfRangeException>(), "forgotten before old");
                Assert.That(() => new MemorySettings(1, 0L, 0L, 0), Throws.TypeOf<ArgumentOutOfRangeException>(), "everything promoted");
                Assert.That(() => new MemorySettings(2, 0L, 0L, 3), Throws.TypeOf<ArgumentOutOfRangeException>(), "unreachable");
                Assert.That(new Memories(Settings).Settings.MaxWitnesses, Is.EqualTo(4));
            });
        }

        [Test]
        public void Default_settings_are_refused_by_the_store()
        {
            // The settings constructor validates every field, so default is
            // the one invalid instance that can exist - a zero witness cap.
            Assert.That(() => new Memories(default), Throws.ArgumentException);
        }

        [Test]
        public void A_default_memory_has_no_witnesses()
        {
            Assert.Multiple(() =>
            {
                Assert.That(default(Memory).WitnessCount, Is.Zero);
                Assert.That(default(Memory).Tier, Is.EqualTo(MemoryTier.None));
            });
        }
    }
}
