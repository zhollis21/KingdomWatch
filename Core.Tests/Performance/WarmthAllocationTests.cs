using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Needs;
using KingdomWatch.Core.Tests.Lifecycle;
using NUnit.Framework;

namespace KingdomWatch.Core.Tests.Performance
{
    /// <summary>
    /// Section 18's zero-allocation tick loop, pointed at <see cref="Warmth"/>:
    /// once its communities are tracked and a winter night has sized its
    /// hearth lists, a span of evenings - warm ones, short ones, exposure
    /// damage and the crossing it raises - allocates nothing.
    /// </summary>
    [TestFixture]
    [Category("Performance")]
    public sealed class WarmthAllocationTests
    {
        private const int Families = 12;

        private sealed class Ignore : IScheduledEventHandler
        {
            public void Handle(ScheduledEvent scheduled, SimulationClock clock)
            {
            }
        }

        [Test]
        public void Winter_evenings_at_steady_state_allocate_nothing()
        {
            var world = new HouseholdWorld();
            var warmth = new Warmth(world.Clock, world.People);
            var router = new ScheduledEventRouter();
            router.Register(ScheduledEventKind.WarmthDue, warmth);
            router.Register(ScheduledEventKind.ExposureCritical, new Ignore());

            // Every household shape the lighting order ranks - with a child,
            // without - plus people in none; and one band short of wood, so
            // the dark path and the damage run too.
            var warm = NewBand(world, wood: 10_000);
            var cold = NewBand(world, wood: 0);
            warmth.Track(warm);
            warmth.Track(cold);

            // Into winter, and long enough that the cold band has taken
            // damage and raised its crossings, so every path the measured
            // span takes has already been JIT-compiled. Fresh health for the
            // cold band's people then runs the damage path again inside it.
            var winter = SimulationTime.FromDays(3L * SimulationTime.DaysPerSeason);
            world.Clock.AdvanceTo(winter.Plus(15L * SimulationTime.TicksPerDay), router);

            for (var i = 0; i < cold.Members.Count; i++)
            {
                world.People.SetHealth(cold.Members[i], 30);
            }

            var allocated = Allocations.Measure(() =>
                world.Clock.AdvanceTo(winter.Plus(29L * SimulationTime.TicksPerDay), router));

            Assert.Multiple(() =>
            {
                Assert.That(allocated, Is.Zero, "bytes allocated on the test thread across 14 winter nights");
                Assert.That(warm.SharedSupplies.Flows(ResourceKind.Wood).Consumed, Is.GreaterThan(0L));
            });
        }

        private static MobileGroup NewBand(HouseholdWorld world, int wood)
        {
            var band = world.NewBand();

            for (var i = 0; i < Families; i++)
            {
                var household = world.NewCouple(out var wife, out var husband);
                band.AddMember(wife);
                band.AddMember(husband);

                if (i % 2 == 0)
                {
                    band.AddMember(world.NewChildOf(household, wife, husband, AgeStage.Child));
                }

                band.AddMember(world.NewPerson(AgeStage.Adult, Sex.Male));
            }

            if (wood > 0)
            {
                band.SharedSupplies.Gather(ResourceKind.Wood, wood);
            }

            return band;
        }
    }
}
