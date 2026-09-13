using System;
using System.Collections.Generic;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;
using NUnit.Framework;

namespace KingdomWatch.Core.Tests.Clock
{
    [TestFixture]
    public sealed class ScheduledEventRouterTests
    {
        private static readonly SimulationTime Noon = SimulationTime.FromHours(12L);

        private static EntityId Person(ulong value) => new EntityId(EntityKind.Person, value);

        private sealed class Recorder : IScheduledEventHandler
        {
            internal List<ScheduledEvent> Handled { get; } = new List<ScheduledEvent>();

            public void Handle(ScheduledEvent scheduled, SimulationClock clock) => Handled.Add(scheduled);
        }

        private static ScheduledEvent Build(ScheduledEventKind kind, ulong person) =>
            new ScheduledEvent(new EventId(1UL), Noon, SimulationPhase.Physical, kind, Person(person), EntityId.None);

        [Test]
        public void Each_kind_reaches_the_handler_that_registered_for_it()
        {
            var ids = new IdAllocator();
            var clock = new SimulationClock(ids);
            var router = new ScheduledEventRouter();
            var tasks = new Recorder();
            var births = new Recorder();
            router.Register(ScheduledEventKind.TaskCompleted, tasks);
            router.Register(ScheduledEventKind.BirthCheck, births);

            clock.Schedule(Noon, SimulationPhase.Physical, ScheduledEventKind.TaskCompleted, Person(1UL), EntityId.None);
            clock.Schedule(Noon, SimulationPhase.Lifecycle, ScheduledEventKind.BirthCheck, Person(2UL), EntityId.None);
            clock.Schedule(Noon, SimulationPhase.Physical, ScheduledEventKind.TaskCompleted, Person(3UL), EntityId.None);
            clock.AdvanceTo(Noon, router);

            Assert.Multiple(() =>
            {
                Assert.That(tasks.Handled, Has.Count.EqualTo(2));
                Assert.That(tasks.Handled[0].PrimaryEntity, Is.EqualTo(Person(1UL)));
                Assert.That(tasks.Handled[1].PrimaryEntity, Is.EqualTo(Person(3UL)));
                Assert.That(births.Handled, Has.Count.EqualTo(1));
                Assert.That(births.Handled[0].PrimaryEntity, Is.EqualTo(Person(2UL)));
            });
        }

        [Test]
        public void The_same_handler_may_own_several_kinds()
        {
            var router = new ScheduledEventRouter();
            var system = new Recorder();
            router.Register(ScheduledEventKind.TaskCompleted, system);
            router.Register(ScheduledEventKind.SocialDecision, system);

            router.Handle(Build(ScheduledEventKind.TaskCompleted, 1UL), new SimulationClock(new IdAllocator()));
            router.Handle(Build(ScheduledEventKind.SocialDecision, 2UL), new SimulationClock(new IdAllocator()));

            Assert.That(system.Handled, Has.Count.EqualTo(2));
        }

        [Test]
        public void A_kind_nobody_registered_for_is_refused_when_it_comes_due()
        {
            var router = new ScheduledEventRouter();
            router.Register(ScheduledEventKind.TaskCompleted, new Recorder());

            Assert.That(
                () => router.Handle(Build(ScheduledEventKind.BirthCheck, 1UL), new SimulationClock(new IdAllocator())),
                Throws.TypeOf<InvalidOperationException>().With.Message.Contains("BirthCheck"));
        }

        [Test]
        public void A_kind_has_exactly_one_owner()
        {
            var router = new ScheduledEventRouter();
            var first = new Recorder();
            router.Register(ScheduledEventKind.TaskCompleted, first);

            Assert.Multiple(() =>
            {
                Assert.That(
                    () => router.Register(ScheduledEventKind.TaskCompleted, new Recorder()),
                    Throws.TypeOf<InvalidOperationException>());
                // Even re-registering the same handler: the second call is a
                // wiring mistake somewhere, and it is cheaper to hear about it.
                Assert.That(
                    () => router.Register(ScheduledEventKind.TaskCompleted, first),
                    Throws.TypeOf<InvalidOperationException>());
            });

            router.Handle(Build(ScheduledEventKind.TaskCompleted, 1UL), new SimulationClock(new IdAllocator()));
            Assert.That(first.Handled, Has.Count.EqualTo(1));
        }

        [Test]
        public void A_router_cannot_own_a_kind_itself()
        {
            // It implements the handler interface, so this compiles - and
            // would dispatch into itself until the stack ran out.
            var router = new ScheduledEventRouter();

            Assert.Multiple(() =>
            {
                Assert.That(
                    () => router.Register(ScheduledEventKind.TaskCompleted, router),
                    Throws.TypeOf<ArgumentException>());
                Assert.That(
                    () => router.Handle(Build(ScheduledEventKind.TaskCompleted, 1UL), new SimulationClock(new IdAllocator())),
                    Throws.TypeOf<InvalidOperationException>(),
                    "the refused registration must not have taken the slot");
            });
        }

        [Test]
        public void Registration_rejects_a_null_handler_and_a_kind_that_is_not_one()
        {
            var router = new ScheduledEventRouter();

            Assert.Multiple(() =>
            {
                Assert.That(
                    () => router.Register(ScheduledEventKind.TaskCompleted, null!),
                    Throws.TypeOf<ArgumentNullException>());
                Assert.That(
                    () => router.Register(ScheduledEventKind.None, new Recorder()),
                    Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(
                    () => router.Register((ScheduledEventKind)999, new Recorder()),
                    Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(
                    () => router.Register((ScheduledEventKind)(-1), new Recorder()),
                    Throws.TypeOf<ArgumentOutOfRangeException>());
            });
        }

        [Test]
        public void A_defaulted_event_is_refused_rather_than_indexed_out_of_range()
        {
            // default(ScheduledEvent) skips the constructor that rejects Kind
            // None. Slot 0 can never be registered, so it is reported as an
            // unowned kind rather than reaching a handler or crashing.
            var router = new ScheduledEventRouter();
            router.Register(ScheduledEventKind.TaskCompleted, new Recorder());

            Assert.That(
                () => router.Handle(default, new SimulationClock(new IdAllocator())),
                Throws.TypeOf<InvalidOperationException>().With.Message.Contains("None"));
        }
    }
}
