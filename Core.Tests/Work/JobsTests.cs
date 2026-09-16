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
                Assert.That(() => new Jobs(null!, w.People, pathfinder), Throws.ArgumentNullException);
                Assert.That(() => new Jobs(w.Clock, null!, pathfinder), Throws.ArgumentNullException);
                Assert.That(() => new Jobs(w.Clock, w.People, null!), Throws.ArgumentNullException);
            });
        }

        [Test]
        public void Tracking_refuses_null_and_the_same_band_twice()
        {
            var w = new WorkWorld();
            var band = w.NewBand(WorkWorld.Camp, 0);

            Assert.Multiple(() =>
            {
                Assert.That(() => w.Jobs.Track(null!), Throws.ArgumentNullException);
                Assert.That(() => w.Jobs.Track(band), Throws.InvalidOperationException);
                Assert.That(w.Jobs.TrackedCount, Is.EqualTo(1));
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
                Assert.That(w.Clock.ScheduledCount, Is.EqualTo(pending - 1), "the completion is gone");
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
        public void A_pass_run_while_workers_are_out_leaves_them_out()
        {
            // No task outlives dusk, so no dawn finds one; but the handler is
            // callable at any hour, and a pass then must not book a second
            // task over a worker's first.
            var w = new WorkWorld();
            var band = w.NewBand(WorkWorld.Camp, 0);
            var adult = w.Join(band, 30L);
            w.AdvanceToDawn();
            var task = w.Jobs.TaskOf(adult);
            w.Advance(SimulationTime.TicksPerHour);
            var pending = w.Clock.ScheduledCount;

            w.Jobs.Handle(
                new ScheduledEvent(new EventId(999UL), w.Now, Jobs.Phase, ScheduledEventKind.WorkDayDue, band.Id, EntityId.None),
                w.Clock);

            Assert.Multiple(() =>
            {
                Assert.That(w.Jobs.TaskOf(adult).Completion, Is.EqualTo(task.Completion), "the same task");
                Assert.That(w.Clock.ScheduledCount, Is.EqualTo(pending + 1), "only the pass's own successor was booked");
            });
        }

        [Test]
        public void A_pass_run_off_hour_books_its_successor_at_dawn_not_a_day_later()
        {
            // Only work-day events on this queue: a band tracked at 07:00
            // has its first pass booked for 06:00 tomorrow. A pass run by
            // hand at 07:00 must book tomorrow's dawn as well, not 07:00
            // tomorrow - or the daily pass drifts to whatever hour it ran.
            var w = new WorkWorld();
            var band = new MobileGroup(
                w.Demographics.Base.Ids.Next(EntityKind.MobileGroup), MobileGroupPurpose.NomadicBand, WorkWorld.Camp);
            w.Clock.AdvanceTo(SimulationTime.FromHours(7L), w.Router);
            w.Jobs.Track(band);
            w.Jobs.Handle(
                new ScheduledEvent(new EventId(999UL), w.Now, Jobs.Phase, ScheduledEventKind.WorkDayDue, band.Id, EntityId.None),
                w.Clock);

            var atDawn = w.Clock.AdvanceTo(SimulationTime.FromHours(6L).Plus(SimulationTime.TicksPerDay), w.Router);
            var byMorning = w.Clock.AdvanceTo(SimulationTime.FromHours(7L).Plus(SimulationTime.TicksPerDay), w.Router);

            Assert.Multiple(() =>
            {
                Assert.That(atDawn, Is.EqualTo(2), "both passes land on dawn");
                Assert.That(byMorning, Is.Zero, "and nothing at seven");
            });
        }

        [Test]
        public void A_band_off_the_map_cannot_be_given_sites()
        {
            var w = new WorkWorld();
            var band = w.NewBand(new WorldPosition(-1, 0), 0);
            w.Join(band, 30L);

            Assert.That(() => w.AdvanceToDawn(), Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void A_band_far_off_the_map_is_refused_not_left_siteless()
        {
            // Too far out for the scan to touch any cell the pathfinder could
            // refuse: without a check up front, the pass would record no
            // sites and the band would idle forever without a word.
            var w = new WorkWorld();
            var band = w.NewBand(new WorldPosition(-100, -100), 0);
            w.Join(band, 30L);

            Assert.That(() => w.AdvanceToDawn(), Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void A_band_standing_where_it_cannot_walk_has_no_sites()
        {
            var w = new WorkWorld();
            var band = w.NewBand(new WorldPosition(WorkWorld.RiverColumn, 3), 0);
            var adult = w.Join(band, 30L);

            w.AdvanceToDawn();

            Assert.Multiple(() =>
            {
                Assert.That(w.Jobs.HasSite(band, JobKind.Forager), Is.False);
                Assert.That(w.Jobs.HasSite(band, JobKind.Woodcutter), Is.False);
                Assert.That(w.Jobs.HasSite(band, JobKind.StoneGatherer), Is.False);
                Assert.That(w.People.GetJob(adult), Is.EqualTo(JobKind.None));
            });
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
    }
}
