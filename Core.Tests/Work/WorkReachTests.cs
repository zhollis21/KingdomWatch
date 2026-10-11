using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Tests.Construction;
using KingdomWatch.Core.Traversal;
using KingdomWatch.Core.Work;
using NUnit.Framework;

namespace KingdomWatch.Core.Tests.Work
{
    /// <summary>
    /// How far work goes: as far as a walk out that leaves the work and the
    /// walk home before dusk, the nearest site first. A village that cleared
    /// its first stand had nothing near to fell for the five years its stumps
    /// take to stand again (#149), and one that built over its near ground
    /// had nowhere near to forage.
    /// </summary>
    [TestFixture]
    public sealed class WorkReachTests
    {
        // The settlement by the west edge, and one tree due east of it, past
        // the sixteen cells work used to reach and well inside a day's walk.
        private static readonly WorldPosition Camp = new WorldPosition(6, 20);
        private static readonly WorldPosition FarTree = new WorldPosition(Camp.X + 24, Camp.Y);

        [Test]
        public void A_settlement_with_no_tree_near_sends_its_woodcutters_further()
        {
            var w = new BuildingsWorld(paint: grid => grid.Set(FarTree, TerrainKind.Forest), camp: Camp);
            w.World.Jobs.Track(w.Settlement);
            w.World.Jobs.RefreshSites(w.Settlement);

            Assert.That(w.World.Jobs.SiteFor(w.Settlement, JobKind.Woodcutter), Is.EqualTo(FarTree));
        }

        [Test]
        public void A_band_with_no_tree_near_goes_as_far()
        {
            // One reach for every community: a band no longer stops short of
            // what a settlement would walk to.
            var w = new BuildingsWorld(paint: grid => grid.Set(FarTree, TerrainKind.Forest), camp: Camp);
            var band = w.World.AddBand(4, Camp);
            w.World.Jobs.RefreshSites(band);

            Assert.That(w.World.Jobs.SiteFor(band, JobKind.Woodcutter), Is.EqualTo(FarTree));
        }

        [Test]
        public void Foragers_go_further_too_when_nothing_near_fruits()
        {
            var w = new BuildingsWorld(paint: grid => grid.Set(FarTree, TerrainKind.Scrub), camp: Camp);
            w.World.Jobs.Track(w.Settlement);
            w.World.Jobs.RefreshSites(w.Settlement);

            Assert.That(w.World.Jobs.SiteFor(w.Settlement, JobKind.Forager), Is.EqualTo(FarTree));
        }

        [Test]
        public void A_tree_near_is_found_first_however_many_stand_further_out()
        {
            var near = new WorldPosition(Camp.X + 5, Camp.Y);
            var w = new BuildingsWorld(
                paint: grid =>
                {
                    grid.Set(FarTree, TerrainKind.Forest);
                    grid.Set(near, TerrainKind.Forest);
                },
                camp: Camp);
            w.World.Jobs.Track(w.Settlement);
            w.World.Jobs.RefreshSites(w.Settlement);

            Assert.That(w.World.Jobs.SiteFor(w.Settlement, JobKind.Woodcutter), Is.EqualTo(near));
        }

        [Test]
        public void A_walk_out_leaves_the_work_and_the_walk_home_inside_the_day()
        {
            // Half of what is left of twelve hours once the work is done, in
            // route cost at two ticks a unit.
            Assert.Multiple(() =>
            {
                Assert.That(Jobs.ReachCost(4L * 3600L), Is.EqualTo(4L * 3600L / Jobs.TicksPerCostUnit));
                Assert.That(Jobs.ReachCost(0L), Is.EqualTo(6L * 3600L / Jobs.TicksPerCostUnit));
                Assert.That(Jobs.ReachCost(Jobs.Dusk - Jobs.Dawn), Is.Zero, "a whole day's work reaches nowhere but underfoot");
                Assert.That(Jobs.ReachCost(2L * (Jobs.Dusk - Jobs.Dawn)), Is.Zero, "nor does more");
                Assert.That(Jobs.ReachCost(JobKind.StoneGatherer, Season.Spring), Is.EqualTo(3L * 3600L / Jobs.TicksPerCostUnit));
                Assert.That(() => Jobs.ReachCost(-1L), Throws.TypeOf<System.ArgumentOutOfRangeException>());
            });
        }

        [Test]
        public void A_near_walk_keeps_every_trip_but_the_one_any_walk_costs()
        {
            // Four hours' work: three trips with no walk, two out to an hour
            // each way. Six hours': two with none, one out to the reach.
            const long Four = 4L * 3600L;
            const long Six = 6L * 3600L;

            Assert.Multiple(() =>
            {
                Assert.That(Jobs.NearWalk(Four), Is.EqualTo(3600L / Jobs.TicksPerCostUnit));
                Assert.That(Jobs.TripsPerDay(Jobs.NearWalk(Four), Four), Is.EqualTo(2L));
                Assert.That(Jobs.TripsPerDay(Jobs.NearWalk(Four) + 1L, Four), Is.EqualTo(1L));
                Assert.That(Jobs.NearWalk(Six), Is.EqualTo(Jobs.ReachCost(Six)));
                Assert.That(Jobs.NearWalk(Jobs.Dusk - Jobs.Dawn), Is.Zero, "a whole day's work keeps only underfoot");
                Assert.That(Jobs.NearWalk(2L * (Jobs.Dusk - Jobs.Dawn)), Is.Zero);
                Assert.That(() => Jobs.NearWalk(0L), Throws.TypeOf<System.ArgumentOutOfRangeException>());
            });
        }

        [Test]
        public void Nobody_looks_for_a_forager_s_site_in_winter()
        {
            // Every bush is bare, so there is none to find, near or far.
            var w = new BuildingsWorld(paint: grid => grid.Set(new WorldPosition(Camp.X + 2, Camp.Y), TerrainKind.Scrub), camp: Camp);
            w.World.Jobs.Track(w.Settlement);
            w.World.Jobs.RefreshSites(w.Settlement);
            var summer = w.World.Jobs.HasSite(w.Settlement, JobKind.Forager);

            w.World.AdvanceTo(SimulationTime.FromDays(3L * SimulationTime.DaysPerSeason));
            w.World.Jobs.RefreshSites(w.Settlement);

            Assert.Multiple(() =>
            {
                Assert.That(summer, Is.True);
                Assert.That(w.World.Jobs.HasSite(w.Settlement, JobKind.Forager), Is.False);
                Assert.That(KingdomWatch.Core.Land.LandCover.BushesFruitIn(Season.Winter), Is.False);
                Assert.That(KingdomWatch.Core.Land.LandCover.BushesFruitIn(Season.Autumn), Is.True);
                Assert.That(w.World.Jobs.SiteRouteOf(w.Settlement, JobKind.Forager), Is.Empty, "and no route left over from autumn");
                Assert.That(() => KingdomWatch.Core.Land.LandCover.BushesFruitIn((Season)99), Throws.TypeOf<System.ArgumentOutOfRangeException>());
            });
        }

        [Test]
        public void Trips_a_day_fall_as_the_walk_grows_and_stop_past_the_reach()
        {
            // Four hours' work in twelve of daylight: three trips with no walk,
            // two while the walk there and back takes four hours at most, one
            // out to the reach, none past it.
            const long Work = 4L * 3600L;
            var anHour = 3600L / Jobs.TicksPerCostUnit;

            Assert.Multiple(() =>
            {
                Assert.That(Jobs.TripsPerDay(0L, Work), Is.EqualTo(3L));
                Assert.That(Jobs.TripsPerDay(1L, Work), Is.EqualTo(2L));
                Assert.That(Jobs.TripsPerDay(anHour, Work), Is.EqualTo(2L), "an hour each way");
                Assert.That(Jobs.TripsPerDay(anHour + 1L, Work), Is.EqualTo(1L));
                Assert.That(Jobs.TripsPerDay(Jobs.ReachCost(Work), Work), Is.EqualTo(1L));
                Assert.That(Jobs.TripsPerDay(Jobs.ReachCost(Work) + 1L, Work), Is.Zero);
                Assert.That(Jobs.TripsPerDay(long.MaxValue, Work), Is.Zero, "and no overflow getting there");
                Assert.That(() => Jobs.TripsPerDay(-1L, Work), Throws.TypeOf<System.ArgumentOutOfRangeException>());
                Assert.That(() => Jobs.TripsPerDay(0L, 0L), Throws.TypeOf<System.ArgumentOutOfRangeException>());
            });
        }
    }
}
