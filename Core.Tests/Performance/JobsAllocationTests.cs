using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Events;
using KingdomWatch.Core.Knowledge;
using KingdomWatch.Core.Land;
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
            var grid = WorkWorld.DefaultMap();
            var pathfinder = new Pathfinder(grid, TerrainRules.Default);
            var knownMaps = new KnownMaps(grid);
            // The default map's one bush feeds forty for a month only if it
            // never runs out; running out is LandCoverTests' and JobsTests'.
            var land = new LandCover(grid, clock);
            WorkWorld.NeverRunsOut(land);
            var jobs = new Jobs(clock, people, pathfinder, knownMaps, land);
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

            // Each band holds a map and sees its own surroundings, as a
            // tracked band does in the world: without one the dawn pass is
            // refused, and the measured span would never run a day's work.
            knownMaps.Track(hungry.Id);
            knownMaps.Track(fed.Id);
            knownMaps.Reveal(hungry.Id, hungry.Position, Jobs.RevealRadius);
            knownMaps.Reveal(fed.Id, fed.Position, Jobs.RevealRadius);

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

        [Test]
        public void Stripping_bushes_and_taking_back_a_returned_trip_allocate_nothing()
        {
            // The paths running out adds (#26, the #141 review): a site spent
            // by the last pick is searched for again, and a search that found
            // nothing looks again once a claim has been given back. A thicket
            // of one-trip bushes and more hands than it has trips, with no
            // hunger to stop the foraging, strips every bush on the first day
            // of a season and then finds nothing. Spring is the warm-up: the
            // route buffers grow to the farthest bush. Summer refills every
            // bush, and the measured season repeats spring's picks exactly, so
            // no route is longer than one already seen.
            var ids = new IdAllocator();
            var clock = new SimulationClock(ids);
            var people = new PersonStore();
            var grid = new TerrainGrid(ThicketSide, ThicketSide, TerrainKind.Scrub);
            var pathfinder = new Pathfinder(grid, TerrainRules.Default);
            var knownMaps = new KnownMaps(grid);
            var land = new LandCover(grid, clock) { BushPicks = 1 };
            var jobs = new Jobs(clock, people, pathfinder, knownMaps, land);
            var router = new ScheduledEventRouter();
            router.Register(ScheduledEventKind.WorkDayDue, jobs);
            router.Register(ScheduledEventKind.TaskCompleted, jobs);

            var band = NewBand(ids, people, new WorldPosition(2, 2), food: 1);
            jobs.Track(band);
            knownMaps.Track(band.Id);
            knownMaps.Reveal(band.Id, band.Position, Jobs.MaxSiteRadius);

            SeasonWithAReturn(clock, router, jobs, band, SimulationTime.Zero);
            var gatheredBefore = band.SharedSupplies.Flows(ResourceKind.Food).Gathered;
            var returnsBefore = land.Returns;

            var summer = SimulationTime.FromDays(SimulationTime.DaysPerSeason);
            var allocated = Allocations.Measure(() => SeasonWithAReturn(clock, router, jobs, band, summer));

            Assert.Multiple(() =>
            {
                Assert.That(allocated, Is.Zero, "bytes allocated on the test thread across a season of stripping");
                Assert.That(
                    band.SharedSupplies.Flows(ResourceKind.Food).Gathered - gatheredBefore,
                    Is.EqualTo((long)ThicketSide * ThicketSide * PrimitiveTier.ForageSummer.Outputs[0].Quantity),
                    "every bush picked once, the returned trip included");
                Assert.That(land.Returns, Is.EqualTo(returnsBefore + 1L), "a claim was given back in the measured season");
                Assert.That(jobs.HasSite(band, JobKind.Forager), Is.False, "and the thicket ran out");
            });
        }

        // A thicket small enough to strip in a morning.
        private const int ThicketSide = 6;

        // From the season's first dawn: an hour in, the first worker out is
        // taken off their trip and the claim goes back; then the rest of a
        // fortnight runs.
        private static void SeasonWithAReturn(
            SimulationClock clock, ScheduledEventRouter router, Jobs jobs, MobileGroup band, SimulationTime seasonStart)
        {
            clock.AdvanceTo(seasonStart.Plus(Jobs.Dawn + SimulationTime.TicksPerHour), router);
            var members = band.Members;

            for (var i = 0; i < members.Count; i++)
            {
                if (jobs.HasTask(members[i]))
                {
                    jobs.Vacate(members[i]);
                    break;
                }
            }

            clock.AdvanceTo(seasonStart.Plus(14L * SimulationTime.TicksPerDay), router);
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
