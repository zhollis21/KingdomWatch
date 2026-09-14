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
            var hunger = new Hunger(clock, bus, people);
            router.Register(ScheduledEventKind.MealDue, hunger);

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

        private static MobileGroup NewBand(IdAllocator ids, PersonStore people, int members, int food)
        {
            var band = new MobileGroup(
                ids.Next(EntityKind.MobileGroup), MobileGroupPurpose.NomadicBand, default);

            for (var i = 0; i < members; i++)
            {
                band.AddMember(people.Add(ids.Next(EntityKind.Person), default, 100, 0, 0, 0, SimulationTime.Zero));
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
