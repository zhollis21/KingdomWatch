using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Events;
using KingdomWatch.Core.History;
using KingdomWatch.Core.Needs;
using NUnit.Framework;

namespace KingdomWatch.Core.Tests.Performance
{
    /// <summary>
    /// Section 18's zero-allocation tick loop, pointed at <see cref="Hunger"/>:
    /// once its holders are tracked, a span of meals - full ones, short ones,
    /// starvation damage, and both famine transitions - allocates nothing.
    /// </summary>
    [TestFixture]
    [Category("Performance")]
    public sealed class HungerAllocationTests
    {
        private const int WellFed = 60;

        private sealed class Ignore : IScheduledEventHandler
        {
            public void Handle(ScheduledEvent scheduled, SimulationClock clock)
            {
            }
        }
        private const int Starving = 45;

        [Test]
        public void Meals_at_steady_state_allocate_nothing()
        {
            var ids = new IdAllocator();
            var clock = new SimulationClock(ids);
            var bus = new DomainEventBus(clock);
            // Sized so the journal never grows during the measured span; a
            // resize there would be the journal's allocation, not a meal's.
            var journal = new EventJournal(64);
            bus.Subscribe(journal);
            var people = new PersonStore();
            var router = new ScheduledEventRouter();
            var hunger = new Hunger(bus, people);
            router.Register(ScheduledEventKind.MealDue, hunger);
            // The starving band reaches zero health in the warm-up, which
            // raises a crossing for Mortality; here nobody dies, so a no-op
            // answers it and the crossing costs what it costs.
            router.Register(ScheduledEventKind.StarvationCritical, new Ignore());

            var fed = NewBand(ids, people, WellFed, food: WellFed * Hunger.DailyRation * 100);
            var starving = NewBand(ids, people, Starving, food: Starving * Hunger.DailyRation * 10);
            hunger.Track(fed);
            hunger.Track(starving);

            // Thirty days: the starving band runs dry on day eleven and is
            // taking starvation damage by the end, so every path the measured
            // span will take has already been JIT-compiled.
            RunDays(clock, router, 30L);

            var allocated = Allocations.Measure(() =>
            {
                RunDays(clock, router, 15L);
                // Relief, then dry again: FamineEnded and a second
                // FamineStarted both publish inside the measured span.
                starving.SharedSupplies.Gather(ResourceKind.Food, Starving * Hunger.DailyRation * 5);
                RunDays(clock, router, 15L);
            });

            Assert.Multiple(() =>
            {
                Assert.That(allocated, Is.Zero, "bytes allocated on the test thread across 30 simulated days");
                Assert.That(journal.Count, Is.EqualTo(3), "FamineStarted, FamineEnded, FamineStarted");
                Assert.That(hunger.IsInFamine(starving), Is.True);
                Assert.That(hunger.IsInFamine(fed), Is.False);
            });
        }

        [Test]
        public void Rationed_winter_meals_at_steady_state_allocate_nothing()
        {
            // Winter meals ration, and to know how hard they count the band's
            // hearths (#149, the #156 review): a path the meals above, all
            // outside winter, never take. A store too small for the winter, a
            // woodpile too small too, so the shared gap is the one counted.
            var ids = new IdAllocator();
            var clock = new SimulationClock(ids);
            var bus = new DomainEventBus(clock);
            var journal = new EventJournal(64);
            bus.Subscribe(journal);
            var people = new PersonStore();
            var router = new ScheduledEventRouter();
            var hunger = new Hunger(bus, people);
            router.Register(ScheduledEventKind.MealDue, hunger);
            router.Register(ScheduledEventKind.StarvationCritical, new Ignore());

            var band = NewBand(ids, people, WellFed, food: WellFed * Hunger.DailyRation * 20);
            band.SharedSupplies.Gather(ResourceKind.Wood, 10);
            clock.AdvanceTo(SimulationTime.FromDays((3L * SimulationTime.DaysPerSeason) - 1L), router);
            hunger.Track(band);

            // Fifteen winter days of warm-up - long enough that those the
            // rations leave out have reached zero and raised their crossings,
            // as the test above lets its starving band - then ten measured.
            RunDays(clock, router, 15L);
            var allocated = Allocations.Measure(() => RunDays(clock, router, 10L));

            Assert.Multiple(() =>
            {
                Assert.That(clock.Now.Season, Is.EqualTo(Season.Winter));
                Assert.That(allocated, Is.Zero, "bytes allocated on the test thread across ten rationed winter days");
            });
        }

        private static MobileGroup NewBand(IdAllocator ids, PersonStore people, int members, int food)
        {
            var band = new MobileGroup(
                ids.Next(EntityKind.MobileGroup), MobileGroupPurpose.NomadicBand, default);

            for (var i = 0; i < members; i++)
            {
                // Every stage, so all three sittings run in the measured span.
                var stage = (AgeStage)(1 + (i % 5));
                band.AddMember(people.Add(ids.Next(EntityKind.Person), default, 100, stage, Sex.Female, 0, 0, SimulationTime.Zero, 0L));
            }

            band.SharedSupplies.Gather(ResourceKind.Food, food);
            return band;
        }

        private static void RunDays(SimulationClock clock, ScheduledEventRouter router, long days)
        {
            var start = clock.Now;

            for (var day = 1L; day <= days; day++)
            {
                clock.AdvanceTo(start.Plus(day * SimulationTime.TicksPerDay), router);
            }
        }
    }
}
