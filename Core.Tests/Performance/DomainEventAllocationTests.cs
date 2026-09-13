using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Events;
using KingdomWatch.Core.History;
using NUnit.Framework;

namespace KingdomWatch.Core.Tests.Performance
{
    /// <summary>
    /// Every system will publish through the bus from inside
    /// <see cref="SimulationClock.AdvanceTo"/>, so the bus, the router and
    /// the journal get the same zero-allocation guard as the scheduler (#59).
    /// </summary>
    [TestFixture]
    [Category("Performance")]
    public sealed class DomainEventAllocationTests
    {
        private const int Entities = 100;
        private const int Days = 30;

        // Publishes on every dispatch and books the next wake-up, which is
        // the shape every M1 system takes; the reaction subscriber books a
        // later-phase reaction the way the bus requires.
        private sealed class Publisher : IScheduledEventHandler
        {
            private readonly DomainEventBus _bus;

            internal Publisher(DomainEventBus bus)
            {
                _bus = bus;
            }

            public void Handle(ScheduledEvent scheduled, SimulationClock clock)
            {
                if (scheduled.Kind == ScheduledEventKind.BirthCheck)
                {
                    _bus.Publish(
                        DomainEventKind.PersonDied,
                        scheduled.PrimaryEntity,
                        EntityId.None,
                        new Reasons(ReasonCode.FoodShortage, ReasonCode.SpouseDied));
                    clock.Schedule(
                        clock.Now.Plus(SimulationTime.TicksPerDay),
                        SimulationPhase.Lifecycle,
                        ScheduledEventKind.BirthCheck,
                        scheduled.PrimaryEntity,
                        EntityId.None);
                }
                else
                {
                    _bus.Publish(DomainEventKind.HouseholdFormed, scheduled.PrimaryEntity, scheduled.SecondaryEntity);
                }
            }
        }

        private sealed class Reactor : IDomainEventSubscriber
        {
            private readonly SimulationClock _clock;

            internal Reactor(SimulationClock clock)
            {
                _clock = clock;
            }

            internal long Heard { get; private set; }

            public void On(in DomainEvent published)
            {
                Heard++;

                if (published.Kind == DomainEventKind.PersonDied)
                {
                    _clock.Schedule(
                        _clock.Now,
                        SimulationPhase.HouseholdAndSocial,
                        ScheduledEventKind.SocialDecision,
                        new EntityId(EntityKind.Household, published.PrimaryEntity.Value),
                        published.PrimaryEntity);
                }
            }
        }

        [Test]
        public void Publishing_under_AdvanceTo_at_steady_state_allocates_nothing()
        {
            var ids = new IdAllocator();
            var clock = new SimulationClock(ids);
            var bus = new DomainEventBus(ids, clock);
            // Two events per entity per day, for both passes; growth past
            // this would be an allocation the test is right to report.
            var journal = new EventJournal(Entities * 2 * Days * 2);
            var reactor = new Reactor(clock);
            bus.Subscribe(journal);
            bus.Subscribe(reactor);

            var router = new ScheduledEventRouter();
            var publisher = new Publisher(bus);
            router.Register(ScheduledEventKind.BirthCheck, publisher);
            router.Register(ScheduledEventKind.SocialDecision, publisher);

            for (var i = 1; i <= Entities; i++)
            {
                // Staggered through hours 1..23, never hour 0: a booking at
                // exactly midnight is due in the same AdvanceTo as its own
                // next-day rebooking, which would dispatch it twice in one
                // day and throw the per-pass count below off.
                clock.Schedule(
                    SimulationTime.FromHours(1L + (i % 23)),
                    SimulationPhase.Lifecycle,
                    ScheduledEventKind.BirthCheck,
                    new EntityId(EntityKind.Person, (ulong)i),
                    EntityId.None);
            }

            // First pass grows the queue and JITs every path. Measure the second.
            RunDays(clock, router, Days);
            var heardAfterWarmUp = reactor.Heard;

            var allocated = Allocations.Measure(() => RunDays(clock, router, Days));

            Assert.Multiple(() =>
            {
                Assert.That(allocated, Is.Zero, "bytes allocated on the test thread across " + Days + " simulated days");
                Assert.That(reactor.Heard - heardAfterWarmUp, Is.EqualTo(Entities * 2 * Days), "the measured span did publish");
                Assert.That(journal.Count, Is.EqualTo(reactor.Heard));
            });
        }

        private static void RunDays(SimulationClock clock, IScheduledEventHandler handler, int days)
        {
            var start = clock.Now;

            for (var day = 1L; day <= days; day++)
            {
                clock.AdvanceTo(start.Plus(day * SimulationTime.TicksPerDay), handler);
            }
        }
    }
}
