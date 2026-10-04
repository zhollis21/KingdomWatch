using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Construction;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Tests.Construction;
using KingdomWatch.Core.Work;
using NUnit.Framework;

namespace KingdomWatch.Core.Tests.Performance
{
    /// <summary>
    /// Section 18's zero-allocation tick loop, pointed at
    /// <see cref="Buildings"/> as <see cref="Jobs"/> drives it: once a village
    /// has its barn and fields, a span of working days - each dawn's look at
    /// whether to build, Farmers claiming and bringing in shares, a harvest -
    /// allocates nothing. Approving a building does allocate, a record and
    /// its cells, a handful of times a village's life, like founding one.
    /// </summary>
    [TestFixture]
    [Category("Performance")]
    public sealed class BuildingsAllocationTests
    {
        [Test]
        public void Farming_days_at_steady_state_allocate_nothing()
        {
            var w = new BuildingsWorld(people: 20);
            w.World.Jobs.Track(w.Settlement);
            w.Wood(Jobs.WoodCap + 2000);
            w.Stores.Gather(ResourceKind.Stone, Jobs.StoneCap);
            w.Stores.Gather(ResourceKind.Food, w.Living * 60);

            // Long enough to raise a house, a barn and fields and to bring a
            // crop in, so every path the measured span takes has run.
            w.World.Advance(40L * SimulationTime.TicksPerDay);
            Assert.That(w.Buildings.All, Has.Some.Matches<Building>(b => b.Kind == BuildingKind.Field && b.IsComplete));

            // No wood to build with from here, so nothing more is approved:
            // what is measured is the looking and the farming. Then a crop's
            // worth of days more, so that every field has been walked to: a
            // route buffer grows to the longest route it has held, which is
            // Jobs' contract, and a new field further out is a longer one.
            w.Stores.Consume(ResourceKind.Wood, w.Stores.Available(ResourceKind.Wood));
            w.Stores.Gather(ResourceKind.Wood, Jobs.WoodCap);
            w.World.Advance(20L * SimulationTime.TicksPerDay);
            var grain = w.Stores.Flows(ResourceKind.Grain).Gathered;
            var count = w.Buildings.All.Count;

            var allocated = Allocations.Measure(() => w.World.Advance(20L * SimulationTime.TicksPerDay));

            Assert.Multiple(() =>
            {
                Assert.That(allocated, Is.Zero, "bytes allocated on the test thread across 20 working days");
                Assert.That(w.Buildings.All.Count, Is.EqualTo(count), "nothing approved");
                Assert.That(w.Stores.Flows(ResourceKind.Grain).Gathered, Is.GreaterThan(grain), "fields harvested");
            });
        }
    }
}
