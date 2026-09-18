using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Events;
using KingdomWatch.Core.Lifecycle;
using KingdomWatch.Core.Nomadic;
using KingdomWatch.Core.Tests.Work;
using NUnit.Framework;

namespace KingdomWatch.Core.Tests.Performance
{
    /// <summary>
    /// Section 18's zero-allocation tick loop, pointed at
    /// <see cref="NomadicBands"/>: once a band has held a council, chosen a
    /// camp and arrived, a span of days that does all three again - with
    /// the work days, meals and camps in between - allocates nothing.
    /// Settling is the one path left out: it makes a settlement, and is
    /// meant to.
    /// </summary>
    [TestFixture]
    [Category("Performance")]
    public sealed class NomadicBandsAllocationTests
    {
        private const int Adults = 30;

        [Test]
        public void Councils_moves_and_camps_at_steady_state_allocate_nothing()
        {
            // Nobody is born or dies, so the span measures the band's
            // wandering and not the demographic model, which has its own
            // fixture.
            var settings = new DemographicSettings
            {
                ConceptionPerMille = 0,
                InfantMortalityPerMille = 0,
                ChildMortalityPerMille = 0,
                AdolescentMortalityPerMille = 0,
                AdultMortalityPerMille = 0,
                ElderMortalityPerMille = 0,
                SoftLifespanYears = 1_000L,
                MaxLifespanYears = 2_000L,
            };
            var w = new WorkWorld(1UL, WorkWorld.DefaultMap(), settings);

            // Fed and not working: Jobs is left off this band because a
            // route from a new camp can be longer than any before it, and
            // its site and slot buffers grow to the longest route they have
            // held - Jobs' documented behaviour, not the council's.
            var band = new MobileGroup(
                w.Demographics.Base.Ids.Next(EntityKind.MobileGroup), MobileGroupPurpose.NomadicBand, WorkWorld.Camp);
            w.Deaths.Track(band);
            w.Hunger.Track(band);
            w.Nomads.Track(band);
            w.JoinAdults(band, Adults);
            band.SharedSupplies.Gather(ResourceKind.Food, WorkWorld.PlentifulFood(Adults));
            band.SharedSupplies.Gather(ResourceKind.Wood, 1_000);

            // Warm-up past the first move and arrival: every path the
            // measured span takes - the daily council, the scored scan of
            // the hop box, the arrival, the camp - has run at least once.
            RunDays(w, NomadicBands.CampDays + 3);
            Assert.That(w.Count(DomainEventKind.CampPitched), Is.GreaterThan(1), "moved during warm-up");
            var campsBefore = w.Count(DomainEventKind.CampPitched);

            var allocated = Allocations.Measure(() => RunDays(w, 2 * NomadicBands.CampDays + 2));

            Assert.Multiple(() =>
            {
                Assert.That(allocated, Is.Zero, "bytes allocated on the test thread across two more camps");
                Assert.That(w.Count(DomainEventKind.CampPitched), Is.GreaterThanOrEqualTo(campsBefore + 2), "moved twice in the span");
                Assert.That(w.Nomads.TrackedCount, Is.EqualTo(1), "still wandering");
            });
        }

        private static void RunDays(WorkWorld w, long days)
        {
            var start = w.Now;

            for (var day = 1L; day <= days; day++)
            {
                w.AdvanceTo(start.Plus(day * SimulationTime.TicksPerDay));
            }
        }
    }
}
