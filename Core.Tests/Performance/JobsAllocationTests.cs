using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Events;
using KingdomWatch.Core.Needs;
using KingdomWatch.Core.Tests.Work;
using KingdomWatch.Core.Traversal;
using KingdomWatch.Core.Work;
using NUnit.Framework;

namespace KingdomWatch.Core.Tests.Performance
{
    /// <summary>
    /// Section 18's zero-allocation tick loop, pointed at <see cref="Jobs"/>:
    /// once a band's people have each held a task, a span of work days -
    /// dawn passes with site searches, picks, tasks started, completed and
    /// chained, meals drawing what was gathered - allocates nothing.
    /// </summary>
    [TestFixture]
    [Category("Performance")]
    public sealed class JobsAllocationTests
    {
        private const int Adults = 40;

        [Test]
        public void Work_days_at_steady_state_allocate_nothing()
        {
            var ids = new IdAllocator();
            var clock = new SimulationClock(ids);
            var bus = new DomainEventBus(clock);
            var people = new PersonStore();
            var pathfinder = new Pathfinder(WorkWorld.DefaultMap(), TerrainRules.Default);
            var jobs = new Jobs(clock, people, pathfinder);
            var hunger = new Hunger(bus, people);
            var router = new ScheduledEventRouter();
            router.Register(ScheduledEventKind.WorkDayDue, jobs);
            router.Register(ScheduledEventKind.TaskCompleted, jobs);
            router.Register(ScheduledEventKind.MealDue, hunger);

            // Two bands so the tracked list is scanned past its first entry:
            // one hungry enough to forage, one fed enough to cut wood and
            // gather stone, both with every worker needing a task slot.
            var hungry = NewBand(ids, people, WorkWorld.Camp, food: Adults * Hunger.DailyRation);
            var fed = NewBand(ids, people, new WorldPosition(8, 8), food: WorkWorld.PlentifulFood(Adults));
            jobs.Track(hungry);
            jobs.Track(fed);
            hunger.Track(hungry);
            hunger.Track(fed);

            // Ten days: every worker has had a task and a route buffer, and
            // the fed band has reached the wood cap and moved on to stone, so
            // every path the measured span takes has run at least once.
            RunDays(clock, router, 10L);

            var allocated = Allocations.Measure(() => RunDays(clock, router, 20L));

            Assert.Multiple(() =>
            {
                Assert.That(allocated, Is.Zero, "bytes allocated on the test thread across 20 simulated days");
                Assert.That(hungry.SharedSupplies.Flows(ResourceKind.Food).Gathered, Is.GreaterThan(0L));
                Assert.That(fed.SharedSupplies.Flows(ResourceKind.Stone).Gathered, Is.GreaterThan(0L));
                Assert.That(hunger.IsInFamine(hungry), Is.False);
            });
        }

        private static MobileGroup NewBand(IdAllocator ids, PersonStore people, WorldPosition at, int food)
        {
            var band = new MobileGroup(ids.Next(EntityKind.MobileGroup), MobileGroupPurpose.NomadicBand, at);

            for (var i = 0; i < Adults; i++)
            {
                band.AddMember(people.Add(
                    ids.Next(EntityKind.Person), at, 100, AgeStage.Adult, Sex.Female, 0, 0, SimulationTime.Zero, 0L));
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
