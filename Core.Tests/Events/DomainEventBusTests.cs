using System;
using System.Collections.Generic;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Events;
using NUnit.Framework;

namespace KingdomWatch.Core.Tests.Events
{
    [TestFixture]
    public sealed class DomainEventBusTests
    {
        private static readonly SimulationTime Noon = SimulationTime.FromHours(12L);

        private static EntityId Person(ulong value) => new EntityId(EntityKind.Person, value);

        // Records what it hears, and optionally does something while hearing
        // it - which is how the rules about publishing mid-notification get
        // exercised.
        private sealed class Recorder : IDomainEventSubscriber
        {
            private readonly Action<DomainEvent>? _whileHearing;

            internal Recorder(Action<DomainEvent>? whileHearing = null)
            {
                _whileHearing = whileHearing;
            }

            internal List<DomainEvent> Heard { get; } = new List<DomainEvent>();

            public void On(in DomainEvent published)
            {
                Heard.Add(published);
                _whileHearing?.Invoke(published);
            }
        }

        // Records the order two subscribers were notified in, across both.
        private sealed class OrderTracker : IDomainEventSubscriber
        {
            private readonly string _name;
            private readonly List<string> _log;

            internal OrderTracker(string name, List<string> log)
            {
                _name = name;
                _log = log;
            }

            public void On(in DomainEvent published) => _log.Add(_name + ":" + published.Kind);
        }

        private sealed class Delegating : IScheduledEventHandler
        {
            private readonly Action<ScheduledEvent, SimulationClock> _handle;

            internal Delegating(Action<ScheduledEvent, SimulationClock> handle)
            {
                _handle = handle;
            }

            public void Handle(ScheduledEvent scheduled, SimulationClock clock) => _handle(scheduled, clock);
        }

        private static (IdAllocator ids, SimulationClock clock, DomainEventBus bus) NewWorld()
        {
            var ids = new IdAllocator();
            var clock = new SimulationClock(ids);
            return (ids, clock, new DomainEventBus(ids, clock));
        }

        [Test]
        public void A_bus_needs_an_allocator_and_a_clock()
        {
            var ids = new IdAllocator();

            Assert.Multiple(() =>
            {
                Assert.That(() => new DomainEventBus(null!, new SimulationClock(ids)), Throws.TypeOf<ArgumentNullException>());
                Assert.That(() => new DomainEventBus(ids, null!), Throws.TypeOf<ArgumentNullException>());
            });
        }

        [Test]
        public void Publishing_stamps_a_fresh_id_and_the_clock_instant_and_returns_the_id()
        {
            var (_, clock, bus) = NewWorld();
            var recorder = new Recorder();
            bus.Subscribe(recorder);
            clock.AdvanceTo(Noon, new Delegating((_, _) => { }));

            var id = bus.Publish(DomainEventKind.PersonBorn, Person(1UL), Person(2UL));

            Assert.Multiple(() =>
            {
                Assert.That(id.IsNone, Is.False);
                Assert.That(recorder.Heard, Has.Count.EqualTo(1));
                Assert.That(recorder.Heard[0].Id, Is.EqualTo(id));
                Assert.That(recorder.Heard[0].Time, Is.EqualTo(Noon));
                Assert.That(recorder.Heard[0].Kind, Is.EqualTo(DomainEventKind.PersonBorn));
                Assert.That(recorder.Heard[0].PrimaryEntity, Is.EqualTo(Person(1UL)));
                Assert.That(recorder.Heard[0].SecondaryEntity, Is.EqualTo(Person(2UL)));
                Assert.That(recorder.Heard[0].Reasons, Is.EqualTo(Reasons.None));
            });
        }

        [Test]
        public void Reasons_ride_on_the_event_exactly_as_given()
        {
            var (_, _, bus) = NewWorld();
            var recorder = new Recorder();
            bus.Subscribe(recorder);
            var reasons = new Reasons(ReasonCode.FoodShortage, ReasonCode.SpouseDied, ReasonCode.KinLiveThere);

            bus.Publish(DomainEventKind.SettlementAbandoned, Person(1UL), EntityId.None, reasons);

            Assert.That(recorder.Heard[0].Reasons, Is.EqualTo(reasons));
        }

        [Test]
        public void Ids_increase_with_each_publish_and_share_the_scheduler_counter()
        {
            var (_, clock, bus) = NewWorld();

            var first = bus.Publish(DomainEventKind.PersonBorn, Person(1UL), EntityId.None);
            var scheduled = clock.Schedule(
                Noon, SimulationPhase.Physical, ScheduledEventKind.TaskCompleted, Person(1UL), EntityId.None);
            var second = bus.Publish(DomainEventKind.PersonBorn, Person(2UL), EntityId.None);

            // One counter: the scheduled event sits between the two published
            // ones, so no id can ever name both a wake-up and a fact.
            Assert.Multiple(() =>
            {
                Assert.That(first, Is.LessThan(scheduled));
                Assert.That(scheduled, Is.LessThan(second));
            });
        }

        [Test]
        public void An_undefined_kind_is_refused_at_the_bus_and_reaches_nobody()
        {
            var (_, _, bus) = NewWorld();
            var recorder = new Recorder();
            bus.Subscribe(recorder);

            Assert.Multiple(() =>
            {
                Assert.That(
                    () => bus.Publish((DomainEventKind)999, EntityId.None, EntityId.None),
                    Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(
                    () => bus.Publish((DomainEventKind)(-1), EntityId.None, EntityId.None),
                    Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(
                    () => bus.Publish(DomainEventKind.None, EntityId.None, EntityId.None, Reasons.None),
                    Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(recorder.Heard, Is.Empty);
            });

            // A refusal does not seal subscription: nothing was published.
            bus.Subscribe(new Recorder());
            Assert.That(bus.SubscriberCount, Is.EqualTo(2));
        }

        [Test]
        public void Subscribers_hear_every_event_in_subscription_order()
        {
            var (_, _, bus) = NewWorld();
            var log = new List<string>();
            bus.Subscribe(new OrderTracker("history", log));
            bus.Subscribe(new OrderTracker("households", log));
            bus.Subscribe(new OrderTracker("feed", log));

            bus.Publish(DomainEventKind.PersonDied, Person(1UL), EntityId.None);
            bus.Publish(DomainEventKind.PersonBorn, Person(2UL), EntityId.None);

            Assert.That(
                log,
                Is.EqualTo(new[]
                {
                    "history:PersonDied", "households:PersonDied", "feed:PersonDied",
                    "history:PersonBorn", "households:PersonBorn", "feed:PersonBorn",
                }));
        }

        [Test]
        public void Publishing_with_no_subscribers_still_consumes_an_id()
        {
            var (ids, _, bus) = NewWorld();

            var id = bus.Publish(DomainEventKind.FamineStarted, EntityId.None, EntityId.None);

            Assert.Multiple(() =>
            {
                Assert.That(bus.SubscriberCount, Is.Zero);
                Assert.That(id, Is.EqualTo(new EventId(1UL)));
                Assert.That(ids.PeekNextEvent(), Is.EqualTo(2UL));
            });
        }

        [Test]
        public void A_null_or_repeated_subscriber_is_refused()
        {
            var (_, _, bus) = NewWorld();
            var recorder = new Recorder();
            bus.Subscribe(recorder);

            Assert.Multiple(() =>
            {
                Assert.That(() => bus.Subscribe(null!), Throws.TypeOf<ArgumentNullException>());
                Assert.That(() => bus.Subscribe(recorder), Throws.TypeOf<ArgumentException>());
                Assert.That(bus.SubscriberCount, Is.EqualTo(1));
            });
        }

        [Test]
        public void Subscription_is_sealed_by_the_first_publish()
        {
            var (_, _, bus) = NewWorld();
            bus.Subscribe(new Recorder());
            bus.Publish(DomainEventKind.PersonBorn, Person(1UL), EntityId.None);

            Assert.Multiple(() =>
            {
                Assert.That(() => bus.Subscribe(new Recorder()), Throws.TypeOf<InvalidOperationException>());
                Assert.That(bus.SubscriberCount, Is.EqualTo(1));
            });
        }

        [Test]
        public void Publishing_from_inside_a_subscriber_is_refused()
        {
            var (_, _, bus) = NewWorld();
            Exception? caught = null;
            var reactor = new Recorder(heard =>
            {
                try
                {
                    bus.Publish(DomainEventKind.HouseholdFormed, heard.PrimaryEntity, EntityId.None);
                }
                catch (InvalidOperationException e)
                {
                    caught = e;
                }
            });
            var later = new Recorder();
            bus.Subscribe(reactor);
            bus.Subscribe(later);

            bus.Publish(DomainEventKind.PersonDied, Person(1UL), EntityId.None);

            Assert.Multiple(() =>
            {
                Assert.That(caught, Is.TypeOf<InvalidOperationException>());
                Assert.That(caught!.Message, Does.Contain("later SimulationPhase"));
                // The refused publish reached nobody; the original still
                // reached everyone after the offender.
                Assert.That(reactor.Heard, Has.Count.EqualTo(1));
                Assert.That(later.Heard, Has.Count.EqualTo(1));
                Assert.That(later.Heard[0].Kind, Is.EqualTo(DomainEventKind.PersonDied));
            });
        }

        [Test]
        public void A_refused_nested_publish_leaves_the_bus_usable()
        {
            var (_, _, bus) = NewWorld();
            var recorder = new Recorder(heard =>
            {
                if (heard.Kind == DomainEventKind.PersonDied)
                {
                    Assert.That(
                        () => bus.Publish(DomainEventKind.HouseholdFormed, EntityId.None, EntityId.None),
                        Throws.TypeOf<InvalidOperationException>());
                }
            });
            bus.Subscribe(recorder);

            bus.Publish(DomainEventKind.PersonDied, Person(1UL), EntityId.None);
            bus.Publish(DomainEventKind.PersonBorn, Person(2UL), EntityId.None);

            Assert.That(recorder.Heard, Has.Count.EqualTo(2));
        }

        [Test]
        public void A_throwing_subscriber_propagates_and_leaves_the_bus_usable()
        {
            var (_, _, bus) = NewWorld();
            var broken = new Recorder(heard =>
            {
                if (heard.Kind == DomainEventKind.PersonDied)
                {
                    throw new InvalidTimeZoneException("subscriber bug");
                }
            });
            var after = new Recorder();
            bus.Subscribe(broken);
            bus.Subscribe(after);

            Assert.That(
                () => bus.Publish(DomainEventKind.PersonDied, Person(1UL), EntityId.None),
                Throws.TypeOf<InvalidTimeZoneException>());

            // The exception is the caller's problem, not swallowed; but the
            // bus must not be left believing it is still mid-publish.
            bus.Publish(DomainEventKind.PersonBorn, Person(2UL), EntityId.None);

            Assert.Multiple(() =>
            {
                Assert.That(after.Heard, Has.Count.EqualTo(1));
                Assert.That(after.Heard[0].Kind, Is.EqualTo(DomainEventKind.PersonBorn));
            });
        }

        [Test]
        public void Publishing_before_the_clock_has_run_is_allowed_at_time_zero()
        {
            // World generation publishes SettlementFounded and PersonBorn
            // before a single tick has elapsed.
            var (_, _, bus) = NewWorld();
            var recorder = new Recorder();
            bus.Subscribe(recorder);

            bus.Publish(DomainEventKind.SettlementFounded, new EntityId(EntityKind.Settlement, 1UL), EntityId.None);

            Assert.That(recorder.Heard[0].Time, Is.EqualTo(SimulationTime.Zero));
        }

        [Test]
        public void A_reaction_flows_through_a_later_phase_and_arrives_in_order()
        {
            // The sanctioned cascade. A Lifecycle wake-up publishes PersonDied;
            // the household subscriber cannot publish back, so it books a
            // HouseholdAndSocial wake-up at the same instant; that handler
            // publishes the consequence. Everything lands in dispatch order
            // and the journal reads as the story happened.
            var (_, clock, bus) = NewWorld();
            var journal = new Recorder();
            var households = new Recorder(heard =>
            {
                if (heard.Kind == DomainEventKind.PersonDied)
                {
                    clock.Schedule(
                        clock.Now,
                        SimulationPhase.HouseholdAndSocial,
                        ScheduledEventKind.SocialDecision,
                        new EntityId(EntityKind.Household, 1UL),
                        heard.PrimaryEntity);
                }
            });
            bus.Subscribe(journal);
            bus.Subscribe(households);

            // Standing in for the systems that will own these kinds: the
            // Lifecycle event is "Aldric starves", the social one is "his
            // household dissolves".
            var systems = new Delegating((scheduled, _) =>
            {
                switch (scheduled.Kind)
                {
                    case ScheduledEventKind.BirthCheck:
                        bus.Publish(
                            DomainEventKind.PersonDied,
                            scheduled.PrimaryEntity,
                            EntityId.None,
                            new Reasons(ReasonCode.FoodShortage));
                        break;
                    case ScheduledEventKind.SocialDecision:
                        bus.Publish(DomainEventKind.SettlementAbandoned, scheduled.PrimaryEntity, scheduled.SecondaryEntity);
                        break;
                }
            });

            clock.Schedule(Noon, SimulationPhase.Lifecycle, ScheduledEventKind.BirthCheck, Person(1UL), EntityId.None);
            clock.AdvanceTo(Noon, systems);

            Assert.Multiple(() =>
            {
                Assert.That(journal.Heard, Has.Count.EqualTo(2));
                Assert.That(journal.Heard[0].Kind, Is.EqualTo(DomainEventKind.PersonDied));
                Assert.That(journal.Heard[0].Reasons[0], Is.EqualTo(ReasonCode.FoodShortage));
                Assert.That(journal.Heard[1].Kind, Is.EqualTo(DomainEventKind.SettlementAbandoned));
                Assert.That(journal.Heard[1].SecondaryEntity, Is.EqualTo(Person(1UL)));
                Assert.That(journal.Heard[1].Time, Is.EqualTo(Noon));
                Assert.That(journal.Heard[0].Id, Is.LessThan(journal.Heard[1].Id));
                Assert.That(clock.ScheduledCount, Is.Zero);
            });
        }
    }
}
