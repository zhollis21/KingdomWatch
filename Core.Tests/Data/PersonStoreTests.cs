using System.Collections.Generic;
using System.Linq;
using KingdomWatch.Core.Data;
using NUnit.Framework;

namespace KingdomWatch.Core.Tests.Data
{
    [TestFixture]
    public sealed class PersonStoreTests
    {
        [Test]
        public void Stored_fields_come_back_through_the_accessors()
        {
            var ids = new IdAllocator();
            var store = new PersonStore();
            var id = ids.Next(EntityKind.Person);

            var handle = store.Add(id, new WorldPosition(3, -4), 90, 2, 1, 200);

            Assert.Multiple(() =>
            {
                Assert.That(store.GetId(handle), Is.EqualTo(id));
                Assert.That(store.GetPosition(handle), Is.EqualTo(new WorldPosition(3, -4)));
                Assert.That(store.GetHealth(handle), Is.EqualTo(90));
                Assert.That(store.GetAgeStage(handle), Is.EqualTo(2));
                Assert.That(store.GetBirthCulture(handle), Is.EqualTo(1));
                Assert.That(store.GetAssimilation(handle), Is.EqualTo(200));
                Assert.That(store.Count, Is.EqualTo(1));
            });
        }

        [Test]
        public void Setters_write_through_to_storage()
        {
            var ids = new IdAllocator();
            var store = new PersonStore();
            var handle = AddPerson(store, ids);

            store.SetPosition(handle, new WorldPosition(7, 8));
            store.SetHealth(handle, 12);
            store.SetAgeStage(handle, 3);
            store.SetBirthCulture(handle, 4);
            store.SetAssimilation(handle, 5);

            Assert.Multiple(() =>
            {
                Assert.That(store.GetPosition(handle), Is.EqualTo(new WorldPosition(7, 8)));
                Assert.That(store.GetHealth(handle), Is.EqualTo(12));
                Assert.That(store.GetAgeStage(handle), Is.EqualTo(3));
                Assert.That(store.GetBirthCulture(handle), Is.EqualTo(4));
                Assert.That(store.GetAssimilation(handle), Is.EqualTo(5));
            });
        }

        [Test]
        public void Only_a_person_kind_id_can_be_stored()
        {
            // A store of people holding a settlement's id would satisfy every
            // uniqueness check the validator makes and still be nonsense.
            var ids = new IdAllocator();
            var store = new PersonStore();

            Assert.Multiple(() =>
            {
                Assert.That(
                    () => store.Add(ids.Next(EntityKind.Settlement), default, 1, 0, 0, 0),
                    Throws.ArgumentException);
                Assert.That(
                    () => store.Add(ids.Next(EntityKind.Household), default, 1, 0, 0, 0),
                    Throws.ArgumentException);
                Assert.That(
                    () => store.Add(EntityId.None, default, 1, 0, 0, 0),
                    Throws.ArgumentException);
                Assert.That(store.Count, Is.Zero, "a refused Add must not consume a slot");
            });
        }

        [Test]
        public void The_first_occupant_of_a_slot_is_generation_one()
        {
            var ids = new IdAllocator();
            var store = new PersonStore();

            var first = AddPerson(store, ids);
            var second = AddPerson(store, ids);

            Assert.Multiple(() =>
            {
                Assert.That(first, Is.EqualTo(new PersonHandle(0, 1)));
                Assert.That(second, Is.EqualTo(new PersonHandle(1, 1)));
            });
        }

        [Test]
        public void A_recycled_slot_makes_the_old_handle_stale_rather_than_wrong()
        {
            // The reason the whole handle/generation split exists. The stale
            // handle must not read the newcomer's data.
            var ids = new IdAllocator();
            var store = new PersonStore();
            var dead = AddPerson(store, ids);
            store.SetHealth(dead, 40);
            store.Remove(dead);

            var newcomer = AddPerson(store, ids);
            store.SetHealth(newcomer, 99);

            Assert.Multiple(() =>
            {
                Assert.That(newcomer.Index, Is.EqualTo(dead.Index), "slot should be reused");
                Assert.That(newcomer.Generation, Is.EqualTo(dead.Generation + 1));
                Assert.That(store.IsAlive(dead), Is.False);
                Assert.That(store.IsAlive(newcomer), Is.True);
                Assert.That(() => store.GetHealth(dead), Throws.ArgumentException);
                Assert.That(store.GetHealth(newcomer), Is.EqualTo(99));
            });
        }

        [Test]
        public void Removing_a_person_does_not_disturb_anyone_else()
        {
            var ids = new IdAllocator();
            var store = new PersonStore();
            var first = AddPerson(store, ids);
            var middle = AddPerson(store, ids);
            var last = AddPerson(store, ids);
            store.SetHealth(first, 10);
            store.SetHealth(last, 30);

            store.Remove(middle);

            // Nobody is compacted into the hole: a swap-to-last would have
            // moved `last` into the middle slot while this handle still points
            // at the old index, and the generation would still match.
            Assert.Multiple(() =>
            {
                Assert.That(store.GetHealth(first), Is.EqualTo(10));
                Assert.That(store.GetHealth(last), Is.EqualTo(30));
                Assert.That(store.Count, Is.EqualTo(2));
                Assert.That(store.IsAlive(middle), Is.False);
            });
        }

        [Test]
        public void Every_accessor_refuses_a_handle_that_addresses_nobody()
        {
            var ids = new IdAllocator();
            var store = new PersonStore();
            var removed = AddPerson(store, ids);
            store.Remove(removed);

            var candidates = new[]
            {
                PersonHandle.None,
                removed,
                new PersonHandle(99, 1),

                // The boundary itself: one past the last slot ever allocated.
                new PersonHandle(store.RecordSpan().Length, 1),
            };

            foreach (var handle in candidates)
            {
                var subject = handle;

                Assert.Multiple(() =>
                {
                    Assert.That(() => store.GetId(subject), Throws.ArgumentException);
                    Assert.That(() => store.GetPosition(subject), Throws.ArgumentException);
                    Assert.That(() => store.GetHealth(subject), Throws.ArgumentException);
                    Assert.That(() => store.GetAgeStage(subject), Throws.ArgumentException);
                    Assert.That(() => store.GetBirthCulture(subject), Throws.ArgumentException);
                    Assert.That(() => store.GetAssimilation(subject), Throws.ArgumentException);
                    Assert.That(
                        () => store.SetPosition(subject, default), Throws.ArgumentException);
                    Assert.That(() => store.SetHealth(subject, 1), Throws.ArgumentException);
                    Assert.That(() => store.SetAgeStage(subject, 1), Throws.ArgumentException);
                    Assert.That(() => store.SetBirthCulture(subject, 1), Throws.ArgumentException);
                    Assert.That(() => store.SetAssimilation(subject, 1), Throws.ArgumentException);
                    Assert.That(() => store.Remove(subject), Throws.ArgumentException);
                    Assert.That(store.IsAlive(subject), Is.False);
                });
            }
        }

        [Test]
        public void Removing_the_same_person_twice_is_refused()
        {
            var ids = new IdAllocator();
            var store = new PersonStore();
            var handle = AddPerson(store, ids);
            store.Remove(handle);

            // The second call is a bug in the caller - a silent no-op would
            // hide whatever double-counted the death.
            Assert.Multiple(() =>
            {
                Assert.That(() => store.Remove(handle), Throws.ArgumentException);
                Assert.That(store.Count, Is.Zero);
            });
        }

        [Test]
        public void A_handle_from_another_store_is_not_honoured()
        {
            var ids = new IdAllocator();
            var store = new PersonStore();
            var other = new PersonStore();
            var foreignHandle = other.Add(ids.Next(EntityKind.Person), default, 50, 0, 0, 0);

            Assert.Multiple(() =>
            {
                Assert.That(store.IsAlive(foreignHandle), Is.False);
                Assert.That(() => store.GetHealth(foreignHandle), Throws.ArgumentException);
            });
        }

        [Test]
        public void Freed_slots_are_reused_in_a_fixed_order()
        {
            // Determinism: which slot the next person lands in has to be a
            // function of history, not of allocator mood.
            var ids = new IdAllocator();
            var store = new PersonStore();
            var first = AddPerson(store, ids);
            var second = AddPerson(store, ids);
            AddPerson(store, ids);

            store.Remove(second);
            store.Remove(first);

            var third = AddPerson(store, ids);
            var fourth = AddPerson(store, ids);

            Assert.Multiple(() =>
            {
                Assert.That(third, Is.EqualTo(new PersonHandle(0, 2)));
                Assert.That(fourth, Is.EqualTo(new PersonHandle(1, 2)));
                Assert.That(store.Count, Is.EqualTo(3));
            });
        }

        [Test]
        public void Alive_lists_the_living_in_slot_order_and_nobody_else()
        {
            var ids = new IdAllocator();
            var store = new PersonStore();
            var first = AddPerson(store, ids);
            var second = AddPerson(store, ids);
            var third = AddPerson(store, ids);
            store.Remove(second);

            var alive = store.Alive().ToList();
            var again = store.Alive().ToList();

            Assert.Multiple(() =>
            {
                Assert.That(alive, Is.EqualTo(new List<PersonHandle> { first, third }));
                Assert.That(again, Is.EqualTo(alive), "repeat enumeration must not vary");
                Assert.That(alive, Has.Count.EqualTo(store.Count));
            });
        }

        [Test]
        public void The_record_span_exposes_allocated_slots_including_the_unoccupied()
        {
            var ids = new IdAllocator();
            var store = new PersonStore();
            var first = AddPerson(store, ids);
            var second = AddPerson(store, ids);
            store.SetHealth(first, 70);
            store.Remove(second);

            // Span is a ref struct, so it cannot cross into an assertion
            // lambda - read what is needed out of it first.
            var span = store.RecordSpan();
            var length = span.Length;
            var firstHealth = span[0].Health;
            var firstHandle = span[0].Handle;
            var occupied = 0;

            foreach (var record in span)
            {
                if (!record.Id.IsNone)
                {
                    occupied++;
                }
            }

            Assert.Multiple(() =>
            {
                Assert.That(length, Is.EqualTo(2), "both slots were allocated");
                Assert.That(occupied, Is.EqualTo(1), "only one is still occupied");
                Assert.That(firstHealth, Is.EqualTo(70));
                Assert.That(firstHandle, Is.EqualTo(first));
            });
        }

        [Test]
        public void Writes_through_the_record_span_reach_storage()
        {
            var ids = new IdAllocator();
            var store = new PersonStore();
            var handle = AddPerson(store, ids);

            var span = store.RecordSpan();
            span[0].Health = 33;

            Assert.That(store.GetHealth(handle), Is.EqualTo(33));
        }

        [Test]
        public void The_span_is_empty_before_anyone_is_stored()
        {
            var store = new PersonStore();

            var length = store.RecordSpan().Length;

            Assert.Multiple(() =>
            {
                Assert.That(length, Is.Zero);
                Assert.That(store.Alive(), Is.Empty);
                Assert.That(store.Count, Is.Zero);
            });
        }

        [Test]
        public void Growing_past_the_initial_capacity_keeps_every_handle_working()
        {
            // The backing array is replaced on growth; handles are indices, so
            // they have to survive it.
            var ids = new IdAllocator();
            var store = new PersonStore();
            var handles = new List<PersonHandle>();

            for (var i = 0; i < 40; i++)
            {
                var handle = AddPerson(store, ids);
                store.SetHealth(handle, (short)i);
                handles.Add(handle);
            }

            var healths = handles.Select(handle => store.GetHealth(handle)).ToList();
            var length = store.RecordSpan().Length;

            Assert.Multiple(() =>
            {
                Assert.That(healths, Is.EqualTo(Enumerable.Range(0, 40).ToList()));
                Assert.That(
                    handles.Select(handle => handle.Index).Distinct().Count(), Is.EqualTo(40));
                Assert.That(store.Count, Is.EqualTo(40));
                Assert.That(length, Is.EqualTo(40));
            });
        }

        [Test]
        public void Durable_ids_survive_a_slot_changing_hands()
        {
            // The handle is storage's business; the id is what history keeps.
            var ids = new IdAllocator();
            var store = new PersonStore();
            var firstId = ids.Next(EntityKind.Person);
            var first = store.Add(firstId, default, 50, 0, 0, 0);
            store.Remove(first);

            var secondId = ids.Next(EntityKind.Person);
            var second = store.Add(secondId, default, 50, 0, 0, 0);

            Assert.Multiple(() =>
            {
                Assert.That(second.Index, Is.EqualTo(first.Index));
                Assert.That(store.GetId(second), Is.EqualTo(secondId));
                Assert.That(secondId, Is.Not.EqualTo(firstId));
            });
        }

        [Test]
        public void A_stale_handle_cannot_remove_the_person_who_took_the_slot()
        {
            // The destructive twin of a stale read, and the worse of the two:
            // silently killing whoever moved into the slot would look exactly
            // like a legitimate death from every angle afterwards.
            var ids = new IdAllocator();
            var store = new PersonStore();
            var dead = AddPerson(store, ids);
            store.Remove(dead);
            var newcomer = AddPerson(store, ids);

            Assert.Multiple(() =>
            {
                Assert.That(() => store.Remove(dead), Throws.ArgumentException);
                Assert.That(store.IsAlive(newcomer), Is.True, "the newcomer must survive");
                Assert.That(store.Count, Is.EqualTo(1));
            });
        }

        [Test]
        public void Adding_more_people_than_were_freed_allocates_new_slots()
        {
            // Slot handout has two branches - recycle a freed slot, or take a
            // fresh one - and this sequence crosses from the first to the
            // second without stopping in between.
            var ids = new IdAllocator();
            var store = new PersonStore();
            var first = AddPerson(store, ids);
            var second = AddPerson(store, ids);
            store.Remove(first);
            store.Remove(second);

            var third = AddPerson(store, ids);
            var fourth = AddPerson(store, ids);
            var fifth = AddPerson(store, ids);

            Assert.Multiple(() =>
            {
                Assert.That(third, Is.EqualTo(new PersonHandle(1, 2)));
                Assert.That(fourth, Is.EqualTo(new PersonHandle(0, 2)));
                Assert.That(
                    fifth,
                    Is.EqualTo(new PersonHandle(2, 1)),
                    "the free list was empty, so this is a fresh slot at generation 1");
                Assert.That(store.Count, Is.EqualTo(3));
            });
        }

        [Test]
        public void Field_extremes_round_trip_without_narrowing()
        {
            // Storage stores what it is given. Whether health of short.MinValue
            // means anything is a question for whichever system owns health -
            // this only proves nothing is lost or clamped on the way in.
            var ids = new IdAllocator();
            var store = new PersonStore();

            var extreme = store.Add(
                ids.Next(EntityKind.Person),
                new WorldPosition(int.MinValue, int.MaxValue),
                short.MinValue,
                byte.MaxValue,
                byte.MaxValue,
                byte.MaxValue);
            var opposite = store.Add(
                ids.Next(EntityKind.Person), default, short.MaxValue, 0, 0, 0);

            Assert.Multiple(() =>
            {
                Assert.That(
                    store.GetPosition(extreme),
                    Is.EqualTo(new WorldPosition(int.MinValue, int.MaxValue)));
                Assert.That(store.GetHealth(extreme), Is.EqualTo(short.MinValue));
                Assert.That(store.GetAgeStage(extreme), Is.EqualTo(byte.MaxValue));
                Assert.That(store.GetBirthCulture(extreme), Is.EqualTo(byte.MaxValue));
                Assert.That(store.GetAssimilation(extreme), Is.EqualTo(byte.MaxValue));
                Assert.That(store.GetHealth(opposite), Is.EqualTo(short.MaxValue));
            });
        }

        [Test]
        public void The_count_never_drifts_from_the_living()
        {
            // Count is bookkeeping kept by hand, so it can disagree with the
            // slots themselves. Crossing growth and recycling in one sequence
            // is where it would.
            var ids = new IdAllocator();
            var store = new PersonStore();
            var handles = new List<PersonHandle>();

            for (var i = 0; i < 30; i++)
            {
                handles.Add(AddPerson(store, ids));
            }

            for (var i = 0; i < handles.Count; i += 3)
            {
                store.Remove(handles[i]);
            }

            for (var i = 0; i < 4; i++)
            {
                AddPerson(store, ids);
            }

            Assert.Multiple(() =>
            {
                Assert.That(store.Count, Is.EqualTo(store.Alive().Count()));
                Assert.That(store.Count, Is.EqualTo(24));
            });
        }

        private static PersonHandle AddPerson(PersonStore store, IdAllocator ids) =>
            store.Add(ids.Next(EntityKind.Person), default, 50, 0, 0, 0);
    }
}
