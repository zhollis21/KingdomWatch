using System;
using System.Collections.Generic;
using System.Linq;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Events;
using NUnit.Framework;

namespace KingdomWatch.Core.Tests.Clock
{
    /// <summary>
    /// Section 17's safe snapshot semantics (#15): a snapshot is permitted
    /// only at a checkpoint, and the pending queue is state that leaves and
    /// returns as data, with its ids, rather than being rebuilt.
    /// </summary>
    [TestFixture]
    public sealed class SimulationClockCheckpointTests
    {
        private static readonly SimulationTime Noon = SimulationTime.FromHours(12L);
        private static readonly SimulationTime Dusk = SimulationTime.FromHours(18L);
        private static readonly SimulationTime Midnight = SimulationTime.FromDays(1L);
        private static readonly SimulationTime NextNoon = SimulationTime.FromHours(36L);

        private static EntityId Person(ulong value) => new EntityId(EntityKind.Person, value);

        [Test]
        public void An_idle_clock_is_at_a_checkpoint()
        {
            var clock = new SimulationClock(new IdAllocator());

            Assert.That(clock.AtCheckpoint, Is.True);

            ScheduleTask(clock, Noon, 1UL);
            clock.AdvanceTo(Midnight, new Recorder());

            Assert.That(clock.AtCheckpoint, Is.True, "after an advance returns, the world is consistent again");
        }

        [Test]
        public void A_clock_mid_dispatch_is_not_at_a_checkpoint()
        {
            // Inside a handler the cascade is half done: the event has been
            // dequeued and whatever it mutates is in progress. This is the
            // state section 17 says must never be serialized.
            var clock = new SimulationClock(new IdAllocator());
            var seen = new List<bool>();
            ScheduleTask(clock, Noon, 1UL);

            clock.AdvanceTo(Midnight, new Recorder((_, c) => seen.Add(c.AtCheckpoint)));

            Assert.That(seen, Is.EqualTo(new[] { false }));
        }

        [Test]
        public void A_clock_mid_publish_is_not_at_a_checkpoint()
        {
            // Deaths publishes first and mutates after, so a subscriber sees
            // the world from just before the event. That is a half-state too,
            // even when no dispatch is running - world generation publishes
            // at T=0 before the clock has moved.
            var clock = new SimulationClock(new IdAllocator());
            var bus = new DomainEventBus(clock);
            var seen = new List<bool>();
            bus.Subscribe(new Listener(_ => seen.Add(clock.AtCheckpoint)));

            bus.Publish(DomainEventKind.PersonBorn, Person(1UL), EntityId.None);

            Assert.Multiple(() =>
            {
                Assert.That(seen, Is.EqualTo(new[] { false }));
                Assert.That(clock.AtCheckpoint, Is.True, "and it is one again once the publish returns");
            });
        }

        [Test]
        public void A_clock_whose_handler_threw_is_never_at_a_checkpoint_again()
        {
            // The event was dequeued and the handler got partway through
            // mutating the world before it threw. The finally blocks put the
            // in-flight flags back, but they cannot put the world back - so a
            // driver that catches and then snapshots would serialize exactly
            // the half-state section 17 forbids. The fault is remembered.
            var clock = new SimulationClock(new IdAllocator());
            var pending = new List<ScheduledEvent>();
            ScheduleTask(clock, Noon, 1UL);
            ScheduleTask(clock, Dusk, 2UL);

            Assert.That(
                () => clock.AdvanceTo(Midnight, new Recorder((_, _) => throw new InvalidOperationException("mid-cascade"))),
                Throws.InvalidOperationException.With.Message.EqualTo("mid-cascade"));

            Assert.Multiple(() =>
            {
                Assert.That(clock.AtCheckpoint, Is.False);
                Assert.That(
                    () => clock.CopyPendingTo(pending),
                    Throws.InvalidOperationException.With.Message.Contains("faulted"));
                Assert.That(pending, Is.Empty);
            });
        }

        [Test]
        public void A_clock_whose_subscriber_threw_is_never_at_a_checkpoint_again()
        {
            // Same rule for the other stream: Deaths publishes first and
            // mutates after, so a subscriber that throws leaves the event
            // announced, some subscribers unheard, and the mutation not made.
            var clock = new SimulationClock(new IdAllocator());
            var bus = new DomainEventBus(clock);
            var pending = new List<ScheduledEvent>();
            bus.Subscribe(new Listener(_ => throw new InvalidOperationException("mid-publish")));

            Assert.That(
                () => bus.Publish(DomainEventKind.PersonBorn, Person(1UL), EntityId.None),
                Throws.InvalidOperationException.With.Message.EqualTo("mid-publish"));

            Assert.Multiple(() =>
            {
                Assert.That(clock.AtCheckpoint, Is.False);
                Assert.That(
                    () => clock.CopyPendingTo(pending),
                    Throws.InvalidOperationException.With.Message.Contains("faulted"));
            });
        }

        [Test]
        public void A_refusal_the_handler_catches_is_not_a_fault()
        {
            // The clock's own guards throw INTO the handler - scheduling in
            // the past, a nested advance. A handler that catches one and
            // carries on finished its work; the world it leaves is whole.
            var clock = new SimulationClock(new IdAllocator());
            ScheduleTask(clock, Noon, 1UL);

            clock.AdvanceTo(Midnight, new Recorder((_, c) =>
            {
                try
                {
                    c.AdvanceTo(Dusk, new Recorder());
                }
                catch (InvalidOperationException)
                {
                    // Refused, as it should be; the handler completes.
                }
            }));

            Assert.That(clock.AtCheckpoint, Is.True);
        }

        [Test]
        public void The_pending_events_cannot_be_exported_mid_dispatch()
        {
            var clock = new SimulationClock(new IdAllocator());
            var pending = new List<ScheduledEvent>();
            Exception? caught = null;
            ScheduleTask(clock, Noon, 1UL);
            ScheduleTask(clock, Dusk, 2UL);

            clock.AdvanceTo(Midnight, new Recorder((_, c) =>
            {
                try
                {
                    c.CopyPendingTo(pending);
                }
                catch (InvalidOperationException e)
                {
                    caught ??= e;
                }
            }));

            Assert.Multiple(() =>
            {
                Assert.That(caught, Is.TypeOf<InvalidOperationException>());
                Assert.That(caught!.Message, Does.Contain("checkpoint"));
                Assert.That(pending, Is.Empty, "nothing was handed out");
            });
        }

        [Test]
        public void A_refused_export_still_clears_the_buffer_it_was_handed()
        {
            // "Cleared before use" has to hold on the refusal path too. The
            // export is taken into a reused buffer once per check over a long
            // run; a refusal that left the previous answer in it would hand a
            // caller that catches the throw a stale queue dressed as this one.
            var clock = new SimulationClock(new IdAllocator());
            ScheduleTask(clock, Noon, 1UL);

            var pending = new List<ScheduledEvent>();
            clock.CopyPendingTo(pending);
            Assume.That(pending, Has.Count.EqualTo(1));

            clock.AdvanceTo(Midnight, new Recorder((_, c) =>
            {
                try
                {
                    c.CopyPendingTo(pending);
                }
                catch (InvalidOperationException)
                {
                    // Refused, as it should be.
                }
            }));

            Assert.That(pending, Is.Empty);
        }

        [Test]
        public void The_pending_events_cannot_be_exported_mid_publish()
        {
            var clock = new SimulationClock(new IdAllocator());
            var bus = new DomainEventBus(clock);
            var pending = new List<ScheduledEvent>();
            Exception? caught = null;
            ScheduleTask(clock, Noon, 1UL);
            bus.Subscribe(new Listener(_ =>
            {
                try
                {
                    clock.CopyPendingTo(pending);
                }
                catch (InvalidOperationException e)
                {
                    caught = e;
                }
            }));

            bus.Publish(DomainEventKind.PersonBorn, Person(1UL), EntityId.None);

            Assert.Multiple(() =>
            {
                Assert.That(caught, Is.TypeOf<InvalidOperationException>());
                Assert.That(pending, Is.Empty);
            });
        }

        [Test]
        public void A_restored_clock_stands_where_it_was_and_carries_the_same_commitments()
        {
            var ids = new IdAllocator();
            var original = new SimulationClock(ids);
            ScheduleTask(original, Noon, 1UL);
            ScheduleTask(original, NextNoon, 3UL);
            ScheduleTask(original, Dusk, 2UL);
            original.AdvanceTo(Midnight, new Recorder());

            var exported = new List<ScheduledEvent>();
            original.CopyPendingTo(exported);

            var restored = new SimulationClock(ids, original.Now, exported);
            var pending = new List<ScheduledEvent>();
            restored.CopyPendingTo(pending);

            Assert.Multiple(() =>
            {
                Assert.That(restored.Now, Is.EqualTo(Midnight));
                Assert.That(restored.ScheduledCount, Is.EqualTo(1));
                Assert.That(pending, Is.EqualTo(exported), "same events, same ids, same order");
                Assert.That(restored.AtCheckpoint, Is.True);
            });
        }

        [Test]
        public void A_restored_clock_dispatches_the_same_sequence_as_the_one_it_came_from()
        {
            // The order is a property of the events, not of the heap they
            // sat in (ScheduledEvent's ordering is total), so a queue rebuilt
            // in any input order dispatches identically.
            var ids = new IdAllocator();
            var original = new SimulationClock(ids);
            ScheduleTask(original, Midnight, 3UL);
            ScheduleTask(original, Noon, 1UL);
            ScheduleTask(original, Noon, 2UL);
            ScheduleTask(original, Dusk, 2UL);

            var exported = new List<ScheduledEvent>();
            original.CopyPendingTo(exported);
            var shuffled = exported.AsEnumerable().Reverse().ToList();

            var restored = new SimulationClock(ids, original.Now, shuffled);
            var fromOriginal = new Recorder();
            var fromRestored = new Recorder();

            original.AdvanceTo(NextNoon, fromOriginal);
            restored.AdvanceTo(NextNoon, fromRestored);

            Assert.Multiple(() =>
            {
                Assert.That(fromRestored.Handled, Is.EqualTo(fromOriginal.Handled));
                Assert.That(restored.Now, Is.EqualTo(original.Now));
            });
        }

        [Test]
        public void A_restored_event_can_be_cancelled_by_its_saved_id()
        {
            // Systems keep the ids they booked (PendingBooking); those ids
            // must still mean the same event after a restore, or every
            // re-prediction would miss and the stale crossing would fire.
            var ids = new IdAllocator();
            var original = new SimulationClock(ids);
            var kept = ScheduleTask(original, Noon, 1UL);
            var exported = new List<ScheduledEvent>();
            original.CopyPendingTo(exported);

            var restored = new SimulationClock(ids, original.Now, exported);

            Assert.Multiple(() =>
            {
                Assert.That(restored.Cancel(kept), Is.True);
                Assert.That(restored.ScheduledCount, Is.Zero);
            });
        }

        [Test]
        public void A_restored_clock_hands_out_ids_after_everything_it_carries()
        {
            // The allocator is resumed alongside the queue. A fresh id must
            // sort after every saved one, or a new booking could land in the
            // middle of a tie it was never part of.
            var ids = new IdAllocator();
            var original = new SimulationClock(ids);
            ScheduleTask(original, Noon, 1UL);
            var exported = new List<ScheduledEvent>();
            original.CopyPendingTo(exported);

            var resumed = new IdAllocator();
            resumed.ResumeEventsFrom(ids.PeekNextEvent());
            var restored = new SimulationClock(resumed, original.Now, exported);

            var fresh = ScheduleTask(restored, Noon, 2UL);

            Assert.That(fresh.Value, Is.GreaterThan(exported[0].Id.Value));
        }

        [Test]
        public void Restoring_refuses_an_event_whose_id_was_never_allocated()
        {
            // An id at or beyond the allocator's next would be handed out
            // again by the next Schedule, and two events would share the
            // last component of a supposedly total order.
            var ids = new IdAllocator();
            var never = new ScheduledEvent(
                new EventId(ids.PeekNextEvent()),
                Noon,
                SimulationPhase.Physical,
                ScheduledEventKind.TaskCompleted,
                Person(1UL),
                EntityId.None);

            Assert.That(
                () => new SimulationClock(ids, SimulationTime.Zero, new[] { never }),
                Throws.ArgumentException.With.Message.Contains("allocated"));
        }

        [Test]
        public void Restoring_refuses_a_defaulted_entry()
        {
            // The constructor guards Phase and Kind, but a default struct
            // never went through it: id None, phase None, kind None. Id 0 is
            // below every allocator's next, so the allocation check alone
            // would wave it through into the queue.
            var ids = new IdAllocator();

            Assert.That(
                () => new SimulationClock(ids, SimulationTime.Zero, new ScheduledEvent[] { default }),
                Throws.ArgumentException.With.Message.Contains("defaulted"));
        }

        [Test]
        public void Restoring_refuses_two_events_with_one_id()
        {
            var ids = new IdAllocator();
            var original = new SimulationClock(ids);
            ScheduleTask(original, Noon, 1UL);
            var exported = new List<ScheduledEvent>();
            original.CopyPendingTo(exported);
            exported.Add(exported[0]);

            Assert.That(
                () => new SimulationClock(ids, SimulationTime.Zero, exported),
                Throws.ArgumentException.With.Message.Contains("twice"));
        }

        [Test]
        public void Restoring_refuses_an_event_already_in_the_past()
        {
            // The same rule Schedule applies while idle: at the current
            // instant is fine, before it is not.
            var ids = new IdAllocator();
            var original = new SimulationClock(ids);
            ScheduleTask(original, Noon, 1UL);
            var exported = new List<ScheduledEvent>();
            original.CopyPendingTo(exported);

            Assert.Multiple(() =>
            {
                Assert.That(
                    () => new SimulationClock(ids, Dusk, exported),
                    Throws.ArgumentException.With.Message.Contains("backwards"));
                Assert.That(
                    () => new SimulationClock(ids, Noon, exported),
                    Throws.Nothing);
            });
        }

        [Test]
        public void Restoring_refuses_nulls()
        {
            var ids = new IdAllocator();

            Assert.Multiple(() =>
            {
                Assert.That(
                    () => new SimulationClock(null!, SimulationTime.Zero, Array.Empty<ScheduledEvent>()),
                    Throws.ArgumentNullException);
                Assert.That(
                    () => new SimulationClock(ids, SimulationTime.Zero, null!),
                    Throws.ArgumentNullException);
            });
        }

        [Test]
        public void A_restored_clock_refuses_to_schedule_before_where_it_stands()
        {
            var ids = new IdAllocator();
            var restored = new SimulationClock(ids, Midnight, Array.Empty<ScheduledEvent>());

            Assert.Multiple(() =>
            {
                Assert.That(() => ScheduleTask(restored, Noon, 1UL), Throws.InvalidOperationException);
                Assert.That(() => restored.AdvanceTo(Noon, new Recorder()), Throws.TypeOf<ArgumentOutOfRangeException>());
            });
        }

        private static EventId ScheduleTask(SimulationClock clock, SimulationTime time, ulong person) =>
            clock.Schedule(
                time,
                SimulationPhase.Physical,
                ScheduledEventKind.TaskCompleted,
                Person(person),
                EntityId.None);

        private sealed class Recorder : IScheduledEventHandler
        {
            private readonly Action<ScheduledEvent, SimulationClock>? _whileHandling;

            internal Recorder(Action<ScheduledEvent, SimulationClock>? whileHandling = null)
            {
                _whileHandling = whileHandling;
            }

            internal List<ScheduledEvent> Handled { get; } = new List<ScheduledEvent>();

            public void Handle(ScheduledEvent scheduled, SimulationClock clock)
            {
                Handled.Add(scheduled);
                _whileHandling?.Invoke(scheduled, clock);
            }
        }

        private sealed class Listener : IDomainEventSubscriber
        {
            private readonly Action<DomainEvent> _whileHearing;

            internal Listener(Action<DomainEvent> whileHearing)
            {
                _whileHearing = whileHearing;
            }

            public void On(in DomainEvent published) => _whileHearing(published);
        }
    }
}
