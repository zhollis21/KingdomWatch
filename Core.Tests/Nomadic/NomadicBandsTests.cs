using System;
using KingdomWatch.Core.Clock;
using KingdomWatch.Core.Data;
using KingdomWatch.Core.Events;
using KingdomWatch.Core.Nomadic;
using KingdomWatch.Core.Tests.Work;
using KingdomWatch.Core.Traversal;
using KingdomWatch.Core.Work;
using NUnit.Framework;

namespace KingdomWatch.Core.Tests.Nomadic
{
    [TestFixture]
    public sealed class NomadicBandsTests
    {
        private static readonly long Day = SimulationTime.TicksPerDay;

        [Test]
        public void Construction_refuses_a_missing_collaborator()
        {
            var w = new WorkWorld();
            var bus = w.Demographics.Bus;
            var path = w.Demographics.Base.Pathfinder;

            Assert.Multiple(() =>
            {
                Assert.That(() => new NomadicBands(null!, w.People, path, w.Founding, w.Demographics.Rng), Throws.ArgumentNullException);
                Assert.That(() => new NomadicBands(bus, null!, path, w.Founding, w.Demographics.Rng), Throws.ArgumentNullException);
                Assert.That(() => new NomadicBands(bus, w.People, null!, w.Founding, w.Demographics.Rng), Throws.ArgumentNullException);
                Assert.That(() => new NomadicBands(bus, w.People, path, null!, w.Demographics.Rng), Throws.ArgumentNullException);
                Assert.That(() => new NomadicBands(bus, w.People, path, w.Founding, null!), Throws.ArgumentNullException);
            });
        }

        [Test]
        public void Tracking_pitches_the_first_camp_and_books_the_first_council()
        {
            var w = new WorkWorld();
            var band = w.NewBand(WorkWorld.Camp, 0);
            band.SharedSupplies.Gather(ResourceKind.Wood, 12);
            var pending = w.Clock.ScheduledCount;

            w.Nomads.Track(band);

            Assert.Multiple(() =>
            {
                Assert.That(w.Nomads.TrackedCount, Is.EqualTo(1));
                Assert.That(w.Count(DomainEventKind.CampPitched), Is.EqualTo(1), "the first camp");
                Assert.That(w.Demographics.Journal[w.Demographics.Journal.Count - 1].PrimaryEntity, Is.EqualTo(band.Id));
                Assert.That(band.SharedSupplies.Available(ResourceKind.Wood), Is.EqualTo(12 - NomadicBands.CampWood), "wood into the camp");
                Assert.That(band.SharedSupplies.Flows(ResourceKind.Wood).Embodied, Is.EqualTo(NomadicBands.CampWood));
                Assert.That(w.Nomads.DaysAtCamp(band), Is.Zero);
                Assert.That(w.Nomads.PressureOf(band), Is.Zero);
                Assert.That(w.Clock.ScheduledCount, Is.EqualTo(pending + 1), "one council");
                Assert.That(w.Clock.TryPeekNext(out var next), Is.True);
                Assert.That(next.Kind, Is.EqualTo(ScheduledEventKind.CouncilDue));
                Assert.That(next.Time.TickOfDay, Is.EqualTo(NomadicBands.FirstLight));
                Assert.That(next.Time, Is.GreaterThan(w.Now));
                Assert.That(next.PrimaryEntity, Is.EqualTo(band.Id));
                Assert.That(next.Phase, Is.EqualTo(NomadicBands.Phase));
            });
        }

        [Test]
        public void A_camp_with_no_wood_is_still_a_camp()
        {
            var w = new WorkWorld();
            var band = w.NewBand(WorkWorld.Camp, 0);

            Assert.That(() => w.Nomads.Track(band), Throws.Nothing);
            Assert.That(band.SharedSupplies.Flows(ResourceKind.Wood).Embodied, Is.Zero);
            Assert.That(w.Count(DomainEventKind.CampPitched), Is.EqualTo(1));
        }

        [Test]
        public void Tracking_refuses_null_a_second_time_another_purpose_off_the_map_and_on_the_road()
        {
            var w = new WorkWorld();
            var band = w.NewWanderingBand(WorkWorld.Camp, 0);
            var ids = w.Demographics.Base.Ids;
            var army = new MobileGroup(ids.Next(EntityKind.MobileGroup), MobileGroupPurpose.Army, WorkWorld.Camp);
            var lost = new MobileGroup(ids.Next(EntityKind.MobileGroup), MobileGroupPurpose.NomadicBand, new WorldPosition(-1, 0));
            var travelling = new MobileGroup(ids.Next(EntityKind.MobileGroup), MobileGroupPurpose.NomadicBand, WorkWorld.Camp)
            {
                Destination = new WorldPosition(3, 3),
            };
            var pending = w.Clock.ScheduledCount;

            Assert.Multiple(() =>
            {
                Assert.That(() => w.Nomads.Track(null!), Throws.ArgumentNullException);
                Assert.That(() => w.Nomads.Track(band), Throws.InvalidOperationException);
                Assert.That(() => w.Nomads.Track(army), Throws.ArgumentException);
                Assert.That(() => w.Nomads.Track(lost), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => w.Nomads.Track(travelling), Throws.InvalidOperationException, "no arrival to book for a move nobody here decided");
                Assert.That(w.Clock.ScheduledCount, Is.EqualTo(pending), "nothing refused booked anything");
                Assert.That(w.Nomads.TrackedCount, Is.EqualTo(1));
            });
        }

        [Test]
        public void Untracking_cancels_the_council_and_refuses_the_untracked()
        {
            var w = new WorkWorld();
            var band = w.NewWanderingBand(WorkWorld.Camp, 0);
            var pending = w.Clock.ScheduledCount;

            w.Nomads.Untrack(band);

            Assert.Multiple(() =>
            {
                Assert.That(w.Nomads.TrackedCount, Is.Zero);
                Assert.That(w.Clock.ScheduledCount, Is.EqualTo(pending - 1));
                Assert.That(() => w.Nomads.Untrack(band), Throws.InvalidOperationException);
                Assert.That(() => w.Nomads.Untrack(null!), Throws.ArgumentNullException);
                Assert.That(() => w.Nomads.PressureOf(band), Throws.InvalidOperationException);
                Assert.That(() => w.Nomads.DaysAtCamp(band), Throws.InvalidOperationException);
                Assert.That(() => w.Nomads.PressureOf(null!), Throws.ArgumentNullException);
                Assert.That(() => w.Nomads.DaysAtCamp(null!), Throws.ArgumentNullException);
                Assert.That(() => w.Nomads.Track(band), Throws.Nothing, "and can be tracked afresh");
            });
        }

        [Test]
        public void Untracking_a_band_on_the_road_cancels_the_arrival_and_the_move()
        {
            var w = new WorkWorld();
            var band = w.NewWanderingBand(new WorldPosition(14, 8), WorkWorld.PlentifulFood(1));
            w.JoinAdults(band, 1);
            AdvanceToCouncil(w, NomadicBands.CampDays);
            Assert.That(band.Destination, Is.Not.Null);
            var pending = w.Clock.ScheduledCount;

            w.Nomads.Untrack(band);

            Assert.Multiple(() =>
            {
                Assert.That(band.Destination, Is.Null, "the move is abandoned");
                Assert.That(band.Position, Is.EqualTo(new WorldPosition(14, 8)), "where it was");
                Assert.That(w.Clock.ScheduledCount, Is.EqualTo(pending - 2), "the arrival and the next council");
                Assert.That(() => w.AdvanceTo(w.Today(Jobs.Dusk)), Throws.Nothing);
                Assert.That(() => w.Nomads.Track(band), Throws.Nothing, "at rest, so trackable again");
            });
        }

        [Test]
        public void The_dead_still_listed_do_not_walk()
        {
            // Deaths was never told about this band, so a dead member stays
            // listed; the arrival moves the living and leaves the dead where
            // they fell, as the council counts only the living.
            var w = new WorkWorld();
            var start = new WorldPosition(14, 8);
            var band = new MobileGroup(
                w.Demographics.Base.Ids.Next(EntityKind.MobileGroup), MobileGroupPurpose.NomadicBand, start);
            w.Nomads.Track(band);
            var adults = w.JoinAdults(band, 2);
            w.Deaths.Die(adults[1], Reasons.None);
            AdvanceToCouncil(w, NomadicBands.CampDays);
            Assert.That(band.Destination, Is.Not.Null);

            Assert.That(() => w.AdvanceTo(w.Today(Jobs.Dusk)), Throws.Nothing);

            Assert.Multiple(() =>
            {
                Assert.That(band.Position, Is.Not.EqualTo(start));
                Assert.That(w.People.GetPosition(adults[0]), Is.EqualTo(band.Position));
                Assert.That(band.Members, Has.Count.EqualTo(2), "the dead handle is still listed");
            });
        }

        [Test]
        public void A_refused_founding_leaves_the_band_wandering()
        {
            // Founding preflights every tracker and refuses before touching
            // anything; a band the world forgot to put on one of them is a
            // wiring bug, and the council must not have dropped the band
            // before finding out.
            var w = new WorkWorld();
            var band = new MobileGroup(
                w.Demographics.Base.Ids.Next(EntityKind.MobileGroup), MobileGroupPurpose.NomadicBand, WorkWorld.Camp);
            w.Deaths.Track(band);
            w.Hunger.Track(band);
            w.Jobs.Track(band);
            w.Nomads.Track(band);
            w.JoinAdults(band, 60);
            band.SharedSupplies.Gather(ResourceKind.Food, WorkWorld.PlentifulFood(60) * 4);
            var councils = (NomadicBands.SettlingPressure + 59) / 60;

            Assert.That(() => w.AdvanceTo(w.Now.Plus((councils + 60) * Day)), Throws.InvalidOperationException, "Fertility and Matchmaking never had it");

            Assert.Multiple(() =>
            {
                Assert.That(w.Nomads.TrackedCount, Is.EqualTo(1), "still wandering");
                Assert.That(w.Founding.All, Is.Empty);
                Assert.That(band.Members, Has.Count.GreaterThan(0), "nobody moved");
                Assert.That(w.Deaths.TrackedCount, Is.EqualTo(1));
                Assert.That(w.Hunger.TrackedCount, Is.EqualTo(1));
                Assert.That(w.Jobs.TrackedCount, Is.EqualTo(1));
            });
        }

        [Test]
        public void A_council_never_sits_on_the_road()
        {
            // An arrival lands the same day it departs, so a council finds
            // the band at rest by construction. A destination set by
            // something else - the wiring bug this catches - does not.
            var w = new WorkWorld();
            var band = w.NewWanderingBand(WorkWorld.Camp, 0);
            band.Destination = new WorldPosition(3, 3);

            Assert.That(() => w.AdvanceToFirstLight(), Throws.InvalidOperationException);
        }

        [Test]
        public void On_the_last_day_of_the_world_the_band_does_not_set_out()
        {
            // Twenty days at camp on the world's last day: the band would
            // move, but there is no dusk to arrive by, so it stays.
            var w = new WorkWorld();
            var band = new MobileGroup(
                w.Demographics.Base.Ids.Next(EntityKind.MobileGroup), MobileGroupPurpose.NomadicBand, WorkWorld.Camp);
            var end = new SimulationTime(long.MaxValue);
            var lastCouncil = new SimulationTime(end.Ticks - end.TickOfDay + NomadicBands.FirstLight);
            w.Clock.AdvanceTo(lastCouncil.Plus(-NomadicBands.CampDays * Day), w.Router);
            w.Nomads.Track(band);

            Assert.That(() => w.AdvanceTo(end), Throws.Nothing);

            Assert.Multiple(() =>
            {
                Assert.That(w.Nomads.DaysAtCamp(band), Is.EqualTo(NomadicBands.CampDays), "wanted to move");
                Assert.That(band.Destination, Is.Null, "stayed");
                Assert.That(band.Position, Is.EqualTo(WorkWorld.Camp));
                Assert.That(w.Clock.ScheduledCount, Is.Zero);
            });
        }

        [Test]
        public void A_camp_too_far_to_reach_by_dusk_is_not_a_candidate()
        {
            // Forest everywhere, a river wall across the map with one gap at
            // the far end, hills just south of the wall. The hills side
            // scores higher, and is four cells away as the crow flies - but
            // the route round the wall is over 250 forest cells, more than
            // a day's walk, so the band stays north.
            const int Width = 128;
            const int WallRow = 8;
            var grid = new TerrainGrid(Width, 16, TerrainKind.Forest);

            for (var x = 0; x < Width - 1; x++)
            {
                grid.Set(new WorldPosition(x, WallRow), TerrainKind.SmallRiver);
            }

            for (var x = 0; x < Width; x++)
            {
                grid.Set(new WorldPosition(x, WallRow + 3), TerrainKind.Hills);
            }

            var w = new WorkWorld(1UL, grid);
            var start = new WorldPosition(2, 6);
            var south = new WorldPosition(2, 10);
            Assert.That(w.Nomads.LandScore(south), Is.EqualTo(3), "hills in reach south of the wall");
            Assert.That(w.Nomads.LandScore(start), Is.EqualTo(2), "forest only north of it");
            var band = w.NewWanderingBand(start, WorkWorld.PlentifulFood(1));
            w.JoinAdults(band, 1);

            AdvanceToCouncil(w, NomadicBands.CampDays);

            Assert.That(band.Destination, Is.Not.Null);
            Assert.That(band.Destination!.Value.Y, Is.LessThan(WallRow), "stayed north of the wall");
        }

        [Test]
        public void The_council_sits_at_first_light_before_the_work_pass_and_rebooks_daily()
        {
            var w = new WorkWorld();
            var band = w.NewWanderingBand(WorkWorld.Camp, 0);
            w.JoinAdults(band, 3);

            w.AdvanceToFirstLight();

            Assert.Multiple(() =>
            {
                Assert.That(w.Now.TickOfDay, Is.EqualTo(NomadicBands.FirstLight));
                Assert.That(w.Nomads.DaysAtCamp(band), Is.EqualTo(1));
                Assert.That(w.Nomads.PressureOf(band), Is.EqualTo(3L), "three living members, one day");
                Assert.That(w.Jobs.HasTask(band.Members[0]), Is.False, "the work pass has not run yet");
            });

            w.AdvanceToFirstLight();

            Assert.That(w.Nomads.DaysAtCamp(band), Is.EqualTo(2));
            Assert.That(w.Nomads.PressureOf(band), Is.EqualTo(6L));
        }

        [Test]
        public void Pressure_counts_the_living_only()
        {
            // Membership lags death until the cascade strikes the dead from
            // the band - here for good, since Deaths was never told about
            // this band - and the dead add no pressure.
            var w = new WorkWorld();
            var band = new MobileGroup(
                w.Demographics.Base.Ids.Next(EntityKind.MobileGroup), MobileGroupPurpose.NomadicBand, WorkWorld.Camp);
            w.Nomads.Track(band);
            var adults = w.JoinAdults(band, 3);
            w.Deaths.Die(adults[1], Reasons.None);
            Assert.That(band.Members, Has.Count.EqualTo(3), "still listed");

            w.AdvanceToFirstLight();

            Assert.That(w.Nomads.PressureOf(band), Is.EqualTo(2L));
        }

        [Test]
        public void Land_is_scored_by_the_jobs_that_would_find_a_site()
        {
            var w = new WorkWorld();

            Assert.Multiple(() =>
            {
                Assert.That(w.Nomads.LandScore(WorkWorld.Camp), Is.EqualTo(3), "plains, forest and hills all within reach");
                Assert.That(w.Nomads.CanSettleAt(WorkWorld.Camp), Is.True);
                Assert.That(w.Nomads.LandScore(new WorldPosition(14, 0)), Is.EqualTo(1), "across the river: plains only");
                Assert.That(w.Nomads.CanSettleAt(new WorldPosition(14, 0)), Is.False, "no wood");
                Assert.That(() => w.Nomads.LandScore(new WorldPosition(99, 99)), Throws.TypeOf<ArgumentOutOfRangeException>());
            });
        }

        [Test]
        public void The_band_settles_once_pressure_is_reached_at_land_that_passes()
        {
            var w = new WorkWorld();
            // Sixty people: the largest starting band, and the one section 15
            // expects to settle first. Nobody has to work - the food lasts.
            var band = w.NewWanderingBand(WorkWorld.Camp, WorkWorld.PlentifulFood(60) * 4);
            var adults = w.JoinAdults(band, 60);
            var councilsToSettle = (NomadicBands.SettlingPressure + 59) / 60;

            // Well past, since every death along the way slows the pressure.
            w.AdvanceTo(w.Now.Plus((councilsToSettle + 60) * Day));

            Assert.Multiple(() =>
            {
                Assert.That(w.Count(DomainEventKind.SettlementFounded), Is.EqualTo(1));
                Assert.That(w.Nomads.TrackedCount, Is.Zero, "the band no longer wanders");
                Assert.That(w.Founding.All, Has.Count.EqualTo(1));
                Assert.That(band.Members, Is.Empty, "everyone moved to the settlement");
                Assert.That(w.Founding.All[0].Members, Has.Count.EqualTo(w.People.Count), "everyone living");
                Assert.That(w.Founding.All[0].Position, Is.EqualTo(band.Position));

                var founded = w.Demographics.Published(DomainEventKind.SettlementFounded)[0];
                Assert.That(founded.SecondaryEntity, Is.EqualTo(band.Id));
                Assert.That(founded.Reasons.Contains(ReasonCode.PopulationPressure), Is.True);
                Assert.That(founded.Reasons.Contains(ReasonCode.LandSuitable), Is.True);
                Assert.That(founded.Time.TickOfDay, Is.EqualTo(NomadicBands.FirstLight), "settled at the council");
            });

            // Life goes on under the settlement's name.
            w.AdvanceToDawn();
            Assert.That(w.Jobs.HasTask(adults[0]), Is.False, "plentiful food, nothing needed");
            Assert.That(() => w.AdvanceTo(w.Now.Plus(10L * Day)), Throws.Nothing);
        }

        [Test]
        public void A_band_under_pressure_at_land_that_fails_does_not_settle()
        {
            // Plains only: food underfoot, no wood anywhere. Pressure alone
            // is not enough, and every hop lands on the same plains.
            var w = new WorkWorld(1UL, WorkWorld.PlainsOnly());
            var band = w.NewWanderingBand(WorkWorld.Camp, WorkWorld.PlentifulFood(60) * 4);
            w.JoinAdults(band, 60);
            var councilsToPressure = (NomadicBands.SettlingPressure + 59) / 60;

            w.AdvanceTo(w.Now.Plus((councilsToPressure + 60) * Day));

            Assert.Multiple(() =>
            {
                Assert.That(w.Nomads.PressureOf(band), Is.GreaterThanOrEqualTo(NomadicBands.SettlingPressure));
                Assert.That(w.Count(DomainEventKind.SettlementFounded), Is.Zero);
                Assert.That(w.Nomads.TrackedCount, Is.EqualTo(1));
                Assert.That(w.Count(DomainEventKind.CampPitched), Is.GreaterThan(1), "it kept moving");
            });
        }

        [Test]
        public void After_camp_days_the_band_moves_to_the_best_reachable_land_and_arrives_by_dusk()
        {
            // From the far plains across the river nothing scores above one;
            // from a camp near the forest, the cells that reach forest and
            // hills score three. The band starts where only plains are in
            // reach and walks toward the better land.
            var w = new WorkWorld();
            var start = new WorldPosition(14, 8);
            Assert.That(w.Nomads.LandScore(start), Is.EqualTo(1));
            var band = w.NewWanderingBand(start, WorkWorld.PlentifulFood(2));
            var adults = w.JoinAdults(band, 2);

            // Councils up to and including the one that decides to move.
            AdvanceToCouncil(w, NomadicBands.CampDays);

            Assert.Multiple(() =>
            {
                Assert.That(band.Destination, Is.Not.Null, "decided to move");
                Assert.That(band.Position, Is.EqualTo(start), "not left yet");
                Assert.That(w.Clock.TryPeekNext(out var next), Is.True);
            });

            var destination = band.Destination!.Value;
            Assert.That(Math.Abs(destination.X - start.X), Is.LessThanOrEqualTo(NomadicBands.HopRadius));
            Assert.That(Math.Abs(destination.Y - start.Y), Is.LessThanOrEqualTo(NomadicBands.HopRadius));
            Assert.That(destination.X, Is.GreaterThan(WorkWorld.RiverColumn), "the river is a wall");

            // The work pass at dawn: nobody goes out.
            w.AdvanceToDawn();
            Assert.That(w.Jobs.HasTask(adults[0]), Is.False, "a moving day");

            // By dusk the band has arrived and made camp.
            w.AdvanceTo(w.Today(Jobs.Dusk));

            Assert.Multiple(() =>
            {
                Assert.That(band.Position, Is.EqualTo(destination));
                Assert.That(band.Destination, Is.Null);
                Assert.That(w.People.GetPosition(adults[0]), Is.EqualTo(destination), "everyone walked with the band");
                Assert.That(w.People.GetPosition(adults[1]), Is.EqualTo(destination));
                Assert.That(w.Nomads.DaysAtCamp(band), Is.Zero);
                Assert.That(w.Count(DomainEventKind.CampPitched), Is.EqualTo(2), "the first camp and this one");
            });

            // Work resumes from the new camp tomorrow, if anything is needed.
            band.SharedSupplies.Consume(ResourceKind.Food, band.SharedSupplies.Available(ResourceKind.Food));
            w.AdvanceToDawn();
            Assert.That(w.Jobs.HasTask(adults[0]), Is.True);
            Assert.That(w.Jobs.TaskOf(adults[0]).Origin, Is.EqualTo(destination));
        }

        [Test]
        [TestCase(1UL)]
        [TestCase(2UL)]
        [TestCase(3UL)]
        public void A_hop_prefers_land_that_scores_higher(ulong seed)
        {
            // Plains, with one forest cell twenty columns east of the camp:
            // out of a woodcutter's reach from the camp and from every
            // candidate but the hop box's far column, which is exactly
            // sixteen cells from it. Thirteen cells of 168 score two; the
            // band goes to one of them, whatever the seed.
            var grid = new TerrainGrid(32, 32, TerrainKind.Plains);
            var camp = new WorldPosition(2, 8);
            var forest = new WorldPosition(camp.X + NomadicBands.HopRadius + Jobs.MaxSiteRadius, camp.Y);
            grid.Set(forest, TerrainKind.Forest);
            var w = new WorkWorld(seed, grid);
            Assert.That(w.Nomads.LandScore(camp), Is.EqualTo(1));
            var band = w.NewWanderingBand(camp, WorkWorld.PlentifulFood(1));
            w.JoinAdults(band, 1);

            AdvanceToCouncil(w, NomadicBands.CampDays);

            Assert.That(band.Destination, Is.Not.Null);
            Assert.That(band.Destination!.Value.X, Is.EqualTo(camp.X + NomadicBands.HopRadius), "the far column");
            Assert.That(w.Nomads.LandScore(band.Destination.Value), Is.EqualTo(2));
        }

        [Test]
        public void The_same_seed_walks_the_same_way_and_another_seed_may_not()
        {
            var first = WalkFor(1UL, 3);
            var again = WalkFor(1UL, 3);
            var other = WalkFor(7UL, 3);

            Assert.Multiple(() =>
            {
                Assert.That(again, Is.EqualTo(first), "deterministic");
                Assert.That(first, Has.Length.EqualTo(3));
                Assert.That(first[0], Is.Not.EqualTo(first[1]).Or.Not.EqualTo(first[2]), "it moves");
            });

            // Ties are broken by a keyed draw, so a different seed may pick a
            // different equal camp. Not guaranteed on every map; recorded
            // when it is, so a change to the draw shows up.
            TestContext.Out.WriteLine("seed 1: " + string.Join(" ", first) + "; seed 7: " + string.Join(" ", other));
        }

        [Test]
        public void With_nowhere_to_go_the_band_stays()
        {
            // One passable cell in a sea of deep water.
            var grid = new TerrainGrid(5, 5, TerrainKind.DeepWater);
            var island = new WorldPosition(2, 2);
            grid.Set(island, TerrainKind.Plains);
            var w = new WorkWorld(1UL, grid);
            var band = w.NewWanderingBand(island, WorkWorld.PlentifulFood(1));
            w.JoinAdults(band, 1);

            w.AdvanceTo(w.Now.Plus((NomadicBands.CampDays + 3) * Day));

            Assert.Multiple(() =>
            {
                Assert.That(band.Destination, Is.Null);
                Assert.That(band.Position, Is.EqualTo(island));
                Assert.That(w.Nomads.DaysAtCamp(band), Is.GreaterThan(NomadicBands.CampDays), "still counting at the same camp");
                Assert.That(w.Count(DomainEventKind.CampPitched), Is.EqualTo(1));
            });
        }

        [Test]
        public void Only_the_council_and_arrival_the_band_booked_run()
        {
            var w = new WorkWorld();
            var band = w.NewWanderingBand(WorkWorld.Camp, 0);
            w.Clock.Schedule(
                w.Now.Plus(1L), NomadicBands.Phase, ScheduledEventKind.CouncilDue, band.Id, EntityId.None);
            Assert.That(() => w.Advance(1L), Throws.InvalidOperationException, "a foreign council");

            w.Clock.Schedule(
                w.Now.Plus(1L), NomadicBands.Phase, ScheduledEventKind.BandArrival, band.Id, EntityId.None);
            Assert.That(() => w.Advance(1L), Throws.InvalidOperationException, "a foreign arrival");

            var stranger = new EntityId(EntityKind.MobileGroup, 999UL);
            w.Clock.Schedule(w.Now.Plus(1L), NomadicBands.Phase, ScheduledEventKind.CouncilDue, stranger, EntityId.None);
            Assert.That(() => w.Advance(1L), Throws.InvalidOperationException, "an untracked band");
        }

        [Test]
        public void Handling_refuses_another_kind_or_another_clock()
        {
            var w = new WorkWorld();
            var other = new SimulationClock(new IdAllocator());
            var meal = new ScheduledEvent(
                new EventId(1UL), w.Now, NomadicBands.Phase, ScheduledEventKind.MealDue, EntityId.None, EntityId.None);
            var council = new ScheduledEvent(
                new EventId(1UL), w.Now, NomadicBands.Phase, ScheduledEventKind.CouncilDue, EntityId.None, EntityId.None);

            Assert.Multiple(() =>
            {
                Assert.That(() => w.Nomads.Handle(meal, w.Clock), Throws.InvalidOperationException);
                Assert.That(() => w.Nomads.Handle(council, other), Throws.InvalidOperationException);
            });
        }

        [Test]
        public void An_arrival_whose_destination_was_cleared_underneath_it_is_a_wiring_bug()
        {
            var w = new WorkWorld();
            var band = w.NewWanderingBand(new WorldPosition(14, 8), WorkWorld.PlentifulFood(1));
            w.JoinAdults(band, 1);
            AdvanceToCouncil(w, NomadicBands.CampDays);
            Assert.That(band.Destination, Is.Not.Null);

            band.Destination = null;

            Assert.That(() => w.AdvanceTo(w.Today(Jobs.Dusk)), Throws.InvalidOperationException);
        }

        [Test]
        public void The_stream_ends_with_time_itself()
        {
            // The last representable instant is 15:30:07 on its day, so its
            // first light is a first light: the council sits, and there is
            // no tomorrow to book.
            var w = new WorkWorld();
            var band = new MobileGroup(
                w.Demographics.Base.Ids.Next(EntityKind.MobileGroup), MobileGroupPurpose.NomadicBand, WorkWorld.Camp);
            var end = new SimulationTime(long.MaxValue);
            var lastCouncil = new SimulationTime(end.Ticks - end.TickOfDay + NomadicBands.FirstLight);
            w.Clock.AdvanceTo(lastCouncil.Plus(-1L), w.Router);
            w.Nomads.Track(band);

            var dispatched = w.Clock.AdvanceTo(end, w.Router);

            Assert.Multiple(() =>
            {
                Assert.That(dispatched, Is.EqualTo(1), "the council");
                Assert.That(w.Nomads.DaysAtCamp(band), Is.EqualTo(1));
                Assert.That(w.Clock.ScheduledCount, Is.Zero);
            });
        }

        // From tick zero, the n-th council sits at first light on day n - 1.
        private static void AdvanceToCouncil(WorkWorld w, int n) =>
            w.AdvanceTo(SimulationTime.FromDays(n - 1L).Plus(NomadicBands.FirstLight));

        private static WorldPosition[] WalkFor(ulong seed, int hops)
        {
            var w = new WorkWorld(seed, WorkWorld.DefaultMap());
            var band = w.NewWanderingBand(WorkWorld.Camp, WorkWorld.PlentifulFood(1));
            w.JoinAdults(band, 1);
            var camps = new WorldPosition[hops];

            for (var i = 0; i < hops; i++)
            {
                w.AdvanceTo(w.Now.Plus((NomadicBands.CampDays + 1) * Day));
                camps[i] = band.Position;
            }

            return camps;
        }
    }
}
