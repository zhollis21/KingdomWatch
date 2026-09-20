using System;
using System.Collections.Generic;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Events;
using KingdomWatch.Core.Needs;
using KingdomWatch.Core.Traversal;
using KingdomWatch.Core.Work;
using NUnit.Framework;

namespace KingdomWatch.Core.Tests.Work
{
    [TestFixture]
    public sealed class JobsTests
    {
        private const long ForageTicks = 4L * SimulationTime.TicksPerHour;

        [Test]
        public void Construction_refuses_a_missing_collaborator()
        {
            var w = new WorkWorld();
            var pathfinder = new Pathfinder(w.Grid, TerrainRules.Default);

            Assert.Multiple(() =>
            {
                Assert.That(() => new Jobs(null!, w.People, pathfinder, w.KnownMaps), Throws.ArgumentNullException);
                Assert.That(() => new Jobs(w.Clock, null!, pathfinder, w.KnownMaps), Throws.ArgumentNullException);
                Assert.That(() => new Jobs(w.Clock, w.People, null!, w.KnownMaps), Throws.ArgumentNullException);
                Assert.That(() => new Jobs(w.Clock, w.People, pathfinder, null!), Throws.ArgumentNullException);
            });
        }

        [Test]
        public void Tracking_refuses_null_the_same_band_twice_off_the_map_and_where_nobody_can_stand()
        {
            var w = new WorkWorld();
            var band = w.NewBand(WorkWorld.Camp, 0);
            var ids = w.Demographics.Base.Ids;
            var lost = new MobileGroup(ids.Next(EntityKind.MobileGroup), MobileGroupPurpose.NomadicBand, new WorldPosition(99, 99));
            var inTheRiver = new MobileGroup(
                ids.Next(EntityKind.MobileGroup), MobileGroupPurpose.NomadicBand, new WorldPosition(WorkWorld.RiverColumn, 4));
            var pending = w.Clock.ScheduledCount;

            Assert.Multiple(() =>
            {
                Assert.That(() => w.Jobs.Track(null!), Throws.ArgumentNullException);
                Assert.That(() => w.Jobs.Track(band), Throws.InvalidOperationException);
                Assert.That(() => w.Jobs.Track(lost), Throws.TypeOf<ArgumentOutOfRangeException>(), "off the map");
                Assert.That(() => w.Jobs.Track(inTheRiver), Throws.TypeOf<ArgumentOutOfRangeException>(), "no site is reachable from a cell nobody can stand on; it would idle forever without a word");
                Assert.That(w.Jobs.TrackedCount, Is.EqualTo(1));
                Assert.That(w.Clock.ScheduledCount, Is.EqualTo(pending), "nothing refused booked a dawn");
            });
        }

        [Test]
        public void Tracking_books_the_next_dawn_and_nothing_else()
        {
            var w = new WorkWorld();
            var before = w.Clock.ScheduledCount;
            w.Jobs.Track(new MobileGroup(
                w.Demographics.Base.Ids.Next(EntityKind.MobileGroup), MobileGroupPurpose.NomadicBand, WorkWorld.Camp));

            Assert.Multiple(() =>
            {
                Assert.That(w.Clock.ScheduledCount, Is.EqualTo(before + 1));
                Assert.That(w.Clock.TryPeekNext(out var next), Is.True);
                Assert.That(next.Kind, Is.EqualTo(ScheduledEventKind.WorkDayDue));
                Assert.That(next.Time, Is.EqualTo(SimulationTime.FromHours(6L)));
                Assert.That(next.Phase, Is.EqualTo(SimulationPhase.Physical));
            });
        }

        [Test]
        public void A_band_tracked_at_dawn_starts_tomorrow()
        {
            var w = new WorkWorld();
            w.Advance(Jobs.Dawn);
            w.Jobs.Track(new MobileGroup(
                w.Demographics.Base.Ids.Next(EntityKind.MobileGroup), MobileGroupPurpose.NomadicBand, WorkWorld.Camp));

            Assert.That(w.Clock.TryPeekNext(out var next) && next.Kind == ScheduledEventKind.WorkDayDue, Is.True);
            Assert.That(next.Time, Is.EqualTo(SimulationTime.FromHours(6L).Plus(SimulationTime.TicksPerDay)));
        }

        [TestCase(AgeStage.Infant, false)]
        [TestCase(AgeStage.Child, false)]
        [TestCase(AgeStage.Adolescent, false)]
        [TestCase(AgeStage.Adult, true)]
        [TestCase(AgeStage.Elder, true)]
        [TestCase(AgeStage.None, false)]
        [TestCase((AgeStage)200, false)]
        public void Adults_and_elders_work(AgeStage stage, bool works) =>
            Assert.That(Jobs.Works(stage), Is.EqualTo(works));

        [Test]
        public void Nobody_has_a_task_or_a_site_before_the_first_dawn()
        {
            var w = new WorkWorld();
            var band = w.NewBand(WorkWorld.Camp, 0);
            var adult = w.Join(band, 30L);

            Assert.Multiple(() =>
            {
                Assert.That(w.Jobs.HasTask(adult), Is.False);
                Assert.That(w.People.GetJob(adult), Is.EqualTo(JobKind.None));
                Assert.That(w.Jobs.HasSite(band, JobKind.Forager), Is.False);
                Assert.That(() => w.Jobs.SiteFor(band, JobKind.Forager), Throws.InvalidOperationException);
                Assert.That(() => w.Jobs.TaskOf(adult), Throws.InvalidOperationException);
                Assert.That(() => w.Jobs.RouteOf(adult).Length, Throws.InvalidOperationException);
                Assert.That(() => w.Jobs.PositionAt(adult, w.Now), Throws.InvalidOperationException);
            });
        }

        [Test]
        public void At_dawn_a_hungry_band_forages_where_it_stands()
        {
            var w = new WorkWorld();
            var band = w.NewBand(WorkWorld.Camp, 0);
            var adults = w.JoinAdults(band, 4);
            var child = w.Join(band, 8L);
            var adolescent = w.Join(band, 14L);
            var elder = w.Join(band, 60L);

            w.AdvanceToDawn();

            Assert.Multiple(() =>
            {
                foreach (var adult in adults)
                {
                    Assert.That(w.People.GetJob(adult), Is.EqualTo(JobKind.Forager), adult.ToString());
                    Assert.That(w.Jobs.HasTask(adult), Is.True);
                }

                Assert.That(w.People.GetJob(elder), Is.EqualTo(JobKind.Forager), "elders work");
                Assert.That(w.People.GetJob(child), Is.EqualTo(JobKind.None));
                Assert.That(w.Jobs.HasTask(child), Is.False);
                Assert.That(w.People.GetJob(adolescent), Is.EqualTo(JobKind.None), "work assistance is #22's");
                Assert.That(w.Jobs.HasTask(adolescent), Is.False);

                Assert.That(w.Jobs.SiteFor(band, JobKind.Forager), Is.EqualTo(WorkWorld.Camp), "plains underfoot");
                var task = w.Jobs.TaskOf(adults[0]);
                Assert.That(task.Job, Is.EqualTo(JobKind.Forager));
                Assert.That(task.Holder, Is.EqualTo(band.Id));
                Assert.That(task.Start, Is.EqualTo(w.Now));
                Assert.That(task.TravelTicks, Is.Zero);
                Assert.That(task.ReturnTicks, Is.Zero);
                Assert.That(task.WorkTicks, Is.EqualTo(PrimitiveTier.Forage.Duration));
                Assert.That(task.Origin, Is.EqualTo(WorkWorld.Camp));
                Assert.That(task.Destination, Is.EqualTo(WorkWorld.Camp));
                Assert.That(w.Jobs.RouteOf(adults[0]).Length, Is.EqualTo(1));
                Assert.That(w.Jobs.PositionAt(adults[0], w.Now), Is.EqualTo(WorkWorld.Camp));
            });
        }

        [Test]
        public void A_fed_band_cuts_wood_then_stone_then_rests()
        {
            var w = new WorkWorld();
            var band = w.NewBand(WorkWorld.Camp, WorkWorld.PlentifulFood(1));
            var adult = w.Join(band, 30L);

            w.AdvanceToDawn();
            Assert.That(w.People.GetJob(adult), Is.EqualTo(JobKind.Woodcutter), "food covered, wood short");
            Assert.That(w.Jobs.SiteFor(band, JobKind.Woodcutter), Is.EqualTo(WorkWorld.ForestCell));
            Assert.That(w.Jobs.TaskOf(adult).TravelTicks, Is.EqualTo(WorkWorld.TicksToForest));
            Assert.That(w.Jobs.TaskOf(adult).ReturnTicks, Is.EqualTo(WorkWorld.TicksBack), "the way home enters plains only");

            band.SharedSupplies.Gather(ResourceKind.Wood, Jobs.WoodCap);
            w.Advance(SimulationTime.TicksPerDay);
            Assert.That(w.People.GetJob(adult), Is.EqualTo(JobKind.StoneGatherer), "wood capped, stone short");
            Assert.That(w.Jobs.SiteFor(band, JobKind.StoneGatherer), Is.EqualTo(WorkWorld.HillsCell));
            Assert.That(w.Jobs.TaskOf(adult).TravelTicks, Is.EqualTo(WorkWorld.TicksToHills));
            Assert.That(w.Jobs.TaskOf(adult).ReturnTicks, Is.EqualTo(WorkWorld.TicksBack));

            band.SharedSupplies.Gather(ResourceKind.Stone, Jobs.StoneCap);
            w.Advance(SimulationTime.TicksPerDay);
            Assert.That(w.People.GetJob(adult), Is.EqualTo(JobKind.None), "everything covered: labour surplus");
            Assert.That(w.Jobs.HasTask(adult), Is.False);
        }

        [Test]
        public void What_is_on_its_way_home_counts_toward_the_food_target()
        {
            // Two people draw six a day; ten days is sixty. With 57 in store
            // the first picker sees nine days and forages; the second sees
            // 57 + 3 on its way = ten days, and goes for wood instead.
            var w = new WorkWorld();
            var band = w.NewBand(WorkWorld.Camp, 57);
            var first = w.Join(band, 30L);
            var second = w.Join(band, 31L);

            w.AdvanceToDawn();

            Assert.Multiple(() =>
            {
                Assert.That(w.People.GetJob(first), Is.EqualTo(JobKind.Forager));
                Assert.That(w.People.GetJob(second), Is.EqualTo(JobKind.Woodcutter));
            });
        }

        [Test]
        public void With_nowhere_to_work_a_job_is_skipped_for_the_day()
        {
            var w = new WorkWorld(1UL, WorkWorld.PlainsOnly());
            var band = w.NewBand(WorkWorld.Camp, WorkWorld.PlentifulFood(1));
            var adult = w.Join(band, 30L);

            w.AdvanceToDawn();

            Assert.Multiple(() =>
            {
                Assert.That(w.Jobs.HasSite(band, JobKind.Forager), Is.True);
                Assert.That(w.Jobs.HasSite(band, JobKind.Woodcutter), Is.False);
                Assert.That(w.Jobs.HasSite(band, JobKind.StoneGatherer), Is.False);
                Assert.That(w.People.GetJob(adult), Is.EqualTo(JobKind.None), "fed, and nothing else within reach");
            });
        }

        [Test]
        public void A_site_across_the_river_is_not_a_site()
        {
            // The only forest is on the far bank. Foraging still works
            // underfoot, so the band eats; it just never cuts wood.
            var grid = WorkWorld.PlainsOnly();
            grid.Set(new WorldPosition(WorkWorld.RiverColumn + 2, 2), TerrainKind.Forest);

            for (var y = 0; y < WorkWorld.Height; y++)
            {
                grid.Set(new WorldPosition(WorkWorld.RiverColumn, y), TerrainKind.SmallRiver);
            }

            var w = new WorkWorld(1UL, grid);
            var band = w.NewBand(WorkWorld.Camp, WorkWorld.PlentifulFood(1));
            var adult = w.Join(band, 30L);

            w.AdvanceToDawn();

            Assert.Multiple(() =>
            {
                Assert.That(w.Jobs.HasSite(band, JobKind.Woodcutter), Is.False);
                Assert.That(w.People.GetJob(adult), Is.EqualTo(JobKind.None));
            });
        }

        [Test]
        public void Sites_are_found_from_where_the_band_stands()
        {
            var w = new WorkWorld();
            var band = w.NewBand(WorkWorld.Camp, WorkWorld.PlentifulFood(1));
            w.AdvanceToDawn();
            Assert.That(w.Jobs.SiteFor(band, JobKind.Woodcutter), Is.EqualTo(WorkWorld.ForestCell));

            // The band walks to stand on the forest; wood is now underfoot.
            band.Position = WorkWorld.ForestCell;
            w.Jobs.RefreshSites(band);

            Assert.Multiple(() =>
            {
                Assert.That(w.Jobs.SiteFor(band, JobKind.Woodcutter), Is.EqualTo(WorkWorld.ForestCell));
                Assert.That(w.Jobs.SiteFor(band, JobKind.Forager), Is.EqualTo(WorkWorld.ForestCell), "forest forages too");
                Assert.That(() => w.Jobs.RefreshSites(new MobileGroup(
                    w.Demographics.Base.Ids.Next(EntityKind.MobileGroup), MobileGroupPurpose.NomadicBand, WorkWorld.Camp)),
                    Throws.InvalidOperationException, "untracked");
                Assert.That(() => w.Jobs.HasSite(band, JobKind.None), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => w.Jobs.HasSite(band, (JobKind)200), Throws.TypeOf<ArgumentOutOfRangeException>());
            });
        }

        [Test]
        public void A_band_that_moved_since_dawn_picks_from_where_it_stands()
        {
            // Sites are found from the band's position at dawn. If the band
            // has moved by the time someone picks, a task built from those
            // sites would start at the new camp and walk a route from the
            // old one; the pick refreshes first instead. The worker came home
            // to the old camp - a task ends where it started - and what a
            // moving band does about that is #54's; here, the next task sets
            // out from wherever the band is.
            var w = new WorkWorld();
            var band = w.NewBand(WorkWorld.Camp, WorkWorld.PlentifulFood(1));
            var adult = w.Join(band, 30L);
            w.AdvanceToDawn();
            var first = w.Jobs.TaskOf(adult);
            Assert.That(first.Origin, Is.EqualTo(WorkWorld.Camp));

            var newCamp = new WorldPosition(6, 3);
            band.Position = newCamp;
            w.AdvanceTo(first.End);

            var next = w.Jobs.TaskOf(adult);
            var route = w.Jobs.RouteOf(adult).ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(next.Origin, Is.EqualTo(newCamp));
                Assert.That(route[0], Is.EqualTo(newCamp), "the route starts where the worker does");
                Assert.That(next.Destination, Is.EqualTo(WorkWorld.ForestCell));
                Assert.That(route.Length, Is.EqualTo(2), "one step from the new camp to the forest");
                Assert.That(next.TravelTicks, Is.EqualTo(200L));
                Assert.That(w.Jobs.SiteFor(band, JobKind.Woodcutter), Is.EqualTo(WorkWorld.ForestCell));
            });
        }

        [Test]
        public void A_band_on_the_road_starts_nobody_at_dawn_and_still_books_tomorrow()
        {
            // A moving day (#54): the band's council set a destination before
            // the work pass, so nobody walks out from a camp the band is
            // leaving. Tomorrow's dawn is still booked - the band arrives
            // today, and works from the new camp tomorrow.
            var w = new WorkWorld();
            var band = w.NewBand(WorkWorld.Camp, 0);
            var adults = w.JoinAdults(band, 3);
            band.Destination = new WorldPosition(6, 3);
            var pending = w.Clock.ScheduledCount;

            w.AdvanceToDawn();

            Assert.Multiple(() =>
            {
                for (var i = 0; i < adults.Count; i++)
                {
                    Assert.That(w.Jobs.HasTask(adults[i]), Is.False, adults[i] + " stays with the band");
                }

                Assert.That(w.Clock.ScheduledCount, Is.EqualTo(pending), "tomorrow's dawn replaced today's, nothing else booked");
            });

            band.Position = band.Destination.Value;
            band.Destination = null;
            w.AdvanceToDawn();

            Assert.That(w.Jobs.HasTask(adults[0]), Is.True, "work resumes from the new camp");
            Assert.That(w.Jobs.TaskOf(adults[0]).Origin, Is.EqualTo(new WorldPosition(6, 3)));
        }

        [Test]
        public void Untracking_cancels_the_dawn_and_refuses_a_band_with_someone_out()
        {
            var w = new WorkWorld();
            var band = w.NewBand(WorkWorld.Camp, 0);
            var adult = w.Join(band, 30L);
            w.AdvanceToDawn();
            Assert.That(w.Jobs.HasTask(adult), Is.True);

            Assert.That(() => w.Jobs.Untrack(band), Throws.InvalidOperationException, "someone is out");

            w.AdvanceTo(w.Today(Jobs.Dusk));
            Assert.That(w.Jobs.HasTask(adult), Is.False, "home by dusk");
            var pending = w.Clock.ScheduledCount;

            w.Jobs.Untrack(band);

            Assert.Multiple(() =>
            {
                Assert.That(w.Jobs.TrackedCount, Is.Zero);
                Assert.That(w.Clock.ScheduledCount, Is.EqualTo(pending - 1), "the dawn is cancelled");
                Assert.That(() => w.Jobs.Untrack(band), Throws.InvalidOperationException, "not tracked now");
                Assert.That(() => w.Jobs.Untrack(null!), Throws.ArgumentNullException);
                Assert.That(() => w.Jobs.Track(band), Throws.Nothing, "and can be tracked afresh");
            });
        }

        [Test]
        public void A_completed_task_delivers_to_the_ledger_and_the_next_begins()
        {
            var w = new WorkWorld();
            var band = w.NewBand(WorkWorld.Camp, 0);
            var adult = w.Join(band, 30L);
            w.AdvanceToDawn();
            var first = w.Jobs.TaskOf(adult);

            w.AdvanceTo(first.End);

            Assert.Multiple(() =>
            {
                Assert.That(band.SharedSupplies.Flows(ResourceKind.Food).Gathered, Is.EqualTo(3L));
                Assert.That(band.SharedSupplies.Available(ResourceKind.Food), Is.EqualTo(3));
                Assert.That(w.Jobs.HasTask(adult), Is.True, "still hungry, still daylight: out again");
                Assert.That(w.Jobs.TaskOf(adult).Start, Is.EqualTo(first.End));
                Assert.That(w.Jobs.TaskOf(adult).Completion, Is.Not.EqualTo(first.Completion));
            });
        }

        [Test]
        public void Work_stops_at_dusk_and_resumes_at_dawn()
        {
            // Foraging underfoot takes four hours: 06-10, 10-14, 14-18, and
            // the trip that would end at 22:00 is not taken.
            var w = new WorkWorld();
            var band = w.NewBand(WorkWorld.Camp, 0);
            var adult = w.Join(band, 30L);

            w.AdvanceTo(w.Today(Jobs.Dusk));
            Assert.Multiple(() =>
            {
                Assert.That(band.SharedSupplies.Flows(ResourceKind.Food).Gathered, Is.EqualTo(9L), "three trips");
                Assert.That(w.Jobs.HasTask(adult), Is.False);
                Assert.That(w.People.GetJob(adult), Is.EqualTo(JobKind.None));
            });

            w.AdvanceTo(w.Today(Jobs.Dawn).Plus(SimulationTime.TicksPerDay).Plus(-1L));
            Assert.That(w.Jobs.HasTask(adult), Is.False, "the night is idle");

            w.Advance(1L);
            Assert.That(w.Jobs.HasTask(adult), Is.True, "and dawn is not");
        }

        [Test]
        public void A_trip_that_would_end_after_dusk_is_not_taken()
        {
            // Standing on hills, the nearest place to forage is a plains cell
            // one step away: 100 ticks each way. Trips end at 10:03:20 and
            // 14:06:40; the third would end at 18:10 and is not started.
            var grid = WorkWorld.PlainsOnly();
            grid.Set(WorkWorld.Camp, TerrainKind.Hills);
            var w = new WorkWorld(1UL, grid);
            var band = w.NewBand(WorkWorld.Camp, 0);
            var adult = w.Join(band, 30L);

            w.AdvanceToDawn();
            Assert.That(w.Jobs.TaskOf(adult).TravelTicks, Is.EqualTo(100L), "out onto plains");
            Assert.That(w.Jobs.TaskOf(adult).ReturnTicks, Is.EqualTo(300L), "back up the hill");

            w.AdvanceTo(w.Today(Jobs.Dusk));

            Assert.Multiple(() =>
            {
                Assert.That(band.SharedSupplies.Flows(ResourceKind.Food).Gathered, Is.EqualTo(6L), "two trips, not three");
                Assert.That(w.Jobs.HasTask(adult), Is.False);
            });
        }

        [Test]
        public void The_reconstruction_places_a_worker_along_the_route()
        {
            var w = new WorkWorld();
            var band = w.NewBand(WorkWorld.Camp, WorkWorld.PlentifulFood(1));
            var adult = w.Join(band, 30L);
            w.AdvanceToDawn();
            var task = w.Jobs.TaskOf(adult);
            var route = w.Jobs.RouteOf(adult).ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(task.Job, Is.EqualTo(JobKind.Woodcutter));
                Assert.That(route.Length, Is.EqualTo(5), "camp, three plains, forest");
                Assert.That(route[0], Is.EqualTo(WorkWorld.Camp));
                Assert.That(route[4], Is.EqualTo(WorkWorld.ForestCell));
                Assert.That(task.TravelTicks, Is.EqualTo(500L));

                var outbound = task.Start.Plus(250L);
                Assert.That(task.PhaseAt(outbound), Is.EqualTo(TaskPhase.Outbound));
                Assert.That(w.Jobs.PositionAt(adult, outbound), Is.EqualTo(new WorldPosition(4, 2)), "halfway out");
                Assert.That(w.Jobs.PositionAt(adult, task.Start), Is.EqualTo(WorkWorld.Camp), "setting out");
                Assert.That(w.Jobs.PositionAt(adult, task.Start.Plus(499L)), Is.EqualTo(new WorldPosition(5, 2)), "the last cell before the forest");

                var working = task.Start.Plus(500L + 100L);
                Assert.That(task.PhaseAt(working), Is.EqualTo(TaskPhase.Working));
                Assert.That(w.Jobs.PositionAt(adult, working), Is.EqualTo(WorkWorld.ForestCell));

                var returning = task.Start.Plus(500L + task.WorkTicks + 250L);
                Assert.That(task.PhaseAt(returning), Is.EqualTo(TaskPhase.Returning));
                Assert.That(w.Jobs.PositionAt(adult, returning), Is.EqualTo(new WorldPosition(4, 2)), "halfway home");
                Assert.That(w.Jobs.PositionAt(adult, task.End), Is.EqualTo(WorkWorld.Camp), "home");

                Assert.That(() => w.Jobs.PositionAt(adult, task.Start.Plus(-1L)), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => w.Jobs.PositionAt(adult, task.End.Plus(1L)), Throws.TypeOf<ArgumentOutOfRangeException>());
            });
        }

        [Test]
        public void A_task_keeps_the_route_it_left_with()
        {
            var w = new WorkWorld();
            var band = w.NewBand(WorkWorld.Camp, WorkWorld.PlentifulFood(1));
            var adult = w.Join(band, 30L);
            w.AdvanceToDawn();

            // A bridge appears and the band's site moves; the worker already
            // out still walks the route they set out on.
            w.Grid.Set(new WorldPosition(3, 2), TerrainKind.Forest);
            w.Jobs.RefreshSites(band);

            Assert.Multiple(() =>
            {
                Assert.That(w.Jobs.SiteFor(band, JobKind.Woodcutter), Is.EqualTo(new WorldPosition(3, 2)));
                Assert.That(w.Jobs.TaskOf(adult).Destination, Is.EqualTo(WorkWorld.ForestCell));
                Assert.That(w.Jobs.RouteOf(adult).Length, Is.EqualTo(5));
            });
        }

        [Test]
        public void Death_cancels_the_task_and_vacates_the_job()
        {
            var w = new WorkWorld();
            var band = w.NewBand(WorkWorld.Camp, 0);
            var worker = w.Join(band, 30L);
            var other = w.Join(band, 31L);
            w.AdvanceToDawn();
            var task = w.Jobs.TaskOf(worker);
            var pending = w.Clock.ScheduledCount;

            w.Advance(ForageTicks / 2L);
            w.Deaths.Die(worker, new Reasons(ReasonCode.Illness));

            Assert.Multiple(() =>
            {
                Assert.That(
                    w.Clock.ScheduledCount,
                    Is.EqualTo(pending - 2),
                    "the completion and the dead worker's yearly check are both gone");
                Assert.That(w.Jobs.HasTask(worker), Is.False);
                Assert.That(w.People.IsAlive(worker), Is.False);
                Assert.That(w.Jobs.HasTask(other), Is.True, "the living carry on");
            });

            // The dead deliver nothing; the survivor's first trip does.
            w.AdvanceTo(task.End);
            Assert.That(band.SharedSupplies.Flows(ResourceKind.Food).Gathered, Is.EqualTo(3L));
        }

        [Test]
        public void Vacating_someone_with_no_task_is_harmless()
        {
            var w = new WorkWorld();
            var band = w.NewBand(WorkWorld.Camp, 0);
            var idle = w.Join(band, 8L);

            Assert.That(() => w.Jobs.Vacate(idle), Throws.Nothing);
            Assert.That(w.People.GetJob(idle), Is.EqualTo(JobKind.None));
        }

        [Test]
        public void A_recycled_slot_does_not_inherit_its_last_occupants_task()
        {
            var w = new WorkWorld();
            var band = w.NewBand(WorkWorld.Camp, 0);
            var worker = w.Join(band, 30L);
            w.AdvanceToDawn();
            Assert.That(w.Jobs.HasTask(worker), Is.True);

            // Vacated by the cascade, so the slot's task is cleared; the
            // stale handle and the slot's next occupant both read as taskless.
            w.Deaths.Die(worker, new Reasons(ReasonCode.Illness));
            var successor = w.Join(band, 30L);

            Assert.Multiple(() =>
            {
                Assert.That(successor.Index, Is.EqualTo(worker.Index), "the store recycles the slot");
                Assert.That(w.Jobs.HasTask(worker), Is.False);
                Assert.That(w.Jobs.HasTask(successor), Is.False);
            });

            w.Advance(SimulationTime.TicksPerDay);
            Assert.Multiple(() =>
            {
                Assert.That(w.Jobs.HasTask(successor), Is.True);
                Assert.That(w.Jobs.TaskOf(successor).Worker, Is.EqualTo(successor));
                Assert.That(w.Jobs.HasTask(worker), Is.False, "the stale handle shares the index, not the generation");
            });
        }

        [Test]
        public void A_completion_for_someone_with_no_task_is_a_wiring_bug()
        {
            var w = new WorkWorld();
            var band = w.NewBand(WorkWorld.Camp, WorkWorld.PlentifulFood(1));
            var idle = w.Join(band, 8L);
            w.Clock.Schedule(
                w.Now.Plus(10L), Jobs.Phase, ScheduledEventKind.TaskCompleted, w.People.GetId(idle), EntityId.None);

            Assert.That(() => w.Advance(10L), Throws.InvalidOperationException);
        }

        [Test]
        public void A_work_day_for_an_untracked_band_is_a_wiring_bug()
        {
            var w = new WorkWorld();
            var stranger = new EntityId(EntityKind.MobileGroup, 999UL);
            w.Clock.Schedule(w.Now.Plus(10L), Jobs.Phase, ScheduledEventKind.WorkDayDue, stranger, EntityId.None);

            Assert.That(() => w.Advance(10L), Throws.InvalidOperationException);
        }

        [Test]
        public void Handling_refuses_another_kind_or_another_clock()
        {
            var w = new WorkWorld();
            var other = new SimulationClock(new IdAllocator());
            var meal = new ScheduledEvent(
                new EventId(1UL), w.Now, Jobs.Phase, ScheduledEventKind.MealDue, EntityId.None, EntityId.None);
            var dawn = new ScheduledEvent(
                new EventId(2UL), w.Now, Jobs.Phase, ScheduledEventKind.WorkDayDue, EntityId.None, EntityId.None);

            Assert.Multiple(() =>
            {
                Assert.That(() => w.Jobs.Handle(meal, w.Clock), Throws.InvalidOperationException);
                Assert.That(() => w.Jobs.Handle(dawn, other), Throws.InvalidOperationException);
            });
        }

        [Test]
        public void Queries_refuse_no_handle_and_vacating_refuses_the_dead()
        {
            var w = new WorkWorld();
            var band = w.NewBand(WorkWorld.Camp, 0);
            var worker = w.Join(band, 30L);
            w.AdvanceToDawn();
            w.Deaths.Die(worker, new Reasons(ReasonCode.Illness));

            Assert.Multiple(() =>
            {
                Assert.That(w.Jobs.HasTask(PersonHandle.None), Is.False);
                Assert.That(() => w.Jobs.TaskOf(PersonHandle.None), Throws.InvalidOperationException);
                Assert.That(() => w.Jobs.Vacate(PersonHandle.None), Throws.ArgumentException, "nobody to vacate");
                Assert.That(() => w.Jobs.Vacate(worker), Throws.ArgumentException, "stale: the cascade vacated before removing");
                Assert.That(() => w.Jobs.RefreshSites(null!), Throws.ArgumentNullException);
                Assert.That(() => w.Jobs.SiteFor(band, JobKind.None), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => w.Jobs.SiteFor(band, (JobKind)200), Throws.TypeOf<ArgumentOutOfRangeException>());
            });
        }

        [Test]
        public void Each_band_picks_from_its_own_ledger_and_members()
        {
            var w = new WorkWorld();
            var hungry = w.NewBand(WorkWorld.Camp, 0);
            var fed = w.NewBand(new WorldPosition(8, 8), WorkWorld.PlentifulFood(2));
            var hungryAdult = w.Join(hungry, 30L);
            var fedAdults = w.JoinAdults(fed, 2);

            w.AdvanceToDawn();

            Assert.Multiple(() =>
            {
                Assert.That(w.People.GetJob(hungryAdult), Is.EqualTo(JobKind.Forager));
                Assert.That(w.People.GetJob(fedAdults[0]), Is.EqualTo(JobKind.Woodcutter));
                Assert.That(w.People.GetJob(fedAdults[1]), Is.EqualTo(JobKind.Woodcutter), "the hungry band's shortfall is not theirs");
                Assert.That(w.Jobs.TaskOf(hungryAdult).Holder, Is.EqualTo(hungry.Id));
                Assert.That(w.Jobs.TaskOf(fedAdults[0]).Holder, Is.EqualTo(fed.Id));
                Assert.That(w.Jobs.TaskOf(fedAdults[0]).Origin, Is.EqualTo(fed.Position));
            });
        }

        [Test]
        public void A_route_buffer_grows_for_a_longer_route_and_reads_short_again_after()
        {
            var w = new WorkWorld();
            var band = w.NewBand(WorkWorld.Camp, 0);
            var adult = w.Join(band, 30L);

            w.AdvanceToDawn();
            Assert.That(w.Jobs.RouteOf(adult).Length, Is.EqualTo(1), "foraging underfoot");

            band.SharedSupplies.Gather(ResourceKind.Food, WorkWorld.PlentifulFood(1));
            w.Advance(SimulationTime.TicksPerDay);
            Assert.That(w.Jobs.RouteOf(adult).Length, Is.EqualTo(5), "out to the forest");
            Assert.That(w.Jobs.RouteOf(adult)[4], Is.EqualTo(WorkWorld.ForestCell));

            band.SharedSupplies.Consume(ResourceKind.Food, band.SharedSupplies.Available(ResourceKind.Food));
            w.Advance(SimulationTime.TicksPerDay);
            var route = w.Jobs.RouteOf(adult);
            Assert.That(route.Length, Is.EqualTo(1), "back to foraging, in a buffer that once held five");
            Assert.That(route[0], Is.EqualTo(WorkWorld.Camp));
            Assert.That(w.Jobs.PositionAt(adult, w.Now), Is.EqualTo(WorkWorld.Camp));
        }

        [Test]
        public void A_band_that_cannot_be_given_a_dawn_is_not_left_half_tracked()
        {
            // The only way the booking fails is the clock standing within a
            // day of the end of time with no dawn left in it.
            var w = new WorkWorld();
            var band = new MobileGroup(
                w.Demographics.Base.Ids.Next(EntityKind.MobileGroup), MobileGroupPurpose.NomadicBand, WorkWorld.Camp);
            w.Clock.AdvanceTo(new SimulationTime(long.MaxValue - 1L), w.Router);

            Assert.Multiple(() =>
            {
                Assert.That(() => w.Jobs.Track(band), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(w.Jobs.TrackedCount, Is.Zero);
                Assert.That(w.Clock.ScheduledCount, Is.Zero);
            });
        }

        [Test]
        public void The_last_dawn_the_world_can_hold_works_what_fits_and_books_nothing_after()
        {
            // The last representable instant is 15:30:07 on its day. Its dawn
            // is a dawn: two forages fit before the world ends, the third
            // would not, and there is no tomorrow to book.
            var w = new WorkWorld();
            var band = new MobileGroup(
                w.Demographics.Base.Ids.Next(EntityKind.MobileGroup), MobileGroupPurpose.NomadicBand, WorkWorld.Camp);
            var adult = w.People.Add(
                w.Demographics.Base.Ids.Next(EntityKind.Person), WorkWorld.Camp, 100, AgeStage.Adult, Sex.Male, 0, 0,
                SimulationTime.Zero, 0L);
            band.AddMember(adult);
            w.KnownMaps.Track(band.Id);
            w.KnownMaps.Reveal(band.Id, WorkWorld.Camp, Jobs.RevealRadius);
            var end = new SimulationTime(long.MaxValue);
            var lastDawn = new SimulationTime(end.Ticks - end.TickOfDay + Jobs.Dawn);
            w.Clock.AdvanceTo(lastDawn.Plus(-1L), w.Router);
            w.Jobs.Track(band);

            var dispatched = w.Clock.AdvanceTo(end, w.Router);

            Assert.Multiple(() =>
            {
                Assert.That(dispatched, Is.EqualTo(3), "the dawn and two completions");
                Assert.That(band.SharedSupplies.Flows(ResourceKind.Food).Gathered, Is.EqualTo(6L));
                Assert.That(w.Jobs.HasTask(adult), Is.False);
                Assert.That(w.Clock.ScheduledCount, Is.Zero);
            });
        }

        [Test]
        public void Priority_cannot_be_changed_from_outside()
        {
            Assert.Multiple(() =>
            {
                Assert.That(Jobs.Priority, Is.EqualTo(new[] { JobKind.Forager, JobKind.Woodcutter, JobKind.StoneGatherer }));
                Assert.That(Jobs.Priority, Is.Not.InstanceOf<JobKind[]>());
                Assert.That(() => ((IList<JobKind>)Jobs.Priority).Clear(), Throws.TypeOf<NotSupportedException>());
            });
        }

        [Test]
        public void An_empty_band_has_a_dawn_pass_and_books_the_next()
        {
            var w = new WorkWorld();
            var band = w.NewBand(WorkWorld.Camp, 0);

            w.AdvanceToDawn();
            Assert.That(w.Jobs.SiteFor(band, JobKind.Forager), Is.EqualTo(WorkWorld.Camp), "sites are found regardless");

            // The next pass proves itself by finding sites from the new spot.
            band.Position = WorkWorld.ForestCell;
            w.Advance(SimulationTime.TicksPerDay);
            Assert.That(w.Jobs.SiteFor(band, JobKind.Forager), Is.EqualTo(WorkWorld.ForestCell));
        }

        [Test]
        public void A_work_day_that_is_not_the_bands_own_is_a_wiring_bug()
        {
            // The band names the dawn it booked, as a task names its
            // completion. Any other WorkDayDue - one scheduled by hand, or
            // rebuilt from a save that disagrees - would run a second pass
            // and book a second stream, doubling every dawn from then on.
            var w = new WorkWorld();
            var band = w.NewBand(WorkWorld.Camp, 0);
            var adult = w.Join(band, 30L);
            var pending = w.Clock.ScheduledCount;
            w.Clock.Schedule(w.Now.Plus(10L), Jobs.Phase, ScheduledEventKind.WorkDayDue, band.Id, EntityId.None);

            Assert.Multiple(() =>
            {
                Assert.That(() => w.Advance(10L), Throws.InvalidOperationException);
                Assert.That(w.Jobs.HasTask(adult), Is.False, "the foreign pass assigned nothing");
                Assert.That(w.Clock.ScheduledCount, Is.EqualTo(pending), "and booked nothing");
            });

            // The band's own stream is untouched.
            w.AdvanceToDawn();
            Assert.That(w.Jobs.HasTask(adult), Is.True);
        }

        [Test]
        public void A_band_that_walked_off_the_map_cannot_be_given_sites()
        {
            // Tracked on the map, then moved off it by whoever owns its
            // position: the dawn pass refuses rather than record no sites.
            var w = new WorkWorld();
            var band = w.NewBand(WorkWorld.Camp, 0);
            w.Join(band, 30L);
            band.Position = new WorldPosition(-1, 0);

            Assert.That(() => w.AdvanceToDawn(), Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void A_band_that_walked_far_off_the_map_is_refused_not_left_siteless()
        {
            // Too far out for the scan to touch any cell the pathfinder could
            // refuse: without a check up front, the pass would record no
            // sites and the band would idle forever without a word.
            var w = new WorkWorld();
            var band = w.NewBand(WorkWorld.Camp, 0);
            w.Join(band, 30L);
            band.Position = new WorldPosition(-100, -100);

            Assert.That(() => w.AdvanceToDawn(), Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void A_band_that_walked_into_the_river_is_refused_at_dawn_not_left_idle()
        {
            // The same for a cell on the map that nobody can stand on: every
            // site search from it fails, and silence would look like a band
            // with nothing to do.
            var w = new WorkWorld();
            var band = w.NewBand(WorkWorld.Camp, 0);
            w.Join(band, 30L);
            band.Position = new WorldPosition(WorkWorld.RiverColumn, 3);

            Assert.That(() => w.AdvanceToDawn(), Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void A_site_is_looked_for_out_to_the_radius_and_no_further()
        {
            var grid = new TerrainGrid(40, 40, TerrainKind.Plains);
            var camp = new WorldPosition(20, 20);
            var atTheEdge = new WorldPosition(20 + Jobs.MaxSiteRadius, 20);
            var pastIt = new WorldPosition(20 - Jobs.MaxSiteRadius - 1, 20);
            grid.Set(atTheEdge, TerrainKind.Forest);
            grid.Set(pastIt, TerrainKind.Hills);
            var w = new WorkWorld(1UL, grid);
            var band = w.NewBand(camp, WorkWorld.PlentifulFood(1));
            w.Join(band, 30L);

            // Both cells known, so the search radius is the only thing that
            // can rule either out; the fog is the next fixture's subject.
            w.KnownMaps.Reveal(band.Id, camp, Jobs.MaxSiteRadius + 1);

            w.AdvanceToDawn();

            Assert.Multiple(() =>
            {
                Assert.That(w.Jobs.HasSite(band, JobKind.Woodcutter), Is.True);
                Assert.That(w.Jobs.SiteFor(band, JobKind.Woodcutter), Is.EqualTo(atTheEdge));
                Assert.That(w.Jobs.HasSite(band, JobKind.StoneGatherer), Is.False);
            });
        }

        [Test]
        public void A_completion_that_is_not_the_tasks_own_is_a_wiring_bug()
        {
            // The task names the completion it booked. One for the same
            // worker with another id - a duplicate, or one rebuilt from a
            // save that disagrees - must not finish the task early.
            var w = new WorkWorld();
            var band = w.NewBand(WorkWorld.Camp, 0);
            var worker = w.Join(band, 30L);
            w.AdvanceToDawn();
            var task = w.Jobs.TaskOf(worker);
            w.Clock.Schedule(
                w.Now.Plus(10L), Jobs.Phase, ScheduledEventKind.TaskCompleted, w.People.GetId(worker), EntityId.None);

            Assert.Multiple(() =>
            {
                Assert.That(() => w.Advance(10L), Throws.InvalidOperationException);
                Assert.That(w.Jobs.TaskOf(worker).Completion, Is.EqualTo(task.Completion), "the task is untouched");
                Assert.That(band.SharedSupplies.Flows(ResourceKind.Food).Gathered, Is.Zero, "nothing was delivered early");
            });
        }

        [Test]
        public void The_pick_counts_tasks_not_the_job_field()
        {
            // The record's Job is a mirror anyone can write through the bulk
            // span. Three people draw nine a day and have 87 in store - nine
            // days, one forage short of the target. A phantom Forager on a
            // child must not be counted as that forage, and an undefined
            // value must not break the pick.
            var w = new WorkWorld();
            var band = w.NewBand(WorkWorld.Camp, 87);
            var adult = w.Join(band, 30L);
            var child = w.Join(band, 8L);
            var other = w.Join(band, 9L);
            var records = w.People.RecordSpan();
            records[child.Index].Job = JobKind.Forager;
            records[other.Index].Job = (JobKind)200;

            w.AdvanceToDawn();

            Assert.That(w.People.GetJob(adult), Is.EqualTo(JobKind.Forager), "87 + nothing on its way = nine days");
        }

        [Test]
        public void A_completion_for_the_dead_is_a_wiring_bug_too()
        {
            // The cascade cancels a dead worker's completion, so one arriving
            // for an id the store no longer knows was booked by something else.
            var w = new WorkWorld();
            var band = w.NewBand(WorkWorld.Camp, 0);
            var worker = w.Join(band, 30L);
            var id = w.People.GetId(worker);
            w.Deaths.Die(worker, new Reasons(ReasonCode.Illness));
            w.Clock.Schedule(w.Now.Plus(10L), Jobs.Phase, ScheduledEventKind.TaskCompleted, id, EntityId.None);

            Assert.That(() => w.Advance(10L), Throws.InvalidOperationException);
        }

        [Test]
        public void The_same_seed_works_the_same_year()
        {
            var first = RunYear(3UL);
            var second = RunYear(3UL);

            Assert.That(first, Is.EqualTo(second));
        }

        [Test]
        public void A_band_with_a_day_of_food_feeds_itself_for_a_year()
        {
            var w = new WorkWorld(5UL, WorkWorld.DefaultMap());
            var band = w.NewBand(WorkWorld.Camp, 30 * Hunger.DailyRation);

            for (var i = 0; i < 30; i++)
            {
                // Half adults, the rest spread across the other stages.
                var age = i % 2 == 0 ? 20L + i : new[] { 1L, 6L, 13L, 60L }[i / 2 % 4];
                w.Join(band, age, i % 3 == 0 ? Sex.Female : Sex.Male);
            }

            w.Advance(SimulationTime.TicksPerYear - 1L);

            Assert.Multiple(() =>
            {
                Assert.That(w.Count(DomainEventKind.FamineStarted), Is.Zero, "nobody went unfed");
                Assert.That(band.SharedSupplies.Flows(ResourceKind.Food).Gathered, Is.GreaterThan(0L));
                Assert.That(band.SharedSupplies.Flows(ResourceKind.Wood).Gathered, Is.GreaterThan(0L), "the surplus went to wood");
                Assert.That(band.SharedSupplies.Available(ResourceKind.Wood), Is.LessThanOrEqualTo(Jobs.WoodCap + PrimitiveTier.GatherWood.Outputs[0].Quantity * 30), "and stopped near the cap");
                Assert.That(band.SharedSupplies.AuditBalances(), Is.True);
                Assert.That(w.Hunger.IsInFamine(band), Is.False);
            });
        }

        private static List<long> RunYear(ulong seed)
        {
            var w = new WorkWorld(seed, WorkWorld.DefaultMap());
            var band = w.NewBand(WorkWorld.Camp, 10 * Hunger.DailyRation);
            w.JoinAdults(band, 10);
            w.Join(band, 60L);
            w.Join(band, 8L);
            w.Advance(SimulationTime.TicksPerYear - 1L);

            var ledger = band.SharedSupplies;
            return new List<long>
            {
                ledger.Flows(ResourceKind.Food).Gathered,
                ledger.Flows(ResourceKind.Food).Consumed,
                ledger.Flows(ResourceKind.Wood).Gathered,
                ledger.Flows(ResourceKind.Stone).Gathered,
                w.Clock.ScheduledCount,
                w.People.Count,
            };
        }

        // ---------------------------------------------------------------
        // The band's counts (#83). They used to be rebuilt by walking every
        // member on every pick; now the dawn pass builds them and the three
        // places a task slot changes keep the on-duty half exact. Each test
        // below sits the wood stock one gather either side of the cap, so a
        // count that is off by one task flips the job that gets picked.
        // ---------------------------------------------------------------

        [Test]
        public void A_starting_task_counts_against_the_cap_at_once()
        {
            // 198 wood and a cap of 200: the first cutter is wanted, and the
            // two his trip will bring home fill the store, so the second is
            // not. Were a started task not counted until it delivered, both
            // would cut and the band would overshoot.
            var w = new WorkWorld();
            var band = w.NewBand(WorkWorld.Camp, WorkWorld.PlentifulFood(2));
            band.SharedSupplies.Gather(ResourceKind.Wood, 198);
            var adults = w.JoinAdults(band, 2);

            w.AdvanceToDawn();

            Assert.Multiple(() =>
            {
                Assert.That(w.People.GetJob(adults[0]), Is.EqualTo(JobKind.Woodcutter), "198 + nothing on its way = under the cap");
                Assert.That(w.People.GetJob(adults[1]), Is.EqualTo(JobKind.StoneGatherer), "198 + his two = the cap, so wood is done");
            });
        }

        [Test]
        public void A_delivering_task_leaves_the_tally_as_it_joins_the_store()
        {
            // 196 wood, one cutter. He delivers two and picks again: 198 in
            // store with nothing on its way is the same expectation as 196
            // with his trip out, and still under the cap, so he cuts again.
            // A task counted both in the store and on the road would read 200
            // and send him to the hills.
            var w = new WorkWorld();
            var band = w.NewBand(WorkWorld.Camp, WorkWorld.PlentifulFood(1));
            band.SharedSupplies.Gather(ResourceKind.Wood, 196);
            var cutter = w.Join(band, 30L);

            w.AdvanceToDawn();
            Assert.That(w.People.GetJob(cutter), Is.EqualTo(JobKind.Woodcutter), "196 is under the cap");

            w.AdvanceTo(w.Jobs.TaskOf(cutter).End);

            Assert.Multiple(() =>
            {
                Assert.That(band.SharedSupplies.Available(ResourceKind.Wood), Is.EqualTo(198L));
                Assert.That(w.People.GetJob(cutter), Is.EqualTo(JobKind.Woodcutter), "198 in store, nothing on the road");
            });
        }

        [Test]
        public void A_death_takes_its_task_off_the_tally_as_well()
        {
            // 198 wood: the cutter's two fill the store, so the stone
            // gatherer goes to the hills. The cutter then dies on the road
            // and his two never arrive, so by the time the gatherer is home
            // wood is wanted again. A dead worker still counted as on his way
            // would keep the store looking full for the rest of the day.
            var w = new WorkWorld();
            var band = w.NewBand(WorkWorld.Camp, WorkWorld.PlentifulFood(2));
            band.SharedSupplies.Gather(ResourceKind.Wood, 198);
            var cutter = w.Join(band, 30L);
            var gatherer = w.Join(band, 31L);

            w.AdvanceToDawn();
            Assert.Multiple(() =>
            {
                Assert.That(w.People.GetJob(cutter), Is.EqualTo(JobKind.Woodcutter));
                Assert.That(w.People.GetJob(gatherer), Is.EqualTo(JobKind.StoneGatherer), "the cutter's two fill the store");
            });

            var stoneTrip = w.Jobs.TaskOf(gatherer);
            w.Advance(SimulationTime.TicksPerHour);
            w.Deaths.Die(cutter, new Reasons(ReasonCode.Illness));
            w.AdvanceTo(stoneTrip.End);

            Assert.Multiple(() =>
            {
                Assert.That(band.SharedSupplies.Available(ResourceKind.Wood), Is.EqualTo(198L), "the dead deliver nothing");
                Assert.That(w.People.GetJob(gatherer), Is.EqualTo(JobKind.Woodcutter), "198 and nobody cutting is under the cap again");
            });
        }

        [Test]
        public void The_bands_appetite_is_the_one_counted_at_dawn()
        {
            // Six adults draw 18 a day, so 120 food is under seven days and
            // foraging is wanted. Three die mid-morning; the survivors draw
            // nine a day, which would make the same store a fortnight and
            // send them for wood instead. Jobs hears about a death but never
            // about a birth or a join, so a count kept through the day would
            // only ever shrink - the band works to the appetite it counted at
            // dawn, and finds out about the loss at the next one.
            var w = new WorkWorld();
            var band = w.NewBand(WorkWorld.Camp, 120);
            var adults = w.JoinAdults(band, 6);

            w.AdvanceToDawn();
            Assert.That(w.People.GetJob(adults[0]), Is.EqualTo(JobKind.Forager), "120 against 18 a day is under the ten-day target");

            var trip = w.Jobs.TaskOf(adults[0]);
            w.Advance(SimulationTime.TicksPerHour);

            for (var i = 3; i < 6; i++)
            {
                w.Deaths.Die(adults[i], new Reasons(ReasonCode.Illness));
            }

            w.AdvanceTo(trip.End);

            Assert.Multiple(() =>
            {
                Assert.That(band.SharedSupplies.Available(ResourceKind.Food), Is.EqualTo(129L), "three of the six delivered");
                Assert.That(w.People.GetJob(adults[0]), Is.EqualTo(JobKind.Forager));
                Assert.That(w.People.GetJob(adults[1]), Is.EqualTo(JobKind.Forager));
                Assert.That(w.People.GetJob(adults[2]), Is.EqualTo(JobKind.Forager), "still feeding the six counted at dawn");
            });
        }

        // ---------------------------------------------------------------
        // Bounded map knowledge at the work site (#84). Section 12's rule is
        // that every decision picking a place picks among cells the holder
        // knows; work sites were the one such decision still reading the
        // whole world. The band's own trips are what move the frontier.
        // ---------------------------------------------------------------

        [Test]
        public void A_site_the_band_has_never_seen_is_not_worked()
        {
            // The only forest is ten cells out: inside the search radius,
            // outside anything the band has seen. It is not a site until the
            // band has seen it, and then it is.
            var w = new WorkWorld(1UL, FogMap());
            var band = w.NewBand(FogCamp, WorkWorld.PlentifulFood(1));
            w.Join(band, 30L);

            Assert.That(w.Jobs.HasSite(band, JobKind.Woodcutter), Is.False, "not tracked yet, and nothing seen that far");

            w.AdvanceToDawn();

            Assert.Multiple(() =>
            {
                Assert.That(w.KnownMaps.Knows(band.Id, FogForest), Is.False, "sixteen cells out, and it sees six");
                Assert.That(w.Jobs.HasSite(band, JobKind.Woodcutter), Is.False);
                Assert.That(w.Jobs.HasSite(band, JobKind.Forager), Is.True, "plains underfoot are always known");
            });

            w.KnownMaps.Reveal(band.Id, FogForest, 0);
            w.Jobs.RefreshSites(band);

            Assert.Multiple(() =>
            {
                Assert.That(w.Jobs.HasSite(band, JobKind.Woodcutter), Is.True);
                Assert.That(w.Jobs.SiteFor(band, JobKind.Woodcutter), Is.EqualTo(FogForest));
            });
        }

        [Test]
        public void The_walking_may_cross_unknown_ground_the_destination_may_not()
        {
            // One seen cell at the edge of the radius, with a band of fog
            // between it and the camp. The band works it: section 12
            // restricts where a search may land, not what it may walk over -
            // a searcher that could not cross unseen ground could never reach
            // anywhere it had only glimpsed the far side of.
            var w = new WorkWorld(1UL, FogMap());
            var band = w.NewBand(FogCamp, WorkWorld.PlentifulFood(1));
            var cutter = w.Join(band, 30L);
            w.KnownMaps.Reveal(band.Id, FogForest, 0);
            var between = new WorldPosition(FogCamp.X + Jobs.RevealRadius + 2, FogCamp.Y);

            Assert.That(w.KnownMaps.Knows(band.Id, between), Is.False, "fog between the camp and the far cell");

            w.AdvanceToDawn();

            Assert.Multiple(() =>
            {
                Assert.That(w.People.GetJob(cutter), Is.EqualTo(JobKind.Woodcutter));
                Assert.That(w.Jobs.TaskOf(cutter).Destination, Is.EqualTo(FogForest));
            });
        }

        [Test]
        public void A_work_trip_reveals_the_ground_it_covers()
        {
            // Section 12 counts "foragers and hunters working out from a
            // settlement" among the things that reveal, and nothing in Jobs
            // used to reveal anything. A trip to the far forest widens the
            // band's map around the whole route, so the frontier creeps
            // outward on the strength of the work itself.
            var w = new WorkWorld(1UL, FogMap());
            var band = w.NewBand(FogCamp, WorkWorld.PlentifulFood(1));
            var cutter = w.Join(band, 30L);
            w.KnownMaps.Reveal(band.Id, FogForest, 0);

            // Halfway along, in the fog between the camp's reveal square and
            // the site's, and off the line the route walks: within reach of
            // the road and of neither end of it, so only a reveal that
            // follows the route can bring it into view.
            var besideTheRoad = new WorldPosition(
                FogCamp.X + (Jobs.MaxSiteRadius / 2), FogCamp.Y + Jobs.RevealRadius);
            Assert.That(w.KnownMaps.Knows(band.Id, besideTheRoad), Is.False);

            w.AdvanceToDawn();

            Assert.Multiple(() =>
            {
                Assert.That(w.People.GetJob(cutter), Is.EqualTo(JobKind.Woodcutter), "the trip is what does the revealing");
                Assert.That(w.KnownMaps.Knows(band.Id, besideTheRoad), Is.True);
                Assert.That(
                    w.KnownMaps.Knows(band.Id, new WorldPosition(FogCamp.X + (Jobs.MaxSiteRadius / 2), FogCamp.Y)),
                    Is.True,
                    "and the ground it walked over itself");
            });
        }

        [Test]
        public void A_band_with_no_map_is_refused_its_work_day()
        {
            // Section 12's map belongs to the community. Jobs only reads it,
            // so a band nobody gave one to is a wiring gap, named at the dawn
            // that needs it rather than left to find nowhere to work.
            var w = new WorkWorld();
            var band = w.NewUnmappedBand(WorkWorld.Camp, 30);
            w.Join(band, 30L);

            Assert.That(() => w.AdvanceToDawn(), Throws.InvalidOperationException);
        }

        [Test]
        public void A_community_that_has_seen_nothing_has_nowhere_to_work()
        {
            // An empty map is a real state, not a broken one - a settlement
            // founded by a band that never wandered starts with exactly that.
            // It is not omniscience: there is nowhere to work at all, not even
            // the plains the band is standing on.
            var w = new WorkWorld();
            var band = w.NewUnmappedBand(WorkWorld.Camp, WorkWorld.PlentifulFood(1));
            var adult = w.Join(band, 30L);
            w.KnownMaps.Track(band.Id);

            w.AdvanceToDawn();

            Assert.Multiple(() =>
            {
                Assert.That(w.Jobs.HasSite(band, JobKind.Forager), Is.False, "not even underfoot");
                Assert.That(w.Jobs.HasSite(band, JobKind.Woodcutter), Is.False);
                Assert.That(w.Jobs.HasSite(band, JobKind.StoneGatherer), Is.False);
                Assert.That(w.Jobs.HasTask(adult), Is.False);
            });
        }

        [Test]
        public void A_settlements_map_keeps_growing_on_the_strength_of_its_work()
        {
            // Section 12's constraint on this rule: whatever restricts a work
            // site must not freeze a settled community's map, or the
            // exploration motive stops existing the moment bands stop
            // wandering. The settlement takes over what the band knew, and its
            // own work trips are what widen it from there.
            var w = new WorkWorld(1UL, FogMap());
            var band = w.NewWanderingBand(FogCamp, WorkWorld.PlentifulFood(4));
            w.JoinAdults(band, 4);
            w.KnownMaps.Reveal(band.Id, FogForest, 0);
            var settlement = w.Founding.Found(band, new Reasons(ReasonCode.FoodShortage));
            var before = KnownCells(w, settlement.Id);

            w.AdvanceToDawn();

            Assert.That(KnownCells(w, settlement.Id), Is.GreaterThan(before), "the work widened the map");
        }

        [Test]
        public void A_settlement_with_nothing_known_worth_walking_to_stops_growing()
        {
            // The limit of the rule above, recorded rather than left to be
            // discovered: reveal rides on trips, so a community whose only
            // work is underfoot makes no trips and learns nothing. Foraging on
            // plains is worked where the band stands, so a settlement that
            // knows no forest and no hills sees exactly what it saw on the day
            // it was founded, for ever.
            //
            // Section 12 answers this with a deliberate Scouting purpose
            // (#85), which is why that issue exists; founding softens it
            // meanwhile, since a band only settles where it already knows both
            // food and wood, so a real settlement starts with somewhere to
            // walk to. Expect this test to change when #85 lands.
            var w = new WorkWorld(1UL, FogMap());
            var band = w.NewWanderingBand(FogCamp, WorkWorld.PlentifulFood(4));
            w.JoinAdults(band, 4);
            var settlement = w.Founding.Found(band, new Reasons(ReasonCode.FoodShortage));
            var before = KnownCells(w, settlement.Id);

            w.AdvanceToDawn();
            w.AdvanceToDawn();

            Assert.Multiple(() =>
            {
                Assert.That(w.Jobs.HasSite(settlement, JobKind.Forager), Is.True, "the plains it stands on");
                Assert.That(w.Jobs.HasSite(settlement, JobKind.Woodcutter), Is.False, "the only forest is unseen");
                Assert.That(KnownCells(w, settlement.Id), Is.EqualTo(before), "so nothing new is ever seen");
            });
        }

        private static int KnownCells(WorkWorld w, EntityId holder)
        {
            var known = w.KnownMaps.For(holder);
            var count = 0;

            for (var i = 0; i < known.Length; i++)
            {
                if (known[i])
                {
                    count++;
                }
            }

            return count;
        }

        // Forty by forty of plains with one forest cell at the far edge of
        // the search radius: reachable, well outside what standing still
        // reveals, and far enough that the camp's reveal square and the
        // site's leave a band of fog in between for the road to lift.
        private static readonly WorldPosition FogCamp = new WorldPosition(20, 20);
        private static readonly WorldPosition FogForest = new WorldPosition(20 + Jobs.MaxSiteRadius, 20);

        private static TerrainGrid FogMap()
        {
            var grid = new TerrainGrid(40, 40, TerrainKind.Plains);
            grid.Set(FogForest, TerrainKind.Forest);
            return grid;
        }
    }
}
